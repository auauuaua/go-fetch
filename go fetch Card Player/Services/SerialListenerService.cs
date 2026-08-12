using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using CardPlayer.Config;

namespace CardPlayer.Services;

public class SerialListenerService : IDisposable
{
    private const string DefaultVid = "2E8A";
    private const string DefaultPid = "000A";
    private static string HardwarePath => Path.Combine(DataDir, "Hardware.json");

    private static (string Vid, string Pid, bool DebugLogging) LoadHardwareConfig()
    {
        if (!File.Exists(HardwarePath)) return (DefaultVid, DefaultPid, false);
        try
        {
            var config = System.Text.Json.JsonSerializer.Deserialize<Config.HardwareConfig>(
                File.ReadAllText(HardwarePath));
            return config != null
                ? (config.Vid, config.Pid, config.DebugLogging)
                : (DefaultVid, DefaultPid, false);
        }
        catch { return (DefaultVid, DefaultPid, false); }
    }

    private static string DataDir => AppPaths.DataDir;
    private static string CardsPath => Path.Combine(DataDir, "Cards.csv");
    private static string ProgramsJsonPath => Path.Combine(DataDir, "Players.json");
    private static string RemotesJsonPath => Path.Combine(DataDir, "RemoteProfiles.json");

    private SerialPort? _serial;
    private System.Threading.Timer? _portTimer;

    private string _foundFilePath = "";
    private string _foundFileType = "";
    private Process? _filerun;
    private JobObject? _job;
    private IntPtr _launchedHwnd;

    // irCode → (keySend, dispatchMethod, tcpPort)
    private Dictionary<string, (string KeySend, string Dispatch, int TcpPort)> _currentIrMap = new();
    private Dictionary<string, (string KeySend, string Dispatch, int TcpPort)> _currentShiftIrMap = new();

    // Shift mode state
    private bool _shiftActive;
    private System.Threading.Timer? _shiftTimer;
    private ProgramConfig? _currentProgramConfig;

    // Set of IR codes that act as shift triggers for the current type
    private HashSet<string> _shiftTriggerCodes = new();

    public event Action<string>? StatusChanged;
    public event Func<string, bool>? IrReceived;

    // ── Remote Passthrough ─────────────────────────────────────────────────
    // When enabled and no card/job is active, IR codes are dispatched as if
    // a card of PassthroughTypeDigit is currently inserted.
    private bool _passthroughEnabled;
    private string _passthroughTypeDigit = "";

    // ── IR debounce (per active remote profile) ────────────────────────────
    private int _debounceMs;
    private DateTime _lastIrTime = DateTime.MinValue;

    /// <summary>
    /// Called from the UI thread to update passthrough state.
    /// Thread-safe: fields are only read on the serial thread after assignment.
    /// </summary>
    public void SetPassthrough(bool enabled, string typeDigit)
    {
        _passthroughEnabled = enabled;
        _passthroughTypeDigit = typeDigit ?? "";
    }

    /// <summary>
    /// Forces IR maps to reload from disk so editor saves (mappings, debounce,
    /// TCP port, etc.) take effect immediately.
    /// - Always invalidates the passthrough cache so it reloads on the next IR event.
    /// - If a card is currently inserted, rebuilds the active IR map right away.
    /// </summary>
    public void InvalidatePassthroughCache()
    {
        _lastPassthroughType = "";

        // If a card/job is active, rebuild its IR map now from the freshly-saved JSON
        if (!string.IsNullOrEmpty(_foundFileType))
        {
            Log("Reloading active IR map after save");
            ReloadIrMappings();
        }
    }

    /// <summary>Simulates a QR card insertion — identical behaviour to a real scan.</summary>
    public void SimulateQrCode(string qrCode)
    {
        if (string.IsNullOrWhiteSpace(qrCode)) return;
        System.Threading.Tasks.Task.Run(() => HandleQrCode(qrCode.Trim()));
    }

