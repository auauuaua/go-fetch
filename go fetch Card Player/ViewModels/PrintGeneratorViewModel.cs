using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CardPlayer.Config;
using CardPlayer.Models;
using CardPlayer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace CardPlayer.ViewModels;

public partial class ReadyEntryViewModel : ViewModelBase
{
    public MediaEntry SourceEntry { get; }
    public string DisplayText { get; }
    public string QrCode { get; }
    public string ArtPath => SourceEntry.ArtPath;
    public string ArtBackPath => SourceEntry.ArtBackPath;
    public string Status => SourceEntry.ComputedStatus;
    [ObservableProperty] private bool _isSelected = true;

    public ReadyEntryViewModel(MediaEntry e)
    {
        SourceEntry = e;
        DisplayText = e.DisplayText;
        QrCode = e.QrCode;
    }
}

public partial class PrintGeneratorViewModel : ViewModelBase
{
    // ── Entry list ────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ReadyEntryViewModel> _readyEntries = new();
    [ObservableProperty] private ReadyEntryViewModel? _selectedEntry;

    public List<ReadyEntryViewModel> GridSelectedEntries { get; } = new();

    [RelayCommand] private void SetGenerate() { foreach (var e in GridSelectedEntries) e.IsSelected = true; }
    [RelayCommand] private void SetDontGenerate() { foreach (var e in GridSelectedEntries) e.IsSelected = false; }

    // ── Profiles ──────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<PrintGeneratorProfile> _profiles = new();
    [ObservableProperty] private PrintGeneratorProfile? _selectedProfile;
    [ObservableProperty] private string _profileName = "";

    private static readonly string ProfilesPath = AppPaths.Combine("Print_Profiles.json");

    // True while bulk-loading so property callbacks don't trigger redundant saves
    private bool _suppressSave = false;

    [ObservableProperty] private bool _isDirty = false;

    partial void OnSelectedProfileChanged(PrintGeneratorProfile? value)
    {
        if (value == null) return;
        _suppressSave = true;
        ProfileName = value.Name;
        _suppressSave = false;
        ApplyProfile(value);
        // Switching profiles restores saved state — not itself a dirty action
    }

