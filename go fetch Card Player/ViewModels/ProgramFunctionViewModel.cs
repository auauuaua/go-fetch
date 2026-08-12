using System;
using CardPlayer.Config;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CardPlayer.ViewModels;

public partial class ProgramFunctionViewModel : ViewModelBase
{
    public ProgramFunction Function { get; }

    public event Action? DataEdited;

    public string Name
    {
        get => Function.Name;
        set { Function.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); DataEdited?.Invoke(); }
    }

    public string KeySend
    {
        get => Function.KeySend;
        set { Function.KeySend = value; OnPropertyChanged(); DataEdited?.Invoke(); }
    }

    // Used in dropdowns
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "" : Name;

    public ProgramFunctionViewModel(ProgramFunction function)
    {
        Function = function;
    }
}
