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
        ApplyBindings();
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

    public FrameworkElement NameTextElement => EntryNameTextBlock;

    private static void OnBindablePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((EntryNameCell)d).ApplyBindings();
    }

    private void ApplyBindings()
    {
        if (RootGrid is null || IconColumnDefinition is null || IconSpacingColumnDefinition is null || IconTextBlock is null || EntryNameTextBlock is null)
        {
            return;
        }

        EntryItemMetrics metrics = Metrics ?? new EntryItemMetrics();
        FileExplorerUI.EntryViewModel? entry = Entry;

        RootGrid.Margin = ContentMargin;
        IconColumnDefinition.Width = new GridLength(metrics.IconColumnWidth);
        IconSpacingColumnDefinition.Width = new GridLength(metrics.IconTextSpacing);

        IconTextBlock.Width = metrics.IconColumnWidth;
        IconTextBlock.FontSize = metrics.IconFontSize;
        IconTextBlock.Foreground = entry?.IconForeground;
        IconTextBlock.Text = entry?.IconGlyph ?? string.Empty;

        EntryNameTextBlock.Margin = new Thickness(0, 0, metrics.NameTrailingSpacing, 0);
        EntryNameTextBlock.FontSize = metrics.NameFontSize;
        EntryNameTextBlock.Text = entry?.Name ?? string.Empty;
    }
}
