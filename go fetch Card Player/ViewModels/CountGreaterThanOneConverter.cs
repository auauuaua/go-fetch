using System;
using System.Collections;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CardPlayer.ViewModels;

/// <summary>Returns true when a collection has more than one item — used to disable Delete when only one profile remains.</summary>
public class CountGreaterThanOneConverter : IValueConverter
{
    public static readonly CountGreaterThanOneConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int count) return count > 1;
        if (value is ICollection col) return col.Count > 1;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
