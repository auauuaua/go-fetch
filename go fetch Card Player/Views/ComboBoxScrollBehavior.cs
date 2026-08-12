using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CardPlayer.Views;

/// <summary>
/// Attached behavior that prevents the scroll wheel from changing a ComboBox
/// value when the dropdown is closed. Apply via a global Style in AppStyles.
/// </summary>
public static class ComboBoxScrollBehavior
{
    public static readonly AttachedProperty<bool> PreventScrollWhenClosedProperty =
        AvaloniaProperty.RegisterAttached<ComboBox, bool>(
            "PreventScrollWhenClosed",
            typeof(ComboBoxScrollBehavior));

    public static bool GetPreventScrollWhenClosed(ComboBox element)
        => element.GetValue(PreventScrollWhenClosedProperty);

    public static void SetPreventScrollWhenClosed(ComboBox element, bool value)
        => element.SetValue(PreventScrollWhenClosedProperty, value);

    static ComboBoxScrollBehavior()
    {
        PreventScrollWhenClosedProperty.Changed.AddClassHandler<ComboBox>(OnPropertyChanged);
    }

    private static void OnPropertyChanged(ComboBox combo, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            combo.AddHandler(InputElement.PointerWheelChangedEvent, OnWheelChanged,
                Avalonia.Interactivity.RoutingStrategies.Tunnel);
        else
            combo.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheelChanged);
    }

    private static void OnWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (sender is ComboBox { IsDropDownOpen: false })
            e.Handled = true;
    }
}
