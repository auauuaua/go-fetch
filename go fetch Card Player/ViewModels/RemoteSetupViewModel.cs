using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CardPlayer.Config;
using CardPlayer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

public partial class RemoteSetupViewModel : ViewModelBase
{
    private string _filePath = "";

    public event Action? ProfileSaved;
    public event Action<RemoteProfileViewModel?>? SelectedProfileChanged;
    public event Action<string>? ProfileAdded;
    public event Action<string, string>? ProfileRenamed;
    public event Action? GridResized;

    [ObservableProperty] private ObservableCollection<RemoteProfileViewModel> _profiles = new();
    [ObservableProperty] private RemoteProfileViewModel? _selectedProfile;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _statusMessage = "";
    /// <summary>Forwarded to main window status bar. Set by MainWindowViewModel.</summary>
    public Action<bool, string>? OnStatus;
    [ObservableProperty] private bool _isDirty;

    // Volatile so serial thread can read it safely without locking
    private volatile bool _isAnyProfileLearning;
    public bool IsAnyProfileLearning => _isAnyProfileLearning;

    public void LoadFromFile(string path)
    {
        _filePath = path;
        var config = JsonConfigService.LoadRemoteProfiles(path);

        if (!config.Profiles.Any())
        {
            config.Profiles.Add(new RemoteProfile { Name = "Default Remote", Rows = 7, Cols = 3 });
            config.ActiveProfile = "Default Remote";
        }

        Profiles = new ObservableCollection<RemoteProfileViewModel>(
            config.Profiles.Select(p =>
            {
                var vm = new RemoteProfileViewModel(p);
                vm.LearnStateChanged += isLearning => _isAnyProfileLearning = isLearning;
                vm.CellModified += () => IsDirty = true;
                vm.NameChanged += (oldName, newName) => ProfileRenamed?.Invoke(oldName, newName);
                vm.GridResized += () => GridResized?.Invoke();
                return vm;
            }));

        _suppressSelectedProfileDirty = true;
        SelectedProfile = Profiles.FirstOrDefault(p => p.Name == config.ActiveProfile)
                       ?? Profiles.First();
        _suppressSelectedProfileDirty = false;

        IsDirty = false;
        StatusIsError = false; StatusMessage = $"Loaded {Profiles.Count} profile(s).";
        OnStatus?.Invoke(false, $"[Remote] {StatusMessage}");
    }

    private bool _suppressSelectedProfileDirty = false;

    partial void OnSelectedProfileChanged(RemoteProfileViewModel? value)
    {
        if (!_suppressSelectedProfileDirty)
            SelectedProfileChanged?.Invoke(value);
    }

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new RemoteProfile { Name = "New Remote", Rows = 7, Cols = 3 };
        var vm = new RemoteProfileViewModel(profile);
        vm.LearnStateChanged += isLearning => _isAnyProfileLearning = isLearning;
        vm.CellModified += () => IsDirty = true;
        vm.NameChanged += (oldName, newName) => ProfileRenamed?.Invoke(oldName, newName);
        vm.GridResized += () => GridResized?.Invoke();
        Profiles.Add(vm);
        SelectedProfile = vm;
        IsDirty = true;
        ProfileAdded?.Invoke(profile.Name);
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null || Profiles.Count <= 1) return;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.First();
        _isAnyProfileLearning = false;
        IsDirty = true;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_filePath)) return;
        DeduplicateProfileNames();
        var config = new RemoteProfilesConfig
        {
            ActiveProfile = SelectedProfile?.Profile.Name ?? "",
            Profiles = Profiles.Select(p => p.Profile).ToList()
        };
        JsonConfigService.SaveRemoteProfiles(_filePath, config);
        IsDirty = false;
        StatusIsError = false; StatusMessage = $"Saved {Profiles.Count} profile(s).";
        OnStatus?.Invoke(false, $"[Remote] {StatusMessage}");
        ProfileSaved?.Invoke();
    }

    private void DeduplicateProfileNames()
    {
        var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in Profiles)
        {
            string name = string.IsNullOrWhiteSpace(vm.Profile.Name) ? "Remote" : vm.Profile.Name;
            if (!seen.Add(name))
            {
                int suffix = 2;
                string candidate;
                do { candidate = $"{name} ({suffix++})"; }
                while (!seen.Add(candidate));
                vm.Name = candidate;
            }
        }
    }

    public bool TryReceiveLearnCode(string irCode)
    {
        if (SelectedProfile?.IsLearning == true)
        {
            SelectedProfile.ReceiveLearnCode(irCode);
            IsDirty = true;
            return true;
        }
        return false;
    }
}