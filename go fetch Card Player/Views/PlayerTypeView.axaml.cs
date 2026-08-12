using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using System.Linq;
using CardPlayer.ViewModels;

namespace CardPlayer.Views;

public partial class PlayerTypeView : UserControl
{
    public PlayerTypeView()
    {
        AvaloniaXamlLoader.Load(this);
        var browseBtn = this.FindControl<Button>("BrowseProgramButton");
        if (browseBtn != null)
            browseBtn.Click += OnBrowseProgramClicked;
    }

    private async void OnBrowseProgramClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var opts = new FilePickerOpenOptions
        {
            Title = "Select Program",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable") { Patterns = new[] { "*.exe", "*.bat", "*.cmd", "*.sh" } },
                new FilePickerFileType("All Files")  { Patterns = new[] { "*" } }
            }
        };

        // Open at the directory of the currently set program path if it exists
        if (DataContext is PlayerTypeViewModel vm && !string.IsNullOrWhiteSpace(vm.ProgramPath))
        {
            var dir = System.IO.Path.GetDirectoryName(vm.ProgramPath);
            if (dir != null && System.IO.Directory.Exists(dir))
                opts.SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(dir);
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(opts);

        var file = files?.FirstOrDefault();
        if (file == null) return;

        if (DataContext is PlayerTypeViewModel vm2)
            vm2.ProgramPath = file.TryGetLocalPath() ?? file.Path.LocalPath;
    }
}