    partial void OnProfileNameChanged(string value)
    {
        if (_suppressSave || SelectedProfile == null) return;
        // Name is [ObservableProperty] on PrintGeneratorProfile so the ComboBox
        // item template binding updates automatically — no remove/insert needed
        SelectedProfile.Name = value;
        SaveProfiles();
    }

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new PrintGeneratorProfile();
        SnapshotToProfile(profile);
        profile.Name = "New Profile";
        Profiles.Add(profile);
        SelectedProfile = profile;
        _suppressSave = true;
        ProfileName = profile.Name;
        _suppressSave = false;
        SaveProfiles();
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null || Profiles.Count <= 1) return;
        var toRemove = SelectedProfile;
        var idx = Profiles.IndexOf(toRemove);
        Profiles.Remove(toRemove);
        SelectedProfile = Profiles[Math.Max(0, idx - 1)];
        // Don't auto-save — mark dirty so Discard can restore the deleted profile
        IsDirty = true;
    }

    public void LoadProfiles()
    {
        PrintGeneratorProfilesConfig? config = null;
        try
        {
            if (File.Exists(ProfilesPath))
                config = JsonSerializer.Deserialize<PrintGeneratorProfilesConfig>(File.ReadAllText(ProfilesPath));
        }
        catch { }

        if (config == null || config.Profiles.Count == 0)
            config = MigrateFromLegacySettings(config);

        Profiles = new ObservableCollection<PrintGeneratorProfile>(config.Profiles);

        // Guard against null ActiveProfile in saved JSON
        var activeName = config.ActiveProfile ?? "";
        var active = (!string.IsNullOrEmpty(activeName)
                        ? Profiles.FirstOrDefault(p => p.Name == activeName)
                        : null)
                  ?? Profiles.FirstOrDefault();

        _suppressSave = true;
        SelectedProfile = active;
        if (active != null)
        {
            ProfileName = active.Name;
            ApplyProfile(active);
        }
        IsDirty = false;
        _suppressSave = false;
    }

    private void SaveProfiles()
    {
        if (_suppressSave) return;
        try
        {
            if (SelectedProfile != null)
                SnapshotToProfile(SelectedProfile);

            var config = new PrintGeneratorProfilesConfig
            {
                ActiveProfile = SelectedProfile?.Name ?? "",
                Profiles = Profiles.ToList()
            };
            File.WriteAllText(ProfilesPath,
                JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            IsDirty = false;
        }
        catch { }
    }

    [RelayCommand] public void Save() => SaveProfiles();

    private PrintGeneratorProfilesConfig MigrateFromLegacySettings(PrintGeneratorProfilesConfig? existing)
    {
        var cfg = existing ?? new PrintGeneratorProfilesConfig();
        var legacy = LoadLegacySettings();
        var profile = new PrintGeneratorProfile { Name = "Default" };
        profile.OutputPath = legacy.OutputPath;
        var dict = legacy.ModeSettings ?? new Dictionary<string, LegacyModeSettings>();
        if (!dict.TryGetValue("Single Card", out var m)) m = new LegacyModeSettings();
        ApplyLegacyToProfile(m, profile);
        profile.CardMode = "Single Card";
        cfg.Profiles.Add(profile);
        cfg.ActiveProfile = profile.Name;
        return cfg;
    }

    private void SnapshotToProfile(PrintGeneratorProfile p)
    {
        p.OutputPath = _outputPath;
        p.CardMode = _cardMode;
        p.CardWidthPx = _cardWidthPx;
        p.CardHeightPx = _cardHeightPx;
        p.QrAboveBottom = _qrAboveBottom;
        p.HorizontalSpacing = _horizontalSpacing;
        p.VerticalSpacing = _verticalSpacing;
        p.GenerateFronts = _generateFronts;
        p.OnlyWithArt = _onlyWithArt;
        p.ArtBleed = _artBleed;
        ;
        p.SheetWidthPx = _sheetWidthPx;
        p.SheetHeightPx = _sheetHeightPx;
        p.VerticalOffset = _verticalOffset;
        p.DrawOutline = _drawOutline;
        p.OutlineCornerRadius = _outlineCornerRadius;
        p.FlipFrontsRow = _flipFrontsRow;
        p.QrGenerateSheet = _qrGenerateSheet;
        p.QrSheetWidthPx = _qrSheetWidthPx;
        p.QrSheetHeightPx = _qrSheetHeightPx;
        p.QrAcross = _qrAcross;
        p.QrDown = _qrDown;
        p.QrHMarginCenter = _qrHMarginCenter;
        p.QrVMarginCenter = _qrVMarginCenter;
        p.QrVerticalOffset = _qrVerticalOffset;
        p.QrStartIndex = _qrStartIndex;
    }

    private void ApplyProfile(PrintGeneratorProfile p)
    {
        _suppressSave = true;
        try
        {
            _outputPath = p.OutputPath;
            _cardMode = p.CardMode;
            _cardWidthPx = p.CardWidthPx;
            _cardHeightPx = p.CardHeightPx;
            _qrAboveBottom = p.QrAboveBottom;
            _horizontalSpacing = p.HorizontalSpacing;
            _verticalSpacing = p.VerticalSpacing;
            _generateFronts = p.GenerateFronts;
            _onlyWithArt = p.OnlyWithArt;
            _artBleed = p.ArtBleed;
            _artBleed = p.ArtBleed;
            _sheetWidthPx = p.SheetWidthPx;
            _sheetHeightPx = p.SheetHeightPx;
            _verticalOffset = p.VerticalOffset;
            _drawOutline = p.DrawOutline;
            _outlineCornerRadius = p.OutlineCornerRadius;
            _flipFrontsRow = p.FlipFrontsRow;
            _qrGenerateSheet = p.QrGenerateSheet;
            _qrSheetWidthPx = p.QrSheetWidthPx;
            _qrSheetHeightPx = p.QrSheetHeightPx;
            _qrAcross = p.QrAcross;
            _qrDown = p.QrDown;
            _qrHMarginCenter = p.QrHMarginCenter;
            _qrVMarginCenter = p.QrVMarginCenter;
            _qrVerticalOffset = p.QrVerticalOffset;
            _qrStartIndex = p.QrStartIndex;

            // Notify all bindings
            OnPropertyChanged(nameof(OutputPath));
            OnPropertyChanged(nameof(CardMode));
            OnPropertyChanged(nameof(IsFullSheet)); OnPropertyChanged(nameof(IsQrOnly)); OnPropertyChanged(nameof(IsFullSheetWithFronts));
            OnPropertyChanged(nameof(CardWidthPx)); OnPropertyChanged(nameof(CardWidthInches));
            OnPropertyChanged(nameof(CardHeightPx)); OnPropertyChanged(nameof(CardHeightInches));
            OnPropertyChanged(nameof(QrAboveBottom)); OnPropertyChanged(nameof(QrAboveBottomInches));
            OnPropertyChanged(nameof(HorizontalSpacing)); OnPropertyChanged(nameof(HorizontalSpacingInches));
            OnPropertyChanged(nameof(VerticalSpacing)); OnPropertyChanged(nameof(VerticalSpacingInches));
            OnPropertyChanged(nameof(GenerateFronts));
            OnPropertyChanged(nameof(OnlyWithArt));
            OnPropertyChanged(nameof(ArtBleed)); OnPropertyChanged(nameof(ArtBleedInches));
            OnPropertyChanged(nameof(SheetWidthPx)); OnPropertyChanged(nameof(SheetWidthInches));
            OnPropertyChanged(nameof(SheetHeightPx)); OnPropertyChanged(nameof(SheetHeightInches));
            OnPropertyChanged(nameof(VerticalOffset)); OnPropertyChanged(nameof(VerticalOffsetInches));
            OnPropertyChanged(nameof(DrawOutline));
            OnPropertyChanged(nameof(OutlineCornerRadius)); OnPropertyChanged(nameof(OutlineCornerRadiusInches));
            OnPropertyChanged(nameof(FlipFrontsRow));
            OnPropertyChanged(nameof(QrGenerateSheet));
            OnPropertyChanged(nameof(QrSheetWidthPx)); OnPropertyChanged(nameof(QrSheetWidthInches));
            OnPropertyChanged(nameof(QrSheetHeightPx)); OnPropertyChanged(nameof(QrSheetHeightInches));
            OnPropertyChanged(nameof(QrAcross));
            OnPropertyChanged(nameof(QrDown));
            OnPropertyChanged(nameof(QrHMarginCenter)); OnPropertyChanged(nameof(QrHMarginCenterInches));
            OnPropertyChanged(nameof(QrVMarginCenter)); OnPropertyChanged(nameof(QrVMarginCenterInches));
            OnPropertyChanged(nameof(QrVerticalOffset)); OnPropertyChanged(nameof(QrVerticalOffsetInches));
            OnPropertyChanged(nameof(QrStartIndex));
        }
        finally { _suppressSave = false; }
    }

    // ── Output settings ───────────────────────────────────────────────────
    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private string _cardMode = "Single Card";
    [ObservableProperty] private int _cardWidthPx = 750;
    [ObservableProperty] private int _cardHeightPx = 1050;
    [ObservableProperty] private int _qrAboveBottom = 38;
    [ObservableProperty] private int _horizontalSpacing = 0;
    [ObservableProperty] private int _verticalSpacing = 0;

    public PrintGeneratorViewModel()
    {
        LoadProfiles();
    }

    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _statusMessage = "";
    /// <summary>Forwarded to main window status bar. Set by MainWindowViewModel.</summary>
    public Action<bool, string>? OnStatus;
    [ObservableProperty] private bool _isGenerating;
    [ObservableProperty] private bool _generateFronts = false;
    [ObservableProperty] private bool _onlyWithArt = false;
    [ObservableProperty] private int _artBleed = 0;
    [ObservableProperty] private bool _qrGenerateSheet = false;
    [ObservableProperty] private int _qrSheetWidthPx = 2550;
    [ObservableProperty] private int _qrSheetHeightPx = 3300;
    [ObservableProperty] private int _qrAcross = 4;
    [ObservableProperty] private int _qrDown = 6;
    [ObservableProperty] private int _qrHMarginCenter = 150;
    [ObservableProperty] private int _qrVMarginCenter = 150;
    [ObservableProperty] private int _qrVerticalOffset = 0;
    [ObservableProperty] private int _qrStartIndex = 1;
    [ObservableProperty] private int _sheetWidthPx = 2550;
    [ObservableProperty] private int _sheetHeightPx = 3300;
    [ObservableProperty] private int _verticalOffset = 0;
    [ObservableProperty] private bool _drawOutline = false;
    [ObservableProperty] private int _outlineCornerRadius = 0;
    [ObservableProperty] private bool _flipFrontsRow = true;

    // ── Inch equivalents ──────────────────────────────────────────────────
    public string QrSheetWidthInches => $"{_qrSheetWidthPx / 300.0:F3}\"";
    public string QrSheetHeightInches => $"{_qrSheetHeightPx / 300.0:F3}\"";
    public string QrHMarginCenterInches => $"{_qrHMarginCenter / 300.0:F3}\"";
    public string QrVMarginCenterInches => $"{_qrVMarginCenter / 300.0:F3}\"";
    public string QrVerticalOffsetInches => $"{_qrVerticalOffset / 300.0:F3}\"";
    public string ArtBleedInches => $"{_artBleed / 300.0:F3}\"";

    public string VerticalOffsetInches => $"{_verticalOffset / 300.0:F3}\"";
    public string CardWidthInches => $"{_cardWidthPx / 300.0:F3}\"";
    public string CardHeightInches => $"{_cardHeightPx / 300.0:F3}\"";
    public string QrAboveBottomInches => $"{_qrAboveBottom / 300.0:F3}\"";
    public string HorizontalSpacingInches => $"{_horizontalSpacing / 300.0:F3}\"";
    public string VerticalSpacingInches => $"{_verticalSpacing / 300.0:F3}\"";
    public string SheetWidthInches => $"{_sheetWidthPx / 300.0:F3}\"";
    public string SheetHeightInches => $"{_sheetHeightPx / 300.0:F3}\"";
    public string OutlineCornerRadiusInches => $"{_outlineCornerRadius / 300.0:F3}\"";

    // ── Property change callbacks — all save & notify inches ─────────────
    partial void OnQrSheetWidthPxChanged(int v) { OnPropertyChanged(nameof(QrSheetWidthInches)); SaveProfiles(); }
    partial void OnQrSheetHeightPxChanged(int v) { OnPropertyChanged(nameof(QrSheetHeightInches)); SaveProfiles(); }
    partial void OnQrHMarginCenterChanged(int v) { OnPropertyChanged(nameof(QrHMarginCenterInches)); SaveProfiles(); }
    partial void OnQrVMarginCenterChanged(int v) { OnPropertyChanged(nameof(QrVMarginCenterInches)); SaveProfiles(); }
    partial void OnQrVerticalOffsetChanged(int v) { OnPropertyChanged(nameof(QrVerticalOffsetInches)); SaveProfiles(); }
    partial void OnQrGenerateSheetChanged(bool v) => SaveProfiles();
    partial void OnQrAcrossChanged(int v) => SaveProfiles();
    partial void OnQrDownChanged(int v) => SaveProfiles();
    partial void OnQrStartIndexChanged(int v) => SaveProfiles();
    partial void OnOutlineCornerRadiusChanged(int v) { OnPropertyChanged(nameof(OutlineCornerRadiusInches)); SaveProfiles(); }
    partial void OnSheetWidthPxChanged(int v) { OnPropertyChanged(nameof(SheetWidthInches)); SaveProfiles(); }
    partial void OnSheetHeightPxChanged(int v) { OnPropertyChanged(nameof(SheetHeightInches)); SaveProfiles(); }
    partial void OnArtBleedChanged(int v)
    {
        OnPropertyChanged(nameof(ArtBleedInches));
        if (!_suppressSave && v > 0)
        {
            _suppressSave = true;
            if (CardMode == "Full Sheet")
            {
                // Full sheet spacing: need at least bleed*2 on each side gap
                int needed = v * 2;
                if (HorizontalSpacing < needed) { HorizontalSpacing = needed; OnPropertyChanged(nameof(HorizontalSpacingInches)); }
                if (VerticalSpacing < needed) { VerticalSpacing = needed; OnPropertyChanged(nameof(VerticalSpacingInches)); }
            }
            else
            {
                // Single card print margins: need at least bleed on each side
                if (HorizontalSpacing < v) { HorizontalSpacing = v; OnPropertyChanged(nameof(HorizontalSpacingInches)); }
                if (VerticalSpacing < v) { VerticalSpacing = v; OnPropertyChanged(nameof(VerticalSpacingInches)); }
            }
            _suppressSave = false;
        }
        SaveProfiles();
    }

    partial void OnVerticalOffsetChanged(int v) { OnPropertyChanged(nameof(VerticalOffsetInches)); SaveProfiles(); }
    partial void OnCardWidthPxChanged(int v) { OnPropertyChanged(nameof(CardWidthInches)); SaveProfiles(); }
    partial void OnCardHeightPxChanged(int v) { OnPropertyChanged(nameof(CardHeightInches)); SaveProfiles(); }
    partial void OnQrAboveBottomChanged(int v) { OnPropertyChanged(nameof(QrAboveBottomInches)); SaveProfiles(); }
    partial void OnHorizontalSpacingChanged(int v)
    {
        OnPropertyChanged(nameof(HorizontalSpacingInches));
        if (!_suppressSave && ArtBleed > 0)
        {
            _suppressSave = true;
            int maxBleed = CardMode == "Full Sheet" ? v / 2 : v;
            if (ArtBleed > maxBleed) { ArtBleed = Math.Max(0, maxBleed); OnPropertyChanged(nameof(ArtBleedInches)); }
            _suppressSave = false;
        }
        SaveProfiles();
    }
    partial void OnVerticalSpacingChanged(int v)
    {
        OnPropertyChanged(nameof(VerticalSpacingInches));
        if (!_suppressSave && ArtBleed > 0)
        {
            _suppressSave = true;
            int maxBleed = CardMode == "Full Sheet" ? v / 2 : v;
            if (ArtBleed > maxBleed) { ArtBleed = Math.Max(0, maxBleed); OnPropertyChanged(nameof(ArtBleedInches)); }
            _suppressSave = false;
        }
        SaveProfiles();
    }
    partial void OnOutputPathChanged(string v) => SaveProfiles();
    partial void OnCardModeChanged(string v)
    {
        OnPropertyChanged(nameof(IsFullSheet));
        OnPropertyChanged(nameof(IsQrOnly));
        OnPropertyChanged(nameof(IsFullSheetWithFronts));
        SaveProfiles();
    }
    partial void OnGenerateFrontsChanged(bool v) { SaveProfiles(); }
    partial void OnOnlyWithArtChanged(bool v) { SaveProfiles(); if (_lastAllEntries != null) RefreshEntries(_lastAllEntries); }
    partial void OnDrawOutlineChanged(bool v) => SaveProfiles();
    partial void OnFlipFrontsRowChanged(bool v) => SaveProfiles();

    // ── Computed booleans ─────────────────────────────────────────────────
    public bool IsFullSheet => CardMode == "Full Sheet";
    public bool IsQrOnly => CardMode == "QR Only";
    public bool IsFullSheetWithFronts => IsFullSheet;

    public static string[] CardModes { get; } = { "Single Card", "Full Sheet", "QR Only" };

    // ── Folder browse ─────────────────────────────────────────────────────
    [RelayCommand]
    private async Task BrowseOutput()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = desktop.Windows.FirstOrDefault(w => w.IsVisible);
            if (window == null) return;
            var sp = Avalonia.Controls.TopLevel.GetTopLevel(window)!.StorageProvider;
            var opts = new FolderPickerOpenOptions { Title = "Select output folder" };
            if (Directory.Exists(OutputPath))
                opts.SuggestedStartLocation = await sp.TryGetFolderFromPathAsync(OutputPath);
            var folders = await sp.OpenFolderPickerAsync(opts);
            if (folders.Count > 0)
                OutputPath = folders[0].Path.LocalPath;
        }
    }

    // ── Entry refresh ─────────────────────────────────────────────────────
    private IEnumerable<MediaEntry>? _lastAllEntries;
    private Action? _onGenerated;
    public void SetOnGeneratedCallback(Action callback) => _onGenerated = callback;

    public void RefreshEntries(IEnumerable<MediaEntry> allEntries)
    {
        _lastAllEntries = allEntries.ToList();
        ReadyEntries = new ObservableCollection<ReadyEntryViewModel>(
            _lastAllEntries
                .Where(e => e.ComputedStatus == "Ready" ||
                           (!OnlyWithArt && e.ComputedStatus == "Ready — no art"))
                .Select(e => new ReadyEntryViewModel(e)));
        StatusIsError = false; StatusMessage = $"{ReadyEntries.Count} entry(s) ready to generate.";
        OnStatus?.Invoke(false, $"[Print] {StatusMessage}");
    }

    // ── Legacy settings migration ─────────────────────────────────────────
    private static readonly string LegacySettingsPath = AppPaths.Combine("card_settings.json");

    private record LegacyModeSettings(
        int CardWidthPx = 750,
        int CardHeightPx = 1050,
        int QrAboveBottom = 38,
        int HorizontalSpacing = 0,
        int VerticalSpacing = 0,
        bool GenerateFronts = false,
        bool OnlyWithArt = false,
        int ArtBleed = 0,
        int SheetWidthPx = 2550,
        int SheetHeightPx = 3300,
        int VerticalOffset = 0,
        bool DrawOutline = false,
        int OutlineCornerRadius = 0,
        bool FlipFrontsRow = true,
        bool QrGenerateSheet = false,
        int QrSheetWidthPx = 2550,
        int QrSheetHeightPx = 3300,
        int QrAcross = 4,
        int QrDown = 6,
        int QrHMarginCenter = 150,
        int QrVMarginCenter = 150,
        int QrVerticalOffset = 0,
        int QrStartIndex = 1);

    private record LegacyCardSettings(
        string OutputPath = "",
        Dictionary<string, LegacyModeSettings>? ModeSettings = null);

    private static LegacyCardSettings LoadLegacySettings()
    {
        try
        {
            if (File.Exists(LegacySettingsPath))
            {
                var s = JsonSerializer.Deserialize<LegacyCardSettings>(File.ReadAllText(LegacySettingsPath));
                if (s != null) return s;
            }
        }
        catch { }
        return new LegacyCardSettings();
    }

    private static void ApplyLegacyToProfile(LegacyModeSettings m, PrintGeneratorProfile p)
    {
        p.CardWidthPx = m.CardWidthPx;
        p.CardHeightPx = m.CardHeightPx;
        p.QrAboveBottom = m.QrAboveBottom;
        p.HorizontalSpacing = m.HorizontalSpacing;
        p.VerticalSpacing = m.VerticalSpacing;
        p.GenerateFronts = m.GenerateFronts;
        p.OnlyWithArt = m.OnlyWithArt;
        p.ArtBleed = m.ArtBleed;
        p.SheetWidthPx = m.SheetWidthPx;
        p.SheetHeightPx = m.SheetHeightPx;
        p.VerticalOffset = m.VerticalOffset;
        p.DrawOutline = m.DrawOutline;
        p.OutlineCornerRadius = m.OutlineCornerRadius;
        p.FlipFrontsRow = m.FlipFrontsRow;
        p.QrGenerateSheet = m.QrGenerateSheet;
        p.QrSheetWidthPx = m.QrSheetWidthPx;
        p.QrSheetHeightPx = m.QrSheetHeightPx;
        p.QrAcross = m.QrAcross;
        p.QrDown = m.QrDown;
        p.QrHMarginCenter = m.QrHMarginCenter;
        p.QrVMarginCenter = m.QrVMarginCenter;
        p.QrVerticalOffset = m.QrVerticalOffset;
        p.QrStartIndex = m.QrStartIndex;
    }

    // ── Generate ──────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task Generate()
    {
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            StatusIsError = true; StatusMessage = "Please select an output folder first.";
            OnStatus?.Invoke(true, $"[Print] {StatusMessage}");
            return;
        }

        if (!Directory.Exists(OutputPath))
        {
            try { Directory.CreateDirectory(OutputPath); }
            catch
            {
                StatusIsError = true; StatusMessage = "Could not create output folder.";
                OnStatus?.Invoke(true, $"[Print] {StatusMessage}"); return;
            }
        }

        var toGenerate = ReadyEntries.Where(e => e.IsSelected).ToList();
        if (!toGenerate.Any())
        {
            StatusIsError = true; StatusMessage = "No entries selected.";
            OnStatus?.Invoke(true, $"[Print] {StatusMessage}"); return;
        }

        IsGenerating = true;
        StatusIsError = false; StatusMessage = $"Generating {toGenerate.Count} card(s)…";
        OnStatus?.Invoke(false, $"[Print] {StatusMessage}");

        if (CardMode == "Full Sheet") { await GenerateFullSheet(toGenerate); return; }
        if (CardMode == "QR Only") { await GenerateQrOnly(toGenerate); return; }

        int success = 0, failed = 0;
        var succeeded = new List<ReadyEntryViewModel>();

        await Task.Run(() =>
        {
            foreach (var entry in toGenerate)
            {
                var src = entry.SourceEntry;
                bool textOnBack = !string.IsNullOrWhiteSpace(entry.DisplayText) && src.TextSide == "back";
                var error = PrintGeneratorService.GenerateSingleCard(
                    entry.QrCode, entry.DisplayText, OutputPath,
                    CardWidthPx, CardHeightPx, QrAboveBottom,
                    textOnBack, src.TextFont, src.TextStyle, src.TextSize, src.TextColor,
                    src.BackBgColor,
                    HorizontalSpacing, VerticalSpacing,
                    ArtBleed,
                    !string.IsNullOrWhiteSpace(entry.ArtBackPath) ? entry.ArtBackPath : null);

                if (error != null) { failed++; continue; }

                bool hasArt = !string.IsNullOrWhiteSpace(entry.ArtPath);
                bool textOnFront = !string.IsNullOrWhiteSpace(entry.DisplayText) && src.TextSide == "front";
                var frontError = PrintGeneratorService.GenerateCardFront(
                    entry.QrCode,
                    hasArt ? entry.ArtPath : "",
                    entry.SourceEntry.ArtFit, OutputPath,
                    CardWidthPx, CardHeightPx, ArtBleed,
                    src.FrontBgColor,
                    textOnFront, src.TextFont, src.TextStyle, src.TextSize, src.TextColor,
                    entry.DisplayText,
                    HorizontalSpacing, VerticalSpacing);
                if (frontError != null) failed++;

                success++;
                succeeded.Add(entry);
            }
        });

        foreach (var entry in succeeded) entry.SourceEntry.State = "generated";
        foreach (var entry in succeeded) ReadyEntries.Remove(entry);
        _onGenerated?.Invoke();

        IsGenerating = false;
        StatusMessage = failed == 0
            ? $"Done — {success} card(s) generated in {OutputPath}"
            : $"Done — {success} succeeded, {failed} failed. Check output folder.";
    }

    private async Task GenerateQrOnly(List<ReadyEntryViewModel> toGenerate)
    {
        int success = 0, failed = 0;
        if (!QrGenerateSheet)
        {
            await Task.Run(() =>
            {
                foreach (var entry in toGenerate)
                {
                    var error = PrintGeneratorService.GenerateQrOnly(entry.QrCode, OutputPath);
                    if (error == null) success++; else failed++;
                }
            });
        }
        else
        {
            await Task.Run(() =>
            {
                try
                {
                    int sheetW = QrSheetWidthPx, sheetH = QrSheetHeightPx;
                    int across = Math.Max(1, QrAcross), down = Math.Max(1, QrDown);
                    int hmCenter = QrHMarginCenter, vmCenter = QrVMarginCenter;
                    int vOffset = QrVerticalOffset, startIdx = Math.Max(1, QrStartIndex) - 1;
                    float hSpacing = across > 1 ? (float)(sheetW - hmCenter * 2) / (across - 1) : 0;
                    float vSpacing = down > 1 ? (float)(sheetH - vmCenter * 2) / (down - 1) : 0;
                    int totalSlots = across * down, entriesPlaced = 0, sheetNum = 1;
                    while (File.Exists(Path.Combine(OutputPath, $"QR_sheet_{sheetNum}.png"))) sheetNum++;
                    while (entriesPlaced < toGenerate.Count)
                    {
                        using var sheetBitmap = new SKBitmap(sheetW, sheetH);
                        using (var canvas = new SKCanvas(sheetBitmap))
                        {
                            canvas.Clear(SKColors.White);
                            int slotOffset = entriesPlaced == 0 ? startIdx : 0;
                            for (int slot = slotOffset; slot < totalSlots && entriesPlaced < toGenerate.Count; slot++)
                            {
                                int col = slot % across, row = slot / across;
                                float cx = hmCenter + col * hSpacing, cy = vmCenter + row * vSpacing + vOffset;
                                var qrBmp = PrintGeneratorService.GenerateQrBitmap(toGenerate[entriesPlaced].QrCode);
                                if (qrBmp != null)
                                {
                                    // Bitmap includes 16px quiet zone each side; data center is at bmp/2
                                    // so draw at cx - bmpW/2, cy - bmpH/2 to keep data centered on cx,cy
                                    float drawX = cx - qrBmp.Width / 2f;
                                    float drawY = cy - qrBmp.Height / 2f;
                                    canvas.DrawBitmap(qrBmp, new SKPoint(drawX, drawY));
                                    qrBmp.Dispose();
                                }
                                entriesPlaced++;
                            }
                        }
                        SaveBitmap(sheetBitmap, Path.Combine(OutputPath, $"QR_sheet_{sheetNum}.png"));
                        success++; sheetNum++;
                        if (entriesPlaced < toGenerate.Count)
                            while (File.Exists(Path.Combine(OutputPath, $"QR_sheet_{sheetNum}.png"))) sheetNum++;
                    }
                }
                catch (Exception ex)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        IsGenerating = false; StatusIsError = true; StatusMessage = $"Error: {ex.Message}";
                        OnStatus?.Invoke(true, $"[Print] {StatusMessage}");
                    });
                    return;
                }
            });
        }
        foreach (var entry in toGenerate) if (entry.SourceEntry.State != "generated") entry.SourceEntry.State = "generated-back-only";
        foreach (var entry in toGenerate) ReadyEntries.Remove(entry);
        _onGenerated?.Invoke();
        IsGenerating = false;
        StatusMessage = failed == 0
            ? $"Done — {(QrGenerateSheet ? "sheet(s)" : $"{success} QR code(s)")} saved to {OutputPath}"
            : $"Done — {success} succeeded, {failed} failed.";
    }

    private async Task GenerateFullSheet(List<ReadyEntryViewModel> entries)
    {
        await Task.Run(() =>
        {
            try
            {
                int cardW = CardWidthPx, cardH = CardHeightPx, hSpacing = HorizontalSpacing, vSpacing = VerticalSpacing;
                int sheetW = SheetWidthPx, sheetH = SheetHeightPx, vOffset = -VerticalOffset;
                int cellW = cardW + hSpacing, cellH = cardH + vSpacing;
                if (cellW <= 0 || cellH <= 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        IsGenerating = false; StatusIsError = true; StatusMessage = "Card size is zero.";
                        OnStatus?.Invoke(true, $"[Print] {StatusMessage}");
                    }); return;
                }
                int cols = sheetW / cellW, rows = sheetH / cellH;
                if (cols == 0 || rows == 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        IsGenerating = false; StatusIsError = true; StatusMessage = "Cards don't fit on sheet.";
                        OnStatus?.Invoke(true, $"[Print] {StatusMessage}");
                    }); return;
                }
                int gridW = cols * cellW, gridH = rows * cellH;
                int originX = (sheetW - gridW) / 2, originY = (sheetH - gridH) / 2 + vOffset;
                int sheetNum = 0, entryIdx = 0;
                while (entryIdx < entries.Count)
                {
                    while (File.Exists(Path.Combine(OutputPath, $"sheet_{sheetNum}.png")) ||
                           File.Exists(Path.Combine(OutputPath, $"sheet_{sheetNum}_fronts.png"))) sheetNum++;
                    var pageEntries = entries.Skip(entryIdx).Take(cols * rows).ToList();
                    entryIdx += cols * rows;
                    using (var backBitmap = new SKBitmap(sheetW, sheetH))
                    using (var canvas = new SKCanvas(backBitmap))
                    {
                        canvas.Clear(SKColors.White);
                        for (int i = 0; i < pageEntries.Count; i++)
                        {
                            int col = i % cols, row = i / cols;
                            int cx = originX + col * cellW + hSpacing / 2;
                            int cy = originY + row * cellH + vSpacing / 2;
                            var src = pageEntries[i].SourceEntry;
                            bool sheetTextOnBack = !string.IsNullOrWhiteSpace(pageEntries[i].DisplayText) && src.TextSide == "back";
                            var backCard = PrintGeneratorService.GenerateSingleCardBitmap(
                                pageEntries[i].QrCode, cardW, cardH, QrAboveBottom,
                                sheetTextOnBack, pageEntries[i].DisplayText,
                                src.TextFont, src.TextStyle, src.TextSize, src.TextColor,
                                src.BackBgColor, ArtBleed,
                                !string.IsNullOrWhiteSpace(pageEntries[i].ArtBackPath) ? pageEntries[i].ArtBackPath : null);
                            if (backCard != null) { canvas.DrawBitmap(backCard, new SKPoint(cx, cy)); backCard.Dispose(); }
                            if (DrawOutline)
                            {
                                using var pen = new SKPaint { Color = SKColors.Black, IsAntialias = OutlineCornerRadius > 0, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
                                var rect = new SKRect(cx, cy, cx + cardW, cy + cardH);
                                if (OutlineCornerRadius > 0) canvas.DrawRoundRect(rect, OutlineCornerRadius, OutlineCornerRadius, pen);
                                else canvas.DrawRect(rect, pen);
                            }
                        }
                        SaveBitmap(backBitmap, Path.Combine(OutputPath, $"sheet_{sheetNum}.png"));
                    }
                    if (true) // always generate fronts
                    {
                        using (var frontBitmap = new SKBitmap(sheetW, sheetH))
                        using (var canvas = new SKCanvas(frontBitmap))
                        {
                            canvas.Clear(SKColors.White);
                            for (int i = 0; i < pageEntries.Count; i++)
                            {
                                int row = i / cols, colFwd = i % cols;
                                int col = FlipFrontsRow ? (cols - 1 - colFwd) : colFwd;
                                int cx = originX + col * cellW + hSpacing / 2;
                                int cy = originY + row * cellH + vSpacing / 2;
                                var entry = pageEntries[i];
                                var srcE = entry.SourceEntry;
                                bool hasArt = !string.IsNullOrWhiteSpace(entry.ArtPath);
                                bool sheetTextOnFront = !string.IsNullOrWhiteSpace(entry.DisplayText) && srcE.TextSide == "front";
                                var frontBmp = PrintGeneratorService.GenerateCardFrontBitmap(
                                    hasArt ? entry.ArtPath : "", entry.SourceEntry.ArtFit,
                                    cardW, cardH, ArtBleed,
                                    srcE.FrontBgColor,
                                    sheetTextOnFront, srcE.TextFont, srcE.TextStyle,
                                    srcE.TextSize, srcE.TextColor, entry.DisplayText);
                                if (frontBmp != null) { canvas.DrawBitmap(frontBmp, new SKPoint(cx, cy)); frontBmp.Dispose(); }
                                if (DrawOutline)
                                {
                                    using var pen = new SKPaint { Color = SKColors.Black, IsAntialias = OutlineCornerRadius > 0, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
                                    var rect = new SKRect(cx, cy, cx + cardW, cy + cardH);
                                    if (OutlineCornerRadius > 0) canvas.DrawRoundRect(rect, OutlineCornerRadius, OutlineCornerRadius, pen);
                                    else canvas.DrawRect(rect, pen);
                                }
                            }
                            SaveBitmap(frontBitmap, Path.Combine(OutputPath, $"sheet_{sheetNum}_fronts.png"));
                        }
                    }
                }
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var e in entries) { e.SourceEntry.State = "generated"; ReadyEntries.Remove(e); }
                    _onGenerated?.Invoke();
                    IsGenerating = false;
                    StatusIsError = false; StatusMessage = $"Done — {sheetNum + 1} sheet(s) saved to {OutputPath}";
                    OnStatus?.Invoke(false, $"[Print] {StatusMessage}");
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                    IsGenerating = false; StatusIsError = true; StatusMessage = $"Error: {ex.Message}";
                    OnStatus?.Invoke(true, $"[Print] {StatusMessage}");
                });
            }
        });
    }

    private static void SaveBitmap(SKBitmap bmp, string path)
    {
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }
}