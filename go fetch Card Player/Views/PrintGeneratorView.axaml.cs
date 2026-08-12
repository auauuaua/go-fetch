using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using CardPlayer.ViewModels;

namespace CardPlayer.Views;

public partial class PrintGeneratorView : UserControl
{
    public PrintGeneratorView() => AvaloniaXamlLoader.Load(this);

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not PrintGeneratorViewModel vm) return;

        foreach (var item in e.RemovedItems.OfType<ReadyEntryViewModel>())
            vm.GridSelectedEntries.Remove(item);
        foreach (var item in e.AddedItems.OfType<ReadyEntryViewModel>())
            if (!vm.GridSelectedEntries.Contains(item))
                vm.GridSelectedEntries.Add(item);
    }

    private void OnCellPointerPressed(object? sender, DataGridCellPointerPressedEventArgs e)
    {
        // Column 0 is the checkbox column — toggle IsSelected on single click
        // without requiring the row to be selected first
        if (e.Column.DisplayIndex != 0) return;
        if (e.PointerPressedEventArgs.GetCurrentPoint(null).Properties.IsLeftButtonPressed
            && e.Row.DataContext is ReadyEntryViewModel entry)
        {
            entry.IsSelected = !entry.IsSelected;
            e.PointerPressedEventArgs.Handled = true;
        }
    }
}
