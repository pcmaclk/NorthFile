using Microsoft.UI.Xaml;

namespace NorthFileUI.Controls;

public sealed class BoolStateTrigger : StateTriggerBase
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(bool),
            typeof(BoolStateTrigger),
            new PropertyMetadata(false, OnValueChanged));

    public bool Value
    {
        get => (bool)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BoolStateTrigger trigger && e.NewValue is bool value)
        {
            trigger.SetActive(value);
        }
    }
}
