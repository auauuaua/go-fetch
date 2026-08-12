using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CardPlayer.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            ScaleToScreen();

            PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == nameof(WindowState))
                {
                    UpdateMaximizeIcon();
                    UpdateMaximizedPadding();
                }
            };

            AddHandler(InputElement.KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Tunnel);
            AddHandler(InputElement.PointerPressedEvent, OnGlobalPointerPressed, RoutingStrategies.Tunnel);
        }

        // ── Dirty-check on close ──────────────────────────────────────────────

        /// <summary>
        /// Intercepts the window-closing event. If there are unsaved changes, shows a
        /// "Save before exit?" dialog. The user can choose to save-then-close or
        /// discard-then-close. Dismissing the dialog via the OS (e.g. Alt+F4 on the
        /// dialog itself) cancels the close of the main window.
        /// </summary>
        protected override async void OnClosing(WindowClosingEventArgs e)
        {
            // If we already confirmed via the dialog, let the close proceed.
            if (_skipDirtyCheck)
            {
                base.OnClosing(e);
                return;
            }

            if (DataContext is CardPlayer.ViewModels.MainWindowViewModel vm && vm.AnyDirty)
            {
                // Cancel the native close so we can await the async dialog.
                e.Cancel = true;

                var dialog = new SaveOnExitDialog();
                await dialog.ShowDialog(this);

                if (dialog.ShouldSave)
                    vm.SaveAllCommand.Execute(null);
                else
                    vm.DiscardAllCommand.Execute(null);

                // Allow the next Close() call to bypass this check.
                _skipDirtyCheck = true;
                Close();
                return;
            }

            base.OnClosing(e);
        }

        // Set to true after the user confirms via the dialog so the re-entrant
        // Close() call is allowed through without showing the dialog again.
        private bool _skipDirtyCheck;

        // ── Helpers ───────────────────────────────────────────────────────────

        private void CommitActiveTextBox()
        {
            TopLevel.GetTopLevel(this)?.FocusManager?.ClearFocus();
        }

        private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+S: commit then save
            if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
            {
                e.Handled = true;
                Dispatcher.UIThread.Post(() =>
                {
                    CommitActiveTextBox();
                    if (DataContext is CardPlayer.ViewModels.MainWindowViewModel vm &&
                        vm.SaveAllCommand.CanExecute(null))
                        vm.SaveAllCommand.Execute(null);
                }, DispatcherPriority.Input);
                return;
            }

            // Enter on any single-line TextBox: commit the field
            if (e.Source is not TextBox tb) return;
            if (tb.AcceptsReturn) return;

            if (e.Key == Key.Enter)
            {
                CommitActiveTextBox();
                e.Handled = true;
            }
        }

        private void OnGlobalPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this)?.FocusManager is not { } fm) return;
            if (fm.GetFocusedElement() is not TextBox) return;
            if (e.Source is TextBox) return;
            CommitActiveTextBox();
        }

        private void ScaleToScreen()
        {
            var screen = Screens.Primary;
            if (screen == null) return;
            double w = screen.WorkingArea.Width * 0.80;
            double h = screen.WorkingArea.Height * 0.80;
            Width = System.Math.Max(w, MinWidth);
            Height = System.Math.Max(h, MinHeight);
        }

        private void UpdateMaximizedPadding()
        {
            Padding = WindowState == WindowState.Maximized
                ? new Thickness(8)
                : new Thickness(0);
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            // Don't start a drag if the press originated on an interactive control
            if (e.Source is Visual v &&
                (v.FindAncestorOfType<Button>() != null ||
                 v.FindAncestorOfType<ComboBox>() != null ||
                 v.FindAncestorOfType<ToggleSwitch>() != null ||
                 v.FindAncestorOfType<TextBox>() != null)) return;
            BeginMoveDrag(e);
        }

        private void MinimizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            => Close(); // OnClosing will intercept if dirty

        private void UpdateMaximizeIcon()
        {
            var icon = this.FindControl<TextBlock>("MaximizeIcon");
            if (icon != null)
                icon.Text = WindowState == WindowState.Maximized ? "❐" : "□";
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
