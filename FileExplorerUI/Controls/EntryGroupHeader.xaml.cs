using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileExplorerUI.Controls;

public sealed partial class EntryGroupHeader : UserControl
{
    public static readonly DependencyProperty HeaderTextProperty =
        DependencyProperty.Register(nameof(HeaderText), typeof(string), typeof(EntryGroupHeader), new PropertyMetadata(string.Empty, OnBindablePropertyChanged));

    public static readonly DependencyProperty CountTextProperty =
        DependencyProperty.Register(nameof(CountText), typeof(string), typeof(EntryGroupHeader), new PropertyMetadata(string.Empty, OnBindablePropertyChanged));

    public static readonly DependencyProperty ExpandGlyphProperty =
        DependencyProperty.Register(nameof(ExpandGlyph), typeof(string), typeof(EntryGroupHeader), new PropertyMetadata(string.Empty, OnBindablePropertyChanged));

    public static readonly DependencyProperty ShowExpandGlyphProperty =
        DependencyProperty.Register(nameof(ShowExpandGlyph), typeof(bool), typeof(EntryGroupHeader), new PropertyMetadata(false, OnBindablePropertyChanged));

    public static readonly DependencyProperty ContentPaddingProperty =
        DependencyProperty.Register(nameof(ContentPadding), typeof(Thickness), typeof(EntryGroupHeader), new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public static readonly DependencyProperty MetricsProperty =
        DependencyProperty.Register(nameof(Metrics), typeof(EntryItemMetrics), typeof(EntryGroupHeader), new PropertyMetadata(new EntryItemMetrics(), OnBindablePropertyChanged));

    public static readonly DependencyProperty HeaderOpacityProperty =
        DependencyProperty.Register(nameof(HeaderOpacity), typeof(double), typeof(EntryGroupHeader), new PropertyMetadata(1d, OnBindablePropertyChanged));

    public static readonly DependencyProperty ShowBottomBorderProperty =
        DependencyProperty.Register(nameof(ShowBottomBorder), typeof(bool), typeof(EntryGroupHeader), new PropertyMetadata(true, OnBindablePropertyChanged));

    public EntryGroupHeader()
    {
        this.InitializeComponent();
    }

    public string HeaderText
    {
        get => (string)GetValue(HeaderTextProperty);
        set => SetValue(HeaderTextProperty, value);
    }

    public string CountText
    {
        get => (string)GetValue(CountTextProperty);
        set => SetValue(CountTextProperty, value);
    }

    public string ExpandGlyph
    {
        get => (string)GetValue(ExpandGlyphProperty);
        set => SetValue(ExpandGlyphProperty, value);
    }

    public bool ShowExpandGlyph
    {
        get => (bool)GetValue(ShowExpandGlyphProperty);
        set => SetValue(ShowExpandGlyphProperty, value);
    }

    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    public EntryItemMetrics Metrics
    {
        get => (EntryItemMetrics)GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    public double HeaderOpacity
    {
        get => (double)GetValue(HeaderOpacityProperty);
        set => SetValue(HeaderOpacityProperty, value);
    }

    public bool ShowBottomBorder
    {
        get => (bool)GetValue(ShowBottomBorderProperty);
        set => SetValue(ShowBottomBorderProperty, value);
    }

    public Visibility ExpandGlyphVisibility => ShowExpandGlyph ? Visibility.Visible : Visibility.Collapsed;

    public GridLength GroupGlyphWidthGridLength => ShowExpandGlyph ? new GridLength(Metrics.GroupGlyphWidth) : new GridLength(0);

    public GridLength GroupHeaderSpacingGridLength => ShowExpandGlyph ? new GridLength(Metrics.GroupHeaderSpacing) : new GridLength(0);

    public Thickness BottomBorderThickness => ShowBottomBorder ? new Thickness(0, 0, 0, 1) : new Thickness(0);

    private static void OnBindablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EntryGroupHeader)d).Bindings.Update();
    }
}
