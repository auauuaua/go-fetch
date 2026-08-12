using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace CardPlayer.ViewModels;

/// <summary>
/// Converts between int and string for TextBox binding.
/// Empty or invalid input is treated as 0.
/// </summary>
public class NullableIntConverter : IValueConverter
{
    public static readonly NullableIntConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? i.ToString() : "0";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            s = s.Trim();
            if (int.TryParse(s, out int result)) return result;
            // Handle lone minus sign while typing — treat as 0
            if (s == "-") return 0;
        }
        return 0;
    }
}
