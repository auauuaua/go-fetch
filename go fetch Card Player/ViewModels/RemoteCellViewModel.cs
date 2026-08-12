using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CardPlayer.Models;

namespace CardPlayer.ViewModels;

/// <summary>
/// Represents one cell in the remote button grid.
/// Wraps an IrCodeEntry and tracks selection state.
/// </summary>
public partial class RemoteCellViewModel : ViewModelBase
{
    public IrCodeEntry Entry { get; }
    public int Row { get; }
    public int Col { get; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isLearning;

    // Empty cell — no mapping assigned yet
    public bool IsEmpty => string.IsNullOrWhiteSpace(Entry.RemoteLabel)
                        && string.IsNullOrWhiteSpace(Entry.KeySend)
                        && string.IsNullOrWhiteSpace(Entry.IrCode);

    public RemoteCellViewModel(IrCodeEntry entry, int row, int col)
    {
        Entry = entry;
        Row   = row;
        Col   = col;
    }
}
