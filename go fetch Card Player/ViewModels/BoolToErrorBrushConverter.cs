using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace CardPlayer.ViewModels;

/// <summary>
/// Returns a red brush when the value is true (error), otherwise returns the
/// standard muted foreground so status messages colour themselves automatically.
/// </summary>
public sealed class BoolToErrorBrushConverter : IValueConverter
{
    public static readonly BoolToErrorBrushConverter Instance = new();

    private static readonly IBrush ErrorBrush  = new SolidColorBrush(Color.Parse("#E05555"));
    private static readonly IBrush NormalBrush = new SolidColorBrush(Color.Parse("#888888"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ErrorBrush : NormalBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
