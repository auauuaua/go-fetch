using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Avalonia.Threading;

namespace CardPlayer.Services;

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    internal const uint WM_CLOSE = 0x0010;

    internal static HashSet<IntPtr> GetVisibleTopLevelWindows()
    {
        var set = new HashSet<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            if (IsWindowVisible(hWnd)) set.Add(hWnd);
            return true;
        }, IntPtr.Zero);
        return set;
    }
}

public static class InputSender
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern ushort MapVirtualKey(uint uCode, uint uMapType);

    private const uint MAPVK_VK_TO_VSC = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT_UNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUT_UNION u;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    // ── Media keys (used by sendkeys method for named tokens) ─────────────
    public static readonly IReadOnlyDictionary<string, ushort> MediaKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        // Playback
        { "MEDIA_PLAY_PAUSE",    0xB3 },
        { "MEDIA_STOP",          0xB2 },
        { "MEDIA_NEXT_TRACK",    0xB0 },
        { "MEDIA_PREV_TRACK",    0xB1 },
        { "MEDIA_PLAY",          0xFA },
        { "MEDIA_PAUSE",         0x13 },
        { "MEDIA_RECORD",        0xB3 },  // no dedicated VK; play/pause is closest
        { "MEDIA_FAST_FORWARD",  0xB0 },  // no dedicated VK; next track is closest
        { "MEDIA_REWIND",        0xB1 },  // no dedicated VK; prev track is closest
        // Volume
        { "VOLUME_MUTE",         0xAD },
        { "VOLUME_UP",           0xAF },
        { "VOLUME_DOWN",         0xAE },
        // Launch
        { "LAUNCH_MEDIA_SELECT", 0xB5 },
        { "LAUNCH_MAIL",         0xB4 },
        { "LAUNCH_APP1",         0xB6 },
        { "LAUNCH_APP2",         0xB7 },
        // Browser
        { "BROWSER_BACK",        0xA6 },
        { "BROWSER_FORWARD",     0xA7 },
        { "BROWSER_REFRESH",     0xA8 },
        { "BROWSER_STOP",        0xA9 },
        { "BROWSER_SEARCH",      0xAA },
        { "BROWSER_FAVORITES",   0xAB },
        { "BROWSER_HOME",        0xAC },
    };

    // ── Named VK aliases (used by vk dispatch method) ─────────────────────
    public static readonly IReadOnlyDictionary<string, ushort> NamedVKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        // Control
        { "BACK",      0x08 }, { "TAB",       0x09 }, { "RETURN",    0x0D },
        { "SHIFT",     0x10 }, { "CONTROL",   0x11 }, { "ALT",       0x12 },
        { "PAUSE",     0x13 }, { "ESCAPE",    0x1B }, { "SPACE",     0x20 },
        { "PRIOR",     0x21 }, { "NEXT",      0x22 }, { "END",       0x23 },
        { "HOME",      0x24 },
        // Arrow keys
        { "LEFT",      0x25 }, { "UP",        0x26 }, { "RIGHT",     0x27 }, { "DOWN",      0x28 },
        // Editing
        { "INSERT",    0x2D }, { "DELETE",    0x2E },
        // Function keys
        { "F1",  0x70 }, { "F2",  0x71 }, { "F3",  0x72 }, { "F4",  0x73 },
        { "F5",  0x74 }, { "F6",  0x75 }, { "F7",  0x76 }, { "F8",  0x77 },
        { "F9",  0x78 }, { "F10", 0x79 }, { "F11", 0x7A }, { "F12", 0x7B },
        { "F13", 0x7C }, { "F14", 0x7D }, { "F15", 0x7E }, { "F16", 0x7F },
        { "F17", 0x80 }, { "F18", 0x81 }, { "F19", 0x82 }, { "F20", 0x83 },
        { "F21", 0x84 }, { "F22", 0x85 }, { "F23", 0x86 }, { "F24", 0x87 },
        // Numpad
        { "NUMPAD0", 0x60 }, { "NUMPAD1", 0x61 }, { "NUMPAD2", 0x62 },
        { "NUMPAD3", 0x63 }, { "NUMPAD4", 0x64 }, { "NUMPAD5", 0x65 },
        { "NUMPAD6", 0x66 }, { "NUMPAD7", 0x67 }, { "NUMPAD8", 0x68 },
        { "NUMPAD9", 0x69 }, { "MULTIPLY", 0x6A }, { "ADD",      0x6B },
        { "SUBTRACT",0x6D }, { "DECIMAL",  0x6E }, { "DIVIDE",   0x6F },
        // Media / volume (available in vk mode too)
        { "MEDIA_PLAY_PAUSE",    0xB3 }, { "MEDIA_STOP",          0xB2 },
        { "MEDIA_NEXT_TRACK",    0xB0 }, { "MEDIA_PREV_TRACK",    0xB1 },
        { "VOLUME_MUTE",         0xAD }, { "VOLUME_UP",           0xAF },
        { "VOLUME_DOWN",         0xAE }, { "LAUNCH_MEDIA_SELECT", 0xB5 },
        { "BROWSER_BACK",        0xA6 }, { "BROWSER_FORWARD",     0xA7 },
        { "BROWSER_REFRESH",     0xA8 }, { "BROWSER_STOP",        0xA9 },
        { "BROWSER_HOME",        0xAC },
    };

    // ── sendkeys dispatch ─────────────────────────────────────────────────
    // Recognises MEDIA_* / VOLUME_* / BROWSER_* / LAUNCH_* tokens and sends
    // them as VK codes; everything else passes through to SendKeys.SendWait.
    public static void Send(string csvValue, Action<string>? log = null)
    {
        if (csvValue == null) return;

        if (MediaKeys.TryGetValue(csvValue.Trim(), out ushort vk))
        {
            log?.Invoke($"SendVK: matched vk=0x{vk:X2}, posting to UI thread");
            SendVK(vk, log);
        }
        else
        {
            log?.Invoke($"SendKeys: sending '{csvValue}'");
            SendKeys.SendWait(csvValue);
        }
    }

    // ── tcp dispatch ──────────────────────────────────────────────────────
    // Opens a short-lived TCP connection to 127.0.0.1:port and writes the
    // command followed by a newline (most receivers read line-by-line).
    public static void SendTcp(string command, int port, Action<string>? log = null)
    {
        log?.Invoke($"SendTcp: connecting to 127.0.0.1:{port} command='{command}'");
        try
        {
            using var client = new TcpClient("127.0.0.1", port);
            using var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(command + "\n");
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
            log?.Invoke($"SendTcp: sent {bytes.Length} bytes to port {port}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"SendTcp: FAILED on port {port} — {ex.Message}");
        }
    }

    // ── vk dispatch ───────────────────────────────────────────────────────
    // Accepts one or more tokens separated by |
    // Each token can be: named key (SPACE, F5, MEDIA_PLAY_PAUSE …)
    //                    hex VK (0x20, 0xB3 …)
    //                    decimal VK (32, 179 …)
    public static void SendByVK(string csvValue, Action<string>? log = null)
    {
        if (csvValue == null) return;
        foreach (var token in csvValue.Split('|'))
            SendSingleVK(token.Trim(), log);
    }

    private static readonly Dictionary<string, ushort> ModifierKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
    {
        { "CTRL",  0x11 },
        { "SHIFT", 0x10 },
        { "ALT",   0x12 },
        { "WIN",   0x5B },
    };

    private static void SendSingleVK(string trimmed, Action<string>? log = null)
    {
        if (string.IsNullOrEmpty(trimmed)) return;

        // Check for modifier combo: CTRL+P, SHIFT+F10, CTRL+SHIFT+S etc.
        // Split on + but only treat parts before the last as modifiers
        var parts = trimmed.Split('+');
        if (parts.Length > 1)
        {
            var modifiers = new List<ushort>();
            bool valid = true;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (ModifierKeys.TryGetValue(parts[i].Trim(), out ushort modVk))
                    modifiers.Add(modVk);
                else
                {
                    valid = false;
                    break;
                }
            }
            if (valid && ResolveVK(parts[^1].Trim(), out ushort keyVk))
            {
                log?.Invoke($"SendByVK: combo '{trimmed}' modifiers=[{string.Join(",", modifiers.Select(m => $"0x{m:X2}"))}] key=0x{keyVk:X2}");
                SendCombo(modifiers, keyVk, log);
                return;
            }
        }

        // Plain key
        if (ResolveVK(trimmed, out ushort vk))
        {
            log?.Invoke($"SendByVK: key '{trimmed}' → 0x{vk:X2}");
            SendVK(vk, log);
        }
        else
        {
            log?.Invoke($"SendByVK: unrecognised token '{trimmed}'");
        }
    }

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    private static bool ResolveVK(string token, out ushort vk)
    {
        if (NamedVKeys.TryGetValue(token, out vk)) return true;
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ushort.TryParse(token.Substring(2), NumberStyles.HexNumber, null, out vk)) return true;
        if (ushort.TryParse(token, out vk)) return true;
        // Single character — resolve via VkKeyScan (handles A-Z, 0-9, punctuation)
        if (token.Length == 1)
        {
            short result = VkKeyScan(token[0]);
            if (result != -1) { vk = (ushort)(result & 0xFF); return true; }
        }
        vk = 0; return false;
    }

    // Keys that require KEYEVENTF_EXTENDEDKEY — right-side modifiers, navigation, media
    private static readonly HashSet<ushort> ExtendedKeys = new HashSet<ushort>
    {
        0xA1, // VK_RSHIFT
        0xA3, // VK_RCONTROL
        0xA5, // VK_RMENU (Right Alt)
        0x21, // VK_PRIOR (Page Up)
        0x22, // VK_NEXT  (Page Down)
        0x23, // VK_END
        0x24, // VK_HOME
        0x25, // VK_LEFT
        0x26, // VK_UP
        0x27, // VK_RIGHT
        0x28, // VK_DOWN
        0x2D, // VK_INSERT
        0x2E, // VK_DELETE
        0x5B, // VK_LWIN
        0x5C, // VK_RWIN
        0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC, // Browser keys
        0xAD, 0xAE, 0xAF,                           // Volume
        0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, // Media / launch
        0x6F, // VK_DIVIDE (numpad /)
    };

    private static uint FlagsFor(ushort vk, bool keyUp = false)
    {
        uint f = ExtendedKeys.Contains(vk) ? KEYEVENTF_EXTENDEDKEY : 0u;
        if (keyUp) f |= KEYEVENTF_KEYUP;
        return f;
    }

    private static void SendCombo(List<ushort> modifiers, ushort key, Action<string>? log)
    {
        // Build: modifier(s) down, key down, key up, modifier(s) up
        var inputs = new INPUT[modifiers.Count * 2 + 2];
        int idx = 0;
        foreach (var mod in modifiers)
        {
            ushort scan = (ushort)MapVirtualKey(mod, MAPVK_VK_TO_VSC);
            inputs[idx++] = MakeInput(mod, scan, FlagsFor(mod));
        }
        ushort keyScan = (ushort)MapVirtualKey(key, MAPVK_VK_TO_VSC);
        inputs[idx++] = MakeInput(key, keyScan, FlagsFor(key));
        inputs[idx++] = MakeInput(key, keyScan, FlagsFor(key, keyUp: true));
        foreach (var mod in Enumerable.Reverse(modifiers))
        {
            ushort scan = (ushort)MapVirtualKey(mod, MAPVK_VK_TO_VSC);
            inputs[idx++] = MakeInput(mod, scan, FlagsFor(mod, keyUp: true));
        }

        Dispatcher.UIThread.Post(() =>
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            int err = Marshal.GetLastWin32Error();
            log?.Invoke($"SendInput combo result: sent={sent} lastError={err}");
        });
    }

    // ── Shared VK sender ──────────────────────────────────────────────────
    private static void SendVK(ushort vk, Action<string>? log = null)
    {
        ushort scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        log?.Invoke($"SendVK: vk=0x{vk:X2} scan=0x{scan:X2} structSize={Marshal.SizeOf(typeof(INPUT))}");

        var inputs = new[]
        {
            MakeInput(vk, scan, FlagsFor(vk)),
            MakeInput(vk, scan, FlagsFor(vk, keyUp: true))
        };

        Dispatcher.UIThread.Post(() =>
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            int err = Marshal.GetLastWin32Error();
            log?.Invoke($"SendInput result: sent={sent} lastError={err}");
        });
    }

    private static INPUT MakeInput(ushort vk, ushort scan, uint flags) => new INPUT
    {
        type = INPUT_KEYBOARD,
        u = new INPUT_UNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = scan,
                dwFlags = flags,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}