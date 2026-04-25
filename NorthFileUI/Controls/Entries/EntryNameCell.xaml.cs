using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.ComponentModel;

namespace NorthFileUI.Controls;

public sealed partial class EntryNameCell : UserControl
{
    private NorthFileUI.EntryViewModel? _subscribedEntry;

    public static readonly DependencyProperty EntryProperty =
        DependencyProperty.Register(
            nameof(Entry),
            typeof(NorthFileUI.EntryViewModel),
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

    public NorthFileUI.EntryViewModel? Entry
    {
        get => (NorthFileUI.EntryViewModel?)GetValue(EntryProperty);
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
            control.UpdateEntrySubscription(e.OldValue as NorthFileUI.EntryViewModel, e.NewValue as NorthFileUI.EntryViewModel);
        }

        control.ApplyBindings();
    }

    private void UpdateEntrySubscription(NorthFileUI.EntryViewModel? oldEntry, NorthFileUI.EntryViewModel? newEntry)
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
        if (e.PropertyName is not (nameof(NorthFileUI.EntryViewModel.DisplayName)
            or nameof(NorthFileUI.EntryViewModel.IconGlyph)
            or nameof(NorthFileUI.EntryViewModel.IconForeground)
            or nameof(NorthFileUI.EntryViewModel.IconOpacity)
            or nameof(NorthFileUI.EntryViewModel.IsNameEditing)))
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
        NorthFileUI.EntryViewModel? entry = Entry;

        RootGrid.Margin = ContentMargin;
        IconColumnDefinition.Width = new GridLength(metrics.IconColumnWidth);
        IconSpacingColumnDefinition.Width = new GridLength(metrics.IconTextSpacing);

        IconTextBlock.Width = metrics.IconColumnWidth;
        IconTextBlock.FontSize = metrics.IconFontSize;
        IconTextBlock.Foreground = entry?.IconForeground;
        IconTextBlock.Opacity = entry?.IconOpacity ?? 1.0;
        IconTextBlock.Text = entry?.IconGlyph ?? string.Empty;

        EntryNameTextBlock.Margin = new Thickness(0, 0, metrics.NameTrailingSpacing, 0);
        EntryNameTextBlock.FontSize = metrics.NameFontSize;
        EntryNameTextBlock.Text = entry?.DisplayName ?? string.Empty;
        EntryNameTextBlock.Visibility = entry?.IsNameEditing == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void EntryNameCell_Unloaded(object sender, RoutedEventArgs e)
    {
        UpdateEntrySubscription(_subscribedEntry, null);
    }
}
