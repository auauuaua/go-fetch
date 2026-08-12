using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CardPlayer.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

public partial class PlayerTypeViewModel : ViewModelBase
{
    public ProgramConfig Config { get; }

    public string TabHeader => string.IsNullOrWhiteSpace(Config.PlayerType)
        ? "New Type" : Config.PlayerType;

    // ── Program fields ────────────────────────────────────────────────────
    public event Action? DataEdited;
    public event Action<string, string>? PlayerTypeRenamed;

    public string PlayerType
    {
        get => Config.PlayerType;
        set
        {
            if (value == null || Config.PlayerType == value) return;
            Config.PlayerType = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TabHeader));
            PlayerTypeRenamed?.Invoke(Config.TypeDigit, value);
            DataEdited?.Invoke();
        }
    }
    public string ProgramName
    {
        get => Config.ProgramName;
        set { if (value == null || Config.ProgramName == value) return; Config.ProgramName = value; OnPropertyChanged(); DataEdited?.Invoke(); }
    }

    /// <summary>Filename without extension extracted from ProgramPath, shown inline next to the label.</summary>
    public string ProgramDisplayName =>
        string.IsNullOrWhiteSpace(Config.ProgramPath)
            ? ""
            : Path.GetFileNameWithoutExtension(Config.ProgramPath);

    public string ProgramPath
    {
        get => Config.ProgramPath;
        set
        {
            var cleaned = (value ?? "").Trim();
            if (cleaned.Length >= 2 && cleaned.StartsWith("\"") && cleaned.EndsWith("\""))
                cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();

            if (Config.ProgramPath == cleaned && cleaned == value) return;
            Config.ProgramPath = cleaned;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgramDisplayName));
            // Always keep ProgramName in sync with filename for internal use
            Config.ProgramName = Path.GetFileNameWithoutExtension(cleaned);
            OnPropertyChanged(nameof(ProgramName));
            DataEdited?.Invoke();
        }
    }

    /// <summary>Invoked by the view's code-behind to open a file picker and set ProgramPath.</summary>
    public Action? BrowseProgramRequested;
    public string Options { get => Config.Options; set { if (value == null || Config.Options == value) return; Config.Options = value; OnPropertyChanged(); DataEdited?.Invoke(); } }
    public bool NoTrailingSpace { get => Config.NoTrailingSpace; set { if (Config.NoTrailingSpace == value) return; Config.NoTrailingSpace = value; OnPropertyChanged(); DataEdited?.Invoke(); } }
    public string SendKeys { get => Config.SendKeys; set { if (Config.SendKeys == value) return; Config.SendKeys = value; OnPropertyChanged(); DataEdited?.Invoke(); } }
    public string SendKeysDelay { get => Config.SendKeysDelay; set { if (Config.SendKeysDelay == value) return; Config.SendKeysDelay = value; OnPropertyChanged(); DataEdited?.Invoke(); } }
    public string DispatchMethod
    {
        get => Config.DispatchMethod;
        set { var v = value ?? Config.DispatchMethod; if (Config.DispatchMethod == v) return; Config.DispatchMethod = v; OnPropertyChanged(); OnPropertyChanged(nameof(IsTcp)); OnPropertyChanged(nameof(IsVk)); DataEdited?.Invoke(); }
    }
    public int TcpPort
    {
        get => Config.TcpPort;
        set { if (Config.TcpPort == value) return; Config.TcpPort = value; OnPropertyChanged(); DataEdited?.Invoke(); }
    }

    public bool IsTcp => DispatchMethod == "tcp";
    public bool IsVk => DispatchMethod == "vk";

    public string ShiftEntryFunction { get => Config.ShiftEntryFunction; set { var v = value ?? Config.ShiftEntryFunction; if (Config.ShiftEntryFunction == v) return; Config.ShiftEntryFunction = v; OnPropertyChanged(); DataEdited?.Invoke(); } }
    public string ShiftExitFunction { get => Config.ShiftExitFunction; set { var v = value ?? Config.ShiftExitFunction; if (Config.ShiftExitFunction == v) return; Config.ShiftExitFunction = v; OnPropertyChanged(); DataEdited?.Invoke(); } }
    public string ShiftEndMethod { get => Config.ShiftEndMethod; set { var v = value ?? Config.ShiftEndMethod; if (Config.ShiftEndMethod == v) return; Config.ShiftEndMethod = v; OnPropertyChanged(); OnPropertyChanged(nameof(IsTimerMode)); DataEdited?.Invoke(); } }
    public int ShiftTimerMs { get => Config.ShiftTimerMs; set { if (Config.ShiftTimerMs == value) return; Config.ShiftTimerMs = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShiftTimerSeconds)); DataEdited?.Invoke(); } }

    public string ShiftTimerSeconds
    {
        get => (Config.ShiftTimerMs / 1000.0).ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double secs) && secs >= 0)
                ShiftTimerMs = (int)(secs * 1000);
        }
    }
    public bool ResetTimerOnKeyPress { get => Config.ResetTimerOnKeyPress; set { if (Config.ResetTimerOnKeyPress == value) return; Config.ResetTimerOnKeyPress = value; OnPropertyChanged(); DataEdited?.Invoke(); } }
    public bool IsTimerMode => ShiftEndMethod == "timer";

    public static IReadOnlyList<string> DispatchMethods { get; } = new[] { "sendkeys", "vk", "tcp" };
    public static IReadOnlyList<string> ShiftEndMethods { get; } = new[] { "nextkey", "shiftkey", "timer" };

    // ── Functions list ────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ProgramFunctionViewModel> _functions = new();
    [ObservableProperty] private ProgramFunctionViewModel? _selectedFunction;

    // ── Mapping grid (built from active remote profile) ───────────────────
    [ObservableProperty] private ObservableCollection<ProgramMappingCellViewModel> _mappingCells = new();
    [ObservableProperty] private ObservableCollection<ProgramMappingCellViewModel> _shiftMappingCells = new();
    [ObservableProperty] private int _mappingGridCols = 3;
    [ObservableProperty] private int _selectedMappingTab; // 0=Default, 1=Shift
    [ObservableProperty] private bool _selectingShiftKey;

    partial void OnSelectedMappingTabChanged(int value) =>
        OnPropertyChanged(nameof(ActiveMappingCells));

    public ObservableCollection<ProgramMappingCellViewModel> ActiveMappingCells =>
        SelectedMappingTab == 1 ? ShiftMappingCells : MappingCells;

    partial void OnSelectingShiftKeyChanged(bool value)
    {
        foreach (var c in MappingCells.Concat(ShiftMappingCells))
            c.IsSelectingShift = value;
    }

    private bool _isLoading;

    public PlayerTypeViewModel(ProgramConfig config)
    {
        Config = config;
        _isLoading = true;
        Functions = new ObservableCollection<ProgramFunctionViewModel>(
            config.Functions.Select(f => SubscribeFunction(new ProgramFunctionViewModel(f))));
        _isLoading = false;
        RebuildAvailableSendFunctions();
    }

    private string _activeProfileName = "";

    public bool HasShiftKeys => ActiveProfileMappings.ShiftKeys.Any();

    private PerProfileMappings ActiveProfileMappings =>
        Config.GetOrCreateProfileMappings(_activeProfileName);

    /// <summary>Per-media-type, per-remote debounce in ms. 0 = off.</summary>
    public int Debounce
    {
        get => ActiveProfileMappings.Debounce;
        set
        {
            if (value < 0 || ActiveProfileMappings.Debounce == value) return;
            ActiveProfileMappings.Debounce = value;
            OnPropertyChanged();
            DataEdited?.Invoke();
        }
    }

    // Called after load with the active remote profile
    public void RebuildMappingGrid(RemoteProfile? activeProfile)
    {
        _isLoading = true;

        // Reset to Default tab so user isn't left on Shift tab for a profile with no shift keys
        SelectedMappingTab = 0;

        // Dispose old VMs before clearing so ComboBox detach events don't corrupt the mapping lists
        foreach (var c in MappingCells.Concat(ShiftMappingCells))
            c.Dispose();

        MappingCells.Clear();
        ShiftMappingCells.Clear();
        if (activeProfile == null) { _isLoading = false; return; }

        _activeProfileName = activeProfile.Name;
        var pm = Config.GetOrCreateProfileMappings(activeProfile.Name);

        MappingGridCols = activeProfile.Cols;

        for (int r = 0; r < activeProfile.Rows; r++)
        {
            for (int c = 0; c < activeProfile.Cols; c++)
            {
                var cell = activeProfile.Cells.FirstOrDefault(x => x.Row == r && x.Col == c);
                string label = cell?.Label ?? "";

                var normalVm = new ProgramMappingCellViewModel(r, c, label, pm, Functions, isShift: false);
                normalVm.IsShiftKey = pm.ShiftKeys.Any(sk => sk.Row == r && sk.Col == c);
                normalVm.DataChanged += () => MappingDataChanged?.Invoke();
                MappingCells.Add(normalVm);

                var shiftVm = new ProgramMappingCellViewModel(r, c, label, pm, Functions, isShift: true);
                shiftVm.IsShiftKey = normalVm.IsShiftKey;
                shiftVm.DataChanged += () => MappingDataChanged?.Invoke();
                ShiftMappingCells.Add(shiftVm);
            }
        }
        _isLoading = false;
        OnPropertyChanged(nameof(Debounce));
    }

    // Fired only when cell mappings are actually edited by the user
    public event Action? MappingDataChanged;

    public bool IsLoading => _isLoading;

    [RelayCommand]
    private void ToggleSelectShiftKey() => SelectingShiftKey = !SelectingShiftKey;

    [RelayCommand]
    private void CellClickedDuringShiftSelect(ProgramMappingCellViewModel cell)
    {
        if (!SelectingShiftKey) return;
        bool wasShiftKey = cell.IsShiftKey;
        var pm = ActiveProfileMappings;

        if (wasShiftKey)
            pm.ShiftKeys.RemoveAll(sk => sk.Row == cell.Row && sk.Col == cell.Col);
        else
            pm.ShiftKeys.Add(new Config.ShiftKey { Row = cell.Row, Col = cell.Col });

        foreach (var c in MappingCells.Concat(ShiftMappingCells)
            .Where(c2 => c2.Row == cell.Row && c2.Col == cell.Col))
            c.IsShiftKey = !wasShiftKey;

        OnPropertyChanged(nameof(HasShiftKeys));
        DataEdited?.Invoke();
    }

    // ── Send-after-launch function picker ────────────────────────────────
    // Mirrors ProgramMappingCellViewModel: stores by function Name, rebuilds
    // by replacing the collection entirely, restores by name match.

    [ObservableProperty]
    private ObservableCollection<ProgramFunctionViewModel> _availableSendFunctions = new();

    [ObservableProperty]
    private ProgramFunctionViewModel? _selectedSendFunction;

    partial void OnSelectedSendFunctionChanged(ProgramFunctionViewModel? oldValue,
                                               ProgramFunctionViewModel? newValue)
    {
        if (_isLoading) return;
        if (newValue == null) return;
        var name = newValue.Name ?? "";
        var fn = Functions.FirstOrDefault(f => f.Name == name);
        var keySend = fn?.KeySend ?? "";
        if (Config.SendKeys == keySend) return;
        Config.SendKeys = keySend;
        OnPropertyChanged(nameof(SendKeys));
        DataEdited?.Invoke();
    }

    [ObservableProperty]
    private ProgramFunctionViewModel? _selectedShiftEntryFunction;

    partial void OnSelectedShiftEntryFunctionChanged(ProgramFunctionViewModel? oldValue,
                                                     ProgramFunctionViewModel? newValue)
    {
        if (_isLoading) return;
        if (newValue == null) return;
        var name = newValue.Name ?? "";
        if (Config.ShiftEntryFunction == name) return;
        Config.ShiftEntryFunction = name;
        OnPropertyChanged(nameof(ShiftEntryFunction));
        DataEdited?.Invoke();
    }

    [ObservableProperty]
    private ProgramFunctionViewModel? _selectedShiftExitFunction;

    partial void OnSelectedShiftExitFunctionChanged(ProgramFunctionViewModel? oldValue,
                                                    ProgramFunctionViewModel? newValue)
    {
        if (_isLoading) return;
        if (newValue == null) return;
        var name = newValue.Name ?? "";
        if (Config.ShiftExitFunction == name) return;
        Config.ShiftExitFunction = name;
        OnPropertyChanged(nameof(ShiftExitFunction));
        DataEdited?.Invoke();
    }

    private void RebuildAvailableSendFunctions()
    {
        bool wasLoading = _isLoading;
        _isLoading = true;

        // ── Launch send ───────────────────────────────────────────────────
        string? currentSendName = SelectedSendFunction?.Name;

        AvailableSendFunctions = new ObservableCollection<ProgramFunctionViewModel>(
            new[] { new ProgramFunctionViewModel(new ProgramFunction { Name = "(none)", KeySend = "" }) }
            .Concat(Functions));

        var sendMatch = AvailableSendFunctions.FirstOrDefault(f => f.Name == currentSendName);
        if (sendMatch == null && !string.IsNullOrEmpty(Config.SendKeys))
        {
            var fn = Functions.FirstOrDefault(f => f.KeySend == Config.SendKeys);
            sendMatch = fn != null ? AvailableSendFunctions.FirstOrDefault(f => f.Name == fn.Name) : null;
        }
        _selectedSendFunction = sendMatch ?? AvailableSendFunctions.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedSendFunction));

        // ── Shift entry ───────────────────────────────────────────────────
        string? currentEntryName = SelectedShiftEntryFunction?.Name ?? Config.ShiftEntryFunction;
        var entryNone = new ProgramFunctionViewModel(new ProgramFunction { Name = "", KeySend = "" });
        var shiftFunctions = new ObservableCollection<ProgramFunctionViewModel>(
            new[] { entryNone }.Concat(Functions));

        _selectedShiftEntryFunction = shiftFunctions.FirstOrDefault(f => f.Name == currentEntryName)
                                   ?? entryNone;
        OnPropertyChanged(nameof(SelectedShiftEntryFunction));

        // ── Shift exit ────────────────────────────────────────────────────
        string? currentExitName = SelectedShiftExitFunction?.Name ?? Config.ShiftExitFunction;
        var exitNone = new ProgramFunctionViewModel(new ProgramFunction { Name = "", KeySend = "" });
        var shiftExitFunctions = new ObservableCollection<ProgramFunctionViewModel>(
            new[] { exitNone }.Concat(Functions));

        _selectedShiftExitFunction = shiftExitFunctions.FirstOrDefault(f => f.Name == currentExitName)
                                  ?? exitNone;
        OnPropertyChanged(nameof(SelectedShiftExitFunction));

        // Expose the shift function lists for binding
        AvailableShiftEntryFunctions = shiftFunctions;
        AvailableShiftExitFunctions = shiftExitFunctions;

        _isLoading = wasLoading;
    }

    [ObservableProperty]
    private ObservableCollection<ProgramFunctionViewModel> _availableShiftEntryFunctions = new();
    [ObservableProperty]
    private ObservableCollection<ProgramFunctionViewModel> _availableShiftExitFunctions = new();

    public IReadOnlyList<string> FunctionKeySends =>
        Functions.Select(f => f.KeySend).Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();

    private ProgramFunctionViewModel SubscribeFunction(ProgramFunctionViewModel vm)
    {
        vm.DataEdited += () =>
        {
            if (_isLoading) return;
            DataEdited?.Invoke();
            OnPropertyChanged(nameof(FunctionKeySends));
            RebuildAvailableSendFunctions();
        };
        return vm;
    }

    // ── Function CRUD ─────────────────────────────────────────────────────

    [RelayCommand]
    private void AddFunction()
    {
        var fn = new ProgramFunction { Name = "New Function", KeySend = "" };
        var vm = SubscribeFunction(new ProgramFunctionViewModel(fn));
        Config.Functions.Add(fn);
        Functions.Add(vm);
        SelectedFunction = vm;
        RefreshMappingDropdowns();
        RebuildAvailableSendFunctions();
        OnPropertyChanged(nameof(FunctionKeySends));
        DataEdited?.Invoke();
    }

    [RelayCommand]
    private void DeleteFunction()
    {
        if (SelectedFunction == null) return;
        Config.Functions.Remove(SelectedFunction.Function);
        Functions.Remove(SelectedFunction);
        SelectedFunction = Functions.LastOrDefault();
        RefreshMappingDropdowns();
        RebuildAvailableSendFunctions();
        OnPropertyChanged(nameof(FunctionKeySends));
        DataEdited?.Invoke();
    }

    [RelayCommand]
    private void MoveFunctionUp()
    {
        if (SelectedFunction == null) return;
        int idx = Functions.IndexOf(SelectedFunction);
        if (idx <= 0) return;
        var current = SelectedFunction;
        // Remove + insert is more reliable than Move for DataGrid visual update
        Functions.RemoveAt(idx);
        Functions.Insert(idx - 1, current);
        var fn = Config.Functions[idx];
        Config.Functions[idx] = Config.Functions[idx - 1];
        Config.Functions[idx - 1] = fn;
        SelectedFunction = current;
        RebuildAvailableSendFunctions();
        DataEdited?.Invoke();
    }

    [RelayCommand]
    private void MoveFunctionDown()
    {
        if (SelectedFunction == null) return;
        int idx = Functions.IndexOf(SelectedFunction);
        if (idx < 0 || idx >= Functions.Count - 1) return;
        var current = SelectedFunction;
        // Remove + insert is more reliable than Move for DataGrid visual update
        Functions.RemoveAt(idx);
        Functions.Insert(idx + 1, current);
        var fn = Config.Functions[idx];
        Config.Functions[idx] = Config.Functions[idx + 1];
        Config.Functions[idx + 1] = fn;
        SelectedFunction = current;
        RebuildAvailableSendFunctions();
        DataEdited?.Invoke();
    }

    private void RefreshMappingDropdowns()
    {
        _isLoading = true;
        foreach (var cell in MappingCells.Concat(ShiftMappingCells))
            cell.RefreshFunctions(Functions);
        _isLoading = false;
    }
}