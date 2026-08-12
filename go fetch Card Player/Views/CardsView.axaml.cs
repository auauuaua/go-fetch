using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CardPlayer.Models;
using CardPlayer.ViewModels;
using System.IO;
using System.Linq;

namespace CardPlayer.Views;

public partial class CardsView : UserControl
{
    public CardsView() => AvaloniaXamlLoader.Load(this);

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not PlayerTypeTabViewModel tabVm) return;

        var primary = grid.SelectedItem as MediaEntry;

        if (primary != null)
        {
            // Real selection change — push to VM
            tabVm.Selected = primary;
            tabVm.SelectedEntries.Clear();
            foreach (var item in grid.SelectedItems.OfType<MediaEntry>())
                tabVm.SelectedEntries.Add(item);
        }
        else if (tabVm.Selected != null)
        {
            // DataGrid cleared its visual selection (focus loss) — restore it
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (grid.SelectedItem == null && tabVm.Selected != null)
                    grid.SelectedItem = tabVm.Selected;
            }, Avalonia.Threading.DispatcherPriority.Input);
        }
    }

    private void OnCellEditEnded(object? sender, DataGridCellEditEndedEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not PlayerTypeTabViewModel tabVm) return;
        tabVm.OnCellEditEnded();
        tabVm.SetDirty();
    }

    private void OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (grid.DataContext is not PlayerTypeTabViewModel tabVm) return;

        var key = e.Column.SortMemberPath;
        switch (key)
        {
            case "DisplayText": tabVm.SortByDisplayTextCommand.Execute(null); break;
            case "QrCode": tabVm.SortByQrCodeCommand.Execute(null); break;
            case "ComputedStatus": tabVm.SortByStatusCommand.Execute(null); break;
        }

        e.Handled = true;
    }

    private void ScanButton_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnDataGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is DataGrid grid)
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
    }

    private void OnGridDragOver(object? sender, DragEventArgs e)
    {
        // Accept drops that contain file paths
        if (e.Data.Contains(DataFormats.Files))
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnGridDrop(object? sender, DragEventArgs e)
    {
        if (sender is not DataGrid grid) return;
        if (!e.Data.Contains(DataFormats.Files)) return;

        var files = e.Data.GetFiles();
        if (files == null) return;

        // Get the first dropped path (file or folder)
        string? droppedPath = null;
        foreach (var f in files)
        {
            droppedPath = f.TryGetLocalPath();
            if (droppedPath != null) break;
        }
        if (droppedPath == null) return;

        // Find which DataGridRow is under the drop point
        var pos = e.GetPosition(grid);
        var row = grid.GetVisualDescendants()
                      .OfType<DataGridRow>()
                      .FirstOrDefault(r =>
                      {
                          var bounds = r.Bounds;
                          var pt = r.TranslatePoint(new Avalonia.Point(0, 0), grid);
                          if (pt == null) return false;
                          return pos.Y >= pt.Value.Y && pos.Y < pt.Value.Y + bounds.Height;
                      });

        if (row?.DataContext is not MediaEntry entry) return;

        // Determine which column by X position — find column header widths
        // Column order: QR CODE | PATH | ART PATH | ART BACK PATH | STATUS
        double x = pos.X;
        double colX = 0;
        string? targetColumn = null;
        foreach (var col in grid.Columns)
        {
            double colW = col.ActualWidth;
            if (x >= colX && x < colX + colW)
            {
                targetColumn = col.Header?.ToString();
                break;
            }
            colX += colW;
        }

        // Write to the appropriate field based on which column was dropped on
        switch (targetColumn)
        {
            case "PATH":
                entry.Path = droppedPath;
                break;
            case "ART PATH":
                entry.ArtPath = droppedPath;
                break;
            case "ART BACK PATH":
                entry.ArtBackPath = droppedPath;
                break;
            default:
                // Dropped on QR, Status, or between columns — write to Path by default
                entry.Path = droppedPath;
                break;
        }

        // Mark dirty and update the grid
        if (grid.DataContext is PlayerTypeTabViewModel tabVm)
        {
            tabVm.SetDirty();
            tabVm.OnCellEditEnded();
        }

        e.Handled = true;
    }
}