using FileExplorerUI.Collections;
using FileExplorerUI.Commands;
using FileExplorerUI.Controls;
using FileExplorerUI.Interop;
using FileExplorerUI.Settings;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private GridLength _sidebarColumnWidth = new(220);
        private UIElement? _activeSplitterElement;
        private SplitterDragMode _activeSplitterDragMode;
        private ColumnSplitterKind? _activeColumnSplitterKind;
        private WorkspacePanelId _activeColumnSplitterPanelId = WorkspacePanelId.Primary;
        private ColumnResizeState? _activeColumnResizeState;
        private double _splitterDragStartX;
        private double? _sidebarDragStartWidth;
        private int _splitterHoverCount;
        private double _sidebarPreferredExpandedWidth = 220;
        private bool _isSidebarCompact;
        private bool _sidebarPinnedCompact;
        private TreeView? _sidebarTreeView;
        private CancellationTokenSource? _sidebarTreeCts;
        private bool _suppressSidebarTreeSelection;
        private int _sidebarTreeScrollRequestVersion;
        private readonly Dictionary<string, string> _sidebarTreeSelectionMemory = new(StringComparer.OrdinalIgnoreCase);
        private EntryViewModel? _pendingContextRenameEntry;
        private WorkspacePanelId _pendingContextRenamePanelId = WorkspacePanelId.Primary;
        private EntriesContextRequest? _entriesContextRequest;
        private EntryViewModel? _lastEntriesContextItem;
        private EntriesContextRequest? _pendingEntriesContextRequest;
        private PendingEntriesContextCommand? _pendingEntriesContextCommand;
        private readonly FavoritesController _favoritesController = new();
        private readonly DirectorySessionController _directorySessionController = new();
        private readonly FileOperationsController _fileOperationsController = new();
        private readonly PaneFileCommandController _paneFileCommandController;
        private readonly SettingsController _settingsController = new();
        private readonly WatcherController _watcherController = new();
        private readonly FileCommandCatalog _fileCommandCatalog = new();
        private readonly InlineEditCoordinator _inlineEditCoordinator = new();
        private readonly DelegateCommand _entriesContextCommand;
        private InlineEditSession? _entriesRenameInlineSession;
        private InlineEditSession? _sidebarTreeRenameInlineSession;
        private InlineEditSession? _addressInlineSession;
        private InlineEditSession? _secondaryAddressInlineSession;
        private SidebarTreeEntry? _pendingSidebarTreeContextEntry;
        private TreeViewNode? _activeSidebarTreeContextNode;
        private MenuFlyoutItem? _sidebarTreeExpandMenuItem;
        private MenuFlyoutItem? _sidebarTreeCollapseMenuItem;
        private MenuFlyoutSeparator? _sidebarTreeExpandCollapseSeparator;
        private Canvas? _sidebarTreeRenameOverlayCanvas;
        private Border? _sidebarTreeRenameOverlayBorder;
        private TextBox? _sidebarTreeRenameTextBox;
        private ControlTemplate? _renameOverlayTextBoxTemplate;
        private SidebarTreeEntry? _activeSidebarTreeRenameEntry;
        private bool _isCommittingSidebarTreeRename;
        private bool _isSidebarSelectionActive;
        private bool _isEntriesSelectionActive = true;
        private bool _isDualPaneEnabled = false;
        private readonly SelectionSurfaceCoordinator _selectionSurfaceCoordinator =
            new(SelectionSurfaceId.Sidebar);

        private const double SidebarExpandedDefaultWidth = 220;
        private const double SidebarExpandedMinWidth = 32;
        private const double SidebarCompactWidth = 48;
        private const double SidebarCompactThreshold = SidebarCompactWidth;
        private const double SidebarCompactExitThreshold = SidebarCompactWidth + 24;
        private const double SidebarSplitterWidth = 0;
        private const double SidebarMinContentWidth = 520;
        private const int SidebarTreeMaxChildren = 5000;
        private const string ShellMyComputerPath = "shell:mycomputer";
        private const double SidebarTreeRenameOffsetX = -2;
        private const double SidebarTreeRenameOffsetY = 0;
        private const double SidebarTreeRenameTextMarginTopOffset = 1;
        private const double SidebarTreeRenameMinWidth = 140;
        private const double SidebarTreeRenameWidthPadding = 12;
        private const double SidebarTreeRenameRightMargin = 8;
        private const double GroupedListColumnSpacing = 12;
        private const double ToolbarSearchMaxWidth = 280;
        private const double ShellWindowHorizontalPadding = 8;
        private const double ShellTitleBarHeightValue = 40;
        private const double ShellControlSizeValue = 32;
        private const double ShellGlyphSizeValue = 12;
        private const double ShellTitleBarLeftInsetWidthValue = 38;
        private const double ShellToolbarBottomSpacing = 12;
        private const double ShellStatusBarHeightValue = 32;
        private const double ShellSplitterWidthValue = 8;
        private const double ExplorerPaneActionRailWidthValue = 48;
        private const double SettingsNavigationCompactPaneLengthValue = 40;
        private static readonly DataTemplate SidebarTreeItemTemplate = CreateSidebarTreeItemTemplate();

        public event PropertyChangedEventHandler? PropertyChanged;

        private const uint InitialPageSize = 96;
        private const uint MinPageSize = 64;
        private const uint MaxPageSize = 1000;
        private PanelViewState PrimaryPanelState => CurrentWorkspaceShellState.Primary;
        private PanelDataSession PrimaryPanelDataSession => PrimaryPanelState.DataSession;
        private readonly EntriesPresentationBuilder _entriesPresentationBuilder = new();
        private readonly EntriesRepeaterLayoutProfile _detailsRepeaterLayoutProfile;
        private readonly FixedExtentVirtualizingLayout _detailsVirtualizingLayout;
        private readonly GroupedListRepeaterLayoutProfile _groupedRepeaterLayoutProfile;
        private readonly GroupedListVirtualizingLayout _groupedVirtualizingLayout;
        private IEntriesViewHost? _detailsEntriesViewHost;
        private IEntriesViewHost? _groupedEntriesViewHost;
        private NavigationPerfSession? _activeNavigationPerfSession;
        public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs => PrimaryPanelNavigation.Breadcrumbs;
        public ObservableCollection<BreadcrumbItemViewModel> VisibleBreadcrumbs => PrimaryPanelNavigation.VisibleBreadcrumbs;
        private bool _entriesFlyoutOpen;
        private CommandMenuFlyout? _activeEntriesContextFlyout;
        private string? _selectedEntryPath
        {
            get => PrimaryPanelState.SelectedEntryPath;
            set => PrimaryPanelState.SelectedEntryPath = value;
        }

        private string? _focusedEntryPath
        {
            get => PrimaryPanelState.FocusedEntryPath;
            set => PrimaryPanelState.FocusedEntryPath = value;
        }
        private string? _pendingParentReturnAnchorPath
        {
            get => PrimaryPanelState.PendingParentReturnAnchorPath;
            set => PrimaryPanelState.PendingParentReturnAnchorPath = value;
        }

        private string? _pendingHistoryStateRestorePath
        {
            get => PrimaryPanelState.PendingHistoryStateRestorePath;
            set => PrimaryPanelState.PendingHistoryStateRestorePath = value;
        }
        private EntryViewMode _currentViewMode
        {
            get => PrimaryPanelState.ViewMode;
            set => PrimaryPanelState.ViewMode = value;
        }
        private EntrySortField _currentSortField
        {
            get => PrimaryPanelState.SortField;
            set => PrimaryPanelState.SortField = value;
        }
        private SortDirection _currentSortDirection
        {
            get => PrimaryPanelState.SortDirection;
            set => PrimaryPanelState.SortDirection = value;
        }
        private EntryGroupField _currentGroupField
        {
            get => PrimaryPanelState.GroupField;
            set => PrimaryPanelState.GroupField = value;
        }
        private ShellMode _shellMode = ShellMode.Explorer;
        private SettingsSection _currentSettingsSection = SettingsSection.General;
        private bool _suppressSettingsNavigationSelection;
        private readonly AppSettingsService _appSettingsService = new();
        private AppSettings _appSettings = new();
        private EntryViewDensityMode _currentEntryViewDensityMode = EntryViewDensityMode.Normal;
        private double _lastDetailsHorizontalOffset
        {
            get => PrimaryPanelState.LastDetailsHorizontalOffset;
            set => PrimaryPanelState.LastDetailsHorizontalOffset = value;
        }

        private double _lastDetailsVerticalOffset
        {
            get => PrimaryPanelState.LastDetailsVerticalOffset;
            set => PrimaryPanelState.LastDetailsVerticalOffset = value;
        }

        private double _lastGroupedHorizontalOffset
        {
            get => PrimaryPanelState.LastGroupedHorizontalOffset;
            set => PrimaryPanelState.LastGroupedHorizontalOffset = value;
        }

        private double _lastGroupedVerticalOffset
        {
            get => PrimaryPanelState.LastGroupedVerticalOffset;
            set => PrimaryPanelState.LastGroupedVerticalOffset = value;
        }
        private double _estimatedItemHeight = 32.0;
        private Stack<string> _backStack => PrimaryPanelNavigation.BackStack;
        private Stack<string> _forwardStack => PrimaryPanelNavigation.ForwardStack;
        private readonly ExplorerService _explorerService = new();
        private readonly FileManagementCoordinator _fileManagementCoordinator;
        private readonly HashSet<string> _suppressedWatcherRefreshPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sparseViewportGate = new();
        private int? _pendingSparseViewportTargetIndex;
        private bool _pendingSparseViewportPreferMinimalPage;
        private bool _isSparseViewportLoadActive;
        private readonly string _engineVersion;
        private EntryViewModel? _activeRenameOverlayEntry;
        private WorkspacePanelId _activeRenameOverlayPanelId = WorkspacePanelId.Primary;
        private EntryViewModel? _pendingCreatedEntrySelection;
        private bool _isCommittingRenameOverlay;
        private bool _suppressRenameOverlayFocusRestoreOnCancel;
        private RustUsnCapability _usnCapability;
        private FileSystemWatcher? _dirWatcher;
        private CancellationTokenSource? _watcherDebounceCts;
        private readonly Dictionary<string, FileSystemWatcher> _favoriteWatchers = new(StringComparer.OrdinalIgnoreCase);
        private bool _localizedUiRefreshScheduled;
        private bool _localizedUiRefreshPending;
        private int _localizedUiRefreshVersion;
        private int _localizedUiDeferredRefreshVersion;
        private string? _lastNotifiedCurrentTabTitleText;
        private string? _lastNotifiedTitleBarTabGlyph;
        private CommandDockSide _commandDockSide = CommandDockSide.Top;
        private bool _showCommandDock = false;
        private bool _sidebarInitialized;
        private long _lastWatcherRefreshTick;
        private bool _allowActualClose;
        private bool _isHiddenToTray;
        private bool _trayIconAdded;
        private int _lastRestoredWindowWidth;
        private int _lastRestoredWindowHeight;
        private bool _windowSizeRestorePending;
        private bool _hasSeenFirstActivation;
        private double _lastWindowWidth = double.NaN;
        private double _lastWindowHeight = double.NaN;
        private readonly WorkspaceSession _workspaceSession;
        private WorkspaceTabManager _workspaceTabManager => _workspaceSession.TabManager;
        private WorkspaceLayoutHost _workspaceLayoutHost => _workspaceSession.LayoutHost;
        private WorkspaceShellState CurrentWorkspaceShellState => _workspaceSession.CurrentShellState;
        private bool _startupWorkspaceSessionRestored;
        private readonly WorkspaceUiApplier _workspaceUiApplier;
        private readonly WorkspaceTabController _workspaceTabController;
        private readonly WorkspaceTabStripHost _workspaceTabStripHost;
        private readonly WorkspaceChromeCoordinator _workspaceChromeCoordinator;
        private PanelNavigationState PrimaryPanelNavigation => PrimaryPanelState.Navigation;
        private DataTransferManager? _shareDataTransferManager;
        private FileCommandTarget? _pendingShareTarget;
        private IntPtr _windowHandle;
        private IntPtr _originalWndProc;
        private WndProcDelegate? _wndProcDelegate;
        private MenuFlyout? _activeBreadcrumbFlyout;
        private List<BreadcrumbItemViewModel> _hiddenBreadcrumbItems => PrimaryPanelNavigation.HiddenBreadcrumbItems;
        private bool _breadcrumbWidthsReady
        {
            get => PrimaryPanelNavigation.BreadcrumbWidthsReady;
            set => PrimaryPanelNavigation.BreadcrumbWidthsReady = value;
        }

        private int _breadcrumbVisibleStartIndex
        {
            get => PrimaryPanelNavigation.BreadcrumbVisibleStartIndex;
            set => PrimaryPanelNavigation.BreadcrumbVisibleStartIndex = value;
        }
        private bool _lastTitleWasReadFailed;
        private readonly SemaphoreSlim _statusDialogSemaphore = new(1, 1);
        private DispatcherTimer? _renameInputTeachingTipTimer;
        private DispatcherTimer? _addressInputTeachingTipTimer;
        private bool _suppressRenameTextFiltering;
        private Controls.ModalActionDialog? _operationFeedbackDialog;
        private Controls.ModalActionDialog? _pasteConflictDialog;

        private readonly record struct PrimaryPresentationNotificationState(
            EntryViewMode ViewMode,
            EntrySortField SortField,
            SortDirection SortDirection,
            EntryGroupField GroupField,
            EntryViewDensityMode DensityMode);

        private readonly record struct PanelColumnLayoutNotificationState(
            bool IsSplit,
            double PrimaryNameColumnWidth,
            double PrimaryTypeColumnWidth,
            double PrimarySizeColumnWidth,
            double PrimaryModifiedColumnWidth,
            double PrimaryDetailsContentWidth,
            double PrimaryDetailsRowWidth,
            double SecondaryNameColumnWidth,
            double SecondaryTypeColumnWidth,
            double SecondarySizeColumnWidth,
            double SecondaryModifiedColumnWidth,
            double SecondaryDetailsContentWidth,
            double SecondaryDetailsRowWidth);
    }
}
