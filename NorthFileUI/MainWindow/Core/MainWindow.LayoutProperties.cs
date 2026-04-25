using NorthFileUI.Controls;
using NorthFileUI.Services;
using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.ComponentModel;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        public EntryItemMetrics EntryItemMetrics { get; private set; } = EntryItemMetrics.CreatePreset(EntryViewDensityMode.Normal);

        public double EntryContainerWidth => GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List ? GetListEntryContainerWidth() : DetailsRowWidth;

        public Visibility EntriesListVisibility => Visibility.Collapsed;

        public Visibility GroupedColumnsVisibility => UsesColumnsListPresentation()
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility DetailsItemsVisibility => GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility DetailsHeaderVisibility => GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Thickness NameHeaderMargin => GetPanelGroupField(WorkspacePanelId.Primary) == EntryGroupField.None
            ? new Thickness(38, 0, 0, 0)
            : new Thickness(36, 0, 0, 0);

        public Thickness DetailsNameCellMargin => GetPanelGroupField(WorkspacePanelId.Primary) == EntryGroupField.None
            ? new Thickness(6, 0, 0, 0)
            : new Thickness(32, 0, 0, 0);

        public ScrollBarVisibility EntriesHorizontalScrollBarVisibility => NeedsEntriesHorizontalScroll()
            ? ScrollBarVisibility.Auto
            : ScrollBarVisibility.Hidden;

        public ScrollMode EntriesHorizontalScrollMode => NeedsEntriesHorizontalScroll()
            ? ScrollMode.Enabled
            : ScrollMode.Disabled;

        public Visibility DetailsEntryVisibility => GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility ListEntryVisibility => GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List
            ? Visibility.Visible
            : Visibility.Collapsed;

        private double GetListEntryContainerWidth()
        {
            double iconWidth = EntryItemMetrics.IconColumnWidth + EntryItemMetrics.IconTextSpacing;
            double trailingWidth = 24;
            double widestText = 0;
            int sampled = 0;

            foreach (EntryViewModel entry in PrimaryEntries)
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

        public Thickness ShellWindowPadding => new(ShellWindowHorizontalPadding, 0, ShellWindowHorizontalPadding, 0);

        public double ShellTitleBarHeight => ShellTitleBarHeightValue;

        public Visibility ShellTabTitleBarVisibility => _shellMode == ShellMode.Explorer
            ? Visibility.Visible
            : Visibility.Collapsed;

        public double ShellControlSize => ShellControlSizeValue;

        public double ShellGlyphSize => ShellGlyphSizeValue;

        public double ShellTitleBarLeftInsetWidth => _shellMode == ShellMode.Explorer && _isSidebarCompact
            ? ShellTitleBarLeftInsetWidthValue
            : 0;

        public Thickness ShellTitleBarMargin => _shellMode == ShellMode.Explorer && !_isSidebarCompact
            ? new Thickness(-8, 0, 0, 0)
            : new Thickness(0);

        public Visibility ShellTitleBarLeftInsetVisibility => _shellMode == ShellMode.Explorer && _isSidebarCompact
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Thickness ShellTitleBarLeftInsetButtonMargin => new(4, 0, 0, 0);

        public Thickness SidebarTopChromeMargin => new(0);

        public Visibility SidebarTopChromeVisibility => _shellMode == ShellMode.Explorer
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility SidebarTopSettingsVisibility => _shellMode == ShellMode.Explorer && !_isSidebarCompact
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility TitleBarSidebarSettingsVisibility => _shellMode == ShellMode.Explorer && _isSidebarCompact
            ? Visibility.Visible
            : Visibility.Collapsed;

        public string SidebarCollapseButtonToolTipText => _isSidebarCompact
            ? LocalizedStrings.Instance.Get("CommonExpand")
            : LocalizedStrings.Instance.Get("CommonCollapse");

        public Thickness ShellToolbarMargin => new(0, ShellToolbarBottomSpacing, 0, ShellToolbarBottomSpacing);

        public Thickness ShellToolbarPadding => new(8, 4, 8, 4);

        public Thickness ExplorerPanelHostMargin => new(0, 8, 0, 0);

        public double ExplorerPanelHostSpacing => ShellToolbarBottomSpacing;

        public Thickness SidebarContentPadding => new(0, ShellToolbarBottomSpacing + 4, 0, 0);

        public Thickness AddWorkspaceTabButtonMargin => new(4, 4, 0, 0);

        public Thickness ExplorerPaneToggleButtonMargin => new(0, 4, 0, 0);

        public double ShellStatusBarHeight => ShellStatusBarHeightValue;

        public Thickness ShellStatusTextMargin => new(22, 0, 18, 0);

        public double ShellSplitterWidth => ShellSplitterWidthValue;

        public GridLength ExplorerPanePrimaryColumnWidth => GetExplorerPanePrimaryColumnWidth();

        public GridLength ExplorerPaneToolbarActionRailColumnWidth => new(ExplorerPaneActionRailWidthValue);

        public GridLength ExplorerPaneActionRailColumnWidth => _isDualPaneEnabled
            ? new GridLength(ExplorerPaneActionRailWidthValue)
            : new GridLength(0);

        public GridLength ExplorerPaneSecondaryColumnWidth => _isDualPaneEnabled
            ? GetExplorerPaneSecondaryColumnWidth()
            : new GridLength(0);

        public Visibility ExplorerPaneToolbarActionRailVisibility => Visibility.Visible;

        public Visibility ExplorerPaneActionRailVisibility => _isDualPaneEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility ExplorerSecondaryPaneVisibility => _isDualPaneEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

        public string ExplorerPaneToggleGlyph => _isDualPaneEnabled
            ? "\uE71A"
            : "\uE8A9";

        public string ExplorerPaneToggleToolTipText => _isDualPaneEnabled
            ? "关闭双面板"
            : "打开双面板";

        public double SettingsNavigationCompactPaneLength => SettingsNavigationCompactPaneLengthValue;

        public double SettingsNavigationOpenPaneLength => SidebarExpandedDefaultWidth;

        public GridLength NameColumnWidth
        {
            get => new(PrimaryPanelState.NameColumnWidth);
            set
            {
                if (Math.Abs(PrimaryPanelState.NameColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                PrimaryPanelState.NameColumnWidth = value.Value;
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
            get => new(PrimaryPanelState.TypeColumnWidth);
            set
            {
                if (Math.Abs(PrimaryPanelState.TypeColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                PrimaryPanelState.TypeColumnWidth = value.Value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TypeColumnWidth)));
            }
        }

        public GridLength SizeColumnWidth
        {
            get => new(PrimaryPanelState.SizeColumnWidth);
            set
            {
                if (Math.Abs(PrimaryPanelState.SizeColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                PrimaryPanelState.SizeColumnWidth = value.Value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeColumnWidth)));
            }
        }

        public GridLength ModifiedColumnWidth
        {
            get => new(PrimaryPanelState.ModifiedColumnWidth);
            set
            {
                if (Math.Abs(PrimaryPanelState.ModifiedColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                PrimaryPanelState.ModifiedColumnWidth = value.Value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedColumnWidth)));
            }
        }

        public GridLength SecondaryNameColumnWidth
        {
            get => new(SecondaryPanelState.NameColumnWidth);
            set
            {
                if (Math.Abs(SecondaryPanelState.NameColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                SecondaryPanelState.NameColumnWidth = value.Value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryNameColumnWidth)));
                RefreshRealizedSecondaryDetailsRowColumnWidths();
            }
        }

        public GridLength SecondaryTypeColumnWidth
        {
            get => new(SecondaryPanelState.TypeColumnWidth);
            set
            {
                if (Math.Abs(SecondaryPanelState.TypeColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                SecondaryPanelState.TypeColumnWidth = value.Value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryTypeColumnWidth)));
                RefreshRealizedSecondaryDetailsRowColumnWidths();
            }
        }

        public GridLength SecondarySizeColumnWidth
        {
            get => new(SecondaryPanelState.SizeColumnWidth);
            set
            {
                if (Math.Abs(SecondaryPanelState.SizeColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                SecondaryPanelState.SizeColumnWidth = value.Value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondarySizeColumnWidth)));
                RefreshRealizedSecondaryDetailsRowColumnWidths();
            }
        }

        public GridLength SecondaryModifiedColumnWidth
        {
            get => new(SecondaryPanelState.ModifiedColumnWidth);
            set
            {
                if (Math.Abs(SecondaryPanelState.ModifiedColumnWidth - value.Value) < 0.1)
                {
                    return;
                }
                SecondaryPanelState.ModifiedColumnWidth = value.Value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryModifiedColumnWidth)));
                RefreshRealizedSecondaryDetailsRowColumnWidths();
            }
        }

        public double DetailsContentWidth
        {
            get => PrimaryPanelState.DetailsContentWidth;
            set
            {
                if (Math.Abs(PrimaryPanelState.DetailsContentWidth - value) < 0.1)
                {
                    return;
                }
                PrimaryPanelState.DetailsContentWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsContentWidth)));
                DetailsRowWidth = value;
            }
        }

        public double DetailsRowWidth
        {
            get => PrimaryPanelState.DetailsRowWidth;
            set
            {
                if (Math.Abs(PrimaryPanelState.DetailsRowWidth - value) < 0.1)
                {
                    return;
                }
                PrimaryPanelState.DetailsRowWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsRowWidth)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollBarVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollMode)));
                InvalidateEntriesLayouts();
            }
        }

        public double SecondaryDetailsContentWidth
        {
            get => SecondaryPanelState.DetailsContentWidth;
            set
            {
                if (Math.Abs(SecondaryPanelState.DetailsContentWidth - value) < 0.1)
                {
                    return;
                }
                SecondaryPanelState.DetailsContentWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryDetailsContentWidth)));
                SecondaryDetailsRowWidth = value;
            }
        }

        public double SecondaryDetailsRowWidth
        {
            get => SecondaryPanelState.DetailsRowWidth;
            set
            {
                if (Math.Abs(SecondaryPanelState.DetailsRowWidth - value) < 0.1)
                {
                    return;
                }
                SecondaryPanelState.DetailsRowWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SecondaryDetailsRowWidth)));
                RefreshRealizedSecondaryDetailsRowColumnWidths();
            }
        }
    }
}
