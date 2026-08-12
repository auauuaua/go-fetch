using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CardPlayer.Models;
using CardPlayer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

public partial class PlayerTypeTabViewModel : ViewModelBase
{
    public string TypeDigit { get; }
    public bool IsUnassigned => TypeDigit == "?";
    [ObservableProperty] private string _tabHeader = "";
    [ObservableProperty] private ObservableCollection<MediaEntry> _entries = new();
    [ObservableProperty] private ObservableCollection<MediaEntry> _selectedEntries = new();
    [ObservableProperty] private MediaEntry? _selected;

    public MediaScanViewModel ScanVm { get; }
    public Action<bool, string>? OnStatus;

    // Set true while PullTextFromEntry is running to suppress dirty marking
    internal bool SuppressDirty;

    public PlayerTypeTabViewModel(string typeDigit, string mediaType, IEnumerable<MediaEntry> entries)
    {
        TypeDigit = typeDigit;
        _tabHeader = string.IsNullOrWhiteSpace(mediaType) ? $"Type {typeDigit}" : mediaType;
        ScanVm = new MediaScanViewModel(this);
        ScanVm.OnStatus = (isErr, msg) => OnStatus?.Invoke(isErr, msg);
        var list = entries.ToList();
        Entries = new ObservableCollection<MediaEntry>(list);
        Entries.CollectionChanged += (_, _) => { RevalidateAll(); RefreshSortedEntries(); SelectFirstIfNone(); };
        foreach (var e in list) WireEntry(e);
        RevalidateAll();
        RefreshSortedEntries();
        SelectFirstIfNone();
    }

    /// <summary>Selects the first sorted entry if nothing is currently selected.</summary>
    public void SelectFirstIfNone()
    {
        if (Selected == null && SortedEntries.Count > 0)
            Selected = SortedEntries[0];
    }

    /// <summary>Add an entry directly without triggering add command flow.</summary>
    public void AddEntryDirect(MediaEntry e) { WireEntry(e); Entries.Add(e); }

    /// <summary>Raised when ArtPath, ArtFit, or QrCode of the selected entry changes (for card preview).</summary>
    public event Action? SelectedEntryPreviewChanged;

    private void WireEntry(MediaEntry e)
    {
        e.PropertyChanged += (_, args) =>
        {
            // QR code change: update status
            if (args.PropertyName == nameof(MediaEntry.State) ||
                args.PropertyName == nameof(MediaEntry.Path) ||
                args.PropertyName == nameof(MediaEntry.ArtPath))
            {
                UpdateSingleStatus(e);
            }
            // Any field change on the entry marks the tab dirty (but not during pull)
            if (args.PropertyName != nameof(MediaEntry.ComputedStatus) && !SuppressDirty)
                _parentVm?.SetDirty();
            // Notify parent CardsViewModel to refresh preview when preview-relevant fields change
            if (e == Selected && (
                args.PropertyName == nameof(MediaEntry.ArtPath) ||
                args.PropertyName == nameof(MediaEntry.ArtFit) ||
                args.PropertyName == nameof(MediaEntry.ArtBackPath) ||
                args.PropertyName == nameof(MediaEntry.QrCode) ||
                args.PropertyName == nameof(MediaEntry.DisplayText) ||
                args.PropertyName == nameof(MediaEntry.FrontBgColor) ||
                args.PropertyName == nameof(MediaEntry.BackBgColor)))
            {
                SelectedEntryPreviewChanged?.Invoke();
            }
        };
    }

    /// <summary>Called from code-behind on cell edit end to revalidate QR duplicates.</summary>
    public void OnCellEditEnded()
    {
        RevalidateAll();
        _parentVm?.TypeTabs
            .Where(t => t != this)
            .ToList()
            .ForEach(t => t.RevalidateAll());
    }

    public void SetDirty() => _parentVm?.SetDirty();

    /// <summary>Updates ComputedStatus for a single entry without rebuilding SortedEntries.</summary>
    private void UpdateSingleStatus(MediaEntry e)
    {
        e.ComputedStatus = ComputeStatus(e);
    }

    private CardsViewModel? _parentVm;
    internal void SetParent(CardsViewModel vm)
    {
        _parentVm = vm;
        ScanVm.LayoutProfiles = vm.LayoutProfiles;
        OnStatus = (isErr, msg) => vm.OnStatus?.Invoke(isErr, msg);
    }

