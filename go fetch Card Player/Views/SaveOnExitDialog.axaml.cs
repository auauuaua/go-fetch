using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace CardPlayer.Views;

/// <summary>
/// Modal dialog shown when the window is closing with unsaved changes.
/// Result is true = save then close, false = discard and close.
/// </summary>
public partial class SaveOnExitDialog : Window
{
    /// <summary>Set after the dialog closes: true = user chose Save, false = user chose Don't Save.</summary>
    public bool ShouldSave { get; private set; }

    public SaveOnExitDialog()
    {
        InitializeComponent();
    }

    private void YesButton_Click(object? sender, RoutedEventArgs e)
    {
        ShouldSave = true;
        Close();
    }

    private void NoButton_Click(object? sender, RoutedEventArgs e)
    {
        ShouldSave = false;
        Close();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
