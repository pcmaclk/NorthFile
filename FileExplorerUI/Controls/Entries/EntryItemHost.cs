using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI.Controls;

public sealed class EntryItemHost : ContentControl
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected),
            typeof(bool),
            typeof(EntryItemHost),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty PointerOverBackgroundProperty =
        DependencyProperty.Register(
            nameof(PointerOverBackground),
            typeof(Brush),
            typeof(EntryItemHost),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty PressedBackgroundProperty =
        DependencyProperty.Register(
            nameof(PressedBackground),
            typeof(Brush),
            typeof(EntryItemHost),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty SelectedBackgroundProperty =
        DependencyProperty.Register(
            nameof(SelectedBackground),
            typeof(Brush),
            typeof(EntryItemHost),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty SelectedPointerOverBackgroundProperty =
        DependencyProperty.Register(
            nameof(SelectedPointerOverBackground),
            typeof(Brush),
            typeof(EntryItemHost),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty SelectedPressedBackgroundProperty =
        DependencyProperty.Register(
            nameof(SelectedPressedBackground),
            typeof(Brush),
            typeof(EntryItemHost),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty IndicatorBrushProperty =
        DependencyProperty.Register(
            nameof(IndicatorBrush),
            typeof(Brush),
            typeof(EntryItemHost),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty IndicatorCornerRadiusProperty =
        DependencyProperty.Register(
            nameof(IndicatorCornerRadius),
            typeof(CornerRadius),
            typeof(EntryItemHost),
            new PropertyMetadata(default(CornerRadius), OnVisualPropertyChanged));

    public static readonly DependencyProperty IndicatorWidthProperty =
        DependencyProperty.Register(
            nameof(IndicatorWidth),
            typeof(double),
            typeof(EntryItemHost),
            new PropertyMetadata(3d, OnVisualPropertyChanged));

    public static readonly DependencyProperty IsAnchorFocusedProperty =
        DependencyProperty.Register(
            nameof(IsAnchorFocused),
            typeof(bool),
            typeof(EntryItemHost),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty AnchorBorderBrushProperty =
        DependencyProperty.Register(
            nameof(AnchorBorderBrush),
            typeof(Brush),
            typeof(EntryItemHost),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty AnchorBorderThicknessProperty =
        DependencyProperty.Register(
            nameof(AnchorBorderThickness),
            typeof(Thickness),
            typeof(EntryItemHost),
            new PropertyMetadata(default(Thickness), OnVisualPropertyChanged));

    private Border? _backgroundBorder;
    private Border? _selectionIndicator;
    private Border? _anchorBorder;
    private bool _isPointerOver;
    private bool _isPressed;

    public EntryItemHost()
    {
        DefaultStyleKey = typeof(EntryItemHost);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;
        IsTabStop = false;
        UseSystemFocusVisuals = true;
        DataContextChanged += EntryItemHost_DataContextChanged;
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public Brush? PointerOverBackground
    {
        get => (Brush?)GetValue(PointerOverBackgroundProperty);
        set => SetValue(PointerOverBackgroundProperty, value);
    }

    public Brush? PressedBackground
    {
        get => (Brush?)GetValue(PressedBackgroundProperty);
        set => SetValue(PressedBackgroundProperty, value);
    }

    public Brush? SelectedBackground
    {
        get => (Brush?)GetValue(SelectedBackgroundProperty);
        set => SetValue(SelectedBackgroundProperty, value);
    }

    public Brush? SelectedPointerOverBackground
    {
        get => (Brush?)GetValue(SelectedPointerOverBackgroundProperty);
        set => SetValue(SelectedPointerOverBackgroundProperty, value);
    }

    public Brush? SelectedPressedBackground
    {
        get => (Brush?)GetValue(SelectedPressedBackgroundProperty);
        set => SetValue(SelectedPressedBackgroundProperty, value);
    }

    public Brush? IndicatorBrush
    {
        get => (Brush?)GetValue(IndicatorBrushProperty);
        set => SetValue(IndicatorBrushProperty, value);
    }

    public CornerRadius IndicatorCornerRadius
    {
        get => (CornerRadius)GetValue(IndicatorCornerRadiusProperty);
        set => SetValue(IndicatorCornerRadiusProperty, value);
    }

    public double IndicatorWidth
    {
        get => (double)GetValue(IndicatorWidthProperty);
        set => SetValue(IndicatorWidthProperty, value);
    }

    public bool IsAnchorFocused
    {
        get => (bool)GetValue(IsAnchorFocusedProperty);
        set => SetValue(IsAnchorFocusedProperty, value);
    }

    public Brush? AnchorBorderBrush
    {
        get => (Brush?)GetValue(AnchorBorderBrushProperty);
        set => SetValue(AnchorBorderBrushProperty, value);
    }

    public Thickness AnchorBorderThickness
    {
        get => (Thickness)GetValue(AnchorBorderThicknessProperty);
        set => SetValue(AnchorBorderThicknessProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _backgroundBorder = GetTemplateChild("BackgroundBorder") as Border;
        _selectionIndicator = GetTemplateChild("SelectionIndicator") as Border;
        _anchorBorder = GetTemplateChild("AnchorBorder") as Border;
        UpdateVisuals();
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        _isPointerOver = true;
        UpdateVisuals();
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        _isPointerOver = false;
        _isPressed = false;
        UpdateVisuals();
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isPressed = true;
        UpdateVisuals();
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPressed = false;
        UpdateVisuals();
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        base.OnPointerCanceled(e);
        _isPressed = false;
        UpdateVisuals();
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isPressed = false;
        UpdateVisuals();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EntryItemHost)d).UpdateVisuals();
    }

    private void EntryItemHost_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        _isPointerOver = false;
        _isPressed = false;
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        if (_backgroundBorder is not null)
        {
            _backgroundBorder.Background = ResolveBackground();
        }

        if (_selectionIndicator is not null)
        {
            _selectionIndicator.Background = IndicatorBrush;
            _selectionIndicator.CornerRadius = IndicatorCornerRadius;
            _selectionIndicator.Width = IndicatorWidth;
            _selectionIndicator.Visibility = IsSelected ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_anchorBorder is not null)
        {
            _anchorBorder.BorderBrush = AnchorBorderBrush;
            _anchorBorder.BorderThickness = AnchorBorderThickness;
            _anchorBorder.Visibility = !IsSelected && IsAnchorFocused ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private Brush? ResolveBackground()
    {
        if (IsSelected)
        {
            return SelectedBackground;
        }

        if (_isPressed && PressedBackground is not null)
        {
            return PressedBackground;
        }

        if (_isPointerOver && PointerOverBackground is not null)
        {
            return PointerOverBackground;
        }

        return Background;
    }
}
