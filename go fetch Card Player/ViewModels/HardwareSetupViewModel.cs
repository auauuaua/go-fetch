using CardPlayer.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Text.Json;

namespace CardPlayer.ViewModels;

public partial class HardwareSetupViewModel : ViewModelBase
{
    private string _filePath = "";

    [ObservableProperty] private string _vid = "2E8A";
    [ObservableProperty] private string _pid = "000A";
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _statusMessage = "";
    /// <summary>Forwarded to main window status bar. Set by MainWindowViewModel.</summary>
    public Action<bool, string>? OnStatus;
    [ObservableProperty] private bool _isDirty;

    partial void OnVidChanged(string value) => IsDirty = true;
    partial void OnPidChanged(string value) => IsDirty = true;

    public void LoadFromFile(string path)
    {
        _filePath = path;
        if (File.Exists(path))
        {
            try
            {
                var config = JsonSerializer.Deserialize<HardwareConfig>(File.ReadAllText(path));
                if (config != null) { Vid = config.Vid; Pid = config.Pid; }
            }
            catch { }
        }
        IsDirty = false;
        StatusIsError = false; StatusMessage = $"VID: {Vid}  PID: {Pid}";
        OnStatus?.Invoke(false, $"[Hardware] {StatusMessage}");
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrEmpty(_filePath)) return;
        // Preserve any fields we don't manage in this VM (e.g. DebugLogging)
        Config.HardwareConfig existing = new();
        try { if (File.Exists(_filePath)) existing = JsonSerializer.Deserialize<Config.HardwareConfig>(File.ReadAllText(_filePath)) ?? existing; }
        catch { }
        existing.Vid = Vid;
        existing.Pid = Pid;
        File.WriteAllText(_filePath, JsonSerializer.Serialize(existing,
            new JsonSerializerOptions { WriteIndented = true }));
        IsDirty = false;
        StatusIsError = false; StatusMessage = "Saved. Restart the app for changes to take effect.";
        OnStatus?.Invoke(false, $"[Hardware] {StatusMessage}");
    }
}