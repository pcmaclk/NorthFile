using FileExplorerUI.Controls;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        public EntryItemMetrics EntryItemMetrics { get; private set; } = EntryItemMetrics.CreatePreset(EntryViewDensityMode.Normal);

        public double EntryContainerWidth => _currentViewMode == EntryViewMode.List ? GetListEntryContainerWidth() : DetailsRowWidth;

        public Visibility EntriesListVisibility => Visibility.Collapsed;

        public Visibility GroupedColumnsVisibility => UsesColumnsListPresentation()
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility DetailsItemsVisibility => _currentViewMode == EntryViewMode.Details
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility DetailsHeaderVisibility => _currentViewMode == EntryViewMode.Details
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Thickness NameHeaderMargin => _currentGroupField == EntryGroupField.None
            ? new Thickness(38, 0, 0, 0)
            : new Thickness(36, 0, 0, 0);

        public Thickness DetailsNameCellMargin => _currentGroupField == EntryGroupField.None
            ? new Thickness(6, 0, 0, 0)
            : new Thickness(32, 0, 0, 0);

        public ScrollBarVisibility EntriesHorizontalScrollBarVisibility => NeedsEntriesHorizontalScroll()
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;

        public ScrollMode EntriesHorizontalScrollMode => NeedsEntriesHorizontalScroll()
            ? ScrollMode.Enabled
            : ScrollMode.Disabled;

        public Visibility DetailsEntryVisibility => _currentViewMode == EntryViewMode.Details
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility ListEntryVisibility => _currentViewMode == EntryViewMode.List
            ? Visibility.Visible
            : Visibility.Collapsed;

        private double GetListEntryContainerWidth()
        {
            double iconWidth = EntryItemMetrics.IconColumnWidth + EntryItemMetrics.IconTextSpacing;
            double trailingWidth = 24;
            double widestText = 0;
            int sampled = 0;

            foreach (EntryViewModel entry in _entries)
            {
                if (!entry.IsLoaded || entry.IsGroupHeader)
                {
                    continue;
                }

                widestText = Math.Max(widestText, EstimateListEntryTextWidth(entry.DisplayName));
                if (++sampled >= 256)
                {
                    break;
                }
            }

            double desiredWidth = iconWidth + widestText + trailingWidth;
            double viewportWidth = GroupedEntriesScrollViewer is null
                ? 0
                : GroupedEntriesScrollViewer.ViewportWidth > 0
                    ? GroupedEntriesScrollViewer.ViewportWidth
                    : GroupedEntriesScrollViewer.ActualWidth;

            if (viewportWidth > 0)
            {
                desiredWidth = Math.Min(desiredWidth, Math.Max(iconWidth + 80, viewportWidth - GroupedListColumnSpacing));
            }

            return Math.Max(iconWidth + 80, desiredWidth);
        }

        private double EstimateListEntryTextWidth(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            double fontSize = EntryItemMetrics.NameFontSize;
            double width = 0;
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    width += fontSize * 0.35;
                }
                else if (ch <= 0x7F)
                {
                    width += fontSize * 0.58;
                }
                else
                {
                    width += fontSize * 0.92;
                }
            }

            return width;
        }

        public Visibility ExplorerChromeVisibility => _shellMode == ShellMode.Explorer
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility ExplorerShellVisibility => _shellMode == ShellMode.Explorer
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility SettingsShellVisibility => _shellMode == ShellMode.Settings
            ? Visibility.Visible
            : Visibility.Collapsed;

        public GridLength NameColumnWidth
        {
            get => _nameColumnWidth;
            set
            {
                if (_nameColumnWidth.Equals(value))
                {
                    return;
                }
                _nameColumnWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameColumnWidth)));
            }
        }

        public GridLength SidebarColumnWidth
        {
            get => _sidebarColumnWidth;
            set
            {
                if (_sidebarColumnWidth.Equals(value))
                {
                    return;
                }

                _sidebarColumnWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SidebarColumnWidth)));
            }
        }

        public GridLength TypeColumnWidth
        {
            get => _typeColumnWidth;
            set
            {
                if (_typeColumnWidth.Equals(value))
                {
                    return;
                }
                _typeColumnWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeColumnWidth)));
            }
        }

        public GridLength SizeColumnWidth
        {
            get => _sizeColumnWidth;
            set
            {
                if (_sizeColumnWidth.Equals(value))
                {
                    return;
                }
                _sizeColumnWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeColumnWidth)));
            }
        }

        public GridLength ModifiedColumnWidth
        {
            get => _modifiedColumnWidth;
            set
            {
                if (_modifiedColumnWidth.Equals(value))
                {
                    return;
                }
                _modifiedColumnWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedColumnWidth)));
            }
        }

        public double DetailsContentWidth
        {
            get => _detailsContentWidth;
            set
            {
                if (Math.Abs(_detailsContentWidth - value) < 0.1)
                {
                    return;
                }
                _detailsContentWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsContentWidth)));
                DetailsRowWidth = value;
            }
        }

        public double DetailsRowWidth
        {
            get => _detailsRowWidth;
            set
            {
                if (Math.Abs(_detailsRowWidth - value) < 0.1)
                {
                    return;
                }
                _detailsRowWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsRowWidth)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollBarVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollMode)));
                InvalidateEntriesLayouts();
            }
        }
    }
}
