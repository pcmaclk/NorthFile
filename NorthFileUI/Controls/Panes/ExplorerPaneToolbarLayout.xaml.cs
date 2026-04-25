using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NorthFileUI.Controls;

public sealed partial class ExplorerPaneToolbarLayout : UserControl
{
    public static readonly DependencyProperty LeadingContentProperty =
        DependencyProperty.Register(
            nameof(LeadingContent),
            typeof(UIElement),
            typeof(ExplorerPaneToolbarLayout),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty CenterContentProperty =
        DependencyProperty.Register(
            nameof(CenterContent),
            typeof(UIElement),
            typeof(ExplorerPaneToolbarLayout),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty TrailingContentProperty =
        DependencyProperty.Register(
            nameof(TrailingContent),
            typeof(UIElement),
            typeof(ExplorerPaneToolbarLayout),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty LayoutHeightProperty =
        DependencyProperty.Register(
            nameof(LayoutHeight),
            typeof(double),
            typeof(ExplorerPaneToolbarLayout),
            new PropertyMetadata(double.NaN, OnBindablePropertyChanged));

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(ColumnSpacing),
            typeof(double),
            typeof(ExplorerPaneToolbarLayout),
            new PropertyMetadata(0d, OnBindablePropertyChanged));

    public ExplorerPaneToolbarLayout()
    {
        InitializeComponent();
    }

    public UIElement? LeadingContent
    {
        get => (UIElement?)GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public UIElement? CenterContent
    {
        get => (UIElement?)GetValue(CenterContentProperty);
        set => SetValue(CenterContentProperty, value);
    }

    public UIElement? TrailingContent
    {
        get => (UIElement?)GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    public double LayoutHeight
    {
        get => (double)GetValue(LayoutHeightProperty);
        set => SetValue(LayoutHeightProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    private static void OnBindablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ExplorerPaneToolbarLayout)d).Bindings.Update();
    }
}
