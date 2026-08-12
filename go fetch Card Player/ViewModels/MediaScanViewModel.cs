using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CardPlayer.Models;
using CardPlayer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

/// <summary>
/// Handles folder scan and CSV import for one media type tab.
/// </summary>
public partial class MediaScanViewModel : ViewModelBase
{
    private readonly PlayerTypeTabViewModel _tab;

    // Set by CardsViewModel after construction so we can access profiles
    internal CardLayoutProfilesViewModel? LayoutProfiles { get; set; }

    [ObservableProperty] private string _scanPath = "";
    [ObservableProperty] private string _scanMode = "folders";
    [ObservableProperty] private int _depth = 0;
    [ObservableProperty] private int _inverseDepth = 0;
    [ObservableProperty] private string _artPattern = "";
    [ObservableProperty] private string _artDirOverride = "";
    [ObservableProperty] private string _defaultProfileName = "";
    [ObservableProperty] private string _excludeFolders = "";
    [ObservableProperty] private string _fileExtensions = "";
    [ObservableProperty] private string _scanStatus = "";
    public Action<bool, string>? OnStatus;

    public bool IsFoldersMode => ScanMode == "folders";

    public static IReadOnlyList<string> ScanModes { get; } = new[] { "folders", "files" };

    /// <summary>The currently selected default profile object (null = none).</summary>
    public CardPlayer.Config.CardLayoutProfile? DefaultProfile
    {
        get => string.IsNullOrEmpty(_defaultProfileName) ? null :
               LayoutProfiles?.Profiles.FirstOrDefault(p =>
                   p.Name.Equals(_defaultProfileName, StringComparison.OrdinalIgnoreCase));
        set
        {
            DefaultProfileName = value?.Name ?? "";
            OnPropertyChanged();
        }
    }

    partial void OnScanPathChanged(string v) => SaveSettings();
    partial void OnScanModeChanged(string v) { OnPropertyChanged(nameof(IsFoldersMode)); SaveSettings(); }
    partial void OnDepthChanged(int v) => SaveSettings();
    partial void OnInverseDepthChanged(int v) => SaveSettings();
    partial void OnArtPatternChanged(string v) => SaveSettings();
    partial void OnArtDirOverrideChanged(string v) => SaveSettings();
    partial void OnDefaultProfileNameChanged(string v) { SaveSettings(); OnPropertyChanged(nameof(DefaultProfile)); }
    partial void OnExcludeFoldersChanged(string v) => SaveSettings();
    partial void OnFileExtensionsChanged(string v) => SaveSettings();

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ClearDefaultProfile() => DefaultProfile = null;

    private static readonly string SettingsPath = AppPaths.Combine("scan_settings.json");

