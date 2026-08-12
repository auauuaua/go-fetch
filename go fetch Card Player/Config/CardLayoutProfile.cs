using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.Config;

public partial class CardLayoutProfile : ObservableObject
{
    [ObservableProperty] private string _name = "Default";
    [ObservableProperty] private string _textSide = "front";
    [ObservableProperty] private string _textFont = "Arial";
    [ObservableProperty] private string _textStyle = "Normal";
    [ObservableProperty] private int _textSize = 36;
    [ObservableProperty] private string _textColor = "#000000";
    [ObservableProperty] private string _artFit = "fill";
    [ObservableProperty] private string _frontBgColor = "#FFFFFF";
    [ObservableProperty] private string _backBgColor = "#FFFFFF";
}