using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CardPlayer.ViewModels;

/// <summary>
/// Converts an int to bool (or double for opacity) by equality check.
/// Pass ConverterParameter="opacity" to get 1.0/0.0 instead of true/false.
/// </summary>
public class EqualityConverter : IValueConverter
{
    public static readonly EqualityConverter Zero = new(0);
    public static readonly EqualityConverter One  = new(1);

    private readonly int _target;
    public EqualityConverter(int target) => _target = target;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool match = value is int i && i == _target;
        if (parameter is string s && s == "opacity")
            return match ? 1.0 : 0.0;
        return match;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
