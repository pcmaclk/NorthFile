using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI.Controls;

public sealed partial class ExplorerPanelHost : UserControl
{
    public static readonly DependencyProperty ToolbarContentProperty =
        DependencyProperty.Register(
            nameof(ToolbarContent),
            typeof(UIElement),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty BodyContentProperty =
        DependencyProperty.Register(
            nameof(BodyContent),
            typeof(UIElement),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty PanelSpacingProperty =
        DependencyProperty.Register(
            nameof(PanelSpacing),
            typeof(double),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(0d, OnBindablePropertyChanged));

    public static readonly DependencyProperty ToolbarPaddingProperty =
        DependencyProperty.Register(
            nameof(ToolbarPadding),
            typeof(Thickness),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty ToolbarBackgroundProperty =
        DependencyProperty.Register(
            nameof(ToolbarBackground),
            typeof(Brush),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ToolbarBorderBrushProperty =
        DependencyProperty.Register(
            nameof(ToolbarBorderBrush),
            typeof(Brush),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty ToolbarBorderThicknessProperty =
        DependencyProperty.Register(
            nameof(ToolbarBorderThickness),
            typeof(Thickness),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty ToolbarOpacityProperty =
        DependencyProperty.Register(
            nameof(ToolbarOpacity),
            typeof(double),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(1d, OnBindablePropertyChanged));

    public static readonly DependencyProperty BodyPaddingProperty =
        DependencyProperty.Register(
            nameof(BodyPadding),
            typeof(Thickness),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty BodyBackgroundProperty =
        DependencyProperty.Register(
            nameof(BodyBackground),
            typeof(Brush),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty BodyBorderBrushProperty =
        DependencyProperty.Register(
            nameof(BodyBorderBrush),
            typeof(Brush),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty BodyBorderThicknessProperty =
        DependencyProperty.Register(
            nameof(BodyBorderThickness),
            typeof(Thickness),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty BodyOpacityProperty =
        DependencyProperty.Register(
            nameof(BodyOpacity),
            typeof(double),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(1d, OnBindablePropertyChanged));

    public static readonly DependencyProperty BodyHasShadowProperty =
        DependencyProperty.Register(
            nameof(BodyHasShadow),
            typeof(bool),
            typeof(ExplorerPanelHost),
            new PropertyMetadata(false, OnBindablePropertyChanged));

    public ExplorerPanelHost()
    {
        InitializeComponent();
    }

    public UIElement? ToolbarContent
    {
        get => (UIElement?)GetValue(ToolbarContentProperty);
        set => SetValue(ToolbarContentProperty, value);
    }

    public UIElement? BodyContent
    {
        get => (UIElement?)GetValue(BodyContentProperty);
        set => SetValue(BodyContentProperty, value);
    }

    public double PanelSpacing
    {
        get => (double)GetValue(PanelSpacingProperty);
        set => SetValue(PanelSpacingProperty, value);
    }

    public Thickness ToolbarPadding
    {
        get => (Thickness)GetValue(ToolbarPaddingProperty);
        set => SetValue(ToolbarPaddingProperty, value);
    }

    public Brush? ToolbarBackground
    {
        get => (Brush?)GetValue(ToolbarBackgroundProperty);
        set => SetValue(ToolbarBackgroundProperty, value);
    }

    public Brush? ToolbarBorderBrush
    {
        get => (Brush?)GetValue(ToolbarBorderBrushProperty);
        set => SetValue(ToolbarBorderBrushProperty, value);
    }

    public Thickness ToolbarBorderThickness
    {
        get => (Thickness)GetValue(ToolbarBorderThicknessProperty);
        set => SetValue(ToolbarBorderThicknessProperty, value);
    }

    public double ToolbarOpacity
    {
        get => (double)GetValue(ToolbarOpacityProperty);
        set => SetValue(ToolbarOpacityProperty, value);
    }

    public Thickness BodyPadding
    {
        get => (Thickness)GetValue(BodyPaddingProperty);
        set => SetValue(BodyPaddingProperty, value);
    }

    public Brush? BodyBackground
    {
        get => (Brush?)GetValue(BodyBackgroundProperty);
        set => SetValue(BodyBackgroundProperty, value);
    }

    public Brush? BodyBorderBrush
    {
        get => (Brush?)GetValue(BodyBorderBrushProperty);
        set => SetValue(BodyBorderBrushProperty, value);
    }

    public Thickness BodyBorderThickness
    {
        get => (Thickness)GetValue(BodyBorderThicknessProperty);
        set => SetValue(BodyBorderThicknessProperty, value);
    }

    public double BodyOpacity
    {
        get => (double)GetValue(BodyOpacityProperty);
        set => SetValue(BodyOpacityProperty, value);
    }

    public bool BodyHasShadow
    {
        get => (bool)GetValue(BodyHasShadowProperty);
        set => SetValue(BodyHasShadowProperty, value);
    }

    private void BodyBorder_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateBodyShadowReceivers();
    }

    private void UpdateBodyShadowReceivers()
    {
        if (BodyThemeShadow is null || BodyShadowReceiverGrid is null)
        {
            return;
        }

        BodyThemeShadow.Receivers.Clear();
        if (BodyHasShadow)
        {
            BodyThemeShadow.Receivers.Add(BodyShadowReceiverGrid);
        }
    }

    private static void OnBindablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ExplorerPanelHost host = (ExplorerPanelHost)d;
        host.Bindings.Update();
        host.UpdateBodyShadowReceivers();
    }
}
