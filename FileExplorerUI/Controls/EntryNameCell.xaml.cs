using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileExplorerUI.Controls;

public sealed partial class EntryNameCell : UserControl
{
    public static readonly DependencyProperty EntryProperty =
        DependencyProperty.Register(
            nameof(Entry),
            typeof(FileExplorerUI.EntryViewModel),
            typeof(EntryNameCell),
            new PropertyMetadata(null, OnBindablePropertyChanged));

    public static readonly DependencyProperty MetricsProperty =
        DependencyProperty.Register(
            nameof(Metrics),
            typeof(EntryItemMetrics),
            typeof(EntryNameCell),
            new PropertyMetadata(new EntryItemMetrics(), OnBindablePropertyChanged));

    public static readonly DependencyProperty ContentMarginProperty =
        DependencyProperty.Register(
            nameof(ContentMargin),
            typeof(Thickness),
            typeof(EntryNameCell),
            new PropertyMetadata(new Thickness(0), OnBindablePropertyChanged));

    public EntryNameCell()
    {
        this.InitializeComponent();
    }

    public FileExplorerUI.EntryViewModel? Entry
    {
        get => (FileExplorerUI.EntryViewModel?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    public EntryItemMetrics Metrics
    {
        get => (EntryItemMetrics)GetValue(MetricsProperty);
        set => SetValue(MetricsProperty, value);
    }

    public Thickness ContentMargin
    {
        get => (Thickness)GetValue(ContentMarginProperty);
        set => SetValue(ContentMarginProperty, value);
    }

    public GridLength IconColumnWidthGridLength => new(Metrics.IconColumnWidth);

    public GridLength IconTextSpacingGridLength => new(Metrics.IconTextSpacing);

    public Thickness NameTextMargin => new(0, 0, Metrics.NameTrailingSpacing, 0);

    private static void OnBindablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EntryNameCell)d).Bindings.Update();
    }
}