    public void RevalidateAll()
    {
        foreach (var entry in Entries)
            entry.ComputedStatus = ComputeStatus(entry);
        RefreshSortedEntries();
    }

    private static string ComputeStatus(MediaEntry entry)
    {
        if (entry.State == "generated") return "Generated";
        if (entry.State == "generated-back-only") return "Generated — back only";
        if (entry.State == "skip") return "Skip";
        // state == "new"
        if (string.IsNullOrWhiteSpace(entry.Path)) return "Not Ready — no path";
        if (string.IsNullOrWhiteSpace(entry.QrCode)) return "Not Ready — no QR code";
        if (string.IsNullOrWhiteSpace(entry.ArtPath)) return "Ready — no art";
        return "Ready";
    }

    public void UpdateHeader(string newName) =>
        TabHeader = string.IsNullOrWhiteSpace(newName) ? $"Type {TypeDigit}" : newName;

    // ── Commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddRow()
    {
        var e = new MediaEntry { TypeDigit = TypeDigit, State = "new" };
        WireEntry(e);
        Entries.Add(e);
        Selected = e;
        RevalidateAll();
        RefreshSortedEntries();
    }

    [RelayCommand]
    private void DeleteRow()
    {
        var toDelete = SelectedEntries.Any()
            ? SelectedEntries.ToList()
            : Selected != null ? new List<MediaEntry> { Selected } : new List<MediaEntry>();
        foreach (var e in toDelete) Entries.Remove(e);
        SelectedEntries.Clear();
        Selected = Entries.LastOrDefault();
        RevalidateAll();
        RefreshSortedEntries();
    }

    [RelayCommand]
    private void UnassignSelected()
    {
        var targets = SelectedEntries.Any()
            ? SelectedEntries.ToList()
            : Selected != null ? new List<MediaEntry> { Selected } : new List<MediaEntry>();
        if (!targets.Any()) return;

        var unassigned = _parentVm?.UnassignedTab;
        if (unassigned == null || unassigned == this) return;

        foreach (var e in targets)
        {
            Entries.Remove(e);
            e.TypeDigit = "?";
            unassigned.AddEntryDirect(e);
        }
        SelectedEntries.Clear();
        RevalidateAll();
        unassigned.RevalidateAll();
        _parentVm?.SetDirty();
    }

    [RelayCommand]
    private void SetArtFit(string value)
    {
        var targets = SelectedEntries.Any()
            ? SelectedEntries.ToList()
            : Selected != null ? new List<MediaEntry> { Selected } : new List<MediaEntry>();
        foreach (var e in targets) e.ArtFit = value;
        _parentVm?.SetDirty();
    }

    [RelayCommand]
    private void SetNew()
    {
        var targets = SelectedEntries.Any()
            ? SelectedEntries.ToList()
            : Selected != null ? new List<MediaEntry> { Selected } : new List<MediaEntry>();
        foreach (var e in targets) e.State = "new";
        RevalidateAll();
        _parentVm?.SetDirty();
    }

    [RelayCommand]
    private void SetSkip()
    {
        var targets = SelectedEntries.Any()
            ? SelectedEntries.ToList()
            : Selected != null ? new List<MediaEntry> { Selected } : new List<MediaEntry>();
        foreach (var e in targets) e.State = "skip";
        RevalidateAll();
        _parentVm?.SetDirty();
    }

    // ── Sort (display only — does not affect save order) ─────────────────
    [ObservableProperty] private ObservableCollection<MediaEntry> _sortedEntries = new();

    private bool _sortAscending = true;
    private string _lastSortKey = "";

    public bool SortAscending => _sortAscending;

    [RelayCommand] private void SortByDisplayText() => SortBy("DisplayText", e => e.DisplayText);
    [RelayCommand] private void SortByQrCode() => SortBy("QrCode", e => e.QrCode);
    [RelayCommand] private void SortByStatus() => SortBy("Status", e => e.ComputedStatus);
    [RelayCommand] private void ClearSort() { _lastSortKey = ""; _sortAscending = true; RefreshSortedEntries(); }

    private void SortBy(string key, Func<MediaEntry, string> selector)
    {
        if (_lastSortKey == key) _sortAscending = !_sortAscending;
        else { _sortAscending = true; _lastSortKey = key; }
        RefreshSortedEntries();
    }

