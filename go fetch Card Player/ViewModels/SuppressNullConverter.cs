using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace CardPlayer.ViewModels;

/// <summary>
/// Two-way converter that passes values through normally,
/// but converts null back to DoNothing so the DataGrid can never
/// clear the VM's SelectedItem when it loses focus.
/// </summary>
public class SuppressNullConverter : IValueConverter
{
    public static readonly SuppressNullConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value ?? BindingOperations.DoNothing;
}
