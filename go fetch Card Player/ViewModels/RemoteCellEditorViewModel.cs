using System;
using CardPlayer.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.ViewModels;

public partial class RemoteCellEditorViewModel : ViewModelBase
{
    public RemoteCell Cell { get; }
    public int Row => Cell.Row;
    public int Col => Cell.Col;

    /// <summary>Raised when the user edits the label or IR code (not during construction).</summary>
    public event Action? Modified;
    private bool _initialized;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isLearning;

    // Bindable wrappers so the UI updates immediately when values change
    [ObservableProperty] private string _label = "";
    [ObservableProperty] private string _irCode = "";

    // Keep Cell POCO in sync when VM properties change
    partial void OnLabelChanged(string value) { Cell.Label = value; if (_initialized) Modified?.Invoke(); }
    partial void OnIrCodeChanged(string value) { Cell.IrCode = value; if (_initialized) Modified?.Invoke(); }

    public RemoteCellEditorViewModel(RemoteCell cell)
    {
        Cell = cell;
        _label = cell.Label ?? "";
        _irCode = cell.IrCode ?? "";
        _initialized = true;
    }

    // Called by learn mode — updates the VM property which triggers UI and syncs to Cell
    public void ReceiveLearnCode(string irCode) => IrCode = irCode;
}