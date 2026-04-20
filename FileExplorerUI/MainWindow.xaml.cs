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
        private readonly bool _hasExplicitInitialPath;
        private readonly string _initialPath;

        public MainWindow(string? initialPath = null)
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null);
            WindowTabDragRegion.MinWidth = 188;
            SettingsTitleBarDragRegion.MinWidth = 188;
            RefreshTitleBarDragRectangles();
            _hasExplicitInitialPath = !string.IsNullOrWhiteSpace(initialPath);
            _initialPath = string.IsNullOrWhiteSpace(initialPath)
                ? ShellMyComputerPath
                : initialPath.Trim();
            _detailsRepeaterLayoutProfile = new EntriesRepeaterLayoutProfile(
                isVertical: true,
                primaryItemExtentProvider: () => Math.Max(32.0, EntryItemMetrics.RowHeight + 4),
                totalItemCountProvider: () => checked((int)Math.Max((uint)PrimaryEntries.Count, GetPanelTotalEntries(WorkspacePanelId.Primary))),
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
                itemsProvider: () => PrimaryEntries,
                itemWidthProvider: () => Math.Max(1, EntryContainerWidth),
                rowExtentProvider: () => Math.Max(1, EntryItemMetrics.RowHeight + 4),
                headerExtentProvider: () => Math.Max(1, EntryItemMetrics.GroupHeaderHeight),
                rowsPerColumnProvider: () =>
                {
                    int rowsPerColumn = Math.Max(1, GetGroupedListRowsPerColumn());
                    SetPrimaryGroupedListRowsPerColumn(rowsPerColumn);
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
            _workspaceSession = new WorkspaceSession(new WorkspaceTabState(), ShellMyComputerPath);
            _workspaceUiApplier = new WorkspaceUiApplier(
                _workspaceSession.LayoutHost,
                ApplyWorkspaceShellStateToUi,
                PrepareWorkspaceShellStateForRestore,
                panelState => RestorePrimaryPanelStateAsync(
                    panelState,
                    preserveViewport: true,
                    ensureSelectionVisible: false,
                    focusEntries: false),
                panelState => RestoreSimplePanelStateAsync(
                    WorkspacePanelId.Secondary,
                    panelState,
                    preserveViewport: false,
                    ensureSelectionVisible: false,
                    focusEntries: false),
                ActivateWorkspacePanel,
                RaiseSecondaryPaneNavigationStateChanged,
                NotifyWorkspacePanelVisualStateChanged,
                NotifyTitleBarTextChanged);
            _workspaceTabController = new WorkspaceTabController(
                _workspaceSession,
                _workspaceUiApplier,
                (panelId, path) => NavigatePanelToPathAsync(panelId, path, pushHistory: false));
            _workspaceTabStripHost = new WorkspaceTabStripHost(
                SingleTabTitleBarView,
                () => _workspaceTabController.BuildTabPresentations(S("SidebarMyComputer"), "NorthFile"),
                _workspaceTabController.ActivateAsync,
                CloseWorkspaceTabButton_Click,
                () => "关闭标签页",
                () => ExplorerShellVisibility,
                () => ActiveTabBackgroundBrushProbe?.Background,
                () => TitleBarPrimaryBrushProbe.Foreground as Brush,
                () => TitleBarSecondaryBrushProbe.Foreground as Brush,
                ShellGlyphSize);
            _workspaceChromeCoordinator = new WorkspaceChromeCoordinator(
                _workspaceTabController,
                _workspaceTabStripHost);
            PaneFileCommandHandler paneFileCommandHandler = new(
                ExecuteNewFileForPaneCoreAsync,
                ExecuteNewFolderForPaneCoreAsync,
                CanCreateForPaneCore,
                CanRenameForPaneCore,
                CanDeleteForPaneCore,
                CanCopyForPaneCore,
                CanCutForPaneCore,
                CanPasteForPaneCore,
                CanRefreshForPaneCore,
                CanPasteTargetForPaneCore,
                CanCreateTargetForPaneCore,
                CanRefreshTargetForPaneCore,
                CanCopyTargetForPaneCore,
                CanCutTargetForPaneCore,
                CanRenameTargetForPaneCore,
                CanDeleteTargetForPaneCore,
                CanCreateShortcutTargetForPaneCore,
                CanCompressZipTargetForPaneCore,
                CanExtractZipTargetForPaneCore,
                CanOpenTargetForPaneCore,
                ExecuteRenameForPaneCoreAsync,
                ExecuteRenameForPaneTargetCoreAsync,
                ExecuteDeleteForPaneCoreAsync,
                ExecuteDeleteForPaneTargetCoreAsync,
                ExecuteCopyForPaneCore,
                ExecuteCutForPaneCore,
                ExecutePasteForPaneCoreAsync,
                ExecutePasteForPaneTargetCoreAsync,
                ExecuteRefreshForPaneCoreAsync,
                ExecuteCreateShortcutForPaneCoreAsync,
                ExecuteCompressZipForPaneCoreAsync,
                ExecuteExtractZipSmartForPaneCoreAsync,
                ExecuteExtractZipHereForPaneCoreAsync,
                ExecuteExtractZipToFolderForPaneCoreAsync,
                ExecuteOpenTargetForPaneCoreAsync);
            _paneFileCommandController = new PaneFileCommandController(
                () => _workspaceLayoutHost.ActivePanel,
                paneFileCommandHandler);
            _fileManagementCoordinator = new FileManagementCoordinator(_explorerService);
            _entriesContextCommand = new DelegateCommand(ExecuteEntriesContextCommand);
            _engineVersion = _explorerService.GetEngineVersion();
            WireRootAndViewportEvents();
            InitializeViewHostsAndSettings();
            WireShellCommandsAndStartup();
            ApplyExplorerPaneLayout();
            InitializeWorkspaceTabs();
            RefreshTitleBarDragRectangles();
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

        private void SecondaryPaneBodyBorder_Loaded(object sender, RoutedEventArgs e)
        {
            if (SecondaryPaneBodyThemeShadow is null || SecondaryPaneShadowReceiverGrid is null)
            {
                return;
            }

            SecondaryPaneBodyThemeShadow.Receivers.Clear();
            SecondaryPaneBodyThemeShadow.Receivers.Add(SecondaryPaneShadowReceiverGrid);
        }

    }

}
