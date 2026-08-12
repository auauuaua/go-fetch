using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CardPlayer.Services;
using CardPlayer.ViewModels;
using CardPlayer.Views;
using System;
using System.Linq;

namespace CardPlayer;

public partial class App : Application
{
    private SerialListenerService? _serialService;
    private MainWindow? _editorWindow;
    private MainWindowViewModel? _editorVm;
    private TrayIcon? _trayIcon;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Ensure %LOCALAPPDATA%\gofetch Card Player exists before any file I/O
            AppPaths.EnsureDataDir();

            _serialService = new SerialListenerService();

            // Route IR to learn mode — checked on the serial thread via IsLearning flag
            // then dispatched to UI thread to update the cell
            _serialService.IrReceived += irCode =>
            {
                // Check learn mode flag first (thread-safe read)
                if (_editorVm == null || !_editorVm.RemoteSetupVm.IsAnyProfileLearning)
                    return false;

                // Consume the code and dispatch to UI thread
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    _editorVm?.RemoteSetupVm.TryReceiveLearnCode(irCode));
                return true;
            };

            _serialService.Start();
            SetupTrayIcon();

            // Start listening for signals from any second-instance launch attempts
            SingleInstanceService.StartServer(ShowEditor);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        var openItem = new NativeMenuItem("Open Editor");
        openItem.Click += (_, _) => ShowEditor();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += async (_, _) =>
        {
            if (_editorVm != null && _editorVm.AnyDirty)
            {
                // Need a visible owner window for ShowDialog — show it if hidden.
                bool wasHidden = _editorWindow != null && !_editorWindow.IsVisible;
                if (wasHidden) _editorWindow!.Show();

                var dialog = new Views.SaveOnExitDialog();
                await dialog.ShowDialog(_editorWindow!);

                if (dialog.ShouldSave)
                    _editorVm.SaveAllCommand.Execute(null);

                // Re-hide the editor if it wasn't open when the user clicked Exit.
                if (wasHidden) _editorWindow!.Hide();
            }

            _serialService?.Dispose();
            _trayIcon?.Dispose();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        };

        var menu = new NativeMenu();
        menu.Add(openItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(
                              new Uri("avares://go fetch Card Player/Resources/CardPlayer.ico"))),
            ToolTipText = "go fetch Card Player",
            Menu = menu,
            IsVisible = true
        };

        _trayIcon.Clicked += (_, _) => ShowEditor();

        _serialService!.StatusChanged += status =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                _trayIcon.ToolTipText = $"CardPlayer — {status}");
    }

    private void ShowEditor()
    {
        if (_editorWindow == null || !_editorWindow.IsLoaded)
        {
            _editorVm = new MainWindowViewModel();
            _editorWindow = new MainWindow { DataContext = _editorVm };
            _editorWindow.Closing += (_, e) =>
            {
                e.Cancel = true;
                _editorWindow.Hide();
            };

            // Forward passthrough state changes to the serial service
            _editorVm.PassthroughChanged += (enabled, mediaTypeName) =>
            {
                // Translate media type name → type digit
                var vm = _editorVm.PlayersVm.Types
                    .FirstOrDefault(t => t.Config.PlayerType == mediaTypeName);
                string typeDigit = vm?.Config.TypeDigit ?? "";
                _serialService?.SetPassthrough(enabled, typeDigit);
            };

            // After a save, refresh the passthrough IR map so edits (e.g. TCP port) apply immediately
            _editorVm.ChangesSaved += () => _serialService?.InvalidatePassthroughCache();

            // Wire Go Fetch button to simulate a card insertion
            _editorVm.CardsVm.SimulateQrCodeAction = qr => _serialService?.SimulateQrCode(qr);

            // Push the state that was restored from Hardware.json during construction
            // (PassthroughChanged was null then, so the service missed it)
            {
                var restoredName = _editorVm.SelectedPassthroughPlayerType ?? "";
                var restoredVm = _editorVm.PlayersVm.Types
                    .FirstOrDefault(t => t.Config.PlayerType == restoredName);
                _serialService?.SetPassthrough(
                    _editorVm.RemotePassthroughEnabled,
                    restoredVm?.Config.TypeDigit ?? "");
            }
        }
        _editorWindow.Show();
        _editorWindow.Activate();
    }
}