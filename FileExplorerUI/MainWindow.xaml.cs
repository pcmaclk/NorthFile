using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;

namespace FileExplorerUI
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly string _initialPath;

        public MainWindow(string? initialPath = null)
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(WindowTabDragRegion);
            WindowTabDragRegion.MinWidth = 188;
            SettingsTitleBarDragRegion.MinWidth = 188;
            _initialPath = string.IsNullOrWhiteSpace(initialPath)
                ? ShellMyComputerPath
                : initialPath.Trim();
            _detailsRepeaterLayoutProfile = new EntriesRepeaterLayoutProfile(
                isVertical: true,
                primaryItemExtentProvider: () => Math.Max(32.0, EntryItemMetrics.RowHeight + 4),
                totalItemCountProvider: () => checked((int)Math.Max((uint)_entries.Count, _totalEntries)),
                crossAxisExtentProvider: () => Math.Max(1, DetailsRowWidth),
                viewportPrimaryExtentProvider: () =>
                {
                    double viewportHeight = DetailsEntriesScrollViewer.ViewportHeight > 0
                        ? DetailsEntriesScrollViewer.ViewportHeight
                        : DetailsEntriesScrollViewer.ActualHeight;
                    return Math.Max(1, viewportHeight);
                });
            _detailsVirtualizingLayout = new FixedExtentVirtualizingLayout(_detailsRepeaterLayoutProfile);
            _groupedRepeaterLayoutProfile = new GroupedListRepeaterLayoutProfile(
                itemsProvider: () => _entries,
                itemWidthProvider: () => Math.Max(1, EntryContainerWidth),
                rowExtentProvider: () => Math.Max(1, EntryItemMetrics.RowHeight + 4),
                headerExtentProvider: () => Math.Max(1, EntryItemMetrics.GroupHeaderHeight),
                rowsPerColumnProvider: () =>
                {
                    int rowsPerColumn = Math.Max(1, GetGroupedListRowsPerColumn());
                    _groupedListRowsPerColumn = rowsPerColumn;
                    return rowsPerColumn;
                },
                viewportHeightProvider: () =>
                {
                    double viewportHeight = GroupedEntriesScrollViewer.ViewportHeight > 0
                        ? GroupedEntriesScrollViewer.ViewportHeight
                        : GroupedEntriesScrollViewer.ActualHeight;
                    return Math.Max(1, viewportHeight);
                },
                groupSpacing: GroupedListColumnSpacing);
            _groupedVirtualizingLayout = new GroupedListVirtualizingLayout(_groupedRepeaterLayoutProfile);
            _workspaceLayoutHost = new WorkspaceLayoutHost(_workspaceShellState);
            _fileManagementCoordinator = new FileManagementCoordinator(_explorerService);
            _entriesContextCommand = new DelegateCommand(ExecuteEntriesContextCommand);
            _engineVersion = _explorerService.GetEngineVersion();
            WireRootAndViewportEvents();
            InitializeViewHostsAndSettings();
            WireShellCommandsAndStartup();
        }

        private void ToolbarChromeBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (ToolbarThemeShadow is null || ToolbarShadowReceiverGrid is null)
            {
                return;
            }

            ToolbarThemeShadow.Receivers.Clear();
            ToolbarThemeShadow.Receivers.Add(ToolbarShadowReceiverGrid);
        }

        private void ExplorerBodyBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (ExplorerBodyThemeShadow is null || ExplorerBodyShadowReceiverGrid is null)
            {
                return;
            }

            ExplorerBodyThemeShadow.Receivers.Clear();
            ExplorerBodyThemeShadow.Receivers.Add(ExplorerBodyShadowReceiverGrid);
        }

    }

}
