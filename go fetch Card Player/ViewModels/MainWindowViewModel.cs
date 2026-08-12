using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CardPlayer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public PlayersViewModel PlayersVm { get; } = new();
    public CardsViewModel CardsVm { get; } = new();
    public RemoteSetupViewModel RemoteSetupVm { get; } = new();
    public HardwareSetupViewModel HardwareSetupVm { get; } = new();
    public PrintGeneratorViewModel PrintGeneratorVm { get; } = new();

    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _statusMessage = "Loading…";
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private int _selectedTabIndex;

    // ── Remote Passthrough ─────────────────────────────────────────────────
    [ObservableProperty] private bool _remotePassthroughEnabled;
    [ObservableProperty] private ObservableCollection<string> _passthroughPlayers = new();
    [ObservableProperty] private string? _selectedPassthroughPlayerType;

    // Tracks selection by TypeDigit so renames don't lose the selection
    private string _selectedPassthroughTypeDigit = "";

    /// <summary>Raised whenever passthrough state changes so App.axaml.cs can forward it to the service.</summary>
    public event Action<bool, string>? PassthroughChanged;

    /// <summary>Raised after SaveAll so the serial service can refresh cached config (e.g. TCP port).</summary>
    public event Action? ChangesSaved;

    private bool _suppressPassthroughSave;
    private Action<string, string>? _cardsRenameHandler;

    partial void OnRemotePassthroughEnabledChanged(bool value)
    {
        PassthroughChanged?.Invoke(value, _selectedPassthroughPlayerType ?? "");
        if (!_suppressPassthroughSave) SavePassthroughState();
    }

    partial void OnSelectedPassthroughPlayerTypeChanged(string? value)
    {
        // Keep the digit in sync so RefreshPassthroughPlayers can survive renames
        var match = PlayersVm.Types.FirstOrDefault(t => t.Config.PlayerType == value);
        if (match != null) _selectedPassthroughTypeDigit = match.Config.TypeDigit;
        PassthroughChanged?.Invoke(_remotePassthroughEnabled, value ?? "");
        if (!_suppressPassthroughSave) SavePassthroughState();
    }

    private string HardwarePath => AppPaths.Combine("Hardware.json");

    private void SavePassthroughState()
    {
        try
        {
            var path = HardwarePath;
            Config.HardwareConfig existing = new();
            try { if (File.Exists(path)) existing = System.Text.Json.JsonSerializer.Deserialize<Config.HardwareConfig>(File.ReadAllText(path)) ?? existing; }
            catch { }
            existing.PassthroughEnabled = _remotePassthroughEnabled;
            existing.PassthroughPlayerType = _selectedPassthroughPlayerType ?? "";
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(existing,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private void LoadPassthroughState()
    {
        try
        {
            var path = HardwarePath;
            if (!File.Exists(path)) return;
            var config = System.Text.Json.JsonSerializer.Deserialize<Config.HardwareConfig>(File.ReadAllText(path));
            if (config == null) return;
            // _suppressPassthroughSave is already true (set by LoadFolder) — no saves during restore

            // Resolve the saved name to a type digit for rename-safe tracking
            var savedType = PlayersVm.Types.FirstOrDefault(t => t.Config.PlayerType == config.PassthroughPlayerType);
            if (savedType != null) _selectedPassthroughTypeDigit = savedType.Config.TypeDigit;

            SelectedPassthroughPlayerType = PassthroughPlayers.Contains(config.PassthroughPlayerType)
                ? config.PassthroughPlayerType
                : PassthroughPlayers.FirstOrDefault();
            RemotePassthroughEnabled = config.PassthroughEnabled;
            // Notify service of restored state (PassthroughChanged may be null here during construction;
            // App.axaml.cs pushes the state again after wiring the event)
            PassthroughChanged?.Invoke(_remotePassthroughEnabled, _selectedPassthroughPlayerType ?? "");
        }
        catch { }
    }

    /// <summary>Reorders Cards tabs to match the current Players type order.</summary>
    private void SyncPlayersTabOrder()
    {
        var orderedDigits = CardsVm.TypeTabs.Select(t => t.TypeDigit).ToList();
        var selected = PlayersVm.SelectedType;

        for (int i = 0; i < orderedDigits.Count; i++)
        {
            var vm = PlayersVm.Types.FirstOrDefault(t => t.Config.TypeDigit == orderedDigits[i]);
            if (vm == null) continue;
            int current = PlayersVm.Types.IndexOf(vm);
            if (current != i && current >= 0)
                PlayersVm.Types.Move(current, i);
        }

        if (selected != null)
            PlayersVm.SelectedType = selected;

        PlayersVm.IsDirty = true;
    }

    private void SyncCardsTabOrder()
    {
        var orderedDigits = PlayersVm.Types.Select(t => t.Config.TypeDigit).ToList();
        var tabsByDigit = CardsVm.TypeTabs.ToDictionary(t => t.TypeDigit);
        var selected = CardsVm.SelectedTab;

        // Reorder in-place using Move to avoid clearing SelectedTab
        for (int i = 0; i < orderedDigits.Count; i++)
        {
            if (!tabsByDigit.TryGetValue(orderedDigits[i], out var tab)) continue;
            int current = CardsVm.TypeTabs.IndexOf(tab);
            if (current != i && current >= 0)
                CardsVm.TypeTabs.Move(current, i);
        }

        // Restore selection explicitly (Move can cause it to reset)
        if (selected != null)
            CardsVm.SelectedTab = selected;

        CardsVm.IsDirty = true;
    }

    public bool AnyDirty => CardsVm.IsDirty || PlayersVm.IsDirty ||
                             RemoteSetupVm.IsDirty || HardwareSetupVm.IsDirty ||
                             PrintGeneratorVm.IsDirty;

    public MainWindowViewModel()
    {
        // Propagate child dirty changes to AnyDirty
        CardsVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CardsVm.IsDirty)) OnPropertyChanged(nameof(AnyDirty)); };
        PlayersVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PlayersVm.IsDirty)) OnPropertyChanged(nameof(AnyDirty)); };
        RemoteSetupVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RemoteSetupVm.IsDirty)) OnPropertyChanged(nameof(AnyDirty)); };
        HardwareSetupVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(HardwareSetupVm.IsDirty)) OnPropertyChanged(nameof(AnyDirty)); };
        PrintGeneratorVm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PrintGeneratorVm.IsDirty)) OnPropertyChanged(nameof(AnyDirty)); };

        // Forward all child VM status messages to the main status bar (most recent wins)
        void fwd(bool isErr, string msg) { StatusIsError = isErr; StatusMessage = msg; }
        CardsVm.OnStatus = fwd;
        PlayersVm.OnStatus = fwd;
        RemoteSetupVm.OnStatus = fwd;
        HardwareSetupVm.OnStatus = fwd;
        PrintGeneratorVm.OnStatus = fwd;
        // Confirm before deleting type with entries
        // Always move entries to Unassigned when deleting a type (entries are never lost)
        CardsVm.ConfirmDeleteWithEntries = _ => false;

        // Sync new Program type → Media Setup immediately
        PlayersVm.TypeAdded += vm =>
        {
            if (CardsVm.TypeTabs.Any(t => t.TypeDigit == vm.Config.TypeDigit)) return;
            var newTab = new ViewModels.PlayerTypeTabViewModel(vm.Config.TypeDigit, vm.Config.PlayerType,
                System.Linq.Enumerable.Empty<Models.MediaEntry>());
            newTab.SetParent(CardsVm);
            int unassignedIdx = CardsVm.TypeTabs.IndexOf(CardsVm.UnassignedTab!);
            if (unassignedIdx >= 0) CardsVm.TypeTabs.Insert(unassignedIdx, newTab);
            else CardsVm.TypeTabs.Add(newTab);
            CardsVm.RefreshAvailableTypes();
            CardsVm.SelectedTab = newTab;
        };

        // Sync new Media type → Program Setup immediately
        CardsVm.TypeAdded += tab =>
        {
            if (PlayersVm.Types.Any(t => t.Config.TypeDigit == tab.TypeDigit)) return;
            var config = new Config.ProgramConfig { TypeDigit = tab.TypeDigit, PlayerType = "New Type" };
            var vm = new ViewModels.PlayerTypeViewModel(config);
            vm.RebuildMappingGrid(RemoteSetupVm.SelectedProfile?.Profile);
            PlayersVm.WireVmDirtyPublic(vm);
            PlayersVm.Types.Add(vm);
        };

        CardsVm.TabOrderChanged += SyncPlayersTabOrder;
        PlayersVm.TabOrderChanged += SyncCardsTabOrder;
        PlayersVm.TypeDeleted += digit =>
        {
            // Remove matching tab from Media Setup
            var tab = CardsVm.TypeTabs.FirstOrDefault(t => t.TypeDigit == digit);
            if (tab != null) CardsVm.TypeTabs.Remove(tab);
            CardsVm.RefreshAvailableTypes();
        };
        CardsVm.TypeDeleted += digit =>
        {
            // Remove matching type from Program Setup
            var vm = PlayersVm.Types.FirstOrDefault(t => t.Config.TypeDigit == digit);
            if (vm != null) PlayersVm.Types.Remove(vm);
            CardsVm.RefreshAvailableTypes();
        };

        // Keep PassthroughPlayers in sync with the program types list.
        // PlayersVm.Types is replaced wholesale on each LoadFromFile, so we
        // re-subscribe CollectionChanged every time the Types property changes.
        void WireTypesCollectionChanged()
        {
            PlayersVm.Types.CollectionChanged += (_, _) => RefreshPassthroughPlayers();
        }
        PlayersVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PlayersVm.Types))
            {
                WireTypesCollectionChanged();
                RefreshPassthroughPlayers();
            }
        };
        WireTypesCollectionChanged();

        // Also refresh when a media type is renamed
        PlayersVm.PlayerTypeRenamed += (_, _) => RefreshPassthroughPlayers();

        // Link sub-tab selection between Media Setup and Media Type Setup
        CardsVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CardsVm.SelectedTab)) return;
            if (CardsVm.SelectedTab?.TypeDigit == "?") return;
            var match = PlayersVm.Types.FirstOrDefault(t =>
                t.Config.TypeDigit == CardsVm.SelectedTab?.TypeDigit);
            if (match != null && PlayersVm.SelectedType != match)
                PlayersVm.SelectedType = match;
        };
        PlayersVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(PlayersVm.SelectedType)) return;
            var match = CardsVm.TypeTabs.FirstOrDefault(t =>
                t.TypeDigit == PlayersVm.SelectedType?.Config.TypeDigit);
            if (match != null && CardsVm.SelectedTab != match)
                CardsVm.SelectedTab = match;
        };
        LoadFolder();
    }


    private void RefreshPassthroughPlayers()
    {
        var newList = PlayersVm.Types.Select(t => t.Config.PlayerType).ToList();

        // Update in-place so the ComboBox ItemsSource binding never changes reference.
        // Replacing with a new collection causes Avalonia to re-bind SelectedItem before
        // the new items are committed, dropping the selection.
        for (int i = 0; i < newList.Count; i++)
        {
            if (i < PassthroughPlayers.Count) PassthroughPlayers[i] = newList[i];
            else PassthroughPlayers.Add(newList[i]);
        }
        while (PassthroughPlayers.Count > newList.Count)
            PassthroughPlayers.RemoveAt(PassthroughPlayers.Count - 1);

        // Resolve by digit first (survives renames), fall back to current name, then first
        var byDigit = !string.IsNullOrEmpty(_selectedPassthroughTypeDigit)
            ? PlayersVm.Types.FirstOrDefault(t => t.Config.TypeDigit == _selectedPassthroughTypeDigit)?.Config.PlayerType
            : null;
        var prev = SelectedPassthroughPlayerType;
        SelectedPassthroughPlayerType =
            byDigit != null && newList.Contains(byDigit) ? byDigit :
            prev != null && newList.Contains(prev) ? prev :
            newList.FirstOrDefault();
    }

    private void LoadFolder()
    {
        if (!Directory.Exists(AppPaths.DataDir)) { StatusIsError = true; StatusMessage = "Data folder not found."; return; }

        // Suppress passthrough saves for the entire load — RefreshPassthroughPlayers fires
        // CollectionChanged callbacks that would otherwise overwrite Hardware.json before
        // LoadPassthroughState has a chance to read it.
        _suppressPassthroughSave = true;
        try
        {
            // Remember current media tab so discard doesn't jump to first tab
            CardsVm.SaveSelectedTab();

            string dataDir = AppPaths.DataDir;
            string playersJson = Path.Combine(dataDir, "Players.json");
            string remotesJson = Path.Combine(dataDir, "RemoteProfiles.json");
            string cardsPath = Path.Combine(dataDir, "Cards.csv");
            string hardwarePath = Path.Combine(dataDir, "Hardware.json");

            EnsureCsv(cardsPath, "Type_Digit,Title,QR_Code,Path,Art_Path,Art_Fit,State");

            RemoteSetupVm.LoadFromFile(remotesJson);
            RemoteSetupVm.ProfileSaved += () =>
                PlayersVm.RefreshMappingGrids(RemoteSetupVm.SelectedProfile?.Profile);

            // Switching profiles marks dirty and refreshes the mapping grid immediately
            RemoteSetupVm.SelectedProfileChanged += profile =>
            {
                RemoteSetupVm.IsDirty = true;
                PlayersVm.RefreshMappingGrids(profile?.Profile);
            };

            // Remote profile added → create an empty PerProfileMappings entry in every media type
            RemoteSetupVm.ProfileAdded += profileName =>
            {
                foreach (var type in PlayersVm.Types)
                    type.Config.GetOrCreateProfileMappings(profileName);
                PlayersVm.IsDirty = true;
            };

            // Remote profile renamed → rename the key in every media type's ProfileMappings
            RemoteSetupVm.ProfileRenamed += (oldName, newName) =>
            {
                foreach (var type in PlayersVm.Types)
                    type.Config.RenameProfileMappings(oldName, newName);
                PlayersVm.IsDirty = true;
            };

            // Remote grid resized → refresh mapping grid immediately (not just on save)
            RemoteSetupVm.GridResized += () =>
                PlayersVm.RefreshMappingGrids(RemoteSetupVm.SelectedProfile?.Profile);

            var activeProfile = RemoteSetupVm.SelectedProfile?.Profile;
            PlayersVm.LoadFromFile(playersJson, activeProfile);
            // Unsub before re-subbing so multiple LoadFolder calls don't stack handlers
            if (_cardsRenameHandler != null) PlayersVm.PlayerTypeRenamed -= _cardsRenameHandler;
            _cardsRenameHandler = (digit, nw) =>
            {
                CardsVm.RenameType(digit, nw);
                CardsVm.RefreshAvailableTypes();
            };
            PlayersVm.PlayerTypeRenamed += _cardsRenameHandler;

            CardsVm.LoadFromFile(cardsPath,
                PlayersVm.GetProgramConfigs()
                    .Select(p => new Models.ProgramEntry
                    {
                        TypeDigit = p.TypeDigit,
                        PlayerType = p.PlayerType
                    }));

            CardsVm.RestoreSelectedTab();

            PrintGeneratorVm.RefreshEntries(
                CardsVm.TypeTabs.SelectMany(t => t.Entries));

            PrintGeneratorVm.SetOnGeneratedCallback(() =>
            {
                foreach (var tab in CardsVm.TypeTabs)
                    tab.RevalidateAll();
                CardsVm.IsDirty = true;
            });

            HardwareSetupVm.LoadFromFile(hardwarePath);
            IsLoaded = true;
            StatusIsError = false; StatusMessage = $"Loaded from: {dataDir}";
            RefreshPassthroughPlayers();
            LoadPassthroughState();
        } // end try
        finally { _suppressPassthroughSave = false; }
    }

    [RelayCommand]
    private void SaveAll()
    {
        PlayersVm.SaveCommand.Execute(null);
        RemoteSetupVm.SaveCommand.Execute(null);
        CardsVm.SaveCommand.Execute(null);
        HardwareSetupVm.SaveCommand.Execute(null);
        PrintGeneratorVm.SaveCommand.Execute(null);
        PrintGeneratorVm.RefreshEntries(CardsVm.TypeTabs.SelectMany(t => t.Entries));
        StatusIsError = false; StatusMessage = "All files saved.";
        ChangesSaved?.Invoke();
    }

    [RelayCommand]
    private void DiscardAll()
    {
        LoadFolder();
        PrintGeneratorVm.LoadProfiles();
        StatusIsError = false; StatusMessage = "Changes discarded — reloaded from disk.";
    }

    private static void EnsureCsv(string path, string header)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, header + Environment.NewLine, System.Text.Encoding.UTF8);
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desk)
            return desk.MainWindow != null ? TopLevel.GetTopLevel(desk.MainWindow) : null;
        return null;
    }
}