    public void RefreshSortedEntries()
    {
        IEnumerable<MediaEntry> view = Entries;
        if (!string.IsNullOrEmpty(_lastSortKey))
        {
            Func<MediaEntry, string> sel = _lastSortKey switch
            {
                "DisplayText" => e => e.DisplayText,
                "QrCode" => e => e.QrCode,
                "Status" => e => e.ComputedStatus,
                _ => e => e.DisplayText
            };
            view = _sortAscending ? Entries.OrderBy(sel) : Entries.OrderByDescending(sel);
        }
        SortedEntries = new ObservableCollection<MediaEntry>(view);
    }
}

public partial class CardsViewModel : ViewModelBase
{
    private string _cardsPath = "";

    [ObservableProperty] private ObservableCollection<PlayerTypeTabViewModel> _typeTabs = new();
    [ObservableProperty] private PlayerTypeTabViewModel? _selectedTab;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _statusMessage = "";
    /// <summary>Forwarded to main window status bar. Set by MainWindowViewModel.</summary>
    public Action<bool, string>? OnStatus;
    [ObservableProperty] private bool _isDirty;

    // ── Card preview ──────────────────────────────────────────────────────
    public CardPreviewViewModel Preview { get; } = new();

    // ── Card layout profiles ──────────────────────────────────────────────
    public CardLayoutProfilesViewModel LayoutProfiles { get; }

    // ── Go Fetch: simulate card insertion for selected entry ──────────────
    /// <summary>Set by App.axaml.cs to route to SerialListenerService.SimulateQrCode.</summary>
    public Action<string>? SimulateQrCodeAction { get; set; }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void GoFetch()
    {
        var qr = SelectedTab?.Selected?.QrCode;
        if (!string.IsNullOrWhiteSpace(qr))
            SimulateQrCodeAction?.Invoke(qr);
    }

    // ── Live text options (mirror selected entry, mark dirty on change) ───
    // These are NOT [ObservableProperty] — we set them manually so we can
    // differentiate user edits from programmatic loads (which should not dirty).
    private string _textSide = "front";
    private string _textFont = "Arial";
    private string _textStyle = "Normal";
    private int _textSize = 36;
    private string _textColor = "#000000";

    public string TextSide { get => _textSide; set { if (_textSide == value) return; _textSide = value; OnPropertyChanged(); PushTextToSelected(); MarkDirtyAndNotifyProfile(); Preview.Update(SelectedTab?.Selected); } }
    public string TextFont { get => _textFont; set { if (_textFont == value) return; _textFont = value; OnPropertyChanged(); PushTextToSelected(); MarkDirtyAndNotifyProfile(); Preview.Update(SelectedTab?.Selected); } }
    public string TextStyle { get => _textStyle; set { if (_textStyle == value) return; _textStyle = value; OnPropertyChanged(); PushTextToSelected(); MarkDirtyAndNotifyProfile(); Preview.Update(SelectedTab?.Selected); } }
    public int TextSize { get => _textSize; set { if (_textSize == value) return; _textSize = value; OnPropertyChanged(); PushTextToSelected(); MarkDirtyAndNotifyProfile(); Preview.Update(SelectedTab?.Selected); } }
    public string TextColor { get => _textColor; set { if (_textColor == value) return; _textColor = value; OnPropertyChanged(); PushTextToSelected(); MarkDirtyAndNotifyProfile(); Preview.Update(SelectedTab?.Selected); } }

    public bool TextOnFront { get => TextSide == "front"; set { if (value) TextSide = "front"; OnPropertyChanged(); OnPropertyChanged(nameof(TextOnBack)); } }
    public bool TextOnBack { get => TextSide == "back"; set { if (value) TextSide = "back"; OnPropertyChanged(); OnPropertyChanged(nameof(TextOnFront)); } }

