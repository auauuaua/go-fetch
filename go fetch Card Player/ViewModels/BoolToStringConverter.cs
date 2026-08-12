using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CardPlayer.ViewModels;

public class BoolToStringConverter : IValueConverter
{
    public static readonly BoolToStringConverter SelectShiftKey =
        new("✕ Done Selecting", "Select Shift Keys");

    private readonly string _trueValue;
    private readonly string _falseValue;

    public BoolToStringConverter(string trueValue, string falseValue)
    {
        _trueValue  = trueValue;
        _falseValue = falseValue;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? _trueValue : _falseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