    public void Start()
    {
        _shiftTimer = new System.Threading.Timer(_ =>
        {
            if (!_shiftActive) return;
            FireShiftExit();
            _shiftActive = false;
        }, null, Timeout.Infinite, Timeout.Infinite);

        _portTimer = new System.Threading.Timer(_ =>
        {
            try { if (_serial == null || !_serial.IsOpen) OpenDetectedPort(); }
            catch { }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    // ── Reload IR map from JSON for the current media type ─────────────────

    public void ReloadIrMappings()
    {
        _currentIrMap.Clear();
        _currentShiftIrMap.Clear();
        _shiftTriggerCodes.Clear();
        _currentProgramConfig = null;

        if (!File.Exists(ProgramsJsonPath)) { Log($"ReloadIR: MISSING {ProgramsJsonPath}"); return; }
        if (!File.Exists(RemotesJsonPath)) { Log($"ReloadIR: MISSING {RemotesJsonPath}"); return; }

        var programsConfig = JsonConfigService.LoadPrograms(ProgramsJsonPath);
        var remotesConfig = JsonConfigService.LoadRemoteProfiles(RemotesJsonPath);

        var program = programsConfig.Players.FirstOrDefault(p => p.TypeDigit == _foundFileType);
        if (program == null) { Log($"ReloadIR: no program for type '{_foundFileType}'"); return; }
        _currentProgramConfig = program;

        LogDebug($"ReloadIR: '{program.ProgramName}' dispatch={program.DispatchMethod}");

        var activeProfile = remotesConfig.Profiles
            .FirstOrDefault(p => p.Name == remotesConfig.ActiveProfile)
            ?? remotesConfig.Profiles.FirstOrDefault();
        if (activeProfile == null) { Log("ReloadIR: no active remote profile"); return; }

        var pm = program.GetOrCreateProfileMappings(activeProfile.Name);
        _debounceMs = pm.Debounce;

        // Build normal and shift IR maps
        foreach (var cell in activeProfile.Cells)
        {
            if (string.IsNullOrWhiteSpace(cell.IrCode)) continue;

            // Normal mapping
            var mapping = pm.Mappings.FirstOrDefault(m => m.Row == cell.Row && m.Col == cell.Col);
            if (mapping != null && !string.IsNullOrWhiteSpace(mapping.FunctionName))
            {
                var fn = program.Functions.FirstOrDefault(f => f.Name == mapping.FunctionName);
                if (fn != null && fn.KeySend != null)
                    _currentIrMap[cell.IrCode] = (fn.KeySend, program.DispatchMethod, program.TcpPort);
            }

            // Shift mapping
            var shiftMapping = pm.ShiftMappings.FirstOrDefault(m => m.Row == cell.Row && m.Col == cell.Col);
            if (shiftMapping != null && !string.IsNullOrWhiteSpace(shiftMapping.FunctionName))
            {
                var fn = program.Functions.FirstOrDefault(f => f.Name == shiftMapping.FunctionName);
                if (fn != null && fn.KeySend != null)
                    _currentShiftIrMap[cell.IrCode] = (fn.KeySend, program.DispatchMethod, program.TcpPort);
            }

            // Shift trigger codes
            if (pm.ShiftKeys.Any(sk => sk.Row == cell.Row && sk.Col == cell.Col))
                _shiftTriggerCodes.Add(cell.IrCode);
        }

        LogDebug($"ReloadIR: normal={_currentIrMap.Count} shift={_currentShiftIrMap.Count} triggers={_shiftTriggerCodes.Count}");
    }

    /// <summary>
    /// Loads IR mappings for a given type digit without requiring an active file/job.
    /// Used by passthrough mode. Only reloads if the type has changed since last load.
    /// </summary>
    private string _lastPassthroughType = "";
    private void LoadPassthroughIrMappings(string typeDigit)
    {
        if (_lastPassthroughType == typeDigit &&
            _currentProgramConfig != null &&
            _currentProgramConfig.TypeDigit == typeDigit)
            return; // already loaded for this type

        _currentIrMap.Clear();
        _currentShiftIrMap.Clear();
        _shiftTriggerCodes.Clear();
        _currentProgramConfig = null;
        _lastPassthroughType = typeDigit;

        if (!File.Exists(ProgramsJsonPath)) return;
        if (!File.Exists(RemotesJsonPath)) return;

        var programsConfig = JsonConfigService.LoadPrograms(ProgramsJsonPath);
        var remotesConfig = JsonConfigService.LoadRemoteProfiles(RemotesJsonPath);

        var program = programsConfig.Players.FirstOrDefault(p => p.TypeDigit == typeDigit);
        if (program == null) { Log($"Passthrough: no program for type '{typeDigit}'"); return; }
        _currentProgramConfig = program;

        var activeProfile = remotesConfig.Profiles
            .FirstOrDefault(p => p.Name == remotesConfig.ActiveProfile)
            ?? remotesConfig.Profiles.FirstOrDefault();
        if (activeProfile == null) return;

        var pm = program.GetOrCreateProfileMappings(activeProfile.Name);
        _debounceMs = pm.Debounce;

        foreach (var cell in activeProfile.Cells)
        {
            if (string.IsNullOrWhiteSpace(cell.IrCode)) continue;

            var mapping = pm.Mappings.FirstOrDefault(m => m.Row == cell.Row && m.Col == cell.Col);
            if (mapping != null && !string.IsNullOrWhiteSpace(mapping.FunctionName))
            {
                var fn = program.Functions.FirstOrDefault(f => f.Name == mapping.FunctionName);
                if (fn != null && fn.KeySend != null)
                    _currentIrMap[cell.IrCode] = (fn.KeySend, program.DispatchMethod, program.TcpPort);
            }

            var shiftMapping = pm.ShiftMappings.FirstOrDefault(m => m.Row == cell.Row && m.Col == cell.Col);
            if (shiftMapping != null && !string.IsNullOrWhiteSpace(shiftMapping.FunctionName))
            {
                var fn = program.Functions.FirstOrDefault(f => f.Name == shiftMapping.FunctionName);
                if (fn != null && fn.KeySend != null)
                    _currentShiftIrMap[cell.IrCode] = (fn.KeySend, program.DispatchMethod, program.TcpPort);
            }

            if (pm.ShiftKeys.Any(sk => sk.Row == cell.Row && sk.Col == cell.Col))
                _shiftTriggerCodes.Add(cell.IrCode);
        }

        LogDebug($"Passthrough IR loaded: type='{typeDigit}' normal={_currentIrMap.Count}");
    }

    // ── Serial port ────────────────────────────────────────────────────────

    private void OpenDetectedPort()
    {
        var (vid, pid, debugLogging) = LoadHardwareConfig();
        DebugLogging = debugLogging;
        string? portName = DetectComPort(vid, pid);
        if (portName == null) return;

        if (_serial != null) _serial.DataReceived -= Serial_DataReceived;
        _serial?.Dispose();

        _serial = new SerialPort(portName, 9600)
        {
            NewLine = "\n",
            ReadTimeout = 500,
            DtrEnable = true,
            RtsEnable = true,
            // Default is ASCII, which turns multibyte chars (en dash, accents, etc.)
            // into '?' and breaks exact QR matching. UTF-8 preserves them.
            Encoding = System.Text.Encoding.UTF8
        };
        _serial.DataReceived += Serial_DataReceived;
        _serial.Open();
        StatusChanged?.Invoke($"Connected on {portName}");
    }

    private static string? DetectComPort(string vid, string pid)
    {
        string pattern = $"VID_{vid}&PID_{pid}";
        var searcher = new ManagementObjectSearcher(
            $"SELECT * FROM Win32_PnPEntity WHERE PNPDeviceID LIKE '%{pattern}%'");
        foreach (ManagementObject device in searcher.Get())
        {
            string? name = device["Name"]?.ToString();
            if (name == null) continue;
            var match = Regex.Match(name, @"COM\d+");
            if (match.Success) return match.Value;
        }
        return null;
    }

    // ── Serial handler ─────────────────────────────────────────────────────

    private void Serial_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            string line = _serial!.ReadLine();
            string coderead = line.Trim();

            if (coderead.StartsWith("QRR:")) HandleQrCode(coderead.Substring(4));
            else if (coderead == "ejected") HandleEject();
            else if (coderead.StartsWith("IR:")) HandleIr(coderead.Substring(3));
        }
        catch (IOException) { HandleDisconnect(); }
        catch (InvalidOperationException) { HandleDisconnect(); }
        catch (TimeoutException) { }
    }

    // ── QR card ────────────────────────────────────────────────────────────

    private void HandleQrCode(string code)
    {
        Log($"QR scanned: '{code}'  DataDir: {DataDir}");

        if (!File.Exists(CardsPath)) { Log($"MISSING: {CardsPath}"); return; }
        if (!File.Exists(ProgramsJsonPath)) { Log($"MISSING: {ProgramsJsonPath}"); return; }

        var allMedia = CsvService.LoadMedia(CardsPath);
        var codematch = allMedia.FirstOrDefault(m => m.QrCode == code);
        if (codematch == null) { Log($"QR not found: '{code}'"); return; }

        _foundFilePath = codematch.Path;
        _foundFileType = codematch.TypeDigit;
        Log($"Matched type='{_foundFileType}' path='{_foundFilePath}'");

        _shiftActive = false;
        _shiftTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _lastPassthroughType = ""; // invalidate passthrough cache — real card takes priority

        var programsConfig = JsonConfigService.LoadPrograms(ProgramsJsonPath);
        var typematch = programsConfig.Players.FirstOrDefault(p => p.TypeDigit == _foundFileType);
        if (typematch == null) { Log($"Type not found: '{_foundFileType}'"); return; }

        if (!File.Exists(typematch.ProgramPath))
        { Log($"Executable not found: '{typematch.ProgramPath}'"); return; }

        ReloadIrMappings();

        string sep = typematch.NoTrailingSpace ? "" : " ";
        Log($"Launching: {typematch.ProgramPath} {typematch.Options}{sep}\"{_foundFilePath}\"");
        _filerun = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = typematch.ProgramPath,
                Arguments = $"{typematch.Options}{sep}\"{_foundFilePath}\"",
                UseShellExecute = true
            }
        };