    // ── Live Art Fit (mirrors selected entry, triggers preview) ───────────
    private string _selectedArtFit = "";
    public string SelectedArtFit
    {
        get => _selectedArtFit;
        set
        {
            if (_selectedArtFit == value) return;
            _selectedArtFit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ArtFitIsFill));
            OnPropertyChanged(nameof(ArtFitIsFit));
            OnPropertyChanged(nameof(ArtFitIsSquareFill));
            OnPropertyChanged(nameof(ArtFitIsSquareFit));
            var tab = SelectedTab;
            if (tab == null) return;
            var targets = tab.SelectedEntries.Any()
                ? tab.SelectedEntries.ToList()
                : tab.Selected != null ? new System.Collections.Generic.List<MediaEntry> { tab.Selected } : new System.Collections.Generic.List<MediaEntry>();
            foreach (var e in targets) e.ArtFit = value;
            IsDirty = true;
            Preview.Update(SelectedTab?.Selected);
            LayoutProfiles.OnLiveSettingsChanged();
        }
    }
    public bool ArtFitIsFill { get => _selectedArtFit == "fill"; set { if (value) SelectedArtFit = "fill"; } }
    public bool ArtFitIsFit { get => _selectedArtFit == "fit"; set { if (value) SelectedArtFit = "fit"; } }
    public bool ArtFitIsSquareFill { get => _selectedArtFit == "square fill"; set { if (value) SelectedArtFit = "square fill"; } }
    public bool ArtFitIsSquareFit { get => _selectedArtFit == "square fit"; set { if (value) SelectedArtFit = "square fit"; } }

    // ── Live background colors ─────────────────────────────────────────────
    private string _frontBgColor = "#FFFFFF";
    private string _backBgColor = "#FFFFFF";

    public string FrontBgColor
    {
        get => _frontBgColor;
        set { if (_frontBgColor == value) return; _frontBgColor = value; OnPropertyChanged(); PushTextToSelected(); IsDirty = true; Preview.Update(SelectedTab?.Selected); LayoutProfiles.OnLiveSettingsChanged(); }
    }
    public string BackBgColor
    {
        get => _backBgColor;
        set { if (_backBgColor == value) return; _backBgColor = value; OnPropertyChanged(); PushTextToSelected(); IsDirty = true; Preview.Update(SelectedTab?.Selected); LayoutProfiles.OnLiveSettingsChanged(); }
    }

    public static string[] FontStyles { get; } = { "Normal", "Bold", "Italic", "Bold Italic" };

    private bool _suppressTextSync;

    // Font picker (mirrors PrintGeneratorViewModel pattern)
    private System.Collections.Generic.List<string>? _installedFonts;
    public System.Collections.Generic.List<string> InstalledFonts =>
        _installedFonts ??= SkiaSharp.SKFontManager.Default.FontFamilies.OrderBy(f => f).ToList();

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool _showFontPicker = false;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private string _fontSearch = "";

    public string? SelectedFontItem
    {
        get => TextFont;
        set { if (value != null) { TextFont = value; ShowFontPicker = false; FontSearch = ""; } OnPropertyChanged(); }
    }

    public System.Collections.Generic.List<string> FilteredFonts =>
        string.IsNullOrWhiteSpace(FontSearch)
            ? InstalledFonts
            : InstalledFonts.Where(f => f.Contains(FontSearch, StringComparison.OrdinalIgnoreCase)).ToList();

    partial void OnFontSearchChanged(string v) => OnPropertyChanged(nameof(FilteredFonts));

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void BrowseFont() => ShowFontPicker = !ShowFontPicker;

    // Push live text fields into the currently selected entry
    private void PushTextToSelected()
    {
        if (_suppressTextSync) return;
        var entry = SelectedTab?.Selected;
        if (entry == null) return;
        _suppressTextSync = true;
        entry.TextSide = _textSide;
        entry.TextFont = _textFont;
        entry.TextStyle = _textStyle;
        entry.TextSize = _textSize;
        entry.TextColor = _textColor;
        entry.FrontBgColor = _frontBgColor;
        entry.BackBgColor = _backBgColor;
        _suppressTextSync = false;
    }

    // Pull text fields from entry into live properties (no dirty, no push-back)
    private void PullTextFromEntry(Models.MediaEntry? entry)
    {
        // Normalize blank values on the entry itself first, under suppression,
        // so that when bindings round-trip and push back, the values are identical
        // and WireEntry sees no change — preventing spurious dirty.
        if (entry != null)
        {
            var tab = SelectedTab;
            if (tab != null) tab.SuppressDirty = true;
            if (string.IsNullOrWhiteSpace(entry.TextSide)) entry.TextSide = "front";
            if (string.IsNullOrWhiteSpace(entry.TextStyle)) entry.TextStyle = "Normal";
            if (entry.TextFont == null) entry.TextFont = "";
            if (string.IsNullOrWhiteSpace(entry.TextColor)) entry.TextColor = "#000000";
            if (string.IsNullOrWhiteSpace(entry.FrontBgColor)) entry.FrontBgColor = "#FFFFFF";
            if (string.IsNullOrWhiteSpace(entry.BackBgColor)) entry.BackBgColor = "#FFFFFF";
            if (entry.TextSize <= 0) entry.TextSize = 36;
            if (tab != null) tab.SuppressDirty = false;
        }

        var tabForPull = SelectedTab;
        if (tabForPull != null) tabForPull.SuppressDirty = true;
        _suppressTextSync = true;
        _textSide = entry?.TextSide ?? "front";
        _textFont = entry?.TextFont ?? "";
        _textStyle = entry?.TextStyle ?? "Normal";
        _textSize = entry?.TextSize ?? 36;
        _textColor = entry?.TextColor ?? "#000000";
        _selectedArtFit = entry?.ArtFit ?? "";
        _frontBgColor = entry?.FrontBgColor ?? "#FFFFFF";
        _backBgColor = entry?.BackBgColor ?? "#FFFFFF";
        OnPropertyChanged(nameof(TextSide));
        OnPropertyChanged(nameof(TextFont));
        OnPropertyChanged(nameof(TextStyle));
        OnPropertyChanged(nameof(TextSize));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(TextOnFront));
        OnPropertyChanged(nameof(TextOnBack));
        OnPropertyChanged(nameof(SelectedFontItem));
        OnPropertyChanged(nameof(SelectedArtFit));
        OnPropertyChanged(nameof(ArtFitIsFill));
        OnPropertyChanged(nameof(ArtFitIsFit));
        OnPropertyChanged(nameof(ArtFitIsSquareFill));
        OnPropertyChanged(nameof(ArtFitIsSquareFit));
        OnPropertyChanged(nameof(FrontBgColor));
        OnPropertyChanged(nameof(BackBgColor));
        _suppressTextSync = false;
        // Keep SuppressDirty true briefly longer to catch async binding round-trips
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () => { if (tabForPull != null) tabForPull.SuppressDirty = false; },
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void MarkDirtyAndNotifyProfile()
    {
        IsDirty = true;
        LayoutProfiles.OnLiveSettingsChanged();
    }

    public CardsViewModel()
    {
        LayoutProfiles = new CardLayoutProfilesViewModel
        {
            SnapshotLiveSettings = () => new Config.CardLayoutProfile
            {
                TextSide = _textSide,
                TextFont = _textFont,
                TextStyle = _textStyle,
                TextSize = _textSize,
                TextColor = _textColor,
                ArtFit = _selectedArtFit,
                FrontBgColor = _frontBgColor,
                BackBgColor = _backBgColor,
            }
        };
        LayoutProfiles.ProfileApplyRequested += ApplyLayoutProfile;
        LayoutProfiles.Load();
    }

    private void ApplyLayoutProfile(Config.CardLayoutProfile p)
    {
        // Apply to all selected entries (or all entries in tab if none selected)
        var tab = SelectedTab;
        if (tab == null) return;
        var targets = tab.SelectedEntries.Any()
            ? tab.SelectedEntries.ToList()
            : tab.Entries.ToList();
        foreach (var e in targets)
            e.ApplyLayoutProfile(p);
        // Pull into live fields from selected entry, then refresh preview
        PullTextFromEntry(tab.Selected);
        Preview.Update(tab.Selected);
        IsDirty = true;
    }

    // Wire preview updates when the active tab changes
    partial void OnSelectedTabChanged(PlayerTypeTabViewModel? oldValue, PlayerTypeTabViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnTabPropertyChanged;
            oldValue.SelectedEntryPreviewChanged -= OnEntryPreviewChanged;
        }
        if (newValue != null)
        {
            newValue.PropertyChanged += OnTabPropertyChanged;
            newValue.SelectedEntryPreviewChanged += OnEntryPreviewChanged;
        }
        Preview.Update(newValue?.Selected);
        PullTextFromEntry(newValue?.Selected);
        newValue?.SelectFirstIfNone();
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerTypeTabViewModel.Selected))
        {
            var entry = ((PlayerTypeTabViewModel)sender!).Selected;
            Preview.Update(entry);
            PullTextFromEntry(entry);
            LayoutProfiles.OnLiveSettingsChanged();
        }
    }

    private void OnEntryPreviewChanged()
        => Preview.Update(SelectedTab?.Selected);

    // Unassigned tab — always last, never deleted
    public PlayerTypeTabViewModel? UnassignedTab => TypeTabs.FirstOrDefault(t => t.TypeDigit == "?");

    // For the assign dropdown in the unassigned tab
    [ObservableProperty] private ObservableCollection<PlayerTypeTabViewModel> _availableTypeTabs = new();
    [ObservableProperty] private PlayerTypeTabViewModel? _selectedAssignTab;
    // SelectedAssignTab replaces SelectedAssignDigit

    [RelayCommand]
    private void AssignSelected()
    {
        var unassigned = UnassignedTab;
        if (unassigned == null || SelectedAssignTab == null) return;

        var toMove = unassigned.SelectedEntries.Any()
            ? unassigned.SelectedEntries.ToList()
            : unassigned.Selected != null ? new List<Models.MediaEntry> { unassigned.Selected } : new List<Models.MediaEntry>();
        if (!toMove.Any()) return;

        var targetTab = SelectedAssignTab;
        if (targetTab == null) return;

        foreach (var entry in toMove)
        {
            entry.TypeDigit = targetTab.TypeDigit;
            unassigned.Entries.Remove(entry);
            targetTab.AddEntryDirect(entry);
        }
        unassigned.SelectedEntries.Clear();
        unassigned.RevalidateAll();
        targetTab.RevalidateAll();
        IsDirty = true;
    }

    public void LoadFromFile(string cardsPath, IEnumerable<ProgramEntry> programTypes)
    {
        _cardsPath = cardsPath;
        var allMedia = CsvService.LoadMedia(cardsPath);

        var tabs = programTypes.Select(p =>
        {
            var mine = allMedia.Where(m => m.TypeDigit == p.TypeDigit).ToList();
            var tab = new PlayerTypeTabViewModel(p.TypeDigit, p.PlayerType, mine);
            tab.Entries.CollectionChanged += (_, _) => IsDirty = true;
            // Don't subscribe to every PropertyChanged — causes focus loss on keystroke
            return tab;
        }).ToList();

        var knownTypes = programTypes.Select(p => p.TypeDigit).ToHashSet();
        var unassignedEntries = allMedia.Where(m => !knownTypes.Contains(m.TypeDigit)).ToList();
        // Always add unassigned tab (even if empty) so it persists
        var unassignedTab = new PlayerTypeTabViewModel("?", "Unassigned", unassignedEntries);
        unassignedTab.Entries.CollectionChanged += (_, _) => IsDirty = true;
        tabs.Add(unassignedTab);

        TypeTabs = new ObservableCollection<PlayerTypeTabViewModel>(tabs);

        // Populate assign dropdown (name only, no digit prefix)
        RefreshAvailableTypes();
        SelectedAssignTab = AvailableTypeTabs.FirstOrDefault();

        // Set parent reference so RevalidateAll can check duplicates across tabs
        foreach (var t in tabs) t.SetParent(this);

        // Cross-tab QR revalidation is handled inside WireEntry's debounce timer

        SelectedTab = TypeTabs.FirstOrDefault(t => t.TypeDigit != "?");
        IsDirty = false;
        StatusIsError = false; StatusMessage = $"Loaded {allMedia.Count} media entries.";
        OnStatus?.Invoke(false, $"[Cards] {StatusMessage}");
    }

    public void RefreshAvailableTypes()
    {
        var prevDigit = SelectedAssignTab?.TypeDigit;
        var tabs = TypeTabs.Where(t => t.TypeDigit != "?").ToList();
        AvailableTypeTabs = new ObservableCollection<PlayerTypeTabViewModel>(tabs);
        SelectedAssignTab = tabs.FirstOrDefault(t => t.TypeDigit == prevDigit)
                         ?? tabs.FirstOrDefault();
    }

    public void SetDirty() => IsDirty = true;

    public void RenameType(string typeDigit, string newName)
    {
        var tab = TypeTabs.FirstOrDefault(t => t.TypeDigit == typeDigit);
        tab?.UpdateHeader(newName);
    }

    // Called before reload to remember which tab was active — keyed by TypeDigit (stable)
    private string? _savedTabDigit;
    private string? _savedEntryKey;
    public void SaveSelectedTab()
    {
        _savedTabDigit = SelectedTab?.TypeDigit;
        var e = SelectedTab?.Selected;
        _savedEntryKey = !string.IsNullOrWhiteSpace(e?.QrCode) ? e!.QrCode : e?.DisplayText;
    }
    public void RestoreSelectedTab()
    {
        if (_savedTabDigit == null) return;
        var tab = TypeTabs.FirstOrDefault(t => t.TypeDigit == _savedTabDigit);
        if (tab != null)
        {
            SelectedTab = tab;
            if (_savedEntryKey != null)
            {
                var entry = tab.Entries.FirstOrDefault(e =>
                    e.QrCode == _savedEntryKey || e.DisplayText == _savedEntryKey);
                if (entry != null) tab.Selected = entry;
            }
        }
        _savedTabDigit = null;
        _savedEntryKey = null;
    }

    public event Action<PlayerTypeTabViewModel>? TypeAdded;
    public event Action<string>? TypeDeleted;
    public Func<string, bool>? ConfirmDeleteWithEntries;

    [RelayCommand]
    private void AddType()
    {
        // Generate a unique TypeDigit
        int next = 1;
        var existing = TypeTabs.Select(t => int.TryParse(t.TypeDigit, out int n) ? n : 0).ToHashSet();
        while (existing.Contains(next)) next++;

        var tab = new PlayerTypeTabViewModel(next.ToString(), "New Type", System.Linq.Enumerable.Empty<Models.MediaEntry>());
        tab.SetParent(this);
        tab.Entries.CollectionChanged += (_, _) => IsDirty = true;

        // Insert before Unassigned tab
        var unassignedIdx = TypeTabs.IndexOf(TypeTabs.FirstOrDefault(t => t.TypeDigit == "?")!);
        if (unassignedIdx >= 0)
            TypeTabs.Insert(unassignedIdx, tab);
        else
            TypeTabs.Add(tab);

        SelectedTab = tab;
        IsDirty = true;
        TypeAdded?.Invoke(tab);
    }

    [RelayCommand]
    private void DeleteType()
    {
        if (SelectedTab == null || SelectedTab.TypeDigit == "?") return;
        string digit = SelectedTab.TypeDigit;
        bool hasEntries = SelectedTab.Entries.Any();
        bool deleteEntries = true;

        if (hasEntries && ConfirmDeleteWithEntries != null)
            deleteEntries = ConfirmDeleteWithEntries(SelectedTab.TabHeader);

        if (hasEntries && !deleteEntries)
        {
            var unassigned = UnassignedTab;
            if (unassigned != null)
            {
                foreach (var e in SelectedTab.Entries.ToList())
                {
                    e.TypeDigit = "?";
                    unassigned.AddEntryDirect(e);
                }
                SelectedTab.Entries.Clear();
                unassigned.RevalidateAll();
            }
        }

        TypeTabs.Remove(SelectedTab);
        SelectedTab = TypeTabs.FirstOrDefault(t => t.TypeDigit != "?") ?? TypeTabs.FirstOrDefault();
        IsDirty = true;
        TypeDeleted?.Invoke(digit);
    }

    [RelayCommand]
    private void MoveTabLeft()
    {
        if (SelectedTab == null) return;
        int i = TypeTabs.IndexOf(SelectedTab);
        if (i <= 0) return;
        var current = SelectedTab;
        TypeTabs.Move(i, i - 1);
        SelectedTab = current;
        IsDirty = true;
        TabOrderChanged?.Invoke();
    }

    [RelayCommand]
    private void MoveTabRight()
    {
        if (SelectedTab == null) return;
        int i = TypeTabs.IndexOf(SelectedTab);
        if (i < 0 || i >= TypeTabs.Count - 1) return;
        var current = SelectedTab;
        TypeTabs.Move(i, i + 1);
        SelectedTab = current;
        IsDirty = true;
        TabOrderChanged?.Invoke();
    }

    public event Action? TabOrderChanged;

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_cardsPath)) return;
        var all = TypeTabs.SelectMany(t => t.Entries).ToList();
        CsvService.SaveMedia(_cardsPath, all);
        IsDirty = false;
        StatusIsError = false; StatusMessage = $"Saved {all.Count} entries.";
        OnStatus?.Invoke(false, $"[Cards] {StatusMessage}");
    }
}