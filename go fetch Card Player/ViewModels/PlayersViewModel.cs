using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CardPlayer.Config;
using CardPlayer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

public partial class PlayersViewModel : ViewModelBase
{
    private string _filePath = "";

    [ObservableProperty] private ObservableCollection<PlayerTypeViewModel> _types = new();
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _statusMessage = "";
    /// <summary>Forwarded to main window status bar. Set by MainWindowViewModel.</summary>
    public Action<bool, string>? OnStatus;
    [ObservableProperty] private bool _isDirty;

    private PlayerTypeViewModel? _selectedType;
    public PlayerTypeViewModel? SelectedType
    {
        get => _selectedType;
        set
        {
            _selectedType = value;
            OnPropertyChanged();
            BeginSuppress();
        }
    }

    private int _suppressCount;
    private bool _suppressDirty => _suppressCount > 0;

    private void BeginSuppress()
    {
        _suppressCount++;
        // Use Render priority (lower than Background) so the suppress window
        // outlasts any DataGrid cell recommits that fire when switching tabs
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => _suppressCount--,
            Avalonia.Threading.DispatcherPriority.Render);
    }

    private RemoteProfile? _activeProfile;

    public void LoadFromFile(string path, RemoteProfile? activeProfile)
    {
        _filePath = path;
        _activeProfile = activeProfile;
        BeginSuppress();

        var config = JsonConfigService.LoadPrograms(path);

        var vms = config.Players.Select(p =>
        {
            var vm = new PlayerTypeViewModel(p);
            vm.RebuildMappingGrid(activeProfile);
            return vm;
        }).ToList();

        Types = new ObservableCollection<PlayerTypeViewModel>(vms);
        SelectedType = Types.FirstOrDefault(); // calls BeginSuppress again — that's fine

        foreach (var vm in vms)
            WireVmDirty(vm);

        IsDirty = false;
        StatusIsError = false; StatusMessage = $"Loaded {Types.Count} player(s).";
        OnStatus?.Invoke(false, $"[Players] {StatusMessage}");
        // Extra background post to force-clear dirty after all bindings settle
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => IsDirty = false,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    public event System.Action<string, string>? PlayerTypeRenamed;

    public void WireVmDirtyPublic(PlayerTypeViewModel vm) => WireVmDirty(vm);

    private void WireVmDirty(PlayerTypeViewModel vm)
    {
        vm.DataEdited += () => { if (!_suppressDirty) IsDirty = true; };
        vm.MappingDataChanged += () => { if (!_suppressDirty) IsDirty = true; };
        vm.Functions.CollectionChanged += (_, _) => { if (!_suppressDirty && !vm.IsLoading) IsDirty = true; };
        vm.PlayerTypeRenamed += (old, nw) => PlayerTypeRenamed?.Invoke(old, nw);
    }

    public event Action? TabOrderChanged;

    [RelayCommand]
    private void MoveTypeLeft()
    {
        if (SelectedType == null) return;
        int i = Types.IndexOf(SelectedType);
        if (i <= 0) return;
        var current = SelectedType;
        BeginSuppress();
        Types.Move(i, i - 1);
        SelectedType = current;
        IsDirty = true;
        TabOrderChanged?.Invoke();
    }

    [RelayCommand]
    private void MoveTypeRight()
    {
        if (SelectedType == null) return;
        int i = Types.IndexOf(SelectedType);
        if (i < 0 || i >= Types.Count - 1) return;
        var current = SelectedType;
        BeginSuppress();
        Types.Move(i, i + 1);
        SelectedType = current;
        IsDirty = true;
        TabOrderChanged?.Invoke();
    }

    [RelayCommand]
    private void AddType()
    {
        int nextDigit = 1;
        var existing = Types.Select(t => int.TryParse(t.Config.TypeDigit, out int n) ? n : 0).ToHashSet();
        while (existing.Contains(nextDigit)) nextDigit++;

        string addName = UniqueTypeName("New Type");
        var config = new ProgramConfig { TypeDigit = nextDigit.ToString(), PlayerType = addName };
        var vm = new PlayerTypeViewModel(config);
        vm.RebuildMappingGrid(_activeProfile);
        WireVmDirty(vm);
        Types.Add(vm);
        SelectedType = vm;
        IsDirty = true;
        TypeAdded?.Invoke(vm);
    }

    public event Action<string>? TypeDeleted;
    public event Action<PlayerTypeViewModel>? TypeAdded;

    [RelayCommand]
    private void DuplicateType()
    {
        if (SelectedType == null) return;

        // Find next available type digit
        int nextDigit = 1;
        var existing = Types.Select(t => int.TryParse(t.Config.TypeDigit, out int n) ? n : 0).ToHashSet();
        while (existing.Contains(nextDigit)) nextDigit++;

        // Deep-copy the config
        var src = SelectedType.Config;
        var copy = new ProgramConfig
        {
            TypeDigit = nextDigit.ToString(),
            PlayerType = src.PlayerType,
            ProgramName = src.ProgramName,
            ProgramPath = src.ProgramPath,
            Options = src.Options,
            NoTrailingSpace = src.NoTrailingSpace,
            SendKeys = src.SendKeys,
            SendKeysDelay = src.SendKeysDelay,
            DispatchMethod = src.DispatchMethod,
            TcpPort = src.TcpPort,
            ShiftEntryFunction = src.ShiftEntryFunction,
            ShiftExitFunction = src.ShiftExitFunction,
            ShiftEndMethod = src.ShiftEndMethod,
            ShiftTimerMs = src.ShiftTimerMs,
            ResetTimerOnKeyPress = src.ResetTimerOnKeyPress,
            Functions = src.Functions.Select(f => new ProgramFunction { Name = f.Name, KeySend = f.KeySend }).ToList(),
            ProfileMappings = src.ProfileMappings.ToDictionary(
                kvp => kvp.Key,
                kvp => new PerProfileMappings
                {
                    Mappings = kvp.Value.Mappings.Select(m => new CellMapping { Row = m.Row, Col = m.Col, FunctionName = m.FunctionName }).ToList(),
                    ShiftMappings = kvp.Value.ShiftMappings.Select(m => new CellMapping { Row = m.Row, Col = m.Col, FunctionName = m.FunctionName }).ToList(),
                    ShiftKeys = kvp.Value.ShiftKeys.Select(k => new ShiftKey { Row = k.Row, Col = k.Col }).ToList(),
                    Debounce = kvp.Value.Debounce,
                }),
        };

        // Resolve name clashes before adding so TypeAdded fires with the final name
        string uniqueName = UniqueTypeName(copy.PlayerType);
        if (uniqueName != copy.PlayerType) copy.PlayerType = uniqueName;
        var vm = new PlayerTypeViewModel(copy);
        vm.RebuildMappingGrid(_activeProfile);
        WireVmDirty(vm);
        Types.Add(vm);
        SelectedType = vm;
        IsDirty = true;
        TypeAdded?.Invoke(vm);
    }

    [RelayCommand]
    private void DeleteType()
    {
        if (SelectedType == null) return;
        string digit = SelectedType.Config.TypeDigit;
        Types.Remove(SelectedType);
        SelectedType = Types.LastOrDefault();
        IsDirty = true;
        TypeDeleted?.Invoke(digit);
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        // Deduplicate player names before saving
        DeduplicateTypeNames();
        // Deduplicate function names within each player before saving
        DeduplicateFunctionNames();

        var config = new ProgramsConfig
        {
            Players = Types.Select(t => t.Config).ToList()
        };

        JsonConfigService.SavePrograms(_filePath, config);
        IsDirty = false;
        StatusIsError = false; StatusMessage = $"Saved {Types.Count} player(s).";
        OnStatus?.Invoke(false, $"[Players] {StatusMessage}");
    }

    /// Returns a name that doesn't clash with any existing type name (excluding the vm itself).
    private string UniqueTypeName(string desired, PlayerTypeViewModel? exclude = null)
    {
        var taken = new HashSet<string>(
            Types.Where(t => t != exclude).Select(t => t.Config.PlayerType),
            StringComparer.OrdinalIgnoreCase) { "Unassigned" };
        if (string.IsNullOrWhiteSpace(desired)) desired = "New Type";
        if (taken.Contains(desired))
        {
            int suffix = 2;
            string candidate;
            do { candidate = $"{desired} ({suffix++})"; }
            while (taken.Contains(candidate));
            return candidate;
        }
        return desired;
    }

    private void DeduplicateTypeNames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Unassigned" };
        foreach (var vm in Types)
        {
            string name = string.IsNullOrWhiteSpace(vm.Config.PlayerType) ? "New Type" : vm.Config.PlayerType;
            if (!seen.Add(name))
            {
                int suffix = 2;
                string candidate;
                do { candidate = $"{name} ({suffix++})"; }
                while (!seen.Add(candidate));
                // Clear Config first so the VM setter doesn't short-circuit on same value
                vm.Config.PlayerType = "";
                vm.PlayerType = candidate;
            }
        }
    }

    private void DeduplicateFunctionNames()
    {
        foreach (var type in Types)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fn in type.Functions)
            {
                string name = string.IsNullOrWhiteSpace(fn.Name) ? "Function" : fn.Name;
                if (!seen.Add(name))
                {
                    int suffix = 2;
                    string candidate;
                    do { candidate = $"{name}{suffix++}"; }
                    while (!seen.Add(candidate));

                    fn.Function.Name = candidate;
                    fn.Name = candidate;
                }
            }
        }
    }

    public void RefreshMappingGrids(RemoteProfile? activeProfile)
    {
        _activeProfile = activeProfile;
        BeginSuppress();
        foreach (var type in Types)
            type.RebuildMappingGrid(activeProfile);
        IsDirty = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => IsDirty = false,
            Avalonia.Threading.DispatcherPriority.Background);
    }

    public System.Collections.Generic.IEnumerable<ProgramConfig> GetProgramConfigs()
        => Types.Select(t => t.Config);
}