using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI.Controls;

public sealed partial class ExplorerPaneToolbarChrome : UserControl
{
    public static readonly DependencyProperty ToolbarContentProperty =
        DependencyProperty.Register(
            nameof(ToolbarContent),
            typeof(UIElement),
            typeof(ExplorerPaneToolbarChrome),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromePaddingProperty =
        DependencyProperty.Register(
            nameof(ChromePadding),
            typeof(Thickness),
            typeof(ExplorerPaneToolbarChrome),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeBackgroundProperty =
        DependencyProperty.Register(
            nameof(ChromeBackground),
            typeof(Brush),
            typeof(ExplorerPaneToolbarChrome),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeBorderBrushProperty =
        DependencyProperty.Register(
            nameof(ChromeBorderBrush),
            typeof(Brush),
            typeof(ExplorerPaneToolbarChrome),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeBorderThicknessProperty =
        DependencyProperty.Register(
            nameof(ChromeBorderThickness),
            typeof(Thickness),
            typeof(ExplorerPaneToolbarChrome),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeOpacityProperty =
        DependencyProperty.Register(
            nameof(ChromeOpacity),
            typeof(double),
            typeof(ExplorerPaneToolbarChrome),
            new PropertyMetadata(1d, OnBindablePropertyChanged));

    public ExplorerPaneToolbarChrome()
    {
        InitializeComponent();
    }

    public UIElement? ToolbarContent
    {
        get => (UIElement?)GetValue(ToolbarContentProperty);
        set => SetValue(ToolbarContentProperty, value);
    }

    public Thickness ChromePadding
    {
        get => (Thickness)GetValue(ChromePaddingProperty);
        set => SetValue(ChromePaddingProperty, value);
    }

    public Brush? ChromeBackground
    {
        get => (Brush?)GetValue(ChromeBackgroundProperty);
        set => SetValue(ChromeBackgroundProperty, value);
    }

    public Brush? ChromeBorderBrush
    {
        get => (Brush?)GetValue(ChromeBorderBrushProperty);
        set => SetValue(ChromeBorderBrushProperty, value);
    }

    public Thickness ChromeBorderThickness
    {
        get => (Thickness)GetValue(ChromeBorderThicknessProperty);
        set => SetValue(ChromeBorderThicknessProperty, value);
    }

    public double ChromeOpacity
    {
        get => (double)GetValue(ChromeOpacityProperty);
        set => SetValue(ChromeOpacityProperty, value);
    }

    private void ChromeBorder_Loaded(object sender, RoutedEventArgs e)
    {
        if (ChromeShadow is null || ShadowReceiverGrid is null)
        {
            return;
        }

        ChromeShadow.Receivers.Clear();
        ChromeShadow.Receivers.Add(ShadowReceiverGrid);
    }

    private static void OnBindablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ExplorerPaneToolbarChrome)d).Bindings.Update();
    }
}
