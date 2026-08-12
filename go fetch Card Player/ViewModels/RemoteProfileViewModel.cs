using System;
using System.Collections.ObjectModel;
using System.Linq;
using CardPlayer.Config;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CardPlayer.ViewModels;

public partial class RemoteProfileViewModel : ViewModelBase
{
    public RemoteProfile Profile { get; }

    public event Action<bool>? LearnStateChanged;
    public event Action? CellModified;
    public event Action<string, string>? NameChanged;
    public event Action? GridResized;

    [ObservableProperty] private ObservableCollection<RemoteCellEditorViewModel> _cells = new();
    [ObservableProperty] private RemoteCellEditorViewModel? _selectedCell;
    [ObservableProperty] private bool _isLearning;

    public string Name
    {
        get => Profile.Name;
        set
        {
            string old = Profile.Name;
            Profile.Name = value;
            OnPropertyChanged();
            if (old != value) NameChanged?.Invoke(old, value);
        }
    }

    public int Rows
    {
        get => Profile.Rows;
        set { if (value >= 1) { Profile.Rows = value; OnPropertyChanged(); RebuildGrid(); CellModified?.Invoke(); GridResized?.Invoke(); } }
    }

    public int Cols
    {
        get => Profile.Cols;
        set { if (value >= 1) { Profile.Cols = value; OnPropertyChanged(); RebuildGrid(); CellModified?.Invoke(); GridResized?.Invoke(); } }
    }

    public RemoteProfileViewModel(RemoteProfile profile)
    {
        Profile = profile;
        BuildGrid();
    }

    private void BuildGrid()
    {
        Cells.Clear();
        for (int r = 0; r < Profile.Rows; r++)
        {
            for (int c = 0; c < Profile.Cols; c++)
            {
                var existing = Profile.Cells.FirstOrDefault(x => x.Row == r && x.Col == c);
                if (existing == null)
                {
                    existing = new RemoteCell { Row = r, Col = c };
                    Profile.Cells.Add(existing);
                }
                var cellVm = new RemoteCellEditorViewModel(existing);
                cellVm.Modified += () => CellModified?.Invoke();
                Cells.Add(cellVm);
            }
        }
        SelectedCell = Cells.FirstOrDefault();
        if (SelectedCell != null) SelectedCell.IsSelected = true;
    }

    private void RebuildGrid()
    {
        bool wasLearning = IsLearning;
        if (wasLearning) CancelLearn();
        Profile.Cells.RemoveAll(c => c.Row >= Profile.Rows || c.Col >= Profile.Cols);
        BuildGrid();
    }

    [RelayCommand]
    private void SelectCell(RemoteCellEditorViewModel cell)
    {
        if (SelectedCell != null)
        {
            SelectedCell.IsSelected = false;
            SelectedCell.IsLearning = false;
        }
        SelectedCell = cell;
        cell.IsSelected = true;
        // If learn is active, move the learning highlight to the new cell
        if (IsLearning)
            cell.IsLearning = true;
    }

    [RelayCommand]
    private void ClearSelected()
    {
        if (SelectedCell == null) return;
        SelectedCell.Label = "";
        SelectedCell.IrCode = "";
        CellModified?.Invoke();
    }

    [RelayCommand]
    private void StartLearn()
    {
        if (SelectedCell == null) return;
        foreach (var c in Cells) c.IsLearning = false;
        IsLearning = true;
        SelectedCell.IsLearning = true;
        LearnStateChanged?.Invoke(true);
    }

    [RelayCommand]
    private void CancelLearn()
    {
        foreach (var c in Cells) c.IsLearning = false;
        IsLearning = false;
        LearnStateChanged?.Invoke(false);
    }

    private DateTime _lastLearnTime = DateTime.MinValue;
    private const int LearnDebounceMs = 300;

    public void ReceiveLearnCode(string irCode)
    {
        if (!IsLearning || SelectedCell == null) return;

        // Debounce — ignore repeat IR frames within the window
        var now = DateTime.UtcNow;
        if ((now - _lastLearnTime).TotalMilliseconds < LearnDebounceMs) return;
        _lastLearnTime = now;

        SelectedCell.ReceiveLearnCode(irCode);
        CellModified?.Invoke();

        // Auto-advance to the next cell on the UI thread
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsLearning) return;
            var currentIdx = Cells.IndexOf(SelectedCell);
            var next = Cells.Skip(currentIdx + 1).FirstOrDefault();
            if (next != null)
            {
                SelectedCell.IsLearning = false;
                SelectedCell.IsSelected = false;
                SelectedCell = next;
                next.IsSelected = true;
                next.IsLearning = true;
            }
            else
            {
                // Reached the last cell — end learning
                CancelLearnCommand.Execute(null);
            }
        });
    }
}