using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace FileExplorerUI.Controls;

public sealed partial class EntryNameCell : UserControl
{
    private FileExplorerUI.EntryViewModel? _subscribedEntry;

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
        Unloaded += EntryNameCell_Unloaded;
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
        var control = (EntryNameCell)d;
        if (e.Property == EntryProperty)
        {
            control.UpdateEntrySubscription(e.OldValue as FileExplorerUI.EntryViewModel, e.NewValue as FileExplorerUI.EntryViewModel);
        }

        control.ApplyBindings();
    }

    private void UpdateEntrySubscription(FileExplorerUI.EntryViewModel? oldEntry, FileExplorerUI.EntryViewModel? newEntry)
    {
        if (ReferenceEquals(oldEntry, newEntry))
        {
            return;
        }

        if (oldEntry is not null)
        {
            oldEntry.PropertyChanged -= Entry_PropertyChanged;
        }

        _subscribedEntry = newEntry;
        if (newEntry is not null)
        {
            newEntry.PropertyChanged += Entry_PropertyChanged;
        }
    }

    private void Entry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(FileExplorerUI.EntryViewModel.Name)
            or nameof(FileExplorerUI.EntryViewModel.IconGlyph)
            or nameof(FileExplorerUI.EntryViewModel.IconForeground)))
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(ApplyBindings);
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

    private void EntryNameCell_Unloaded(object sender, RoutedEventArgs e)
    {
        UpdateEntrySubscription(_subscribedEntry, null);
    }
}