    public MediaScanViewModel(PlayerTypeTabViewModel tab)
    {
        _tab = tab;
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (!System.IO.File.Exists(SettingsPath)) return;
            var config = System.Text.Json.JsonSerializer.Deserialize<CardPlayer.Config.ScanConfig>(
                System.IO.File.ReadAllText(SettingsPath));
            if (config == null || !config.ByTypeDigit.TryGetValue(_tab.TypeDigit, out var s)) return;
            _scanPath = s.ScanPath;
            _scanMode = s.ScanMode;
            _depth = s.Depth;
            _inverseDepth = s.InverseDepth;
            _artPattern = s.ArtPattern;
            _artDirOverride = s.ArtDirOverride;
            _defaultProfileName = s.DefaultProfileName ?? "";
            _excludeFolders = s.ExcludeFolders;
            _fileExtensions = s.FileExtensions;
        }
        catch { }
    }

    private void SaveSettings()
    {
        try
        {
            CardPlayer.Config.ScanConfig config;
            if (System.IO.File.Exists(SettingsPath))
            {
                config = System.Text.Json.JsonSerializer.Deserialize<CardPlayer.Config.ScanConfig>(
                    System.IO.File.ReadAllText(SettingsPath))
                    ?? new CardPlayer.Config.ScanConfig();
            }
            else config = new CardPlayer.Config.ScanConfig();

            config.ByTypeDigit[_tab.TypeDigit] = new CardPlayer.Config.ScanTypeConfig
            {
                ScanPath = ScanPath,
                ScanMode = ScanMode,
                Depth = Depth,
                InverseDepth = InverseDepth,
                ArtPattern = ArtPattern,
                ArtDirOverride = ArtDirOverride,
                DefaultProfileName = DefaultProfileName,
                ExcludeFolders = ExcludeFolders,
                FileExtensions = FileExtensions,
            };

            System.IO.File.WriteAllText(SettingsPath,
                System.Text.Json.JsonSerializer.Serialize(config,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    // ── Folder scan ───────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseScanPath()
    {
        var opts = new FolderPickerOpenOptions { Title = "Select folder to scan", AllowMultiple = false };
        if (Directory.Exists(ScanPath))
            opts.SuggestedStartLocation = await GetStorageProvider().TryGetFolderFromPathAsync(ScanPath);
        var folders = await GetStorageProvider().OpenFolderPickerAsync(opts);
        if (folders.Count > 0)
            ScanPath = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private void RunScan()
    {
        if (!Directory.Exists(ScanPath)) { ScanStatus = "Folder not found."; OnStatus?.Invoke(true, $"[Scan] {ScanStatus}"); return; }

        // Collect paths at the exact specified depth (0 = direct children)
        var excludeSet = ExcludeFolders
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extSet = FileExtensions
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var paths = ScanMode == "files"
            ? GetAtDepth(ScanPath, Depth, returnFiles: true, excludeFolders: excludeSet, fileExtensions: extSet)
            : GetAtDepth(ScanPath, Depth, returnFiles: false, excludeFolders: excludeSet, fileExtensions: extSet);

        var existing = _tab.Entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingQr = _tab.Entries.Select(e => e.QrCode)
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;

        foreach (var p in paths)
        {
            if (existing.Contains(p)) continue;

            // Always build display name from path
            string displayText = BuildDisplayName(p, InverseDepth);

            // Always generate QR code from display name; trim and dedup automatically
            string qrRaw = Services.QrCodeValidator.TrimToQrLimit(displayText);
            string qrCode = Services.CsvService.MakeUniqueQrCode(qrRaw, existingQr);
            existingQr.Add(qrCode);

            var entry = new MediaEntry
            {
                TypeDigit = _tab.TypeDigit,
                Path = p,
                DisplayText = displayText,
                QrCode = qrCode,
                State = "new"
            };
            var profile = DefaultProfile;
            if (profile != null) entry.ApplyLayoutProfile(profile);
            _tab.AddEntryDirect(entry);
            existing.Add(p);
            added++;
        }

        _tab.RevalidateAll();
        ScanStatus = added > 0 ? $"Added {added} new path(s)." : "No new paths found.";
        OnStatus?.Invoke(false, $"[Scan] {ScanStatus}");
        ScanArt();
    }

    /// <summary>
    /// Returns paths at exactly the specified depth below root.
    /// Depth 0 = direct children of root.
    /// Depth 1 = children of those children, etc.
    /// </summary>
    private static List<string> GetAtDepth(string root, int depth, bool returnFiles,
        HashSet<string> excludeFolders, HashSet<string> fileExtensions)
    {
        var results = new List<string>();
        CollectAtDepth(root, depth, 0, returnFiles, excludeFolders, fileExtensions, results);
        return results;
    }

    private static void CollectAtDepth(string current, int targetDepth, int currentDepth,
        bool returnFiles, HashSet<string> excludeFolders, HashSet<string> fileExtensions,
        List<string> results)
    {
        try
        {
            if (currentDepth == targetDepth)
            {
                if (returnFiles)
                {
                    var files = Directory.GetFiles(current);
                    foreach (var f in files)
                    {
                        if (fileExtensions.Count > 0 &&
                            !fileExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                            continue;
                        results.Add(f);
                    }
                }
                else
                {
                    foreach (var d in Directory.GetDirectories(current))
                    {
                        if (excludeFolders.Count > 0 &&
                            excludeFolders.Contains(Path.GetFileName(d)))
                            continue;
                        results.Add(d);
                    }
                }
                return;
            }
            foreach (var dir in Directory.GetDirectories(current))
            {
                if (excludeFolders.Contains(Path.GetFileName(dir))) continue;
                CollectAtDepth(dir, targetDepth, currentDepth + 1, returnFiles,
                    excludeFolders, fileExtensions, results);
            }
        }
        catch { }
    }

    /// <summary>
    /// Builds a display name from a path using inverse depth.
    /// inverseDepth=0 → just the name of the item
    /// inverseDepth=1 → "parent - name"
    /// inverseDepth=2 → "grandparent - parent - name"
    /// </summary>
    private static string BuildDisplayName(string path, int inverseDepth)
    {
        var parts = new List<string>();
        string? current = path;

        for (int i = 0; i <= inverseDepth; i++)
        {
            if (current == null) break;
            string name;
            if (i == 0 && File.Exists(current))
                // Leaf is a file — drop the extension for cleaner titles and QR codes
                name = Path.GetFileNameWithoutExtension(current);
            else
                name = Path.GetFileName(current) is { Length: > 0 } n ? n : current; // root drive
            parts.Insert(0, name);
            current = Path.GetDirectoryName(current);
        }

        return string.Join(" - ", parts);
    }



    // ── CSV import ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ImportCsv()
    {
        var files = await GetStorageProvider().OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import CSV",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } }
            }
        });

        if (files.Count == 0) return;

        string path = files[0].Path.LocalPath;
        if (!File.Exists(path)) { ScanStatus = "File not found."; OnStatus?.Invoke(true, $"[Scan] {ScanStatus}"); return; }

        var allRows = CsvService.ReadRows(path).ToList();
        if (allRows.Count < 2) { ScanStatus = "CSV is empty."; OnStatus?.Invoke(true, $"[Scan] {ScanStatus}"); return; }

        // Build column map from header — order-independent, unknown columns are ignored
        var colMap = CsvService.BuildColumnMap(allRows[0], CsvService.MediaColumns);

        // Path column is mandatory — abort if not present
        int pathCol = colMap[Array.IndexOf(CsvService.MediaColumns, "Path")];
        if (pathCol < 0) { ScanStatus = "CSV has no 'Path' column — cannot import."; OnStatus?.Invoke(true, $"[Scan] {ScanStatus}"); return; }

        var existing = _tab.Entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingQr = _tab.Entries.Select(e => e.QrCode)
                             .Where(q => !string.IsNullOrWhiteSpace(q))
                             .ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = 0;

        foreach (var rawRow in allRows.Skip(1))
        {
            // Remap to canonical column order
            var row = CsvService.RemapRow(rawRow, colMap, CsvService.MediaColumns.Length);

            string entryPath = StripQuotes(row[3]);  // Path is canonical col 3
            if (string.IsNullOrWhiteSpace(entryPath)) continue;
            if (existing.Contains(entryPath)) continue;

            string displayText = row[1].Trim();
            string qrRaw = row[2].Trim();
            string artPath = StripQuotes(row[4]);
            string artFit = row[5].Trim();
            string artBackPath = StripQuotes(row[6]);
            string state = string.IsNullOrWhiteSpace(row[7]) ? "new" : row[7].Trim();

            string qrCode = string.IsNullOrWhiteSpace(qrRaw)
                ? ""
                : Services.CsvService.MakeUniqueQrCode(
                    Services.QrCodeValidator.TrimToQrLimit(qrRaw), existingQr);
            if (!string.IsNullOrWhiteSpace(qrCode)) existingQr.Add(qrCode);

            var entry = new MediaEntry
            {
                TypeDigit = _tab.TypeDigit,
                Path = entryPath,
                DisplayText = displayText,
                QrCode = qrCode,
                ArtPath = artPath,
                ArtFit = artFit,
                ArtBackPath = artBackPath,
                State = state,
            };
            var importProfile = DefaultProfile;
            if (importProfile != null) entry.ApplyLayoutProfile(importProfile);
            _tab.AddEntryDirect(entry);
            existing.Add(entryPath);
            added++;
        }

        _tab.RevalidateAll();
        ScanStatus = added > 0 ? $"Imported {added} new entry(s)." : "No new entries found.";
        OnStatus?.Invoke(false, $"[Scan] {ScanStatus}");
    }

    [RelayCommand]
    private async Task BrowseArtDirOverride()
    {
        var opts = new FolderPickerOpenOptions { Title = "Select art directory override", AllowMultiple = false };
        if (Directory.Exists(ArtDirOverride))
            opts.SuggestedStartLocation = await GetStorageProvider().TryGetFolderFromPathAsync(ArtDirOverride);
        var folders = await GetStorageProvider().OpenFolderPickerAsync(opts);
        if (folders.Count > 0)
            ArtDirOverride = folders[0].Path.LocalPath;
    }

    [RelayCommand]
    private void ScanArt()
    {
        int updated = 0;
        foreach (var entry in _tab.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path)) continue;

            // Determine search dir: if path is a file use its parent, if folder use it directly
            string searchDir = File.Exists(entry.Path)
                ? Path.GetDirectoryName(entry.Path) ?? ""
                : Directory.Exists(entry.Path) ? entry.Path : "";

            if (string.IsNullOrWhiteSpace(searchDir)) continue;

            // Only scan for front art if not already set
            if (string.IsNullOrWhiteSpace(entry.ArtPath))
            {
                string art = FindArt(searchDir, entry.DisplayText, entry.QrCode);
                if (!string.IsNullOrEmpty(art))
                {
                    entry.ArtPath = art;
                    // Only set ArtFit from profile — don't overwrite font/color/etc on existing entries
                    var artProfile = DefaultProfile;
                    if (artProfile != null && string.IsNullOrWhiteSpace(entry.ArtFit))
                        entry.ArtFit = artProfile.ArtFit;
                    updated++;
                }
            }

            // Only scan for back art if not already set
            if (string.IsNullOrWhiteSpace(entry.ArtBackPath) && !string.IsNullOrWhiteSpace(entry.QrCode))
            {
                string backArt = FindBackArt(searchDir, entry.QrCode);
                if (!string.IsNullOrEmpty(backArt))
                {
                    entry.ArtBackPath = backArt;
                    updated++;
                }
            }
        }

        ScanStatus = updated > 0 ? $"Art updated for {updated} entry(s)." : "No art found for any entries.";
        OnStatus?.Invoke(false, $"[Scan] {ScanStatus}");
    }

    // ── Art finding ───────────────────────────────────────────────────────

    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".heic" };

    /// <summary>
    /// Finds an art image for a given media path based on pattern and optional override dir.
    /// searchDir: the directory of the scanned path (file's parent or folder itself).
    /// </summary>
    private string FindArt(string searchDir, string displayName, string qrCode)
    {
        string dir = !string.IsNullOrWhiteSpace(ArtDirOverride) && Directory.Exists(ArtDirOverride)
            ? ArtDirOverride
            : searchDir;

        if (!Directory.Exists(dir)) return "";

        var images = Directory.GetFiles(dir)
            .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();

        if (!images.Any()) return "";

        string pattern = ArtPattern.Trim().ToLowerInvariant();

        // Blank pattern: pick the largest file (most likely the full-res art)
        if (string.IsNullOrEmpty(pattern))
            return images.OrderByDescending(f => new System.IO.FileInfo(f).Length).First();

        return pattern switch
        {
            "first" => images.First(),
            "last" => images.Last(),
            "display" => images.FirstOrDefault(f =>
                             Path.GetFileNameWithoutExtension(f)
                                 .Contains(displayName, StringComparison.OrdinalIgnoreCase)) ?? "",
            "qr" => images.FirstOrDefault(f =>
                             Path.GetFileNameWithoutExtension(f)
                                 .Contains(qrCode, StringComparison.OrdinalIgnoreCase)) ?? "",
            _ => MatchByPatternTerms(images, ArtPattern),
        };
    }

    /// <summary>
    /// Looks for a back-art image named exactly &lt;qrCode&gt;_art_back.&lt;ext&gt; in the
    /// same directory FindArt would search (respects ArtDirOverride).
    /// </summary>
    private string FindBackArt(string searchDir, string qrCode)
    {
        string dir = !string.IsNullOrWhiteSpace(ArtDirOverride) && Directory.Exists(ArtDirOverride)
            ? ArtDirOverride
            : searchDir;
        if (!Directory.Exists(dir)) return "";

        string stem = qrCode.Trim() + "_art_back";
        foreach (var ext in ImageExtensions)
        {
            string candidate = Path.Combine(dir, stem + ext);
            if (File.Exists(candidate)) return candidate;
        }
        return "";
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string StripQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1].Trim();
        return s;
    }

    /// <summary>
    /// Returns the first image whose filename contains ANY of the comma-separated pattern terms.
    /// </summary>
    private static string MatchByPatternTerms(List<string> images, string pattern)
    {
        var terms = pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var term in terms)
        {
            var match = images.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Contains(term, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return "";
    }

    private static IStorageProvider GetStorageProvider()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Find first visible window since we manage windows manually
            var window = desktop.Windows.FirstOrDefault(w => w.IsVisible);
            if (window != null)
                return Avalonia.Controls.TopLevel.GetTopLevel(window)!.StorageProvider;
        }
        throw new InvalidOperationException("No active window found.");
    }
}