using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Numerics;

namespace FileExplorerUI.Controls;

public sealed partial class ExplorerPaneNavigationBar : UserControl
{
    public static readonly DependencyProperty LeadingContentProperty =
        DependencyProperty.Register(
            nameof(LeadingContent),
            typeof(UIElement),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty AddressContentProperty =
        DependencyProperty.Register(
            nameof(AddressContent),
            typeof(UIElement),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty SearchContentProperty =
        DependencyProperty.Register(
            nameof(SearchContent),
            typeof(UIElement),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromePaddingProperty =
        DependencyProperty.Register(
            nameof(ChromePadding),
            typeof(Thickness),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeBackgroundProperty =
        DependencyProperty.Register(
            nameof(ChromeBackground),
            typeof(Brush),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeBorderBrushProperty =
        DependencyProperty.Register(
            nameof(ChromeBorderBrush),
            typeof(Brush),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeBorderThicknessProperty =
        DependencyProperty.Register(
            nameof(ChromeBorderThickness),
            typeof(Thickness),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeOpacityProperty =
        DependencyProperty.Register(
            nameof(ChromeOpacity),
            typeof(double),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(1d, OnBindablePropertyChanged));

    public static readonly DependencyProperty ChromeTranslationProperty =
        DependencyProperty.Register(
            nameof(ChromeTranslation),
            typeof(Vector3),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(new Vector3(0, 0, 6), OnBindablePropertyChanged));

    public static readonly DependencyProperty LayoutHeightProperty =
        DependencyProperty.Register(
            nameof(LayoutHeight),
            typeof(double),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(double.NaN, OnBindablePropertyChanged));

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(ColumnSpacing),
            typeof(double),
            typeof(ExplorerPaneNavigationBar),
            new PropertyMetadata(0d, OnBindablePropertyChanged));

    public ExplorerPaneNavigationBar()
    {
        InitializeComponent();
    }

    public UIElement? LeadingContent
    {
        get => (UIElement?)GetValue(LeadingContentProperty);
        set => SetValue(LeadingContentProperty, value);
    }

    public UIElement? AddressContent
    {
        get => (UIElement?)GetValue(AddressContentProperty);
        set => SetValue(AddressContentProperty, value);
    }

    public UIElement? SearchContent
    {
        get => (UIElement?)GetValue(SearchContentProperty);
        set => SetValue(SearchContentProperty, value);
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

    public Vector3 ChromeTranslation
    {
        get => (Vector3)GetValue(ChromeTranslationProperty);
        set => SetValue(ChromeTranslationProperty, value);
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
        ((ExplorerPaneNavigationBar)d).Bindings.Update();
    }
}