        // Create a job object so all child processes (including spawned ones)
        // are tracked and killed together on eject
        _job?.Dispose();
        _job = new JobObject();

        // Snapshot existing windows before launch so we can find the new one
        var windowsBefore = NativeMethods.GetVisibleTopLevelWindows();
        LogDebug($"Windows before launch: {windowsBefore.Count}");

        _filerun.Start();
        Log("Process started");

        // Find the new window that wasn't present before launch
        _launchedHwnd = IntPtr.Zero;
        System.Threading.Tasks.Task.Run(() =>
        {
            // Poll for up to 10 seconds for a new visible window to appear
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(250);
                var windowsNow = NativeMethods.GetVisibleTopLevelWindows();
                var newWindows = windowsNow.Except(windowsBefore).ToList();
                LogDebug($"Poll {i}: total={windowsNow.Count} new={newWindows.Count}");
                if (newWindows.Count > 0)
                {
                    var fg = NativeMethods.GetForegroundWindow();
                    _launchedHwnd = newWindows.Contains(fg) ? fg : newWindows[0];
                    LogDebug($"Captured new window HWND: 0x{_launchedHwnd:X} (fg=0x{fg:X})");
                    return;
                }
            }
            // Fallback: just use foreground window
            _launchedHwnd = NativeMethods.GetForegroundWindow();
            LogDebug($"Captured foreground HWND (fallback): 0x{_launchedHwnd:X}");
        });

        try { _job.Assign(_filerun); }
        catch (Exception ex) { Log($"Job assign failed: {ex.Message}"); }

        if (!string.IsNullOrEmpty(typematch.SendKeys))
        {
            if (double.TryParse(typematch.SendKeysDelay, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double secs) && secs > 0)
                Thread.Sleep((int)(secs * 1000));
            IntPtr handle = _filerun.MainWindowHandle;
            if (handle != IntPtr.Zero)
                NativeMethods.SetForegroundWindow(handle);
            if (typematch.DispatchMethod == "vk")
                InputSender.SendByVK(typematch.SendKeys, Log);
            else
                InputSender.Send(TranslateSendKeys(typematch.SendKeys), Log);
        }
    }

    // ── Card eject ─────────────────────────────────────────────────────────

    private void HandleEject()
    {
        _shiftTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _shiftActive = false;
        _currentProgramConfig = null;
        _lastPassthroughType = ""; // allow passthrough to reload after eject

        // Close the captured window first (handles Chrome and other launcher-style apps)
        if (_launchedHwnd != IntPtr.Zero)
        {
            NativeMethods.PostMessage(_launchedHwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _launchedHwnd = IntPtr.Zero;
            // Give the window a moment to close gracefully before terminating the job
            Thread.Sleep(300);
        }

        if (_job != null)
        {
            _job.Terminate();
            _job.Dispose();
            _job = null;
        }
        else if (_filerun != null && !_filerun.HasExited)
        {
            _filerun.CloseMainWindow();
        }
        StatusChanged?.Invoke("Card ejected");
    }

    // ── IR remote ──────────────────────────────────────────────────────────

    private void HandleIr(string irCode)
    {
        if (IrReceived?.Invoke(irCode) == true) return;

        // Use job as the authoritative "something is running" check —
        // _filerun.HasExited is unreliable for launcher-style apps
        bool jobActive = _job != null || (_filerun != null && !_filerun.HasExited);

        if (!jobActive)
        {
            // No card/job active — use passthrough if enabled
            if (!_passthroughEnabled || string.IsNullOrEmpty(_passthroughTypeDigit))
                return;

            // Load IR mappings for the passthrough type (without launching anything)
            LoadPassthroughIrMappings(_passthroughTypeDigit);
        }

        // ── Debounce — rate-limit repeats to at most one per _debounceMs ──────
        // Advancing the window by exactly _debounceMs (rather than resetting to
        // 'now') makes the fire rate scale smoothly with the setting, instead of
        // producing a hard cliff when _debounceMs crosses the remote's repeat interval.
        if (_debounceMs > 0)
        {
            var now = DateTime.UtcNow;
            double sinceLast = (now - _lastIrTime).TotalMilliseconds;
            if (sinceLast < _debounceMs)
            {
                LogDebug($"IR '{irCode}' debounced ({_debounceMs}ms)");
                return;
            }
            // If we've been idle far longer than the window, start fresh from now;
            // otherwise advance by exactly one window to keep a steady cadence.
            _lastIrTime = sinceLast > _debounceMs * 2 ? now : _lastIrTime.AddMilliseconds(_debounceMs);
        }

        bool isShiftTrigger = _shiftTriggerCodes.Contains(irCode);
        var cfg = _currentProgramConfig;

        // ── Normal function fires first regardless of shift trigger ───────
        if (_shiftActive)
        {
            // In shift mode — look up shifted map
            if (_currentShiftIrMap.TryGetValue(irCode, out var shiftEntry))
                Dispatch(shiftEntry);

            // Handle shift end conditions
            if (cfg != null)
            {
                if (cfg.ShiftEndMethod == "shiftkey" && isShiftTrigger)
                {
                    ExitShift();
                    return;
                }
                if (cfg.ShiftEndMethod == "nextkey" && !isShiftTrigger)
                {
                    ExitShift();
                    return;
                }
                if (cfg.ShiftEndMethod == "timer" && cfg.ResetTimerOnKeyPress)
                    _shiftTimer?.Change(cfg.ShiftTimerMs, Timeout.Infinite);
            }
        }
        else
        {
            // Normal mode — fire normal function
            if (_currentIrMap.TryGetValue(irCode, out var entry))
                Dispatch(entry);

            // Then enter shift if this is a shift trigger
            if (isShiftTrigger)
                EnterShift();
        }
    }

    private void EnterShift()
    {
        var cfg = _currentProgramConfig;
        if (cfg == null) return;
        _shiftActive = true;

        // Fire entry function if set
        if (!string.IsNullOrWhiteSpace(cfg.ShiftEntryFunction))
        {
            var fn = cfg.Functions.FirstOrDefault(f => f.Name == cfg.ShiftEntryFunction);
            if (fn != null && fn.KeySend != null)
                Dispatch((fn.KeySend, cfg.DispatchMethod, cfg.TcpPort));
        }

        // Start timer if timer mode
        if (cfg.ShiftEndMethod == "timer")
            _shiftTimer?.Change(cfg.ShiftTimerMs, Timeout.Infinite);

        LogDebug("Shift mode entered");
    }

    private void ExitShift()
    {
        _shiftTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        var cfg = _currentProgramConfig;

        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.ShiftExitFunction))
        {
            var fn = cfg.Functions.FirstOrDefault(f => f.Name == cfg.ShiftExitFunction);
            if (fn != null && fn.KeySend != null)
                Dispatch((fn.KeySend, cfg.DispatchMethod, cfg.TcpPort));
        }

        _shiftActive = false;
        LogDebug("Shift mode exited");
    }

    private void FireShiftExit()
    {
        // Called from timer thread — just fires the exit action, state managed by caller
        var cfg = _currentProgramConfig;
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.ShiftExitFunction))
        {
            var fn = cfg.Functions.FirstOrDefault(f => f.Name == cfg.ShiftExitFunction);
            if (fn != null && fn.KeySend != null)
                Dispatch((fn.KeySend, cfg.DispatchMethod, cfg.TcpPort));
        }
        LogDebug("Shift mode timed out");
    }

    private void Dispatch((string KeySend, string Dispatch, int TcpPort) entry)
    {
        LogDebug($"Dispatch: key='{entry.KeySend}' method='{entry.Dispatch}' port={entry.TcpPort}");
        if (entry.Dispatch == "tcp")
            InputSender.SendTcp(entry.KeySend, entry.TcpPort, Log);
        else if (entry.Dispatch == "vk")
            InputSender.SendByVK(entry.KeySend, Log);
        else
            InputSender.Send(TranslateSendKeys(entry.KeySend), Log);
    }

    /// <summary>
    /// Translates friendly aliases before passing to SendKeys.
    /// {SPACE} → " " (SendKeys doesn't accept {SPACE} but users naturally write it).
    /// </summary>
    private static string TranslateSendKeys(string input) =>
        input.Replace("{SPACE}", " ", System.StringComparison.OrdinalIgnoreCase);

    // ── Helpers ────────────────────────────────────────────────────────────

    public bool DebugLogging { get; set; } = false;

    private void Log(string msg)
    {
        try
        {
            File.AppendAllText(Path.Combine(DataDir, "cardplayer_debug.log"),
                $"{DateTime.Now:HH:mm:ss} {msg}\n");
        }
        catch { }
        StatusChanged?.Invoke(msg);
    }

    // Only written to log file when DebugLogging is enabled
    private void LogDebug(string msg)
    {
        if (!DebugLogging) return;
        try
        {
            File.AppendAllText(Path.Combine(DataDir, "cardplayer_debug.log"),
                $"{DateTime.Now:HH:mm:ss} {msg}\n");
        }
        catch { }
    }

    private void HandleDisconnect()
    {
        try
        {
            if (_serial != null)
            {
                _serial.DataReceived -= Serial_DataReceived;
                if (_serial.IsOpen) _serial.Close();
                _serial.Dispose();
                _serial = null;
            }
        }
        catch { }
        StatusChanged?.Invoke("Device disconnected — reconnecting…");
    }

    public void Dispose()
    {
        _portTimer?.Dispose();
        _shiftTimer?.Dispose();
        if (_serial != null)
        {
            _serial.DataReceived -= Serial_DataReceived;
            if (_serial.IsOpen) _serial.Close();
            _serial.Dispose();
        }
        if (_filerun != null && !_filerun.HasExited)
        {
            _filerun.CloseMainWindow();
            _filerun.Dispose();
        }
        _job?.Dispose();
        _job = null;
        _launchedHwnd = IntPtr.Zero;
    }
}