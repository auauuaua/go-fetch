using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using CardPlayer.Config;
using CardPlayer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

/// <summary>
/// Manages Card_Layout_Profiles.json and drives the profile selector in the Cards tab.
/// Does NOT own the live text fields — those live on MediaEntry (per row).
/// Instead, it reads from / writes to a "current settings" snapshot that the
/// Cards tab exposes via the LiveXxx properties.
/// </summary>
public partial class CardLayoutProfilesViewModel : ViewModelBase
{
    private static readonly string ProfilesPath = AppPaths.Combine("Card_Layout_Profiles.json");

    [ObservableProperty] private ObservableCollection<CardLayoutProfile> _profiles = new();
    [ObservableProperty] private CardLayoutProfile? _selectedProfile;
    [ObservableProperty] private string _profileName = "";

    /// <summary>True only when a profile is selected — name field is read-only when blank/unmatched.</summary>
    public bool IsProfileNameEditable => _selectedProfile != null;

    // Fired when user picks a profile, so CardsViewModel can apply it
    public event Action<CardLayoutProfile>? ProfileApplyRequested;

    // Delegate that reads current live settings from CardsViewModel
    public Func<CardLayoutProfile>? SnapshotLiveSettings;

    private bool _suppressSync;

    // ── Init ──────────────────────────────────────────────────────────────
    public void Load()
    {
        if (File.Exists(ProfilesPath))
        {
            try
            {
                var json = File.ReadAllText(ProfilesPath);
                var loaded = JsonSerializer.Deserialize<CardLayoutProfile[]>(json);
                if (loaded?.Length > 0)
                {
                    Profiles = new ObservableCollection<CardLayoutProfile>(loaded);
                    _suppressSync = true;
                    SelectedProfile = null;
                    ProfileName = "";
                    _suppressSync = false;
                    return;
                }
            }
            catch { }
        }
        // Default empty state — no profiles yet
        Profiles = new();
        SelectedProfile = null;
        ProfileName = "";
    }

    // ── Profile selection ─────────────────────────────────────────────────
    partial void OnSelectedProfileChanged(CardLayoutProfile? value)
    {
        if (_suppressSync || value == null) return;
        _suppressSync = true;
        ProfileName = value.Name;
        _suppressSync = false;
        OnPropertyChanged(nameof(IsProfileNameEditable));
        ProfileApplyRequested?.Invoke(value);
    }

    partial void OnProfileNameChanged(string value)
    {
        if (_suppressSync || SelectedProfile == null) return;
        SelectedProfile.Name = value;
        Save();
    }

    /// <summary>
    /// Call this whenever live settings change so the selector blanks
    /// if they no longer match the selected profile.
    /// </summary>
    public void OnLiveSettingsChanged()
    {
        if (_suppressSync || SelectedProfile == null || SnapshotLiveSettings == null) return;
        var live = SnapshotLiveSettings();
        if (!ProfileMatches(SelectedProfile, live))
        {
            _suppressSync = true;
            SelectedProfile = null;
            ProfileName = "";
            _suppressSync = false;
            OnPropertyChanged(nameof(IsProfileNameEditable));
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────
    [RelayCommand]
    private void AddProfile()
    {
        if (SnapshotLiveSettings == null) return;
        var p = SnapshotLiveSettings();

        // Start with whatever is typed, or "New Profile"
        string baseName = string.IsNullOrWhiteSpace(ProfileName) ? "New Profile" : ProfileName.Trim();

        // Find a unique name by appending a sequential number if needed
        string candidate = baseName;
        int suffix = 2;
        while (Profiles.Any(x => x.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} {suffix}";
            suffix++;
        }

        p.Name = candidate;
        Profiles.Add(p);
        _suppressSync = true;
        SelectedProfile = p;
        ProfileName = p.Name;
        _suppressSync = false;
        OnPropertyChanged(nameof(IsProfileNameEditable));
        Save();
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null || Profiles.Count == 0) return;
        var toRemove = SelectedProfile;
        var idx = Profiles.IndexOf(toRemove);
        Profiles.Remove(toRemove);
        _suppressSync = true;
        SelectedProfile = Profiles.Count > 0 ? Profiles[Math.Max(0, idx - 1)] : null;
        ProfileName = SelectedProfile?.Name ?? "";
        _suppressSync = false;
        OnPropertyChanged(nameof(IsProfileNameEditable));
        Save();
    }

    // ── Persistence ───────────────────────────────────────────────────────
    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Profiles.ToArray(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ProfilesPath, json);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    private static bool ProfileMatches(CardLayoutProfile p, CardLayoutProfile live) =>
        p.TextSide == live.TextSide &&
        p.TextFont == live.TextFont &&
        p.TextStyle == live.TextStyle &&
        p.TextSize == live.TextSize &&
        p.TextColor == live.TextColor &&
        p.ArtFit == live.ArtFit &&
        p.FrontBgColor == live.FrontBgColor &&
        p.BackBgColor == live.BackBgColor;
}