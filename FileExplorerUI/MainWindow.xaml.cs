using FileExplorerUI.Commands;
using FileExplorerUI.Collections;
using FileExplorerUI.Controls;
using FileExplorerUI.Interop;
using FileExplorerUI.Settings;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using Windows.Foundation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT;
using WinRT.Interop;

namespace FileExplorerUI
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        private sealed class NavigationPerfSession
        {
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private long _lastElapsedMs;

            public NavigationPerfSession(string targetPath, string trigger)
            {
                TargetPath = targetPath;
                Trigger = trigger;
                Id = Interlocked.Increment(ref s_navigationPerfSequence);
                Mark("session.start");
            }

            public int Id { get; }

            public string TargetPath { get; }

            public string Trigger { get; }

            public void Mark(string stage, string? detail = null)
            {
                long totalMs = _stopwatch.ElapsedMilliseconds;
                long deltaMs = totalMs - _lastElapsedMs;
                _lastElapsedMs = totalMs;

                string message = $"[NAV-PERF #{Id}] total={totalMs}ms delta={deltaMs}ms stage={stage} trigger={Trigger} path=\"{TargetPath}\"";
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message += $" detail={detail}";
                }

                Debug.WriteLine(message);
                AppendNavigationPerfLog(message);
            }
        }

        private enum PresentationReloadReason
        {
            ViewModeSwitch,
            PresentationSettingsChange,
            DataRefresh
        }

        private enum ShellMode
        {
            Explorer,
            Settings
        }

        private static int s_navigationPerfSequence;
        private static readonly object s_navigationPerfLogLock = new();
        private static readonly string s_navigationPerfLogPath = Path.Combine(
            AppContext.BaseDirectory,
            "navigation-perf.log");
        private static int s_detailsViewportPerfSequence;
        private const int SettingsShellMinWindowWidth = 1024;

        private sealed record EntriesContextRequest(
            UIElement Anchor,
            Point Position,
            EntryViewModel? Entry,
            bool IsItemTarget);

        private sealed record PendingEntriesContextCommand(
            string CommandId,
            FileCommandTarget Target);

        private sealed class DelegateCommand : ICommand
        {
            private readonly Action<object?> _execute;

            public DelegateCommand(Action<object?> execute)
            {
                _execute = execute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter) => _execute(parameter);

            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class InlineEditCoordinator
        {
            private InlineEditSession? _activeSession;

            public bool HasActiveSession => _activeSession is not null;

            public void BeginSession(InlineEditSession session)
            {
                if (_activeSession is not null && !ReferenceEquals(_activeSession, session))
                {
                    _activeSession.Cancel();
                }

                _activeSession = session;
            }

            public void ClearSession(InlineEditSession session)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }
            }

            public bool IsSourceWithinActiveSession(DependencyObject? source)
            {
                return _activeSession?.ContainsSource(source) == true;
            }

            public Task CommitActiveSessionAsync()
            {
                return _activeSession?.CommitAsync() ?? Task.CompletedTask;
            }

            public void CancelActiveSession()
            {
                _activeSession?.Cancel();
            }
        }

        private sealed class InlineEditSession
        {
            private readonly Func<Task> _commitAsync;
            private readonly Action _cancel;
            private readonly Func<DependencyObject?, bool> _containsSource;

            public InlineEditSession(
                Func<Task> commitAsync,
                Action cancel,
                Func<DependencyObject?, bool> containsSource)
            {
                _commitAsync = commitAsync;
                _cancel = cancel;
                _containsSource = containsSource;
            }

            public Task CommitAsync() => _commitAsync();

            public void Cancel() => _cancel();

            public bool ContainsSource(DependencyObject? source) => _containsSource(source);
        }

        private sealed class SelectionSurfaceCoordinator
        {
            private bool _isWindowActive = true;

            public SelectionSurfaceCoordinator(SelectionSurfaceId initialActiveSurface)
            {
                ActiveSurface = initialActiveSurface;
            }

            public SelectionSurfaceId ActiveSurface { get; private set; }

            public bool SetWindowActive(bool isActive)
            {
                if (_isWindowActive == isActive)
                {
                    return false;
                }

                _isWindowActive = isActive;
                return true;
            }

            public bool SetActiveSurface(SelectionSurfaceId surface)
            {
                if (ActiveSurface == surface)
                {
                    return false;
                }

                ActiveSurface = surface;
                return true;
            }

            public bool IsSurfaceActive(SelectionSurfaceId surface)
            {
                return _isWindowActive && ActiveSurface == surface;
            }
        }

        private GridLength _nameColumnWidth = new(220);
        private GridLength _typeColumnWidth = new(150);
        private GridLength _sizeColumnWidth = new(120);
        private GridLength _modifiedColumnWidth = new(180);
        private GridLength _sidebarColumnWidth = new(220);
        private double _detailsContentWidth = 694;
        private double _detailsRowWidth = 714;
        private UIElement? _activeSplitterElement;
        private SplitterDragMode _activeSplitterDragMode;
        private ColumnSplitterKind? _activeColumnSplitterKind;
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
        private readonly Dictionary<string, string> _sidebarTreeSelectionMemory = new(StringComparer.OrdinalIgnoreCase);
        private bool _suppressSidebarNavSelection;
        private EntryViewModel? _pendingContextRenameEntry;
        private EntriesContextRequest? _entriesContextRequest;
        private EntryViewModel? _lastEntriesContextItem;
        private EntriesContextRequest? _pendingEntriesContextRequest;
        private PendingEntriesContextCommand? _pendingEntriesContextCommand;
        private readonly FileCommandCatalog _fileCommandCatalog = new();
        private readonly InlineEditCoordinator _inlineEditCoordinator = new();
        private readonly DelegateCommand _entriesContextCommand;
        private InlineEditSession? _entriesRenameInlineSession;
        private InlineEditSession? _sidebarTreeRenameInlineSession;
        private InlineEditSession? _addressInlineSession;
        private MenuFlyout? _sidebarTreeContextFlyout;
        private SidebarTreeEntry? _pendingSidebarTreeContextEntry;
        private Canvas? _sidebarTreeRenameOverlayCanvas;
        private Border? _sidebarTreeRenameOverlayBorder;
        private TextBox? _sidebarTreeRenameTextBox;
        private ControlTemplate? _renameOverlayTextBoxTemplate;
        private SidebarTreeEntry? _activeSidebarTreeRenameEntry;
        private bool _isCommittingSidebarTreeRename;
        private bool _isSidebarSelectionActive;
        private bool _isEntriesSelectionActive = true;
        private readonly SelectionSurfaceCoordinator _selectionSurfaceCoordinator =
            new(SelectionSurfaceId.Sidebar);

        private const double SidebarExpandedDefaultWidth = 220;
        private const double SidebarExpandedMinWidth = 32;
        private const double SidebarCompactWidth = 40;
        private const double SidebarCompactThreshold = SidebarCompactWidth;
        private const double SidebarCompactExitThreshold = SidebarCompactWidth + 24;
        private const double SidebarSplitterWidth = 0;
        private const double SidebarMinContentWidth = 520;
        private const int SidebarTreeMaxChildren = 5000;
        private const string ShellMyComputerPath = "shell:mycomputer";
        private const double SidebarTreeRenameOffsetX = -2;
        private const double SidebarTreeRenameOffsetY = 0;
        private const double SidebarTreeRenameMinWidth = 140;
        private const double SidebarTreeRenameWidthPadding = 12;
        private const double SidebarTreeRenameRightMargin = 8;
        private static readonly DataTemplate SidebarTreeItemTemplate = CreateSidebarTreeItemTemplate();
        private static string S(string key) => LocalizedStrings.Instance.Get(key);
        private static string SF(string key, params object[] args)
        {
            string? format = S(key);
            if (string.IsNullOrEmpty(format))
            {
                return key;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }
        private void UpdateStatusKey(string key, params object[] args) => UpdateStatus(SF(key, args));
        private static string CreateKindLabel(bool isDirectory) => S(isDirectory ? "CreateKindFolder" : "CreateKindFile");

        public event PropertyChangedEventHandler? PropertyChanged;

        public EntryItemMetrics EntryItemMetrics { get; private set; } = EntryItemMetrics.CreatePreset(EntryViewDensityMode.Normal);

        public double EntryContainerWidth => _currentViewMode == EntryViewMode.List ? 360 : DetailsRowWidth;

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

        private const uint InitialPageSize = 96;
        private const uint MinPageSize = 64;
        private const uint MaxPageSize = 1000;
        private readonly BatchObservableCollection<EntryViewModel> _entries = new();
        private readonly ObservableCollection<GroupedEntryColumnViewModel> _groupedEntryColumns = new();
        private readonly List<EntryViewModel> _presentationSourceEntries = new();
        private List<GroupedEntryColumnViewModel>? _groupedColumnsProjectionCache;
        private int _presentationSourceVersion;
        private int _groupedColumnsCacheSourceVersion = -1;
        private EntrySortField _groupedColumnsCacheSortField;
        private SortDirection _groupedColumnsCacheSortDirection;
        private EntryGroupField _groupedColumnsCacheGroupField;
        private readonly EntriesPresentationBuilder _entriesPresentationBuilder = new();
        private readonly EntriesRepeaterLayoutProfile _detailsRepeaterLayoutProfile;
        private readonly FixedExtentVirtualizingLayout _detailsVirtualizingLayout;
        private IEntriesViewHost? _detailsEntriesViewHost;
        private IEntriesViewHost? _groupedEntriesViewHost;
        private NavigationPerfSession? _activeNavigationPerfSession;
        public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = new();
        public ObservableCollection<BreadcrumbItemViewModel> VisibleBreadcrumbs { get; } = new();
        private ulong _nextCursor;
        private bool _hasMore;
        private bool _isLoading;
        private bool _entriesFlyoutOpen;
        private CommandMenuFlyout? _activeEntriesContextFlyout;
        private string _currentPath = ShellMyComputerPath;
        private string? _selectedEntryPath;
        private string? _focusedEntryPath;
        private string? _pendingParentReturnAnchorPath;
        private string? _pendingHistoryStateRestorePath;
        private readonly Dictionary<string, DirectoryViewState> _directoryViewStates = new(StringComparer.OrdinalIgnoreCase);
        private uint _currentPageSize = InitialPageSize;
        private uint _lastFetchMs;
        private uint _totalEntries;
        private DirectorySortMode _currentSortMode = DirectorySortMode.FolderFirstNameAsc;
        private EntryViewMode _currentViewMode = EntryViewMode.Details;
        private EntrySortField _currentSortField = EntrySortField.Name;
        private SortDirection _currentSortDirection = SortDirection.Ascending;
        private EntryGroupField _currentGroupField = EntryGroupField.None;
        private ShellMode _shellMode = ShellMode.Explorer;
        private SettingsSection _currentSettingsSection = SettingsSection.General;
        private bool _suppressSettingsNavigationSelection;
        private readonly AppSettingsService _appSettingsService = new();
        private AppSettings _appSettings = new();
        private EntryViewDensityMode _currentEntryViewDensityMode = EntryViewDensityMode.Normal;
        private double _lastDetailsHorizontalOffset = double.NaN;
        private double _lastDetailsVerticalOffset = double.NaN;
        private double _lastGroupedHorizontalOffset = double.NaN;
        private double _lastGroupedVerticalOffset = double.NaN;
        private double _lastDetailsVerticalDelta;
        private int _lastDetailsViewportStartIndex = -1;
        private int _lastDetailsViewportIndexDelta;
        private double _estimatedItemHeight = 32.0;
        private int _groupedListRowsPerColumn = -1;
        private Brush? _pathDefaultBorderBrush;
        private readonly Stack<string> _backStack = new();
        private readonly Stack<string> _forwardStack = new();
        private string _currentQuery = string.Empty;
        private readonly ExplorerService _explorerService = new();
        private readonly FileManagementCoordinator _fileManagementCoordinator;
        private IEntryResultSet? _activeEntryResultSet;
        private readonly HashSet<string> _suppressedWatcherRefreshPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sparseViewportGate = new();
        private int? _pendingSparseViewportTargetIndex;
        private bool _pendingSparseViewportPreferMinimalPage;
        private bool _isSparseViewportLoadActive;
        private readonly string _engineVersion;
        private EntryViewModel? _activeRenameOverlayEntry;
        private EntryViewModel? _pendingCreatedEntrySelection;
        private bool _isCommittingRenameOverlay;
        private RustUsnCapability _usnCapability;
        private FileSystemWatcher? _dirWatcher;
        private CancellationTokenSource? _watcherDebounceCts;
        private CancellationTokenSource? _directoryLoadCts;
        private CancellationTokenSource? _metadataPrefetchCts;
        private int _metadataViewportRequestVersion;
        private long _directorySnapshotVersion;
        private long _lastDetailsScrollInteractionTick;
        private CommandDockSide _commandDockSide = CommandDockSide.Top;
        private bool _showCommandDock = false;
        private bool _sidebarInitialized;
        private long _lastWatcherRefreshTick;
        private readonly Dictionary<string, NavigationViewItem> _sidebarPathButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sidebarQuickAccessPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sidebarDrivePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _groupExpansionStates = new(StringComparer.Ordinal);
        private readonly WorkspaceShellState _workspaceShellState = new();
        private readonly WorkspaceLayoutHost _workspaceLayoutHost;
        private DataTransferManager? _shareDataTransferManager;
        private FileCommandTarget? _pendingShareTarget;
        private IntPtr _windowHandle;
        private IntPtr _originalWndProc;
        private WndProcDelegate? _wndProcDelegate;
        private MenuFlyout? _activeBreadcrumbFlyout;
        private readonly List<BreadcrumbItemViewModel> _hiddenBreadcrumbItems = new();
        private bool _breadcrumbWidthsReady;
        private int _breadcrumbVisibleStartIndex = -1;
        private bool _lastTitleWasReadFailed;

        private enum CommandDockSide
        {
            Top,
            Right,
            Bottom
        }

        private enum SplitterDragMode
        {
            None,
            Column,
            Sidebar
        }

        private enum ColumnSplitterKind
        {
            Name = 1,
            Type = 2,
            Size = 3,
            Modified = 4
        }

        private readonly record struct ColumnResizeState(
            double Name,
            double Type,
            double Size,
            double Modified,
            double ContentWidth);

        private const int GWL_WNDPROC = -4;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int WM_SETCURSOR = 0x0020;
        private const int IDC_ARROW = 32512;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private sealed record MetadataWorkItem(int Index, string Name, ulong MftRef, bool IsDirectory, bool IsLink);

        private sealed record MetadataPayload(string SizeText, string ModifiedText, string IconGlyph, Brush IconForeground);

        private sealed record MetadataHydrationResult(MetadataWorkItem Item, MetadataPayload Payload);

        private sealed record ViewportMetadataResult(
            int Index,
            long? SizeBytes,
            string SizeText,
            DateTime? ModifiedAt,
            string ModifiedText);

        private sealed class DirectoryViewState
        {
            public double DetailsVerticalOffset { get; init; }
            public string? SelectedEntryPath { get; init; }
        }

        private sealed record EntryGroupDescriptor(string BucketKey, string StateKey, string Label, string OrderKey);

        private enum SelectionSurfaceId
        {
            Sidebar,
            PrimaryPane,
            SecondaryPane
        }

        private sealed class EntryGroupBucket
        {
            public required EntryGroupDescriptor Descriptor { get; init; }

            public List<EntryViewModel> Items { get; } = new();
        }

        private enum EntryIconKind
        {
            Folder,
            FolderLink,
            File,
            FileLink,
            Text,
            Archive,
            Image,
            Video,
            Audio,
            Pdf,
            Word,
            Excel,
            PowerPoint,
            Code,
            Executable,
            Shortcut,
            DiskImage
        }

        private static readonly Brush FolderIconBrush = CreateBrush(0xC4, 0x93, 0x2A);
        private static readonly Brush FolderLinkIconBrush = CreateBrush(0xA7, 0x79, 0x1F);
        private static readonly Brush FileIconBrush = CreateBrush(0x6C, 0x72, 0x7D);
        private static readonly Brush FileLinkIconBrush = CreateBrush(0x5E, 0x79, 0xB9);
        private static readonly Brush TextIconBrush = CreateBrush(0x5B, 0x7F, 0xA3);
        private static readonly Brush ArchiveIconBrush = CreateBrush(0x8B, 0x6A, 0x3F);
        private static readonly Brush ImageIconBrush = CreateBrush(0xA4, 0x62, 0xB8);
        private static readonly Brush VideoIconBrush = CreateBrush(0xC6, 0x5C, 0x5C);
        private static readonly Brush AudioIconBrush = CreateBrush(0x3F, 0x93, 0x8D);
        private static readonly Brush PdfIconBrush = CreateBrush(0xC2, 0x4F, 0x4A);
        private static readonly Brush WordIconBrush = CreateBrush(0x4C, 0x74, 0xC9);
        private static readonly Brush ExcelIconBrush = CreateBrush(0x3E, 0x8A, 0x63);
        private static readonly Brush PowerPointIconBrush = CreateBrush(0xD0, 0x72, 0x44);
        private static readonly Brush CodeIconBrush = CreateBrush(0x6A, 0x66, 0xC7);
        private static readonly Brush ExecutableIconBrush = CreateBrush(0xB0, 0x5A, 0x7A);
        private static readonly Brush ShortcutIconBrush = CreateBrush(0x5E, 0x79, 0xB9);
        private static readonly Brush DiskImageIconBrush = CreateBrush(0x6B, 0x73, 0x88);
        private const double BreadcrumbOverflowButtonWidth = 34;
        private const double BreadcrumbItemSpacing = 2;
        private const double BreadcrumbWidthReserve = 4;
        private const string BreadcrumbMyComputerGlyph = "\uE7F4";
        private readonly string _initialPath;

        public MainWindow(string? initialPath = null)
        {
            InitializeComponent();
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
            _workspaceLayoutHost = new WorkspaceLayoutHost(_workspaceShellState);
            _fileManagementCoordinator = new FileManagementCoordinator(_explorerService);
            _entriesContextCommand = new DelegateCommand(ExecuteEntriesContextCommand);
            if (Content is FrameworkElement rootElement)
            {
                rootElement.ActualThemeChanged += MainWindowRoot_ActualThemeChanged;
                rootElement.PointerEntered += RootElement_PointerEnteredOrMoved;
                rootElement.PointerMoved += RootElement_PointerEnteredOrMoved;
                rootElement.AddHandler(
                    UIElement.PointerPressedEvent,
                    new PointerEventHandler(RootElement_PointerPressedPreview),
                    true);
                rootElement.AddHandler(
                    UIElement.PreviewKeyDownEvent,
                    new KeyEventHandler(RootElement_PreviewKeyDown),
                    true);
                rootElement.GotFocus += RootElement_GotFocus;
            }
            PathTextBox.Text = _initialPath;
            RegisterColumnSplitterHandlers(HeaderSplitter1);
            RegisterColumnSplitterHandlers(HeaderSplitter2);
            RegisterColumnSplitterHandlers(HeaderSplitter3);
            RegisterColumnSplitterHandlers(HeaderSplitter4);
            RegisterSidebarSplitterHandlers(SidebarSplitter);
            DetailsEntriesRepeater.Layout = _detailsVirtualizingLayout;
            RegisterEntriesKeyHandlers(DetailsEntriesScrollViewer);
            RegisterEntriesKeyHandlers(GroupedEntriesScrollViewer);
            DetailsEntriesScrollViewer.ViewChanged += DetailsEntriesScrollViewer_ViewChanged;
            GroupedEntriesScrollViewer.ViewChanged += GroupedEntriesScrollViewer_ViewChanged;
            _detailsEntriesViewHost = new RepeaterEntriesViewHost(
                DetailsEntriesScrollViewer,
                DetailsEntriesRepeater,
                _detailsRepeaterLayoutProfile);
            _detailsEntriesViewHost.SetItems(_entries);
            _groupedEntriesViewHost = new GroupedColumnsEntriesViewHost(GroupedEntriesScrollViewer);
            UpdateEntriesContextOverlayTargets();
            this.SizeChanged += MainWindow_SizeChanged;
            this.Activated += MainWindow_Activated;
            _pathDefaultBorderBrush = PathTextBox.BorderBrush;
            _engineVersion = _explorerService.GetEngineVersion();
            _appSettings = _appSettingsService.Load();
            LocalizedStrings.Instance.PropertyChanged += LocalizedStrings_PropertyChanged;
            InitializeWorkspaceShellState();
#if !DEBUG
            LanguageToggleButton.Visibility = Visibility.Collapsed;
#endif

            UpdateNavButtonsState();
            UpdateWindowTitle();
            ApplyTitleBarTheme();
            StyledSidebarView.NavigateRequested += StyledSidebarView_NavigateRequested;
            StyledSidebarView.SettingsRequested += StyledSidebarView_SettingsRequested;
            SettingsViewControl.VisibleSectionChanged += SettingsViewControl_VisibleSectionChanged;
            SettingsViewControl.SidebarSectionVisibilityChanged += SettingsViewControl_SidebarSectionVisibilityChanged;
            SettingsViewControl.DeleteConfirmationChanged += SettingsViewControl_DeleteConfirmationChanged;
            BuildSidebarItems();
            ApplyAppSettingsToUi();
            _sidebarInitialized = true;
            ApplyCommandDockLayout();
            _ = LoadFirstPageAsync();
        }

        private void InitializeWorkspaceShellState()
        {
            _workspaceLayoutHost.LayoutMode = WorkspaceLayoutMode.Single;
            _workspaceLayoutHost.ActivatePanel(WorkspacePanelId.Primary);
            SyncActivePanelPresentationState();
        }

        private void RaisePropertyChanged(params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void EnterSettingsShell(SettingsSection section = SettingsSection.General)
        {
            _shellMode = ShellMode.Settings;
            ApplyWindowMinimumWidthForShellMode();
            SetCurrentSettingsSection(section, updateSelection: true);
            RaisePropertyChanged(
                nameof(ExplorerChromeVisibility),
                nameof(ExplorerShellVisibility),
                nameof(SettingsShellVisibility));
        }

        private void ExitSettingsShell()
        {
            _shellMode = ShellMode.Explorer;
            ApplyWindowMinimumWidthForShellMode();
            RaisePropertyChanged(
                nameof(ExplorerChromeVisibility),
                nameof(ExplorerShellVisibility),
                nameof(SettingsShellVisibility));
        }

        private void SetCurrentSettingsSection(SettingsSection section, bool updateSelection)
        {
            _currentSettingsSection = section;

            if (updateSelection && SettingsNavigationView is not null)
            {
                _suppressSettingsNavigationSelection = true;
                foreach (object item in SettingsNavigationView.MenuItems)
                {
                    if (item is NavigationViewItem navigationViewItem
                        && navigationViewItem.Tag is string tag
                        && TryParseSettingsSection(tag, out SettingsSection parsed)
                        && parsed == section)
                    {
                        SettingsNavigationView.SelectedItem = navigationViewItem;
                        break;
                    }
                }
                _suppressSettingsNavigationSelection = false;
            }

            if (updateSelection)
            {
                SettingsViewControl?.ScrollToSection(section);
            }
        }

        private void ApplyWindowMinimumWidthForShellMode()
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            if (_shellMode == ShellMode.Settings)
            {
                presenter.PreferredMinimumWidth = SettingsShellMinWindowWidth;
            }
            else
            {
                presenter.PreferredMinimumWidth = 0;
            }
        }

        private static bool TryParseSettingsSection(string? tag, out SettingsSection section)
        {
            return Enum.TryParse(tag, ignoreCase: true, out section);
        }

        private bool UsesClientPresentationPipeline()
        {
            return _currentViewMode != EntryViewMode.Details
                || _currentSortField != EntrySortField.Name
                || _currentSortDirection != SortDirection.Ascending
                || _currentGroupField != EntryGroupField.None;
        }

        private bool UsesColumnsListPresentation()
        {
            return _currentViewMode == EntryViewMode.List;
        }

        private FrameworkElement GetVisibleEntriesRoot()
        {
            return _currentViewMode == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;
        }

        private void UpdateEntriesContextOverlayTargets()
        {
            UIElement overlayTarget = GetVisibleEntriesRoot();
            FileEntriesContextFlyout.OverlayInputPassThroughElement = overlayTarget;
            FolderEntriesContextFlyout.OverlayInputPassThroughElement = overlayTarget;
            BackgroundEntriesContextFlyout.OverlayInputPassThroughElement = overlayTarget;
        }

        private IEntriesViewHost? GetVisibleEntriesViewHost()
        {
            return _currentViewMode == EntryViewMode.Details
                ? _detailsEntriesViewHost
                : _groupedEntriesViewHost;
        }

        private bool NeedsEntriesHorizontalScroll()
        {
            if (_currentViewMode != EntryViewMode.Details)
            {
                return true;
            }

            double viewportWidth = DetailsEntriesScrollViewer.ViewportWidth > 0
                ? DetailsEntriesScrollViewer.ViewportWidth
                : DetailsEntriesScrollViewer.ActualWidth;
            if (viewportWidth <= 0)
            {
                return true;
            }

            return DetailsRowWidth > viewportWidth - 1;
        }

        public IReadOnlyList<GroupedEntryColumnViewModel> GroupedEntryColumns => _groupedEntryColumns;

        private PanelViewState GetActivePanelState()
        {
            return _workspaceLayoutHost.GetActivePanelState();
        }

        private void SyncActivePanelPresentationState()
        {
            PanelViewState activePanel = GetActivePanelState();
            activePanel.ViewMode = _currentViewMode;
            activePanel.SortField = _currentSortField;
            activePanel.SortDirection = _currentSortDirection;
            activePanel.GroupField = _currentGroupField;
            activePanel.QueryText = _currentQuery;
            activePanel.CurrentPath = _currentPath;
            activePanel.SelectedEntryPath = _selectedEntryPath;
        }

        private void NotifyPresentationModeChanged()
        {
            _groupedListRowsPerColumn = -1;
            ApplyEntryItemMetricsPreset();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryContainerWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesListVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupedColumnsVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsHeaderVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameHeaderMargin)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsNameCellMargin)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollBarVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollMode)));
            UpdateViewCommandStates();
            SyncActivePanelPresentationState();
        }

        private void ApplyEntryItemMetricsPreset()
        {
            EntryItemMetrics = EntryItemMetrics.CreatePreset(_currentEntryViewDensityMode);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryItemMetrics)));
            InvalidateEntriesLayouts();
        }

        private void UpdateViewCommandStates()
        {
            if (ViewDetailsMenuItem is not null)
            {
                ViewDetailsMenuItem.IsChecked = _currentViewMode == EntryViewMode.Details;
            }

            if (ViewListMenuItem is not null)
            {
                ViewListMenuItem.IsChecked = _currentViewMode == EntryViewMode.List;
            }

            if (SortByNameMenuItem is not null)
            {
                SortByNameMenuItem.IsChecked = _currentSortField == EntrySortField.Name;
                SortByTypeMenuItem.IsChecked = _currentSortField == EntrySortField.Type;
                SortBySizeMenuItem.IsChecked = _currentSortField == EntrySortField.Size;
                SortByModifiedDateMenuItem.IsChecked = _currentSortField == EntrySortField.ModifiedDate;
                SortAscendingMenuItem.IsChecked = _currentSortDirection == SortDirection.Ascending;
                SortDescendingMenuItem.IsChecked = _currentSortDirection == SortDirection.Descending;
            }

            if (GroupByNoneMenuItem is not null)
            {
                GroupByNoneMenuItem.IsChecked = _currentGroupField == EntryGroupField.None;
                GroupByNameMenuItem.IsChecked = _currentGroupField == EntryGroupField.Name;
                GroupByTypeMenuItem.IsChecked = _currentGroupField == EntryGroupField.Type;
                GroupByModifiedDateMenuItem.IsChecked = _currentGroupField == EntryGroupField.ModifiedDate;
            }
        }

        private async Task SetViewModeAsync(EntryViewMode mode)
        {
            if (_currentViewMode == mode)
            {
                UpdateViewCommandStates();
                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                return;
            }

            _currentViewMode = mode;
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.ViewModeSwitch);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEntriesContextOverlayTargets();
                GetVisibleEntriesRoot().UpdateLayout();
                FocusEntriesList();
            });
        }

        private async Task SetSortAsync(EntrySortField field, SortDirection? explicitDirection = null)
        {
            _currentSortField = field;
            _currentSortDirection = explicitDirection ?? field switch
            {
                EntrySortField.ModifiedDate or EntrySortField.Size => SortDirection.Descending,
                _ => SortDirection.Ascending
            };
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task SetSortDirectionAsync(SortDirection direction)
        {
            _currentSortDirection = direction;
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task SetGroupAsync(EntryGroupField field)
        {
            _currentGroupField = field;
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task ReloadCurrentPresentationAsync(PresentationReloadReason reason = PresentationReloadReason.DataRefresh)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                PopulateMyComputerEntries();
                ApplyCurrentPresentation();
                return;
            }

            if (TryApplyPresentationFastPath(reason))
            {
                return;
            }

            if (UsesClientPresentationPipeline())
            {
                await LoadAllEntriesForPresentationAsync(_currentPath);
                return;
            }

            await LoadPageAsync(_currentPath, cursor: 0, append: false);
        }

        private async void ViewDetailsMenuItem_Click(object sender, RoutedEventArgs e) => await SetViewModeAsync(EntryViewMode.Details);
        private async void ViewListMenuItem_Click(object sender, RoutedEventArgs e) => await SetViewModeAsync(EntryViewMode.List);
        private async void SortByNameMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.Name);
        private async void SortByTypeMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.Type);
        private async void SortBySizeMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.Size);
        private async void SortByModifiedDateMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.ModifiedDate);
        private async void SortAscendingMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortDirectionAsync(SortDirection.Ascending);
        private async void SortDescendingMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortDirectionAsync(SortDirection.Descending);
        private async void GroupByNoneMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.None);
        private async void GroupByNameMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.Name);
        private async void GroupByTypeMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.Type);
        private async void GroupByModifiedDateMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.ModifiedDate);

        private void LocalizedStrings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not "Item[]" and not nameof(LocalizedStrings.DebugLanguageButtonText))
            {
                return;
            }

            _ = DispatcherQueue.TryEnqueue(RefreshLocalizedUi);
        }

        private void RefreshLocalizedUi()
        {
            UpdateDetailsHeaders();
            UpdateWindowTitle();

            if (_isLoading)
            {
                UpdateStatus(S("StatusLoading"));
                return;
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus(SF("StatusDriveCount", _entries.Count));
                return;
            }

            UpdateStatus(SF("StatusCurrentFolderItems", _totalEntries));
        }

        private void UpdateWindowTitle()
        {
            if (AppWindow is null)
            {
                return;
            }

            if (_lastTitleWasReadFailed)
            {
                this.AppWindow.Title = SF("WindowTitleReadFailedFormat", _engineVersion);
                return;
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                this.AppWindow.Title = SF("WindowTitleDrivesFormat", _engineVersion, _entries.Count);
                return;
            }

            this.AppWindow.Title = SF("WindowTitleItemsFormat", _engineVersion, _entries.Count);
        }

        private void MainWindowRoot_ActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyTitleBarTheme();
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            _selectionSurfaceCoordinator.SetWindowActive(args.WindowActivationState != WindowActivationState.Deactivated);
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                await _inlineEditCoordinator.CommitActiveSessionAsync();
            }

            UpdateSelectionActivityState();
            TryResetSystemCursorToArrow();
        }

        private void RootElement_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdateSelectionActivityState();
        }

        private void RootElement_PointerEnteredOrMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitterElement is null)
            {
                TryResetSystemCursorToArrow();
            }
        }

        private async void RootElement_PointerPressedPreview(object sender, PointerRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject pointerSource)
            {
                if (IsDescendantOf(pointerSource, StyledSidebarView))
                {
                    SetActiveSelectionSurface(SelectionSurfaceId.Sidebar);
                }
                else if (IsDescendantOf(pointerSource, ExplorerBodyGrid))
                {
                    SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
                }
            }

            if (!_inlineEditCoordinator.HasActiveSession)
            {
                return;
            }

            if (e.OriginalSource is DependencyObject source &&
                _inlineEditCoordinator.IsSourceWithinActiveSession(source))
            {
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private void RootElement_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled || _inlineEditCoordinator.HasActiveSession)
            {
                return;
            }

            if (IsTextInputSource(e.OriginalSource as DependencyObject))
            {
                return;
            }

            e.Handled = HandleGlobalShortcutKey(e.Key);
        }

        private void ApplyTitleBarTheme()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }
            AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        }

        private void RegisterColumnSplitterHandlers(UIElement splitter)
        {
            splitter.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ColumnSplitter_PointerPressed), true);
            splitter.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(ColumnSplitter_PointerMoved), true);
            splitter.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ColumnSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ColumnSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ColumnSplitter_PointerReleased), true);
        }

        private void RegisterEntriesKeyHandlers(UIElement host)
        {
            host.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(EntriesView_KeyDown), true);
        }

        private void RegisterSidebarSplitterHandlers(UIElement splitter)
        {
            splitter.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SidebarSplitter_PointerPressed), true);
            splitter.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(SidebarSplitter_PointerMoved), true);
            splitter.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(SidebarSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(SidebarSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(SidebarSplitter_PointerReleased), true);
        }

        private void InstallWindowHook()
        {
            try
            {
                _windowHandle = WindowNative.GetWindowHandle(this);
                if (_windowHandle == IntPtr.Zero)
                {
                    return;
                }

                _wndProcDelegate = WindowProc;
                IntPtr newProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
                _originalWndProc = NativeMethods.SetWindowLongPtr(_windowHandle, GWL_WNDPROC, newProc);
            }
            catch
            {
                // Non-fatal: flyout still supports normal light-dismiss behavior.
            }
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN)
            {
                _ = DispatcherQueue.TryEnqueue(CloseActiveBreadcrumbFlyout);
            }

            return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        private void CloseActiveBreadcrumbFlyout()
        {
            if (_activeBreadcrumbFlyout?.IsOpen == true)
            {
                _activeBreadcrumbFlyout.Hide();
            }
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateToPathAsync(PathTextBox.Text.Trim(), pushHistory: true);
        }

        private void ColumnSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter || !TryGetColumnSplitterKind(splitter, out ColumnSplitterKind kind))
            {
                return;
            }

            _activeSplitterElement = splitter;
            _activeSplitterDragMode = SplitterDragMode.Column;
            _activeColumnSplitterKind = kind;
            _activeColumnResizeState = new ColumnResizeState(
                NameColumnWidth.Value,
                TypeColumnWidth.Value,
                SizeColumnWidth.Value,
                ModifiedColumnWidth.Value,
                DetailsContentWidth);
            _splitterDragStartX = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            splitter.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void ColumnSplitterHover_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _splitterHoverCount++;
        }

        private void ColumnSplitterHover_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_splitterHoverCount > 0)
            {
                _splitterHoverCount--;
            }
        }

        private void ColumnSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter
                || !ReferenceEquals(splitter, _activeSplitterElement)
                || _activeSplitterDragMode != SplitterDragMode.Column
                || _activeColumnSplitterKind is not ColumnSplitterKind kind
                || _activeColumnResizeState is not ColumnResizeState state)
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double x = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            double delta = x - _splitterDragStartX;
            if (Math.Abs(delta) < 0.5)
            {
                return;
            }

            const double minType = 90;
            const double minSize = 80;
            const double minName = 120;
            const double minModified = 120;

            double name = state.Name;
            double type = state.Type;
            double size = state.Size;
            double modified = state.Modified;
            double content = state.ContentWidth;

            switch (kind)
            {
                case ColumnSplitterKind.Name:
                    {
                        name = Math.Max(minName, state.Name + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case ColumnSplitterKind.Type:
                    {
                        type = Math.Max(minType, state.Type + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case ColumnSplitterKind.Size:
                    {
                        size = Math.Max(minSize, state.Size + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case ColumnSplitterKind.Modified:
                    {
                        modified = Math.Max(minModified, state.Modified + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
            }

            NameColumnWidth = new GridLength(name);
            TypeColumnWidth = new GridLength(type);
            SizeColumnWidth = new GridLength(size);
            ModifiedColumnWidth = new GridLength(modified);
            DetailsContentWidth = content;
            e.Handled = true;
        }

        private void ColumnSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndActiveSplitterDrag(sender as UIElement);
            e.Handled = true;
        }

        private static bool TryGetColumnSplitterKind(UIElement splitter, out ColumnSplitterKind kind)
        {
            kind = default;
            if (splitter is not FrameworkElement element
                || element.Tag is not string tagText
                || !int.TryParse(tagText, out int tagValue)
                || !Enum.IsDefined(typeof(ColumnSplitterKind), tagValue))
            {
                return false;
            }

            kind = (ColumnSplitterKind)tagValue;
            return true;
        }

        private void EndActiveSplitterDrag(UIElement? splitter)
        {
            splitter?.ReleasePointerCaptures();
            _activeSplitterElement = null;
            _activeSplitterDragMode = SplitterDragMode.None;
            _activeColumnSplitterKind = null;
            _activeColumnResizeState = null;
            _sidebarDragStartWidth = null;
            _splitterDragStartX = 0;
        }


        private async void PathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CancelAddressEdit();
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await CommitAddressEditIfActiveAsync();
        }

        private async void PathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (IsFocusedElementWithinAddressEdit())
            {
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private void AddressBreadcrumbBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            EnterAddressEditMode(selectAll: true);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadNextPageAsync();
        }

        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                _currentQuery = string.Empty;
                SearchTextBox.Text = string.Empty;
                await ReloadCurrentPresentationAsync();
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            _currentQuery = SearchTextBox.Text?.Trim() ?? string.Empty;
            await ReloadCurrentPresentationAsync();
        }

        private void ApplySidebarSelectionImmediate(string target)
        {
            if (SidebarNavView is null)
            {
                return;
            }

            NavigationViewItem? selected = null;
            int bestLen = -1;
            string selectedPath = Path.GetFullPath(target).TrimEnd('\\');
            foreach ((string path, NavigationViewItem item) in _sidebarPathButtons)
            {
                string currentPath = Path.GetFullPath(path).TrimEnd('\\');
                bool matched = string.Equals(currentPath, selectedPath, StringComparison.OrdinalIgnoreCase)
                    || selectedPath.StartsWith(currentPath + "\\", StringComparison.OrdinalIgnoreCase);
                if (!matched)
                {
                    continue;
                }

                if (path.Length > bestLen)
                {
                    bestLen = path.Length;
                    selected = item;
                }
            }

            _suppressSidebarNavSelection = true;
            SidebarNavView.SelectedItem = selected;
            _suppressSidebarNavSelection = false;

            StyledSidebarView.SetSelectedPath(target);
            _ = SelectSidebarTreePathAsync(target);
        }

        private void DockRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb || rb.Tag is not string tag)
            {
                return;
            }
            _commandDockSide = tag switch
            {
                "Right" => CommandDockSide.Right,
                "Bottom" => CommandDockSide.Bottom,
                _ => CommandDockSide.Top,
            };
            ApplyCommandDockLayout();
        }

        private void CommandAutoHideSwitch_Toggled(object sender, RoutedEventArgs e)
        {
            ApplyCommandDockLayout();
        }

        private void CommandPeekButton_Click(object sender, RoutedEventArgs e)
        {
            if (CommandDockPanel.Visibility == Visibility.Visible)
            {
                return;
            }
            CommandDockPanel.Visibility = Visibility.Visible;
            CommandPeekButton.Visibility = Visibility.Collapsed;
        }

        private void CommandDockPanel_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (CommandAutoHideSwitch.IsOn)
            {
                CommandDockPanel.Visibility = Visibility.Collapsed;
                CommandPeekButton.Visibility = Visibility.Visible;
            }
        }

        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ExecuteRenameSelectedAsync();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteDeleteSelectedAsync();
        }

        private async void NewFileButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteNewFileAsync();
        }

        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteNewFolderAsync();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCopy();
        }

        private void CutButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCut();
        }

        private async void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecutePasteAsync();
        }

        private bool TryGetSelectedLoadedEntry([NotNullWhen(true)] out EntryViewModel? entry)
        {
            entry = GetSelectedLoadedEntry()!;
            return entry is not null;
        }

        private EntryViewModel? GetSelectedLoadedEntry()
        {
            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return null;
            }

            return _entries.FirstOrDefault(entry =>
                entry.IsLoaded &&
                !entry.IsGroupHeader &&
                string.Equals(entry.FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase));
        }

        private int GetSelectedEntryIndex()
        {
            EntryViewModel? selected = GetSelectedLoadedEntry();
            return selected is null ? -1 : _entries.IndexOf(selected);
        }

        private bool CanPasteIntoCurrentDirectory()
        {
            return !string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanCreateInCurrentDirectory()
        {
            return CanPasteIntoCurrentDirectory();
        }

        private bool TryEnsureCurrentDirectoryAvailable(out string errorMessage)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = S("ErrorOpenFolderFirst");
                return false;
            }

            if (!_explorerService.DirectoryExists(_currentPath))
            {
                errorMessage = S("ErrorCurrentFolderUnavailable");
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private bool CanCopySelectedEntry()
        {
            return TryGetSelectedLoadedEntry(out _);
        }

        private bool CanCutSelectedEntry()
        {
            return TryGetSelectedLoadedEntry(out _);
        }

        private bool CanRenameSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out _))
            {
                return false;
            }

            int selectedIndex = GetSelectedEntryIndex();
            return selectedIndex >= 0 && selectedIndex < _entries.Count;
        }

        private bool CanDeleteSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out _))
            {
                return false;
            }

            int selectedIndex = GetSelectedEntryIndex();
            return selectedIndex >= 0 && selectedIndex < _entries.Count;
        }

        private void UpdateFileCommandStates()
        {
            bool canCreate = CanCreateInCurrentDirectory();
            bool canRename = CanRenameSelectedEntry();
            bool canDelete = CanDeleteSelectedEntry();
            bool canCopy = CanCopySelectedEntry();
            bool canCut = CanCutSelectedEntry();
            bool canPaste = CanPasteIntoCurrentDirectory() && _fileManagementCoordinator.HasAvailablePasteItems();

            if (NewFileButton is not null)
            {
                NewFileButton.IsEnabled = canCreate;
            }

            if (NewFolderButton is not null)
            {
                NewFolderButton.IsEnabled = canCreate;
            }

            if (RenameButton is not null)
            {
                RenameButton.IsEnabled = canRename;
            }

            if (DeleteButton is not null)
            {
                DeleteButton.IsEnabled = canDelete;
            }

            if (CopyButton is not null)
            {
                CopyButton.IsEnabled = canCopy;
            }

            if (CutButton is not null)
            {
                CutButton.IsEnabled = canCut;
            }

            if (PasteButton is not null)
            {
                PasteButton.IsEnabled = canPaste;
            }
        }

        private Task ExecuteNewFileAsync()
        {
            return ExecuteNewEntryAsync(isDirectory: false);
        }

        private Task ExecuteNewFolderAsync()
        {
            return ExecuteNewEntryAsync(isDirectory: true);
        }

        private async Task ExecuteNewEntryAsync(bool isDirectory)
        {
            if (!CanCreateInCurrentDirectory())
            {
                UpdateStatusKey("StatusNewFailedOpenFolderFirst", CreateKindLabel(isDirectory));
                return;
            }

            if (!TryEnsureCurrentDirectoryAvailable(out string createError))
            {
                UpdateStatusKey("StatusNewFailedWithReason", CreateKindLabel(isDirectory), createError);
                return;
            }

            await CreateNewEntryAsync(isDirectory);
        }

        private Task ExecuteRenameSelectedAsync()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return Task.CompletedTask;
            }

            int selectedIndex = GetSelectedEntryIndex();
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatusKey("StatusRenameFailedInvalidIndex");
                return Task.CompletedTask;
            }

            if (_entriesFlyoutOpen)
            {
                _pendingContextRenameEntry = entry;
                return Task.CompletedTask;
            }

            return BeginRenameOverlayAsync(entry);
        }

        private async Task ExecuteDeleteSelectedAsync()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusDeleteFailedSelectLoaded");
                return;
            }

            int selectedIndex = GetSelectedEntryIndex();
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatusKey("StatusDeleteFailedInvalidIndex");
                return;
            }

            bool recursive = entry.IsDirectory;
            string target = Path.Combine(_currentPath, entry.Name);
            await DeleteEntryAsync(entry, selectedIndex, target, recursive);
        }

        private void ExecuteCopy()
        {
            if (!CanCopySelectedEntry())
            {
                UpdateStatusKey("StatusCopyFailedSelectLoaded");
                return;
            }

            CopySelectedEntry();
        }

        private void ExecuteCut()
        {
            if (!CanCutSelectedEntry())
            {
                UpdateStatusKey("StatusCutFailedSelectLoaded");
                return;
            }

            CutSelectedEntry();
        }

        private Task ExecutePasteAsync()
        {
            return PasteIntoDirectoryAsync(_currentPath, selectPastedEntry: true);
        }

        private void CopySelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusCopyFailedSelectLoaded");
                return;
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusCopyFailedDriveRootsUnsupported");
                return;
            }

            _fileManagementCoordinator.SetClipboard(new[] { entry.FullPath }, FileTransferMode.Copy);
            UpdateFileCommandStates();
            UpdateStatusKey("StatusCopyReady", entry.Name);
        }

        private void CutSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusCutFailedSelectLoaded");
                return;
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusCutFailedDriveRootsUnsupported");
                return;
            }

            _fileManagementCoordinator.SetClipboard(new[] { entry.FullPath }, FileTransferMode.Cut);
            UpdateFileCommandStates();
            UpdateStatusKey("StatusCutReady", entry.Name);
        }

        private Task PasteIntoCurrentDirectoryAsync()
        {
            return PasteIntoDirectoryAsync(_currentPath, selectPastedEntry: true);
        }

        private async Task PasteIntoDirectoryAsync(string targetDirectoryPath, bool selectPastedEntry)
        {
            if (string.IsNullOrWhiteSpace(targetDirectoryPath) ||
                string.Equals(targetDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusPasteFailedOpenFolderFirst");
                return;
            }

            if (!_explorerService.DirectoryExists(targetDirectoryPath))
            {
                UpdateStatusKey("StatusPasteFailedWithReason", S("ErrorCurrentFolderUnavailable"));
                return;
            }

            if (!_fileManagementCoordinator.HasAvailablePasteItems())
            {
                UpdateStatusKey("StatusPasteFailedClipboardEmpty");
                return;
            }

            try
            {
                FilePasteOperationResult pasteOperation = await _fileManagementCoordinator.TryPasteAsync(targetDirectoryPath);
                if (!pasteOperation.Succeeded || pasteOperation.PasteResult is null)
                {
                    UpdateStatusKey("StatusPasteFailedWithReason", pasteOperation.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }

                FilePasteResult result = pasteOperation.PasteResult;
                int appliedCount = 0;
                int conflictCount = 0;
                int samePathCount = 0;
                int failureCount = 0;
                string? firstAppliedPath = null;
                bool appliedDirectory = false;

                foreach (FilePasteItemResult item in result.Items)
                {
                    if (item.Applied)
                    {
                        appliedCount++;
                        firstAppliedPath ??= item.TargetPath;
                        appliedDirectory |= item.IsDirectory;
                        continue;
                    }

                    if (item.Conflict)
                    {
                        conflictCount++;
                        continue;
                    }

                    if (item.SamePath)
                    {
                        samePathCount++;
                        continue;
                    }

                    failureCount++;
                }

                if (appliedCount == 0)
                {
                    if (conflictCount > 0)
                    {
                        UpdateStatusKey("StatusPasteSkippedConflicts", conflictCount);
                    }
                    else if (samePathCount > 0)
                    {
                        UpdateStatusKey("StatusPasteSkippedSamePath");
                    }
                    else if (failureCount > 0 && result.Items.Count > 0)
                    {
                        string? message = result.Items[0].ErrorMessage;
                        UpdateStatusKey("StatusPasteFailedWithReason", message ?? string.Empty);
                    }
                    else
                    {
                        UpdateStatusKey("StatusPasteSkippedNothingApplied");
                    }
                    return;
                }

                if (!result.TargetChanged)
                {
                    EnsurePersistentRefreshFallbackInvalidation(targetDirectoryPath, result.Mode == FileTransferMode.Cut ? "cut-paste" : "copy-paste");
                }

                if (selectPastedEntry && appliedCount == 1 && !string.IsNullOrWhiteSpace(firstAppliedPath))
                {
                    _selectedEntryPath = firstAppliedPath;
                }

                await LoadFirstPageAsync();

                if (appliedDirectory &&
                    FindSidebarTreeNodeByPath(targetDirectoryPath) is TreeViewNode parentNode &&
                    parentNode.IsExpanded)
                {
                    await PopulateSidebarTreeChildrenAsync(parentNode, targetDirectoryPath, CancellationToken.None, expandAfterLoad: true);
                }

                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);

                UpdateFileCommandStates();
                string modeText = result.Mode == FileTransferMode.Cut ? S("OperationMove") : S("OperationPaste");
                UpdateStatusKey("StatusTransferSuccess", modeText, appliedCount, conflictCount, samePathCount, failureCount);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPasteFailedWithReason", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task CreateNewEntryAsync(bool isDirectory)
        {
            string createKind = CreateKindLabel(isDirectory);
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusNewFailedOpenFolderFirst", createKind);
                return;
            }

            if (!_explorerService.DirectoryExists(_currentPath))
            {
                UpdateStatusKey("StatusNewFailedWithReason", createKind, S("ErrorCurrentFolderUnavailable"));
                return;
            }

            try
            {
                SuppressNextWatcherRefresh(_currentPath);
                FileOperationResult<CreatedEntryInfo> createResult = await _fileManagementCoordinator.TryCreateEntryAsync(_currentPath, isDirectory);
                if (!createResult.Succeeded)
                {
                    UpdateStatusKey("StatusCreateFailed", createResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                CreatedEntryInfo created = createResult.Value!;
                if (!created.ChangeNotified)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, isDirectory ? "create-folder" : "create-file");
                }

                EntryViewModel entry = CreateLocalCreatedEntryModel(created.Name, created.IsDirectory);
                int insertIndex = FindInsertIndexForEntry(entry);
                if (!IsIndexInCurrentViewport(insertIndex))
                {
                    await EnsureCreateInsertVisibleAsync(insertIndex);
                }

                InsertLocalCreatedEntry(entry, insertIndex);
                _pendingCreatedEntrySelection = entry;
                UpdateStatusKey("StatusCreateSuccess", created.Name);
                await StartRenameForCreatedEntryAsync(entry, insertIndex);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCreateFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async void RenameOverlayTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await Task.Delay(1);
            if (IsFocusedElementWithinRenameOverlay())
            {
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private async void RenameOverlayTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await CommitRenameOverlayAsync();
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                HideRenameOverlay();
                FocusEntriesList();
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right
                or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown)
            {
                e.Handled = true;
                HideRenameOverlay();
                FocusEntriesList();
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    HandleEntriesNavigationKey(e.Key);
                });
            }
        }

        private void DetailsEntriesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer)
            {
                return;
            }

            double previousVerticalOffset = _lastDetailsVerticalOffset;
            bool scrolled = HasScrollOffsetChanged(viewer, ref _lastDetailsHorizontalOffset, ref _lastDetailsVerticalOffset);
            _lastDetailsVerticalDelta = double.IsNaN(previousVerticalOffset)
                ? 0.0
                : Math.Abs(viewer.VerticalOffset - previousVerticalOffset);
            if (scrolled && _entriesFlyoutOpen && (_activeEntriesContextFlyout?.IsOpen ?? false))
            {
                HideActiveEntriesContextFlyout();
            }

            if (scrolled && RenameOverlayBorder.Visibility == Visibility.Visible)
            {
                HideRenameOverlay();
            }

            int previousViewportStartIndex = _lastDetailsViewportStartIndex;
            int viewportStartIndex = -1;
            int viewportBottomIndex = -1;
            if (scrolled)
            {
                _lastDetailsScrollInteractionTick = Environment.TickCount64;
                InvalidateDetailsViewportRealization();
            }

            if (DetailsHeaderTranslateTransform is not null)
            {
                DetailsHeaderTranslateTransform.X = -viewer.HorizontalOffset;
            }

            UpdateEstimatedItemHeight();
            int logicalCount = GetLogicalEntryCount();
            if (logicalCount > 0)
            {
                viewportStartIndex = EstimateViewportIndex(viewer);
                viewportBottomIndex = EstimateViewportBottomIndex(viewer);
                LogDetailsViewportPerf(
                    "view-changed",
                    $"intermediate={e.IsIntermediate} offset={viewer.VerticalOffset:F1} scrollable={viewer.ScrollableHeight:F1} start={viewportStartIndex} end={viewportBottomIndex} entries={_entries.Count} total={_totalEntries}");
                _ = EnsureDataForViewportAsync(viewportStartIndex, viewportBottomIndex, preferMinimalPage: e.IsIntermediate);
            }

            if (viewportStartIndex >= 0)
            {
                _lastDetailsViewportIndexDelta = previousViewportStartIndex < 0
                    ? 0
                    : Math.Abs(viewportStartIndex - previousViewportStartIndex);
                _lastDetailsViewportStartIndex = viewportStartIndex;
            }

            if (e.IsIntermediate)
            {
                unchecked
                {
                    _metadataViewportRequestVersion++;
                }

                return;
            }

            RequestMetadataForCurrentViewportDeferred(48);
        }

        private void GroupedEntriesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer)
            {
                return;
            }

            bool scrolled = HasScrollOffsetChanged(viewer, ref _lastGroupedHorizontalOffset, ref _lastGroupedVerticalOffset);
            if (scrolled && RenameOverlayBorder.Visibility == Visibility.Visible)
            {
                HideRenameOverlay();
            }
        }

        private static bool HasScrollOffsetChanged(ScrollViewer viewer, ref double lastHorizontalOffset, ref double lastVerticalOffset)
        {
            bool changed =
                double.IsNaN(lastHorizontalOffset) ||
                double.IsNaN(lastVerticalOffset) ||
                Math.Abs(viewer.HorizontalOffset - lastHorizontalOffset) > 0.1 ||
                Math.Abs(viewer.VerticalOffset - lastVerticalOffset) > 0.1;

            lastHorizontalOffset = viewer.HorizontalOffset;
            lastVerticalOffset = viewer.VerticalOffset;
            return changed;
        }

        private void GroupedEntriesScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RefreshGroupedColumnsForViewport();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            ApplySidebarWidthLayout();
            UpdateEstimatedItemHeight();
            RequestViewportWork();
            RefreshGroupedColumnsForViewport();
            UpdateVisibleBreadcrumbs();
            UpdateRenameOverlayPosition();
            TryResetSystemCursorToArrow();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollBarVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollMode)));
        }

        private void ResetEntriesViewport()
        {
            DetailsEntriesScrollViewer.ChangeView(0, 0, null, disableAnimation: true);
            GroupedEntriesScrollViewer.ChangeView(0, 0, null, disableAnimation: true);
            _lastDetailsHorizontalOffset = double.NaN;
            _lastDetailsVerticalOffset = double.NaN;
            _lastGroupedHorizontalOffset = double.NaN;
            _lastGroupedVerticalOffset = double.NaN;
            _lastDetailsViewportStartIndex = -1;
            _lastDetailsViewportIndexDelta = 0;

            if (DetailsHeaderTranslateTransform is not null)
            {
                DetailsHeaderTranslateTransform.X = 0;
            }

        }

        private void InvalidateEntriesLayouts()
        {
            DetailsEntriesRepeater.InvalidateMeasure();
        }

        private void InvalidateDetailsViewportRealization(bool preferMinimalBuffer = false, bool forceSynchronous = false)
        {
            DetailsEntriesRepeater.InvalidateMeasure();
            if (forceSynchronous)
            {
                DetailsEntriesScrollViewer.UpdateLayout();
            }
        }

        private bool IsDetailsViewportInteractionHot()
        {
            if (_currentViewMode != EntryViewMode.Details)
            {
                return false;
            }

            return Environment.TickCount64 - Interlocked.Read(ref _lastDetailsScrollInteractionTick) < 180;
        }

        private bool IsSparseViewportLoadQueuedOrActive()
        {
            lock (_sparseViewportGate)
            {
                return _isSparseViewportLoadActive || _pendingSparseViewportTargetIndex is not null;
            }
        }

        private bool RangeHasPendingMetadata(int startIndex, int endIndex)
        {
            if (_entries.Count == 0 || startIndex < 0 || endIndex < startIndex)
            {
                return false;
            }

            int cappedEnd = Math.Min(endIndex, _entries.Count - 1);
            for (int i = Math.Max(0, startIndex); i <= cappedEnd; i++)
            {
                EntryViewModel entry = _entries[i];
                if (entry.IsLoaded && !entry.IsMetadataLoaded)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ViewportHasPendingMetadata()
        {
            if (_entries.Count == 0)
            {
                return false;
            }

            int startIndex;
            int endIndex;
            if (_currentViewMode != EntryViewMode.Details)
            {
                startIndex = 0;
                endIndex = _entries.Count - 1;
            }
            else
            {
                startIndex = EstimateViewportIndex(DetailsEntriesScrollViewer);
                endIndex = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
            }

            return RangeHasPendingMetadata(startIndex, endIndex);
        }

        private void CancelPendingViewportMetadataWork()
        {
            unchecked
            {
                _metadataViewportRequestVersion++;
            }

            CancelAndDispose(ref _metadataPrefetchCts);
        }

        private static void TryResetSystemCursorToArrow()
        {
            IntPtr cursor = NativeMethods.LoadCursor(IntPtr.Zero, IDC_ARROW);
            if (cursor != IntPtr.Zero)
            {
                NativeMethods.SetCursor(cursor);
            }
        }

        private async Task LoadFirstPageAsync()
        {
            NavigationPerfSession? perf = TryGetCurrentNavigationPerfSession();
            perf?.Mark("load-first-page.enter");
            _currentPath = string.IsNullOrWhiteSpace(PathTextBox.Text) ? ShellMyComputerPath : PathTextBox.Text.Trim();
            if (!_sidebarInitialized)
            {
                BuildSidebarItems();
                _sidebarInitialized = true;
            }
            else
            {
                UpdateSidebarSelectionOnly();
            }
            _currentPageSize = InitialPageSize;
            _lastFetchMs = 0;
            UpdateBreadcrumbs(_currentPath);
            UpdateDetailsHeaders();
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                _usnCapability = default;
                ConfigureDirectoryWatcher(string.Empty);
                PopulateMyComputerEntries();
                ApplyCurrentPresentation();
                UpdateFileCommandStates();
                if (perf is not null)
                {
                    ScheduleNavigationPerfFirstFrameMark(perf, "load-first-page.first-frame");
                }
                perf?.Mark("load-first-page.my-computer.completed");
                return;
            }

            UpdateUsnCapability(_currentPath);
            ConfigureDirectoryWatcher(_currentPath);
            EnsureRefreshFallbackInvalidation(_currentPath, "manual_load");
            SyncActivePanelPresentationState();
            perf?.Mark("load-first-page.pipeline-selected", UsesClientPresentationPipeline() ? "client" : "paged");
            if (UsesClientPresentationPipeline())
            {
                await LoadAllEntriesForPresentationAsync(_currentPath, perf);
                return;
            }
            await LoadPageAsync(_currentPath, cursor: 0, append: false, perf);
        }

        private async Task NavigateToPathAsync(string path, bool pushHistory, bool focusEntriesAfterNavigation = true)
        {
            HideRenameOverlay();

            string target = string.IsNullOrWhiteSpace(path) ? @"C:\" : path.Trim();
            CaptureCurrentDirectoryViewState();
            NavigationPerfSession perf = BeginNavigationPerfSession(target, pushHistory ? "navigate" : "history");
            try
            {
                if (string.Equals(target, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (pushHistory && !string.Equals(_currentPath, target, StringComparison.OrdinalIgnoreCase))
                    {
                        _backStack.Push(_currentPath);
                        _forwardStack.Clear();
                    }

                    _currentPath = ShellMyComputerPath;
                    _pendingHistoryStateRestorePath = pushHistory ? null : ShellMyComputerPath;
                    PathTextBox.Text = ShellMyComputerPath;
                    _currentQuery = string.Empty;
                    SearchTextBox.Text = string.Empty;
                    UpdateBreadcrumbs(_currentPath);
                    UpdateNavButtonsState();
                    _ = SelectSidebarTreePathAsync(_currentPath);
                    await LoadFirstPageAsync();
                    ClearListSelection();
                    if (focusEntriesAfterNavigation)
                    {
                        _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                    }
                    perf.Mark("navigate.completed");
                    return;
                }

                if (!_explorerService.DirectoryExists(target))
                {
                    SetPathInputInvalid();
                    UpdateStatusKey("StatusPathNotFound", target);
                    perf.Mark("navigate.path-missing");
                    return;
                }
                SetPathInputValid();
                perf.Mark("navigate.validated");

                if (pushHistory && !string.Equals(_currentPath, target, StringComparison.OrdinalIgnoreCase))
                {
                    _backStack.Push(_currentPath);
                    _forwardStack.Clear();
                }

                _currentPath = target;
                _pendingHistoryStateRestorePath = pushHistory ? null : target;
                PathTextBox.Text = target;
                _currentQuery = string.Empty;
                SearchTextBox.Text = string.Empty;
                UpdateBreadcrumbs(target);
                UpdateNavButtonsState();
                _ = SelectSidebarTreePathAsync(_currentPath);
                await LoadFirstPageAsync();
                ClearListSelection();
                if (focusEntriesAfterNavigation)
                {
                    _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                }
                perf.Mark("navigate.completed");
            }
            finally
            {
                EndNavigationPerfSession(perf);
            }
        }

        private async Task LoadNextPageAsync()
        {
            if (!_hasMore)
            {
                UpdateStatusKey("StatusNoMoreEntries");
                return;
            }

            await LoadPageAsync(_currentPath, _nextCursor, append: true);
        }

        private void EnsureActiveEntryResultSet(string path)
        {
            string query = _currentQuery;
            if (_activeEntryResultSet is not null &&
                string.Equals(_activeEntryResultSet.Path, path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_activeEntryResultSet.Query, query, StringComparison.Ordinal) &&
                _activeEntryResultSet.SortMode == _currentSortMode)
            {
                return;
            }

            _activeEntryResultSet = string.IsNullOrWhiteSpace(query)
                ? _explorerService.CreateDirectoryResultSet(path, _currentSortMode)
                : _explorerService.CreateSearchResultSet(path, query, _currentSortMode);
        }

        private async Task LoadPageAsync(string path, ulong cursor, bool append, NavigationPerfSession? perf = null)
        {
            if (_isLoading)
            {
                perf?.Mark("load-page.skipped", "already-loading");
                return;
            }

            InvalidatePresentationSourceCache();
            perf?.Mark("load-page.enter", $"append={append} cursor={cursor}");

            if (!append)
            {
                BeginDirectorySnapshot();
            }

            _isLoading = true;
            LoadButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            SidebarNavView.IsEnabled = false;
            StyledSidebarView.IsEnabled = false;

            try
            {
                EnsureActiveEntryResultSet(path);
                uint requestedPageSize = _currentPageSize;
                Stopwatch sw = Stopwatch.StartNew();
                FileBatchPage page;
                bool ok;
                int rustErrorCode;
                string rustErrorMessage;
                IEntryResultSet? resultSet = _activeEntryResultSet;
                if (resultSet is null)
                {
                    throw new InvalidOperationException("active entry result set was not initialized");
                }

                (ok, page, rustErrorCode, rustErrorMessage) = await Task.Run(
                    () =>
                    {
                        bool success = resultSet.TryReadRange(
                            cursor,
                            requestedPageSize,
                            _lastFetchMs,
                            out FileBatchPage p,
                            out int code,
                            out string msg
                        );
                        return (success, p, code, msg);
                    }
                );

                if (!ok)
                {
                    if (IsRustAccessDenied(rustErrorCode, rustErrorMessage))
                    {
                        _hasMore = false;
                        _nextCursor = 0;
                        if (!append)
                        {
                            ResetEntriesViewport();
                            _entries.Clear();
                            _totalEntries = 0;
                            InvalidateEntriesLayouts();
                        }

                        UpdateStatusKey("StatusPathAccessDeniedSkip", path);
                        return;
                    }

                    throw new InvalidOperationException($"Rust error {rustErrorCode}: {rustErrorMessage}");
                }

                sw.Stop();
                perf?.Mark("load-page.fetch-completed", $"rows={page.Rows.Count} total={page.TotalEntries} source={_explorerService.DescribeBatchSource(page.SourceKind)}");
                _lastFetchMs = (uint)Math.Clamp(sw.ElapsedMilliseconds, 0, int.MaxValue);
                _totalEntries = page.TotalEntries;

                if (!append)
                {
                    ResetEntriesViewport();
                    _entries.Clear();
                    EnsureLoadedRangeCapacity(0, page.Rows.Count);
                    FillPageRows(0, page.Rows, path);
                    perf?.Mark("load-page.visible-entries-updated", $"count={_entries.Count}");
                }
                else
                {
                    EnsureLoadedRangeCapacity((int)cursor, page.Rows.Count);
                    FillPageRows((int)cursor, page.Rows, path);
                    perf?.Mark("load-page.visible-entries-updated", $"count={_entries.Count}");
                }
                InvalidateEntriesLayouts();
                perf?.Mark("load-page.layouts-invalidated");
                if (!append && perf is not null)
                {
                    ScheduleNavigationPerfFirstFrameMark(perf, "load-page.first-frame");
                }
                if (!append)
                {
                    perf?.Mark("load-page.selection-restore.begin");
                    RestoreListSelectionByPath(ensureVisible: false);
                    bool historyRestored = RestoreHistoryViewStateIfPending();
                    perf?.Mark(historyRestored ? "load-page.history-state-restored" : "load-page.history-state-skip");
                    if (!historyRestored)
                    {
                        perf?.Mark("load-page.parent-anchor-restore.begin");
                        RestoreParentReturnAnchorIfPending();
                        perf?.Mark("load-page.parent-anchor-restore.end");
                    }
                    perf?.Mark("load-page.selection-restored");
                }
                _nextCursor = page.NextCursor;
                _hasMore = page.HasMore;
                _currentPageSize = ClampPageSize(page.SuggestedNextLimit, requestedPageSize);
                string source = _explorerService.DescribeBatchSource(page.SourceKind);
                perf?.Mark("load-page.bind-completed", $"visible={_entries.Count} hasMore={_hasMore}");

                void FinalizeLoadedPageUi()
                {
                    perf?.Mark("load-page.ui-finalize.begin");
                    UpdateFileCommandStates();
                    if (_currentViewMode == EntryViewMode.Details)
                    {
                        CancelPendingViewportMetadataWork();
                    }
                    else if (!append)
                    {
                        RequestMetadataForCurrentViewportDeferred(48);
                        perf?.Mark("load-page.viewport-metadata-deferred");
                    }
                    else
                    {
                        RequestMetadataForCurrentViewport();
                        perf?.Mark("load-page.viewport-metadata-requested");
                    }

                    _lastTitleWasReadFailed = false;
                    UpdateWindowTitle();
                    UpdateStatus(SF("StatusCurrentFolderItems", _totalEntries));
                    LogPerfSnapshot(
                        mode: string.IsNullOrWhiteSpace(_currentQuery) ? "browse" : "search",
                        path: path,
                        query: _currentQuery,
                        source: source,
                        loaded: page.Rows.Count,
                        total: page.TotalEntries,
                        scanned: page.ScannedEntries,
                        matched: page.MatchedEntries,
                        fetchMs: sw.ElapsedMilliseconds,
                        batch: _currentPageSize,
                        hasMore: _hasMore,
                        nextCursor: _nextCursor,
                        usn: DescribeUsnCapability(_usnCapability)
                    );
                    perf?.Mark("load-page.ui-finalize.end");
                }

                if (!append)
                {
                    perf?.Mark("load-page.ui-finalize.deferred");
                    ScheduleAfterNextFrame(FinalizeLoadedPageUi);
                }
                else
                {
                    FinalizeLoadedPageUi();
                }
            }
            catch (Exception ex)
            {
                _lastTitleWasReadFailed = true;
                UpdateWindowTitle();
                UpdateStatusKey("StatusPathError", path, FileOperationErrors.ToUserMessage(ex));
                perf?.Mark("load-page.failed", ex.Message);
            }
            finally
            {
                _isLoading = false;
                LoadButton.IsEnabled = true;
                NextButton.IsEnabled = _hasMore;
                SidebarNavView.IsEnabled = true;
                StyledSidebarView.IsEnabled = true;
                perf?.Mark("load-page.exit");
            }
        }

        private async Task LoadAllEntriesForPresentationAsync(string path, NavigationPerfSession? perf = null)
        {
            if (_isLoading)
            {
                perf?.Mark("load-all.skipped", "already-loading");
                return;
            }

            BeginDirectorySnapshot();
            perf?.Mark("load-all.enter");
            _isLoading = true;
            LoadButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            SidebarNavView.IsEnabled = false;
            StyledSidebarView.IsEnabled = false;

            try
            {
                EnsureActiveEntryResultSet(path);
                IEntryResultSet? resultSet = _activeEntryResultSet;
                if (resultSet is null)
                {
                    throw new InvalidOperationException("active entry result set was not initialized");
                }

                var loadedEntries = new List<EntryViewModel>();
                ulong cursor = 0;
                uint limit = 512;
                uint totalEntries = 0;
                bool hasMore;
                do
                {
                    FileBatchPage page;
                    bool ok;
                    int rustErrorCode;
                    string rustErrorMessage;

                    (ok, page, rustErrorCode, rustErrorMessage) = await Task.Run(() =>
                    {
                        bool success = resultSet.TryReadRange(
                            cursor,
                            limit,
                            _lastFetchMs,
                            out FileBatchPage p,
                            out int code,
                            out string msg);
                        return (success, p, code, msg);
                    });

                    if (!ok)
                    {
                        throw new InvalidOperationException($"Rust error {rustErrorCode}: {rustErrorMessage}");
                    }

                    totalEntries = page.TotalEntries;
                    foreach (FileRow row in page.Rows)
                    {
                        EntryViewModel entry = CreateLoadedEntryModel(path, row);
                        PopulateEntryMetadata(entry);
                        loadedEntries.Add(entry);
                    }

                    cursor = page.NextCursor;
                    hasMore = page.HasMore;
                    perf?.Mark("load-all.batch", $"loaded={loadedEntries.Count} total={totalEntries} hasMore={hasMore}");
                } while (hasMore);

                SetPresentationSourceEntries(loadedEntries);
                perf?.Mark("load-all.fetch-completed", $"loaded={loadedEntries.Count}");
                ApplyCurrentPresentation(perf);
                _totalEntries = totalEntries == 0 ? (uint)loadedEntries.Count : totalEntries;
                InvalidateEntriesLayouts();
                _nextCursor = 0;
                _hasMore = false;
                _currentPageSize = InitialPageSize;
                _lastTitleWasReadFailed = false;
                UpdateWindowTitle();
                UpdateStatus(SF("StatusCurrentFolderItems", _totalEntries));
                if (perf is not null)
                {
                    ScheduleNavigationPerfFirstFrameMark(perf, "load-all.first-frame");
                }
                perf?.Mark("load-all.completed", $"visible={_entries.Count}");
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusLoadFailedWithReason", FileOperationErrors.ToUserMessage(ex));
                perf?.Mark("load-all.failed", ex.Message);
            }
            finally
            {
                _isLoading = false;
                LoadButton.IsEnabled = true;
                NextButton.IsEnabled = false;
                SidebarNavView.IsEnabled = true;
                StyledSidebarView.IsEnabled = true;
                UpdateFileCommandStates();
                perf?.Mark("load-all.exit");
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void UpdateDetailsHeaders()
        {
            if (NameHeaderTextBlock is null || TypeHeaderTextBlock is null || SizeHeaderTextBlock is null || ModifiedHeaderTextBlock is null)
            {
                return;
            }

            NameHeaderTextBlock.Text = S("ColumnNameHeader");
            TypeHeaderTextBlock.Text = S("ColumnTypeHeader");
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                SizeHeaderTextBlock.Text = S("ColumnTotalSizeHeader");
                ModifiedHeaderTextBlock.Text = S("ColumnFreeSpaceHeader");
                return;
            }

            SizeHeaderTextBlock.Text = S("ColumnSizeHeader");
            ModifiedHeaderTextBlock.Text = S("ColumnModifiedHeader");
        }

        private static void LogPerfSnapshot(
            string mode,
            string path,
            string query,
            string source,
            int loaded,
            uint total,
            uint scanned,
            uint matched,
            long fetchMs,
            uint batch,
            bool hasMore,
            ulong nextCursor,
            string usn
        )
        {
            double hitRate = scanned > 0 ? matched * 100.0 / scanned : 0.0;
            string q = string.IsNullOrWhiteSpace(query) ? "-" : query;
            Debug.WriteLine(
                $"[PERF] mode={mode} path=\"{path}\" query=\"{q}\" source={source} loaded={loaded} total={total} scanned={scanned} matched={matched} hit={hitRate:F1}% fetch_ms={fetchMs} batch={batch} has_more={hasMore} next={nextCursor} usn={usn}"
            );
        }

        private NavigationPerfSession BeginNavigationPerfSession(string targetPath, string trigger)
        {
            var session = new NavigationPerfSession(targetPath, trigger);
            _activeNavigationPerfSession = session;
            return session;
        }

        private NavigationPerfSession? TryGetCurrentNavigationPerfSession()
        {
            return _activeNavigationPerfSession;
        }

        private void EndNavigationPerfSession(NavigationPerfSession session)
        {
            if (ReferenceEquals(_activeNavigationPerfSession, session))
            {
                _activeNavigationPerfSession = null;
            }
        }

        private static void AppendNavigationPerfLog(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (s_navigationPerfLogLock)
            {
                File.AppendAllText(s_navigationPerfLogPath, line, Encoding.UTF8);
            }
        }

        private void ScheduleNavigationPerfFirstFrameMark(NavigationPerfSession perf, string stage)
        {
            void OnRendering(object? sender, object args)
            {
                CompositionTarget.Rendering -= OnRendering;
                perf.Mark(stage);
            }

            CompositionTarget.Rendering += OnRendering;
        }

        private void ScheduleAfterNextFrame(Action action)
        {
            void OnRendering(object? sender, object args)
            {
                CompositionTarget.Rendering -= OnRendering;
                _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => action());
            }

            CompositionTarget.Rendering += OnRendering;
        }

        private ScrollViewer GetCurrentViewportScrollViewer()
        {
            return _currentViewMode == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;
        }

        private void EnsureRefreshFallbackInvalidation(string path, string reason)
        {
            // Week4 strategy: if USN capability is unavailable, force invalidate to keep consistency.
            if (_usnCapability.available != 0)
            {
                return;
            }

            try
            {
                _explorerService.InvalidateMemorySessionDirectory(path);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPathInvalidateWarning", path, reason, FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void EnsurePersistentRefreshFallbackInvalidation(string path, string reason)
        {
            if (_usnCapability.available != 0)
            {
                return;
            }

            try
            {
                _explorerService.InvalidateMemoryDirectory(path);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPathInvalidateWarning", path, reason, FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void UpdateUsnCapability(string path)
        {
            try
            {
                _usnCapability = _explorerService.ProbeUsnCapability(path);
            }
            catch
            {
                _usnCapability = default;
            }
        }

        private void BuildSidebarItems()
        {
            if (SidebarNavView is null)
            {
                return;
            }

            SidebarNavView.MenuItems.Clear();
            _sidebarPathButtons.Clear();
            _sidebarQuickAccessPaths.Clear();
            _sidebarDrivePaths.Clear();

            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            NavigationViewItem CreateLeaf(string label, string path, Symbol icon, bool inQuickAccess, bool inDrives)
            {
                var item = new NavigationViewItem
                {
                    Content = label,
                    Tag = path,
                    Icon = new SymbolIcon(icon)
                };
                _sidebarPathButtons[path] = item;
                if (inQuickAccess)
                {
                    _sidebarQuickAccessPaths.Add(path);
                }
                if (inDrives)
                {
                    _sidebarDrivePaths.Add(path);
                }
                return item;
            }

            var quickAccess = new NavigationViewItem
            {
                Content = S("SidebarPinned"),
                Icon = new SymbolIcon(Symbol.Favorite),
                SelectsOnInvoked = false
            };
            quickAccess.MenuItems.Add(CreateLeaf(S("SidebarDesktop"), desktopPath, Symbol.Home, true, false));
            quickAccess.MenuItems.Add(CreateLeaf(S("SidebarDocuments"), documentsPath, Symbol.Document, true, false));
            quickAccess.MenuItems.Add(CreateLeaf(S("SidebarDownloads"), downloadsPath, Symbol.Download, true, false));
            SidebarNavView.MenuItems.Add(quickAccess);

            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = S("SidebarCloud"), Icon = new SymbolIcon(Symbol.World), SelectsOnInvoked = false });
            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = S("SidebarNetwork"), Icon = new SymbolIcon(Symbol.Globe), SelectsOnInvoked = false });
            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = S("SidebarTags"), Icon = new SymbolIcon(Symbol.Tag), SelectsOnInvoked = false });

            StyledSidebarView.ConfigurePinnedPaths(desktopPath, documentsPath, downloadsPath, picturesPath);
            StyledSidebarView.SetExtraItems(new[]
            {
                new SidebarNavItemModel("cloud", S("SidebarCloud"), null, "\uE753", selectable: false),
                new SidebarNavItemModel("network", S("SidebarNetwork"), null, "\uE774", selectable: false),
                new SidebarNavItemModel("tags", S("SidebarTags"), null, "\uE8EC", selectable: false)
            });

            EnsureSidebarTreeView();
            if (_sidebarTreeView is not null)
            {
                StyledSidebarView.AttachTreeView(_sidebarTreeView);
                _ = PopulateSidebarTreeRootsAsync();
            }

            StyledSidebarView.SetSectionVisibility(
                _appSettings.ShowFavorites,
                _appSettings.ShowCloud,
                _appSettings.ShowNetwork,
                _appSettings.ShowTags);
            ApplySidebarCompactState(_isSidebarCompact);
            UpdateSidebarSelectionOnly();
        }

        private void ApplyAppSettingsToUi()
        {
            SettingsViewControl.SetSidebarSectionVisibility(
                _appSettings.ShowFavorites,
                _appSettings.ShowCloud,
                _appSettings.ShowNetwork,
                _appSettings.ShowTags);
            SettingsViewControl.SetDeleteConfirmationEnabled(_appSettings.ConfirmDelete);

            StyledSidebarView.SetSectionVisibility(
                _appSettings.ShowFavorites,
                _appSettings.ShowCloud,
                _appSettings.ShowNetwork,
                _appSettings.ShowTags);
        }

        private void EnsureSidebarTreeView()
        {
            if (_sidebarTreeView is not null)
            {
                return;
            }

            _sidebarTreeView = new TreeView
            {
                ItemTemplate = SidebarTreeItemTemplate,
                SelectionMode = TreeViewSelectionMode.Single,
                CanDragItems = false,
                CanReorderItems = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                UseSystemFocusVisuals = false
            };
            _sidebarTreeView.SelectionChanged += SidebarTree_SelectionChanged;
            _sidebarTreeView.Expanding += SidebarTree_Expanding;
            _sidebarTreeView.Collapsed += SidebarTree_Collapsed;
            _sidebarTreeView.ItemInvoked += SidebarTree_ItemInvoked;
            _sidebarTreeView.ContextRequested += SidebarTree_ContextRequested;
            StyledSidebarView.AttachTreeView(_sidebarTreeView);

            _sidebarTreeContextFlyout = new MenuFlyout();
            var renameItem = new MenuFlyoutItem { Text = S("CommonRename") };
            renameItem.Click += SidebarTreeContextRename_Click;
            _sidebarTreeContextFlyout.Items.Add(renameItem);
        }

        private async Task PopulateSidebarTreeRootsAsync()
        {
            EnsureSidebarTreeView();
            if (_sidebarTreeView is null)
            {
                return;
            }

            _sidebarTreeCts?.Cancel();
            var cts = new CancellationTokenSource();
            _sidebarTreeCts = cts;

            _sidebarTreeView.RootNodes.Clear();
            var computerEntry = new SidebarTreeEntry(S("SidebarMyComputer"), "shell:mycomputer", "\uE7F4");
            var computerNode = CreateSidebarTreeNode(computerEntry, hasUnrealizedChildren: false);
            computerNode.IsExpanded = true;
            _sidebarTreeView.RootNodes.Add(computerNode);

            foreach (DriveInfo drive in _explorerService.GetReadyDrives())
            {
                string root = drive.RootDirectory.FullName;
                string label = drive.Name.TrimEnd('\\');
                var rootEntry = new SidebarTreeEntry(label, root);
                computerNode.Children.Add(CreateSidebarTreeNode(rootEntry, hasUnrealizedChildren: _explorerService.DirectoryHasChildDirectories(root)));
            }

            await SelectSidebarTreePathAsync(_currentPath);
        }

        private static TreeViewNode CreateSidebarTreeNode(SidebarTreeEntry entry, bool hasUnrealizedChildren)
        {
            var node = new TreeViewNode
            {
                Content = entry,
                HasUnrealizedChildren = hasUnrealizedChildren
            };
            return node;
        }

        private static DataTemplate CreateSidebarTreeItemTemplate()
        {
            const string xaml =
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Grid>" +
                "<StackPanel VerticalAlignment=\"Center\" Orientation=\"Horizontal\" Spacing=\"6\">" +
                "<FontIcon VerticalAlignment=\"Center\" FontFamily=\"Segoe Fluent Icons\" FontSize=\"12\" Glyph=\"{Binding Content.IconGlyph}\" />" +
                "<TextBlock x:Name=\"SidebarTreeNameTextBlock\" VerticalAlignment=\"Center\" Text=\"{Binding Content.Name}\" TextTrimming=\"CharacterEllipsis\" />" +
                "</StackPanel>" +
                "</Grid>" +
                "</DataTemplate>";

            return (DataTemplate)XamlReader.Load(xaml);
        }

        private ControlTemplate? GetRenameOverlayTextBoxTemplate()
        {
            if (_renameOverlayTextBoxTemplate is not null)
            {
                return _renameOverlayTextBoxTemplate;
            }

            const string xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                "TargetType=\"TextBox\">" +
                "<Grid>" +
                "<Border x:Name=\"BorderElement\" " +
                "Background=\"Transparent\" " +
                "BorderBrush=\"Transparent\" " +
                "BorderThickness=\"0\" " +
                "Control.IsTemplateFocusTarget=\"True\" />" +
                "<ScrollViewer x:Name=\"ContentElement\" " +
                "AutomationProperties.AccessibilityView=\"Raw\" " +
                "Foreground=\"{TemplateBinding Foreground}\" " +
                "HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" " +
                "HorizontalScrollBarVisibility=\"{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}\" " +
                "HorizontalScrollMode=\"{TemplateBinding ScrollViewer.HorizontalScrollMode}\" " +
                "IsDeferredScrollingEnabled=\"{TemplateBinding ScrollViewer.IsDeferredScrollingEnabled}\" " +
                "IsHorizontalRailEnabled=\"{TemplateBinding ScrollViewer.IsHorizontalRailEnabled}\" " +
                "IsTabStop=\"False\" " +
                "IsVerticalRailEnabled=\"{TemplateBinding ScrollViewer.IsVerticalRailEnabled}\" " +
                "Margin=\"{TemplateBinding BorderThickness}\" " +
                "Padding=\"{TemplateBinding Padding}\" " +
                "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\" " +
                "VerticalScrollBarVisibility=\"{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}\" " +
                "VerticalScrollMode=\"{TemplateBinding ScrollViewer.VerticalScrollMode}\" " +
                "ZoomMode=\"Disabled\" />" +
                "</Grid>" +
                "</ControlTemplate>";

            try
            {
                _renameOverlayTextBoxTemplate = (ControlTemplate)XamlReader.Load(xaml);
            }
            catch
            {
                _renameOverlayTextBoxTemplate = null;
            }

            return _renameOverlayTextBoxTemplate;
        }

        private async Task PopulateSidebarTreeChildrenAsync(TreeViewNode node, string path, CancellationToken ct, bool expandAfterLoad = false)
        {
            List<SidebarTreeEntry> children = await _explorerService.EnumerateSidebarDirectoriesAsync(path, ct, SidebarTreeMaxChildren);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            node.Children.Clear();
            foreach (SidebarTreeEntry child in children)
            {
                node.Children.Add(CreateSidebarTreeNode(child, hasUnrealizedChildren: _explorerService.DirectoryHasChildDirectories(child.FullPath)));
            }
            node.HasUnrealizedChildren = false;
            if (expandAfterLoad)
            {
                node.IsExpanded = true;
            }
        }

        private TreeViewNode? FindSidebarTreeNodeByPath(string path)
        {
            if (_sidebarTreeView is null)
            {
                return null;
            }

            foreach (TreeViewNode root in _sidebarTreeView.RootNodes)
            {
                TreeViewNode? match = FindSidebarTreeNodeByPath(root, path);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private static TreeViewNode? FindSidebarTreeNodeByPath(TreeViewNode node, string path)
        {
            if (node.Content is SidebarTreeEntry entry &&
                string.Equals(entry.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            foreach (TreeViewNode child in node.Children)
            {
                TreeViewNode? match = FindSidebarTreeNodeByPath(child, path);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private TreeViewNode? FindSidebarTreeNodeFromSource(DependencyObject? source)
        {
            TreeViewItem? item = FindAncestor<TreeViewItem>(source);
            if (item?.DataContext is TreeViewNode nodeFromDataContext)
            {
                return nodeFromDataContext;
            }

            if (item?.Content is TreeViewNode nodeFromContent)
            {
                return nodeFromContent;
            }

            return _sidebarTreeView?.SelectedNode;
        }

        private static void UpdateSidebarTreeNodePath(TreeViewNode node, string oldPath, string newPath, string? replacementName = null)
        {
            if (node.Content is SidebarTreeEntry entry)
            {
                string updatedPath = entry.FullPath;
                if (string.Equals(entry.FullPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    updatedPath = newPath;
                }
                else if (entry.FullPath.StartsWith(oldPath + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    updatedPath = newPath + entry.FullPath[oldPath.Length..];
                }

                if (!string.Equals(updatedPath, entry.FullPath, StringComparison.OrdinalIgnoreCase) || replacementName is not null)
                {
                    node.Content = new SidebarTreeEntry(replacementName ?? entry.Name, updatedPath, entry.IconGlyph);
                }
            }

            foreach (TreeViewNode child in node.Children)
            {
                UpdateSidebarTreeNodePath(child, oldPath, newPath);
            }
        }

        private async Task SelectSidebarTreePathAsync(string path)
        {
            if (_sidebarTreeView is null)
            {
                return;
            }

            string target = string.IsNullOrWhiteSpace(path) ? @"C:\" : path.Trim();
            if (string.Equals(target, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                foreach (TreeViewNode node in _sidebarTreeView.RootNodes)
                {
                    if (node.Content is SidebarTreeEntry entry &&
                        string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _suppressSidebarTreeSelection = true;
                        _sidebarTreeView.SelectedNode = node;
                        _suppressSidebarTreeSelection = false;
                        return;
                    }
                }

                return;
            }

            string root = Path.GetPathRoot(target) ?? string.Empty;
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            TreeViewNode? current = null;
            TreeViewNode? computer = null;
            foreach (TreeViewNode node in _sidebarTreeView.RootNodes)
            {
                if (node.Content is SidebarTreeEntry entry &&
                    string.Equals(entry.FullPath, "shell:mycomputer", StringComparison.OrdinalIgnoreCase))
                {
                    computer = node;
                    break;
                }
            }

            if (computer is null)
            {
                return;
            }

            current = computer;
            if (!computer.IsExpanded)
            {
                _suppressSidebarTreeSelection = true;
                _sidebarTreeView.SelectedNode = current;
                _suppressSidebarTreeSelection = false;
                return;
            }

            foreach (TreeViewNode node in computer.Children)
            {
                if (node.Content is SidebarTreeEntry entry &&
                    string.Equals(entry.FullPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    current = node;
                    break;
                }
            }

            if (current is null)
            {
                return;
            }

            string relative = target.Substring(root.Length).Trim('\\');
            if (!string.IsNullOrEmpty(relative))
            {
                string[] parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                string walk = root;
                foreach (string part in parts)
                {
                    if (!ReferenceEquals(current, computer) && !current.IsExpanded)
                    {
                        break;
                    }

                    walk = Path.Combine(walk, part);
                    TreeViewNode? next = null;
                    foreach (TreeViewNode child in current.Children)
                    {
                        if (child.Content is SidebarTreeEntry childEntry &&
                            string.Equals(childEntry.FullPath, walk, StringComparison.OrdinalIgnoreCase))
                        {
                            next = child;
                            break;
                        }
                    }

                    if (next is null)
                    {
                        if (current.HasUnrealizedChildren && current.IsExpanded)
                        {
                            if (current.Content is SidebarTreeEntry currentEntry)
                            {
                                await PopulateSidebarTreeChildrenAsync(current, currentEntry.FullPath, CancellationToken.None);
                            }
                        }
                        foreach (TreeViewNode child in current.Children)
                        {
                            if (child.Content is SidebarTreeEntry childEntry &&
                                string.Equals(childEntry.FullPath, walk, StringComparison.OrdinalIgnoreCase))
                            {
                                next = child;
                                break;
                            }
                        }
                    }

                    if (next is null)
                    {
                        break;
                    }

                    current = next;

                    if (!string.Equals(walk, target, StringComparison.OrdinalIgnoreCase) && !current.IsExpanded)
                    {
                        break;
                    }

                    if (current.HasUnrealizedChildren && current.IsExpanded)
                    {
                        await PopulateSidebarTreeChildrenAsync(current, walk, CancellationToken.None);
                    }
                }
            }

            _suppressSidebarTreeSelection = true;
            _sidebarTreeView.SelectedNode = current;
            _suppressSidebarTreeSelection = false;
        }

        private async Task<bool> CanReadDirectoryAsync(string path)
        {
            try
            {
                (bool ok, FileBatchPage _, int rustErrorCode, string rustErrorMessage) = await Task.Run(
                    () =>
                    {
                        bool success = _explorerService.TryReadDirectoryRowsAuto(
                            path,
                            0,
                            1,
                            _lastFetchMs,
                            _currentSortMode,
                            out FileBatchPage page,
                            out int code,
                            out string message
                        );
                        return (success, page, code, message);
                    }
                );

                if (ok)
                {
                    return true;
                }

                if (IsRustAccessDenied(rustErrorCode, rustErrorMessage))
                {
                    UpdateStatusKey("StatusPathAccessDenied", path);
                    return false;
                }

                UpdateStatusKey("StatusPathRustError", path, rustErrorCode, rustErrorMessage);
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPathError", path, FileOperationErrors.ToUserMessage(ex));
                return false;
            }
        }

        private void PopulateMyComputerEntries()
        {
            _entries.Clear();
            _selectedEntryPath = null;
            var drives = new List<EntryViewModel>();
            foreach (DriveInfo drive in _explorerService.GetReadyDrives())
            {
                string root = drive.RootDirectory.FullName;
                string label = drive.Name.TrimEnd('\\');
                string type = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? S("DriveTypeLocalDisk")
                    : SF("DriveTypeVolumeFormat", drive.VolumeLabel, drive.DriveFormat);

                drives.Add(new EntryViewModel
                {
                    Name = label,
                    PendingName = label,
                    FullPath = root,
                    Type = type,
                    IconGlyph = "\uE7F8",
                    IconForeground = FolderIconBrush,
                    MftRef = 0,
                    SizeText = FormatBytes(drive.TotalSize),
                    ModifiedText = FormatBytes(drive.AvailableFreeSpace),
                    IsDirectory = true,
                    IsLink = false,
                    IsLoaded = true,
                    IsMetadataLoaded = true
                });
            }

            foreach (EntryViewModel driveEntry in drives)
            {
                _entries.Add(driveEntry);
            }

            SetPresentationSourceEntries(drives);

            _totalEntries = (uint)_entries.Count;
            InvalidateEntriesLayouts();
            _nextCursor = 0;
            _hasMore = false;
            UpdateFileCommandStates();
            _lastTitleWasReadFailed = false;
            UpdateWindowTitle();
            UpdateStatus(SF("StatusDriveCount", _entries.Count));
        }

        private void SidebarTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (_suppressSidebarTreeSelection)
            {
                _suppressSidebarTreeSelection = false;
                return;
            }
        }

        private async void SidebarTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (args.Node.Content is not SidebarTreeEntry entry)
            {
                return;
            }

            try
            {
                if (args.Node.HasUnrealizedChildren)
                {
                    await PopulateSidebarTreeChildrenAsync(args.Node, entry.FullPath, CancellationToken.None, expandAfterLoad: true);
                }
                await RestoreSidebarTreeSelectionAsync(args.Node);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarTreeExpandFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void SidebarTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            if (_sidebarTreeView is null || args.Node.Content is not SidebarTreeEntry entry)
            {
                return;
            }

            bool shouldSelectCollapsedNode = false;
            TreeViewNode? selected = _sidebarTreeView.SelectedNode;
            if (selected is not null &&
                !ReferenceEquals(selected, args.Node) &&
                IsTreeNodeDescendant(args.Node, selected) &&
                selected.Content is SidebarTreeEntry selectedEntry)
            {
                _sidebarTreeSelectionMemory[entry.FullPath] = selectedEntry.FullPath;
                shouldSelectCollapsedNode = true;
            }
            else if (IsPathWithin(_currentPath, entry.FullPath))
            {
                _sidebarTreeSelectionMemory[entry.FullPath] = _currentPath;
                shouldSelectCollapsedNode = true;
            }

            if (shouldSelectCollapsedNode)
            {
                _suppressSidebarTreeSelection = true;
                _sidebarTreeView.SelectedNode = args.Node;
                _suppressSidebarTreeSelection = false;
            }
        }

        private async void SidebarTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            SidebarTreeEntry? entry = args.InvokedItem switch
            {
                SidebarTreeEntry direct => direct,
                TreeViewNode node when node.Content is SidebarTreeEntry nested => nested,
                _ => sender.SelectedNode?.Content as SidebarTreeEntry
            };

            if (entry is null)
            {
                return;
            }

            await NavigateSidebarTreeEntryAsync(entry);
        }

        private void SidebarTree_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            if (_sidebarTreeView is null || _sidebarTreeContextFlyout is null)
            {
                return;
            }

            TreeViewNode? node = FindSidebarTreeNodeFromSource(e.OriginalSource as DependencyObject);
            bool isRootPath = false;
            if (node?.Content is SidebarTreeEntry candidate)
            {
                string root = Path.GetPathRoot(candidate.FullPath) ?? string.Empty;
                isRootPath = !string.IsNullOrEmpty(root) &&
                    string.Equals(root.TrimEnd('\\'), candidate.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }

            if (node?.Content is not SidebarTreeEntry entry ||
                string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                isRootPath)
            {
                return;
            }

            _pendingSidebarTreeContextEntry = entry;
            if (!ReferenceEquals(_sidebarTreeView.SelectedNode, node))
            {
                _suppressSidebarTreeSelection = true;
                _sidebarTreeView.SelectedNode = node;
                _suppressSidebarTreeSelection = false;
            }

            if (e.TryGetPosition(_sidebarTreeView, out Point point))
            {
                _sidebarTreeContextFlyout.ShowAt(_sidebarTreeView, new FlyoutShowOptions
                {
                    Position = point
                });
            }
            else
            {
                _sidebarTreeContextFlyout.ShowAt(_sidebarTreeView);
            }

            e.Handled = true;
        }

        private async void SidebarTreeContextRename_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingSidebarTreeContextEntry is null)
            {
                UpdateStatusKey("StatusRenameFailedSelectTreeNode");
                return;
            }

            SidebarTreeEntry entry = _pendingSidebarTreeContextEntry;
            _pendingSidebarTreeContextEntry = null;
            await BeginSidebarTreeRenameAsync(entry);
        }

        private async Task NavigateSidebarTreeEntryAsync(SidebarTreeEntry entry)
        {
            FocusSidebarSurface();
            ClearListSelectionAndAnchor();

            if (string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentPath = ShellMyComputerPath;
                PathTextBox.Text = ShellMyComputerPath;
                _currentQuery = string.Empty;
                SearchTextBox.Text = string.Empty;
                UpdateBreadcrumbs(_currentPath);
                UpdateNavButtonsState();
                _ = SelectSidebarTreePathAsync(_currentPath);
                await LoadFirstPageAsync();
                return;
            }

            Debug.WriteLine($"[Tree] Selected: {entry.FullPath}");
            if (_isLoading)
            {
                UpdateStatusKey("StatusSidebarTreeNavIgnoredLoading");
                return;
            }

            if (IsExactCurrentPath(entry.FullPath))
            {
                return;
            }

            try
            {
                Debug.WriteLine($"[Tree] Navigate: {entry.FullPath}");
                await NavigateToPathAsync(entry.FullPath, pushHistory: true, focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarTreeNavFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }


        private async Task RestoreSidebarTreeSelectionAsync(TreeViewNode node)
        {
            if (node.Content is not SidebarTreeEntry entry)
            {
                return;
            }

            if (!_sidebarTreeSelectionMemory.TryGetValue(entry.FullPath, out string? target))
            {
                return;
            }

            _sidebarTreeSelectionMemory.Remove(entry.FullPath);
            Debug.WriteLine($"[Tree] Restore: {entry.FullPath} -> {target}");
            await SelectSidebarTreePathAsync(target);
        }

        private static bool IsTreeNodeDescendant(TreeViewNode ancestor, TreeViewNode node)
        {
            TreeViewNode? current = node;
            while (current is not null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
                current = current.Parent;
            }
            return false;
        }

        private static bool IsPathWithin(string candidate, string ancestor)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(ancestor))
            {
                return false;
            }

            if (ancestor.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(ancestor, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate, ancestor, StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd('\\');
                string normalizedAncestor = Path.GetFullPath(ancestor).TrimEnd('\\');
                return string.Equals(normalizedCandidate, normalizedAncestor, StringComparison.OrdinalIgnoreCase)
                    || normalizedCandidate.StartsWith(normalizedAncestor + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private async void SidebarNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_suppressSidebarNavSelection || args.IsSettingsSelected)
            {
                return;
            }

            if (args.SelectedItemContainer is not NavigationViewItem item || item.Tag is not string target || string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            if (_isLoading)
            {
                UpdateStatusKey("StatusSidebarNavIgnoredLoading");
                return;
            }

            FocusSidebarSurface();
            ClearListSelectionAndAnchor();

            if (IsCurrentPath(target))
            {
                return;
            }

            try
            {
                await NavigateToPathAsync(target, pushHistory: true, focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async void StyledSidebarView_NavigateRequested(object? sender, SidebarNavigateRequestedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Path))
            {
                return;
            }

            if (_isLoading)
            {
                UpdateStatusKey("StatusSidebarNavIgnoredLoading");
                return;
            }

            FocusSidebarSurface();
            ClearListSelectionAndAnchor();

            if (IsCurrentPath(e.Path))
            {
                StyledSidebarView.SetSelectedPath(_currentPath);
                return;
            }

            try
            {
                await NavigateToPathAsync(e.Path, pushHistory: true, focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", FileOperationErrors.ToUserMessage(ex));
                StyledSidebarView.SetSelectedPath(_currentPath);
            }
        }

        private void StyledSidebarView_SettingsRequested(object? sender, EventArgs e)
        {
            EnterSettingsShell();
        }

        private void SettingsNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
        {
            if (_suppressSettingsNavigationSelection)
            {
                return;
            }

            if (e.SelectedItemContainer is not NavigationViewItem item
                || item.Tag is not string tag)
            {
                return;
            }

            if (string.Equals(tag, "Back", StringComparison.OrdinalIgnoreCase))
            {
                ExitSettingsShell();
                SetCurrentSettingsSection(_currentSettingsSection, updateSelection: true);
                return;
            }

            if (!TryParseSettingsSection(tag, out SettingsSection section))
            {
                return;
            }

            SetCurrentSettingsSection(section, updateSelection: true);
        }

        private void SettingsViewControl_VisibleSectionChanged(SettingsSection section)
        {
            if (_currentSettingsSection == section)
            {
                return;
            }

            _currentSettingsSection = section;
            if (SettingsNavigationView is null)
            {
                return;
            }

            _suppressSettingsNavigationSelection = true;
            foreach (object item in SettingsNavigationView.MenuItems)
            {
                if (item is NavigationViewItem navigationViewItem
                    && navigationViewItem.Tag is string tag
                    && TryParseSettingsSection(tag, out SettingsSection parsed)
                    && parsed == section)
                {
                    SettingsNavigationView.SelectedItem = navigationViewItem;
                    break;
                }
            }
            _suppressSettingsNavigationSelection = false;
        }

        private void SettingsViewControl_SidebarSectionVisibilityChanged(bool showFavorites, bool showCloud, bool showNetwork, bool showTags)
        {
            _appSettings.ShowFavorites = showFavorites;
            _appSettings.ShowCloud = showCloud;
            _appSettings.ShowNetwork = showNetwork;
            _appSettings.ShowTags = showTags;

            _appSettingsService.Save(_appSettings);
            StyledSidebarView.SetSectionVisibility(showFavorites, showCloud, showNetwork, showTags);
        }

        private void SettingsViewControl_DeleteConfirmationChanged(bool enabled)
        {
            _appSettings.ConfirmDelete = enabled;
            _appSettingsService.Save(_appSettings);
        }

        private void SidebarSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter)
            {
                return;
            }

            _activeSplitterElement = splitter;
            _activeSplitterDragMode = SplitterDragMode.Sidebar;
            _splitterDragStartX = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            _sidebarDragStartWidth = SidebarColumnWidth.Value;
            splitter.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void SidebarSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter
                || !ReferenceEquals(splitter, _activeSplitterElement)
                || _activeSplitterDragMode != SplitterDragMode.Sidebar
                || _sidebarDragStartWidth is not double startWidth)
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double x = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            double delta = x - _splitterDragStartX;
            double requestedWidth = startWidth + delta;
            ApplySidebarWidthLayout(requestedWidth, fromUserDrag: true);
            e.Handled = true;
        }

        private void SidebarSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitterDragMode != SplitterDragMode.Sidebar)
            {
                return;
            }

            EndActiveSplitterDrag(sender as UIElement);
            e.Handled = true;
        }

        private void ApplySidebarWidthLayout(double? requestedWidth = null, bool fromUserDrag = false)
        {
            if (ExplorerShellGrid is null)
            {
                return;
            }

            double bodyWidth = ExplorerShellGrid.ActualWidth;
            if (bodyWidth <= 0)
            {
                SidebarColumnWidth = new GridLength((_sidebarPinnedCompact || _isSidebarCompact) ? SidebarCompactWidth : _sidebarPreferredExpandedWidth);
                return;
            }

            double maxSidebarWidth = Math.Max(SidebarCompactWidth, bodyWidth - SidebarSplitterWidth - SidebarMinContentWidth);
            if (fromUserDrag)
            {
                double dragWidth = requestedWidth ?? _sidebarPreferredExpandedWidth;
                if (dragWidth <= SidebarCompactThreshold)
                {
                    _sidebarPinnedCompact = true;
                }
                else
                {
                    _sidebarPinnedCompact = false;
                    _sidebarPreferredExpandedWidth = Math.Max(dragWidth, SidebarExpandedMinWidth);
                }
            }

            bool forcedCompact = _isSidebarCompact
                ? maxSidebarWidth <= SidebarCompactExitThreshold
                : maxSidebarWidth <= SidebarCompactThreshold;
            bool compact = forcedCompact || _sidebarPinnedCompact;
            double clampedWidth = Math.Min(_sidebarPreferredExpandedWidth, maxSidebarWidth);
            double finalWidth = compact
                ? SidebarCompactWidth
                : Math.Max(clampedWidth, SidebarExpandedMinWidth);

            SidebarColumnWidth = new GridLength(finalWidth);
            ApplySidebarCompactState(compact);
        }

        private void ApplySidebarCompactState(bool compact)
        {
            if (_isSidebarCompact == compact)
            {
                StyledSidebarView.SetCompact(compact);
                return;
            }

            _isSidebarCompact = compact;
            StyledSidebarView.SetCompact(compact);
            if (SidebarNavView is not null)
            {
                SidebarNavView.PaneDisplayMode = compact ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                SidebarNavView.IsPaneOpen = !compact;
                SidebarNavView.CompactPaneLength = SidebarCompactWidth;
                SidebarNavView.OpenPaneLength = compact ? SidebarCompactWidth : SidebarColumnWidth.Value;
            }
        }

        private void UpdateSidebarSelectionOnly()
        {
            NavigationViewItem? selected = null;
            int bestLen = -1;
            foreach ((string path, NavigationViewItem item) in _sidebarPathButtons)
            {
                if (!IsCurrentPath(path))
                {
                    continue;
                }

                if (path.Length > bestLen)
                {
                    bestLen = path.Length;
                    selected = item;
                }
            }

            if (SidebarNavView is not null)
            {
                _suppressSidebarNavSelection = true;
                SidebarNavView.SelectedItem = selected;
                _suppressSidebarNavSelection = false;
            }

            StyledSidebarView.SetSelectedPath(_currentPath);
            _ = SelectSidebarTreePathAsync(_currentPath);
        }

        private bool IsCurrentPath(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return false;
            }

            if (string.Equals(candidate, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(candidate, _currentPath, StringComparison.OrdinalIgnoreCase);
            }

            string curr = Path.GetFullPath(_currentPath).TrimEnd('\\');
            string cand = Path.GetFullPath(candidate).TrimEnd('\\');
            if (string.Equals(curr, cand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // Highlight sidebar parent items too (e.g. inside Downloads subtree).
            return curr.StartsWith(cand + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsExactCurrentPath(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(_currentPath))
            {
                return false;
            }

            try
            {
                string curr = Path.GetFullPath(_currentPath).TrimEnd('\\');
                string cand = Path.GetFullPath(candidate).TrimEnd('\\');
                return string.Equals(curr, cand, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void ApplyCommandDockLayout()
        {
            if (CommandDockPanel is null || CommandPeekButton is null)
            {
                return;
            }
            if (!_showCommandDock)
            {
                CommandDockPanel.Visibility = Visibility.Collapsed;
                CommandPeekButton.Visibility = Visibility.Collapsed;
                return;
            }

            bool autoHide = CommandAutoHideSwitch?.IsOn == true;
            CommandDockPanel.Visibility = autoHide ? Visibility.Collapsed : Visibility.Visible;
            CommandPeekButton.Visibility = autoHide ? Visibility.Visible : Visibility.Collapsed;

            switch (_commandDockSide)
            {
                case CommandDockSide.Right:
                    CommandDockPanel.HorizontalAlignment = HorizontalAlignment.Right;
                    CommandDockPanel.VerticalAlignment = VerticalAlignment.Center;
                    CommandDockPanel.Margin = new Thickness(10, 0, 10, 0);
                    CommandPeekButton.HorizontalAlignment = HorizontalAlignment.Right;
                    CommandPeekButton.VerticalAlignment = VerticalAlignment.Center;
                    CommandPeekButton.Margin = new Thickness(0, 0, 10, 0);
                    break;
                case CommandDockSide.Bottom:
                    CommandDockPanel.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandDockPanel.VerticalAlignment = VerticalAlignment.Bottom;
                    CommandDockPanel.Margin = new Thickness(10);
                    CommandPeekButton.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandPeekButton.VerticalAlignment = VerticalAlignment.Bottom;
                    CommandPeekButton.Margin = new Thickness(10);
                    break;
                default:
                    CommandDockPanel.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandDockPanel.VerticalAlignment = VerticalAlignment.Top;
                    CommandDockPanel.Margin = new Thickness(10);
                    CommandPeekButton.HorizontalAlignment = HorizontalAlignment.Center;
                    CommandPeekButton.VerticalAlignment = VerticalAlignment.Top;
                    CommandPeekButton.Margin = new Thickness(10);
                    break;
            }
        }

        private void ConfigureDirectoryWatcher(string path)
        {
            _dirWatcher?.Dispose();
            _dirWatcher = null;

            // Week4 MVP: when USN is unavailable, consume incremental changes via watcher.
            if (_usnCapability.available != 0)
            {
                return;
            }
            if (!_explorerService.DirectoryExists(path))
            {
                return;
            }
            if (IsDriveRoot(path))
            {
                // Root volumes produce noisy system events; skip watcher to avoid refresh storms.
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                watcher.Changed += Watcher_OnChanged;
                watcher.Created += Watcher_OnChanged;
                watcher.Deleted += Watcher_OnChanged;
                watcher.Renamed += Watcher_OnRenamed;
                _dirWatcher = watcher;
            }
            catch
            {
                // Non-fatal: we can still rely on manual refresh + TTL.
            }
        }

        private static bool IsDriveRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string full = Path.GetFullPath(path).TrimEnd('\\');
            return full.Length == 2 && full[1] == ':';
        }

        private void Watcher_OnChanged(object sender, FileSystemEventArgs e)
        {
            ScheduleIncrementalRefreshFromWatcher("changed");
        }

        private void Watcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            ScheduleIncrementalRefreshFromWatcher("renamed");
        }

        private void ScheduleIncrementalRefreshFromWatcher(string reason)
        {
            string snapPath = _currentPath;
            _watcherDebounceCts?.Cancel();
            _watcherDebounceCts = new CancellationTokenSource();
            CancellationToken token = _watcherDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (!string.Equals(snapPath, _currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }
                        if (_isLoading)
                        {
                            return;
                        }
                        long now = Environment.TickCount64;
                        if (now - _lastWatcherRefreshTick < 1000)
                        {
                            return;
                        }
                        if (ConsumeSuppressedWatcherRefresh(_currentPath))
                        {
                            Debug.WriteLine($"[Watcher] Suppressed self-refresh for {_currentPath}");
                            return;
                        }
                        _lastWatcherRefreshTick = now;

                        try
                        {
                            _explorerService.MarkPathChanged(_currentPath);
                        }
                        catch
                        {
                            // Ignore mark failures; background refresh will still attempt to recover.
                        }

                        EnsurePersistentRefreshFallbackInvalidation(_currentPath, $"watcher_{reason}");
                        _ = RefreshCurrentDirectoryInBackgroundAsync();
                    });
                }
                catch (TaskCanceledException)
                {
                    // Ignore debounce cancellation.
                }
            });
        }

        private static string DescribeUsnCapability(RustUsnCapability c)
        {
            if (c.error_code != 0)
            {
                return SF("UsnCapabilityError", c.error_code);
            }
            if (c.available != 0)
            {
                return S("UsnCapabilityAvailable");
            }
            if (c.is_ntfs_local == 0)
            {
                return S("UsnCapabilityNotNtfs");
            }
            if (c.access_denied != 0)
            {
                return S("UsnCapabilityDenied");
            }
            return S("UsnCapabilityUnavailable");
        }

        private static string DescribeSourceDetail(byte sourceKind, RustUsnCapability c)
        {
            if (sourceKind != 1)
            {
                return string.Empty;
            }
            if (c.error_code != 0)
            {
                return SF("UsnFallbackProbeError", c.error_code);
            }
            if (c.is_ntfs_local == 0)
            {
                return S("UsnFallbackNotLocalNtfs");
            }
            if (c.access_denied != 0)
            {
                return S("UsnFallbackAccessDenied");
            }
            if (c.available != 0)
            {
                return S("UsnFallbackBatchUnavailable");
            }
            return S("UsnFallbackUnavailable");
        }

        private static uint ClampPageSize(uint suggested, uint fallback)
        {
            uint value = suggested == 0 ? fallback : suggested;
            if (value < MinPageSize)
            {
                return MinPageSize;
            }
            if (value > MaxPageSize)
            {
                return MaxPageSize;
            }
            return value;
        }

        private static bool IsRustAccessDenied(int errorCode, string message)
        {
            if (errorCode == 2001 && message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private int GetLogicalEntryCount()
        {
            return Math.Max(_entries.Count, checked((int)Math.Min(int.MaxValue, _totalEntries)));
        }

        private int EstimateViewportIndex(ScrollViewer viewer)
        {
            int logicalCount = GetLogicalEntryCount();
            if (logicalCount <= 1)
            {
                return 0;
            }

            if (_currentViewMode == EntryViewMode.List)
            {
                int rowsPerColumn = Math.Max(1, GetGroupedListRowsPerColumn());
                double columnStride = Math.Max(1, EntryContainerWidth + 16);
                int columnIndex = (int)Math.Floor(viewer.HorizontalOffset / columnStride);
                return Math.Clamp(columnIndex * rowsPerColumn, 0, logicalCount - 1);
            }

            double itemExtent = Math.Max(1.0, _estimatedItemHeight);
            int index = (int)Math.Floor(Math.Max(0.0, viewer.VerticalOffset) / itemExtent);
            return Math.Clamp(index, 0, logicalCount - 1);
        }

        private int EstimateViewportBottomIndex(ScrollViewer viewer)
        {
            int topIndex = EstimateViewportIndex(viewer);
            int visibleCount = _currentViewMode == EntryViewMode.List
                ? Math.Max(1, GetGroupedListRowsPerColumn() * Math.Max(1, (int)Math.Ceiling(viewer.ViewportWidth / Math.Max(1, EntryContainerWidth + 16))))
                : Math.Max(1, (int)Math.Ceiling(viewer.ViewportHeight / _estimatedItemHeight));
            int bottom = topIndex + visibleCount;
            return Math.Min(GetLogicalEntryCount() - 1, Math.Max(0, bottom));
        }

        private async Task EnsureDataForViewportAsync(int startIndex, int endIndex, bool preferMinimalPage = false)
        {
            int logicalCount = GetLogicalEntryCount();
            if (logicalCount <= 0)
            {
                return;
            }

            int safeStartIndex = Math.Clamp(Math.Min(startIndex, endIndex), 0, logicalCount - 1);
            int safeEndIndex = Math.Clamp(Math.Max(startIndex, endIndex), safeStartIndex, logicalCount - 1);
            LogDetailsViewportPerf(
                "ensure-viewport",
                $"start={safeStartIndex} end={safeEndIndex} entries={_entries.Count} total={_totalEntries} loading={_isLoading}");

            if (_currentViewMode == EntryViewMode.Details && _entries.Count < logicalCount)
            {
                EnsurePlaceholderCount(logicalCount);
                InvalidateEntriesLayouts();
            }

            if (!IsViewportRangeLoaded(safeStartIndex, safeEndIndex))
            {
                LogDetailsViewportPerf("ensure-viewport.sparse-start", $"index={safeStartIndex}");
                await QueueSparseViewportLoadAsync(safeStartIndex, preferMinimalPage);
                return;
            }

            if (!MaybePrefetchDetailsViewportBlock(safeStartIndex, safeEndIndex, preferMinimalPage))
            {
                LogDetailsViewportPerf("ensure-viewport.loaded", $"start={safeStartIndex} end={safeEndIndex}");
            }
        }

        private bool IsViewportRangeLoaded(int startIndex, int endIndex)
        {
            if (startIndex < 0 || endIndex < startIndex)
            {
                return true;
            }

            if (startIndex >= _entries.Count)
            {
                return false;
            }

            int cappedEnd = Math.Min(endIndex, _entries.Count - 1);
            for (int index = startIndex; index <= cappedEnd; index++)
            {
                if (!_entries[index].IsLoaded)
                {
                    return false;
                }
            }

            return cappedEnd >= endIndex;
        }

        private bool MaybePrefetchDetailsViewportBlock(int startIndex, int endIndex, bool preferMinimalPage)
        {
            if (_currentViewMode != EntryViewMode.Details)
            {
                return false;
            }

            if (IsSparseViewportLoadQueuedOrActive())
            {
                LogDetailsViewportPerf("ensure-viewport.skip-queued", $"start={startIndex} end={endIndex}");
                return false;
            }

            int logicalCount = GetLogicalEntryCount();
            if (logicalCount <= 0 || endIndex < 0)
            {
                return false;
            }

            int visibleCount = Math.Max(1, endIndex - startIndex + 1);
            int prefetchDistance = Math.Max(6, visibleCount / 3);
            int searchStart = Math.Min(logicalCount - 1, endIndex + 1);
            int searchEnd = Math.Min(logicalCount - 1, endIndex + Math.Max(visibleCount * 2, 36));
            int firstUnloadedIndex = -1;
            for (int index = searchStart; index <= searchEnd; index++)
            {
                if (index >= _entries.Count || !_entries[index].IsLoaded)
                {
                    firstUnloadedIndex = index;
                    break;
                }
            }

            if (firstUnloadedIndex < 0)
            {
                return false;
            }

            int distanceToViewportEnd = firstUnloadedIndex - endIndex;
            if (distanceToViewportEnd > prefetchDistance)
            {
                LogDetailsViewportPerf(
                    "ensure-viewport.loaded",
                    $"start={startIndex} end={endIndex} next-unloaded={firstUnloadedIndex} distance={distanceToViewportEnd}");
                return false;
            }

            LogDetailsViewportPerf(
                "ensure-viewport.prefetch",
                $"index={firstUnloadedIndex} distance={distanceToViewportEnd} threshold={prefetchDistance}");
            _ = QueueSparseViewportLoadAsync(firstUnloadedIndex, preferMinimalPage);
            return true;
        }

        private async Task QueueSparseViewportLoadAsync(int targetIndex, bool preferMinimalPage = false)
        {
            bool shouldStartPump = false;
            int logicalCount = GetLogicalEntryCount();
            if (logicalCount <= 0)
            {
                return;
            }

            int clampedTargetIndex = Math.Clamp(targetIndex, 0, logicalCount - 1);

            lock (_sparseViewportGate)
            {
                _pendingSparseViewportTargetIndex = clampedTargetIndex;
                _pendingSparseViewportPreferMinimalPage = preferMinimalPage;
                if (!_isSparseViewportLoadActive)
                {
                    _isSparseViewportLoadActive = true;
                    shouldStartPump = true;
                }
            }

            if (!shouldStartPump)
            {
                LogDetailsViewportPerf("sparse-queue.update", $"target={clampedTargetIndex}");
                return;
            }

            try
            {
                while (true)
                {
                    int nextTargetIndex;
                    bool consumeMinimalPage;
                    lock (_sparseViewportGate)
                    {
                        if (_pendingSparseViewportTargetIndex is null)
                        {
                            _isSparseViewportLoadActive = false;
                            return;
                        }

                        nextTargetIndex = _pendingSparseViewportTargetIndex.Value;
                        consumeMinimalPage = _pendingSparseViewportPreferMinimalPage;
                        _pendingSparseViewportTargetIndex = null;
                        _pendingSparseViewportPreferMinimalPage = false;
                    }

                    LogDetailsViewportPerf("sparse-queue.consume", $"target={nextTargetIndex} minimal={consumeMinimalPage}");
                    await LoadSparseViewportPageAsync(nextTargetIndex, consumeMinimalPage);
                }
            }
            finally
            {
                lock (_sparseViewportGate)
                {
                    _isSparseViewportLoadActive = false;
                }
            }
        }

        private bool IsInitialDetailsSparseBootstrap(int targetIndex)
        {
            if (_currentViewMode != EntryViewMode.Details || targetIndex < 0 || targetIndex >= _entries.Count)
            {
                return false;
            }

            if (_entries[targetIndex].IsLoaded)
            {
                return false;
            }

            int loadedThreshold = Math.Max(192, checked((int)_currentPageSize) * 2);
            int loadedCount = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].IsLoaded)
                {
                    loadedCount++;
                    if (loadedCount > loadedThreshold)
                    {
                        return false;
                    }
                }
            }

            return loadedCount <= loadedThreshold;
        }

        private async Task LoadSparseViewportPageAsync(int targetIndex, bool preferMinimalPage)
        {
            if (_currentViewMode != EntryViewMode.Details || string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int logicalCount = GetLogicalEntryCount();
            if (logicalCount <= 0)
            {
                return;
            }

            double viewportHeight = DetailsEntriesScrollViewer.ViewportHeight > 0
                ? DetailsEntriesScrollViewer.ViewportHeight
                : DetailsEntriesScrollViewer.ActualHeight;
            int visibleCount = Math.Max(1, (int)Math.Ceiling(Math.Max(1, viewportHeight) / _estimatedItemHeight));
            int blockSize = preferMinimalPage
                ? Math.Max(64, visibleCount * 4)
                : Math.Max(192, visibleCount * 8);
            blockSize = Math.Min(blockSize, logicalCount);
            int alignedStartIndex = (targetIndex / Math.Max(1, blockSize)) * blockSize;
            int maxStartIndex = Math.Max(0, logicalCount - blockSize);
            int startIndex = Math.Clamp(alignedStartIndex, 0, maxStartIndex);
            int pageSize = Math.Min(blockSize, logicalCount - startIndex);
            ulong cursor = (ulong)startIndex;
            int requestId = Interlocked.Increment(ref s_detailsViewportPerfSequence);

            string path = _currentPath;
            uint lastFetchMs = _lastFetchMs;
            long snapshotVersion = _directorySnapshotVersion;
            EnsureActiveEntryResultSet(path);
            IEntryResultSet? resultSet = _activeEntryResultSet;
            if (resultSet is null)
            {
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();
            LogDetailsViewportPerf(
                "sparse-fetch.begin",
                $"req={requestId} target={targetIndex} cursor={cursor} pageSize={pageSize} visible={visibleCount} block={blockSize} minimal={preferMinimalPage} entries={_entries.Count} total={_totalEntries}");

            FileBatchPage page;
            bool ok;
            int rustErrorCode;
            string rustErrorMessage;
            int viewportIndexDelta = _lastDetailsViewportIndexDelta;
            int viewportBlockDelta = Math.Max(0, (int)Math.Ceiling(viewportIndexDelta / (double)Math.Max(1, blockSize)));
            bool useSynchronousRead = preferMinimalPage &&
                (viewportBlockDelta <= 1 || IsInitialDetailsSparseBootstrap(targetIndex));

            if (useSynchronousRead)
            {
                ok = resultSet.TryReadRange(
                    cursor,
                    (uint)pageSize,
                    lastFetchMs,
                    out page,
                    out rustErrorCode,
                    out rustErrorMessage);
            }
            else
            {
                (ok, page, rustErrorCode, rustErrorMessage) = await Task.Run(() =>
                {
                    bool success = resultSet.TryReadRange(
                        cursor,
                        (uint)pageSize,
                        lastFetchMs,
                        out FileBatchPage p,
                        out int code,
                        out string msg);
                    return (success, p, code, msg);
                });
            }

            sw.Stop();
            LogDetailsViewportPerf(
                "sparse-fetch.end",
                $"req={requestId} ok={ok} elapsed={sw.ElapsedMilliseconds}ms cursor={cursor} rows={page.Rows.Count} total={page.TotalEntries} rust={rustErrorCode} sync={useSynchronousRead} delta={_lastDetailsVerticalDelta:F1} indexDelta={viewportIndexDelta} blockDelta={viewportBlockDelta}");

            if (!ok || snapshotVersion != _directorySnapshotVersion || !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                LogDetailsViewportPerf(
                    "sparse-fetch.discard",
                    $"req={requestId} ok={ok} snapshotMatch={snapshotVersion == _directorySnapshotVersion} pathMatch={string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase)}");
                return;
            }

            Stopwatch bindSw = Stopwatch.StartNew();
            _totalEntries = Math.Max(_totalEntries, page.TotalEntries);
            EnsurePlaceholderCount(checked((int)Math.Min(int.MaxValue, _totalEntries)));
            FillPageRows((int)cursor, page.Rows, path);
            InvalidateEntriesLayouts();
            CancelPendingViewportMetadataWork();
            bindSw.Stop();
            LogDetailsViewportPerf(
                "sparse-bind.end",
                $"req={requestId} elapsed={bindSw.ElapsedMilliseconds}ms cursor={cursor} rows={page.Rows.Count} entries={_entries.Count} total={_totalEntries}");
        }

        private static void LogDetailsViewportPerf(string stage, string detail)
        {
            string message = $"[DETAILS-VP] stage={stage} {detail}";
            Debug.WriteLine(message);
            AppendNavigationPerfLog(message);
        }

        private void RequestViewportWork()
        {
            if (_currentViewMode != EntryViewMode.Details)
            {
                return;
            }

            RequestPrefetchForCurrentViewport();
        }

        private void RequestPrefetchForCurrentViewport()
        {
            if (!_hasMore)
            {
                return;
            }

            int startIndex = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int endIndex = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
            _ = EnsureDataForViewportAsync(startIndex, endIndex);
        }

        private void RequestMetadataForCurrentViewportDeferred(int delayMs = 1)
        {
            int requestVersion = unchecked(++_metadataViewportRequestVersion);
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                if (requestVersion != _metadataViewportRequestVersion)
                {
                    return;
                }

                RequestMetadataForCurrentViewport();
            });
        }

        private void RequestMetadataForCurrentViewport()
        {
            unchecked
            {
                _metadataViewportRequestVersion++;
            }

            if (IsDetailsViewportInteractionHot() || IsSparseViewportLoadQueuedOrActive())
            {
                RequestMetadataForCurrentViewportDeferred(96);
                return;
            }

            if (_entries.Count == 0)
            {
                CancelAndDispose(ref _metadataPrefetchCts);
                return;
            }

            int visibleStart;
            int visibleEnd;
            if (_currentViewMode != EntryViewMode.Details)
            {
                visibleStart = 0;
                visibleEnd = _entries.Count - 1;
            }
            else
            {
                visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
                visibleEnd = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
            }

            if (visibleEnd < visibleStart)
            {
                return;
            }

            int visibleCount = Math.Max(1, visibleEnd - visibleStart + 1);
            bool throttleMetadataPrefetch = _entries.Count > 1024;
            int lookahead = throttleMetadataPrefetch
                ? 0
                : Math.Max(visibleCount, (int)_currentPageSize);
            int prefetchEnd = Math.Min(_entries.Count - 1, visibleEnd + lookahead);

            List<MetadataWorkItem> visibleItems = CollectMetadataWorkItems(visibleStart, visibleEnd);
            List<MetadataWorkItem> prefetchItems = throttleMetadataPrefetch
                ? []
                : CollectMetadataWorkItems(visibleEnd + 1, prefetchEnd);
            if (visibleItems.Count == 0 && prefetchItems.Count == 0)
            {
                return;
            }

            CancellationTokenSource? baseCts = _directoryLoadCts;
            if (baseCts is null)
            {
                return;
            }

            CancelAndDispose(ref _metadataPrefetchCts);
            _metadataPrefetchCts = CancellationTokenSource.CreateLinkedTokenSource(baseCts.Token);
            CancellationToken token = _metadataPrefetchCts.Token;
            long snapshotVersion = _directorySnapshotVersion;
            string path = _currentPath;

            _ = Task.Run(async () =>
            {
                await HydrateMetadataBatchAsync(path, snapshotVersion, visibleItems, token);
                await HydrateMetadataBatchAsync(path, snapshotVersion, prefetchItems, token);
            }, token);
        }

        private void RequestMetadataForLoadedRange(int startIndex, int endIndex)
        {
            if (IsDetailsViewportInteractionHot() || IsSparseViewportLoadQueuedOrActive())
            {
                return;
            }

            List<MetadataWorkItem> items = CollectMetadataWorkItems(startIndex, endIndex);
            if (items.Count == 0)
            {
                return;
            }

            CancellationTokenSource? baseCts = _directoryLoadCts;
            if (baseCts is null)
            {
                return;
            }

            long snapshotVersion = _directorySnapshotVersion;
            string path = _currentPath;
            CancellationToken token = baseCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await HydrateMetadataBatchAsync(path, snapshotVersion, items, token);
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        private List<MetadataWorkItem> CollectMetadataWorkItems(int startIndex, int endIndex)
        {
            var items = new List<MetadataWorkItem>();
            if (startIndex < 0 || startIndex >= _entries.Count || endIndex < startIndex)
            {
                return items;
            }

            int cappedEnd = Math.Min(endIndex, _entries.Count - 1);
            for (int i = startIndex; i <= cappedEnd; i++)
            {
                EntryViewModel entry = _entries[i];
                if (!entry.IsLoaded || entry.IsMetadataLoaded)
                {
                    continue;
                }

                items.Add(new MetadataWorkItem(i, entry.Name, entry.MftRef, entry.IsDirectory, entry.IsLink));
            }

            return items;
        }

        private async Task HydrateMetadataBatchAsync(
            string path,
            long snapshotVersion,
            IReadOnlyList<MetadataWorkItem> items,
            CancellationToken token
        )
        {
            if (items.Count == 0)
            {
                return;
            }

            var results = new List<MetadataHydrationResult>(items.Count);
            foreach (MetadataWorkItem item in items)
            {
                token.ThrowIfCancellationRequested();

                MetadataPayload payload = BuildMetadataPayload(path, item.Name, item.IsDirectory, item.IsLink, token);
                results.Add(new MetadataHydrationResult(item, payload));
            }

            if (token.IsCancellationRequested || results.Count == 0)
            {
                return;
            }

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (snapshotVersion != _directorySnapshotVersion)
                {
                    return;
                }

                foreach (MetadataHydrationResult result in results)
                {
                    ApplyMetadataPayload(snapshotVersion, result.Item, result.Payload);
                }
            });
        }

        private MetadataPayload BuildMetadataPayload(
            string path,
            string name,
            bool isDirectory,
            bool isLink,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            string fullPath = Path.Combine(path, name);
            string sizeText = isDirectory ? string.Empty : GetFileSizeText(fullPath);
            token.ThrowIfCancellationRequested();
            string modifiedText = GetModifiedTimeText(fullPath, isDirectory);
            string iconGlyph = GetEntryIconGlyph(isDirectory, isLink, name);
            Brush iconForeground = GetEntryIconBrush(isDirectory, isLink, name);
            return new MetadataPayload(sizeText, modifiedText, iconGlyph, iconForeground);
        }

        private static List<ViewportMetadataResult> BuildViewportMetadataResults(
            string path,
            int pageStartIndex,
            IReadOnlyList<FileRow> rows)
        {
            if (rows.Count == 0)
            {
                return [];
            }

            var results = new List<ViewportMetadataResult>(rows.Count);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                int logicalIndex = pageStartIndex + rowIndex;
                FileRow row = rows[rowIndex];
                string fullPath = Path.Combine(path, row.Name);
                if (row.IsDirectory)
                {
                    DateTime? modifiedAt = TryGetModifiedTime(fullPath, isDirectory: true);
                    results.Add(new ViewportMetadataResult(
                        logicalIndex,
                        null,
                        string.Empty,
                        modifiedAt,
                        FormatModifiedTime(modifiedAt)));
                    continue;
                }

                (long? sizeBytes, string sizeText) = TryGetFileSize(fullPath);
                DateTime? modifiedFileAt = TryGetModifiedTime(fullPath, isDirectory: false);
                results.Add(new ViewportMetadataResult(
                    logicalIndex,
                    sizeBytes,
                    sizeText,
                    modifiedFileAt,
                    FormatModifiedTime(modifiedFileAt)));
            }

            return results;
        }

        private void ApplyViewportMetadataResults(long snapshotVersion, IReadOnlyList<ViewportMetadataResult> results)
        {
            if (snapshotVersion != _directorySnapshotVersion || results.Count == 0)
            {
                return;
            }

            foreach (ViewportMetadataResult result in results)
            {
                if (result.Index < 0 || result.Index >= _entries.Count)
                {
                    continue;
                }

                EntryViewModel entry = _entries[result.Index];
                if (!entry.IsLoaded)
                {
                    continue;
                }

                entry.SizeBytes = result.SizeBytes;
                entry.SizeText = result.SizeText;
                entry.ModifiedAt = result.ModifiedAt;
                entry.ModifiedText = result.ModifiedText;
                entry.IsMetadataLoaded = true;
            }
        }

        private void ApplyMetadataPayload(long snapshotVersion, MetadataWorkItem item, MetadataPayload payload)
        {
            if (snapshotVersion != _directorySnapshotVersion)
            {
                return;
            }

            if (item.Index < 0 || item.Index >= _entries.Count)
            {
                return;
            }

            EntryViewModel current = _entries[item.Index];
            if (!current.IsLoaded)
            {
                return;
            }

            if (current.MftRef != item.MftRef || !string.Equals(current.Name, item.Name, StringComparison.Ordinal))
            {
                return;
            }

            current.SizeText = payload.SizeText;
            current.ModifiedText = payload.ModifiedText;
            current.IconGlyph = payload.IconGlyph;
            current.IconForeground = payload.IconForeground;
            current.IsMetadataLoaded = true;
        }

        private void BeginDirectorySnapshot()
        {
            CancelAndDispose(ref _metadataPrefetchCts);
            CancelAndDispose(ref _directoryLoadCts);
            _directoryLoadCts = new CancellationTokenSource();
            _directorySnapshotVersion++;
        }

        private static void CancelAndDispose(ref CancellationTokenSource? cts)
        {
            if (cts is null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch
            {
            }

            cts.Dispose();
            cts = null;
        }

        private void UpdateEstimatedItemHeight()
        {
            _estimatedItemHeight = Math.Max(32.0, EntryItemMetrics.RowHeight + 4);
        }

        private void ReplaceEntriesWithLoadedRows(string basePath, IReadOnlyList<FileRow> rows)
        {
            _entries.Clear();
            AppendLoadedRows(basePath, rows);
        }

        private void AppendLoadedRows(string basePath, IReadOnlyList<FileRow> rows)
        {
            if (rows.Count == 0)
            {
                return;
            }

            foreach (FileRow row in rows)
            {
                _entries.Add(CreateLoadedEntryModel(basePath, row));
            }
        }

        private void EnsurePlaceholderCount(int target)
        {
            _entries.Resize(target, CreatePlaceholderEntryModel);
        }

        private void EnsureLoadedRangeCapacity(int startIndex, int rowCount)
        {
            if (startIndex < 0 || rowCount <= 0)
            {
                return;
            }

            int target = checked(startIndex + rowCount);
            if (target > _entries.Count)
            {
                EnsurePlaceholderCount(target);
            }
        }

        private EntryViewModel CreatePlaceholderEntryModel()
        {
            return new EntryViewModel
            {
                Name = string.Empty,
                PendingName = string.Empty,
                FullPath = string.Empty,
                Type = string.Empty,
                IconGlyph = string.Empty,
                IconForeground = FileIconBrush,
                MftRef = 0,
                SizeText = string.Empty,
                ModifiedText = string.Empty,
                IsDirectory = false,
                IsLink = false,
                IsLoaded = false,
                IsMetadataLoaded = false
            };
        }

        private void FillPageRows(int startIndex, IReadOnlyList<FileRow> rows, string? basePathOverride = null)
        {
            if (startIndex < 0 || startIndex >= _entries.Count)
            {
                return;
            }

            string basePath = string.IsNullOrWhiteSpace(basePathOverride) ? _currentPath : basePathOverride;
            int max = Math.Min(rows.Count, _entries.Count - startIndex);
            for (int i = 0; i < max; i++)
            {
                ApplyLoadedEntryRow(_entries[startIndex + i], basePath, rows[i]);
            }
        }

        private EntryViewModel CreateLoadedEntryModel(string basePath, FileRow row)
        {
            var entry = new EntryViewModel();
            ApplyLoadedEntryRow(entry, basePath, row);
            return entry;
        }

        private void ApplyLoadedEntryRow(EntryViewModel entry, string basePath, FileRow row)
        {
            entry.Name = row.Name;
            entry.PendingName = row.Name;
            entry.FullPath = Path.Combine(basePath, row.Name);
            entry.Type = GetEntryTypeText(row.Name, row.IsDirectory, row.IsLink);
            entry.IconGlyph = GetEntryIconGlyph(row.IsDirectory, row.IsLink, row.Name);
            entry.IconForeground = GetEntryIconBrush(row.IsDirectory, row.IsLink, row.Name);
            entry.MftRef = row.MftRef;
            entry.SizeBytes = row.SizeBytes;
            entry.SizeText = row.IsDirectory
                ? string.Empty
                : row.SizeBytes is long sizeBytes
                    ? FormatBytes(sizeBytes)
                    : "-";
            entry.ModifiedAt = row.ModifiedAt;
            entry.ModifiedText = FormatModifiedTime(row.ModifiedAt);
            entry.IsDirectory = row.IsDirectory;
            entry.IsLink = row.IsLink;
            entry.IsPendingCreate = false;
            entry.PendingCreateIsDirectory = false;
            entry.IsLoaded = true;
            entry.IsMetadataLoaded = row.ModifiedAt.HasValue || row.IsDirectory || row.SizeBytes.HasValue;
        }

        private void PopulateEntryMetadata(EntryViewModel entry)
        {
            if (!entry.IsLoaded)
            {
                return;
            }

            if (entry.IsMetadataLoaded)
            {
                return;
            }

            if (entry.IsDirectory)
            {
                entry.SizeBytes = null;
                entry.SizeText = string.Empty;
            }
            else
            {
                try
                {
                    var fi = new FileInfo(entry.FullPath);
                    entry.SizeBytes = fi.Exists ? fi.Length : null;
                    entry.SizeText = fi.Exists ? FormatBytes(fi.Length) : "-";
                }
                catch
                {
                    entry.SizeBytes = null;
                    entry.SizeText = "-";
                }
            }

            try
            {
                DateTime modified = entry.IsDirectory
                    ? new DirectoryInfo(entry.FullPath).LastWriteTime
                    : new FileInfo(entry.FullPath).LastWriteTime;
                entry.ModifiedAt = modified == DateTime.MinValue ? null : modified;
                entry.ModifiedText = entry.ModifiedAt?.ToString("g", CultureInfo.CurrentCulture) ?? "-";
            }
            catch
            {
                entry.ModifiedAt = null;
                entry.ModifiedText = "-";
            }

            entry.IsMetadataLoaded = true;
        }

        private void ApplyCurrentPresentation(NavigationPerfSession? perf = null)
        {
            perf?.Mark("apply-presentation.enter", $"view={_currentViewMode} group={_currentGroupField} sort={_currentSortField}/{_currentSortDirection}");
            List<EntryViewModel> sourceEntries = _presentationSourceEntries.Count > 0
                ? _presentationSourceEntries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList()
                : _entries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList();

            sourceEntries.Sort(CompareEntriesForPresentation);
            perf?.Mark("apply-presentation.sorted", $"count={sourceEntries.Count}");
            string? selectedPath = _selectedEntryPath;
            if (UsesColumnsListPresentation())
            {
                _groupedListRowsPerColumn = GetGroupedListRowsPerColumn();
                ApplyEntryViewState(sourceEntries);

                if (!MatchesCurrentVisibleEntries(sourceEntries))
                {
                    ReplaceVisibleEntries(sourceEntries);
                }

                if (TryUseGroupedColumnsCache())
                {
                    ApplyGroupedColumnsProjection(_groupedColumnsProjectionCache!);
                    perf?.Mark("apply-presentation.columns-cache-hit", $"columns={_groupedEntryColumns.Count}");
                }
                else
                {
                    RebuildGroupedEntryColumns(sourceEntries);
                    perf?.Mark("apply-presentation.columns-rebuilt", $"columns={_groupedEntryColumns.Count}");
                }
                _ = DispatcherQueue.TryEnqueue(RefreshGroupedColumnsForViewport);
            }
            else
            {
                List<EntryViewModel> presentedEntries = BuildPresentedEntries(sourceEntries);
                perf?.Mark("apply-presentation.rows-built", $"count={presentedEntries.Count}");
                if (!MatchesCurrentVisibleEntries(presentedEntries))
                {
                    ReplaceVisibleEntries(presentedEntries);
                }

                _groupedEntryColumns.Clear();
            }

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                RestoreListSelectionByPath(ensureVisible: false);
            }

            UpdateEntrySelectionVisuals();
            UpdateViewCommandStates();
            perf?.Mark("apply-presentation.exit", $"visible={_entries.Count}");
        }

        private bool MatchesCurrentVisibleEntries(IReadOnlyList<EntryViewModel> entries)
        {
            if (_entries.Count != entries.Count)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!ReferenceEquals(_entries[i], entries[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void ReplaceVisibleEntries(IReadOnlyList<EntryViewModel> entries)
        {
            _entries.ReplaceAll(entries);
        }

        private void RebuildGroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries)
        {
            List<GroupedEntryColumnViewModel> projection = BuildGroupedEntryColumns(orderedEntries);
            _groupedColumnsProjectionCache = projection;
            ApplyGroupedColumnsProjection(projection);
            UpdateGroupedColumnsCacheStamp();
        }

        private void ApplyGroupedColumnsProjection(IReadOnlyList<GroupedEntryColumnViewModel> projection)
        {
            _groupedEntryColumns.Clear();
            foreach (GroupedEntryColumnViewModel column in projection)
            {
                _groupedEntryColumns.Add(column);
            }
        }

        private List<GroupedEntryColumnViewModel> BuildGroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries)
        {
            int rowsPerColumn = GetGroupedListRowsPerColumn();
            if (_currentGroupField == EntryGroupField.None)
            {
                return BuildUngroupedEntryColumns(orderedEntries, rowsPerColumn);
            }

            var buckets = new Dictionary<string, EntryGroupBucket>(StringComparer.OrdinalIgnoreCase);
            foreach (EntryViewModel entry in orderedEntries)
            {
                EntryGroupDescriptor descriptor = GetGroupDescriptor(entry);
                if (!buckets.TryGetValue(descriptor.BucketKey, out EntryGroupBucket? bucket))
                {
                    bucket = new EntryGroupBucket { Descriptor = descriptor };
                    buckets.Add(descriptor.BucketKey, bucket);
                }

                bucket.Items.Add(entry);
            }

            return buckets.Values
                .OrderBy(bucket => bucket.Descriptor.OrderKey, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(bucket => bucket.Descriptor.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select(bucket => CreateGroupedEntryColumn(bucket, rowsPerColumn))
                .ToList();
        }

        private List<GroupedEntryColumnViewModel> BuildUngroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries, int rowsPerColumn)
        {
            IReadOnlyList<EntryViewModel> items = orderedEntries.ToList();
            ApplyEntryViewState(items);

            return BuildGroupedEntryItemColumns(items, rowsPerColumn)
                .Select((column, index) => new GroupedEntryColumnViewModel
                {
                    GroupKey = $"list-column-{index}",
                    HeaderText = string.Empty,
                    ItemCount = column.Items.Count,
                    HeaderVisibility = Visibility.Collapsed,
                    ItemColumns = [column]
                })
                .ToList();
        }

        private GroupedEntryColumnViewModel CreateGroupedEntryColumn(EntryGroupBucket bucket, int rowsPerColumn)
        {
            IReadOnlyList<EntryViewModel> items = bucket.Items.ToList();
            ApplyEntryViewState(items);
            return new GroupedEntryColumnViewModel
            {
                GroupKey = bucket.Descriptor.StateKey,
                HeaderText = bucket.Descriptor.Label,
                ItemCount = bucket.Items.Count,
                ItemColumns = BuildGroupedEntryItemColumns(items, rowsPerColumn)
            };
        }

        private static IReadOnlyList<GroupedEntryItemColumnViewModel> BuildGroupedEntryItemColumns(IReadOnlyList<EntryViewModel> items, int rowsPerColumn)
        {
            int safeRowsPerColumn = Math.Max(1, rowsPerColumn);
            var columns = new List<GroupedEntryItemColumnViewModel>((items.Count + safeRowsPerColumn - 1) / safeRowsPerColumn);
            for (int i = 0; i < items.Count; i += safeRowsPerColumn)
            {
                columns.Add(new GroupedEntryItemColumnViewModel
                {
                    Items = items.Skip(i).Take(safeRowsPerColumn).ToList()
                });
            }

            return columns;
        }

        private int GetGroupedListRowsPerColumn()
        {
            double rowPitch = EntryItemMetrics.RowHeight + 4;
            double verticalPadding = GroupedEntriesScrollViewer.Padding.Top + GroupedEntriesScrollViewer.Padding.Bottom;
            double headerHeight = _currentGroupField == EntryGroupField.None ? 0 : EntryItemMetrics.GroupHeaderHeight;
            double viewportHeight = GroupedEntriesScrollViewer.ActualHeight;
            if (viewportHeight <= headerHeight)
            {
                viewportHeight = GroupedEntriesScrollViewer.ViewportHeight > 0
                    ? GroupedEntriesScrollViewer.ViewportHeight
                    : GroupedEntriesScrollViewer.ActualHeight;
            }

            if (viewportHeight <= headerHeight)
            {
                return 12;
            }

            double availableHeight = Math.Max(rowPitch, viewportHeight - verticalPadding - headerHeight);
            return Math.Max(1, (int)Math.Floor(availableHeight / rowPitch));
        }

        private void RefreshGroupedColumnsForViewport()
        {
            if (!UsesColumnsListPresentation())
            {
                return;
            }

            int rowsPerColumn = GetGroupedListRowsPerColumn();
            if (rowsPerColumn == _groupedListRowsPerColumn)
            {
                return;
            }

            _groupedListRowsPerColumn = rowsPerColumn;
            RebuildGroupedEntryColumns(_entries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList());
        }

        private bool TryApplyPresentationFastPath(PresentationReloadReason reason)
        {
            if (!CanApplyPresentationFastPath(reason))
            {
                return false;
            }

            EnsurePresentationSourceCacheFromCurrentEntries();
            ApplyCurrentPresentation();
            return true;
        }

        private bool CanApplyPresentationFastPath(PresentationReloadReason reason)
        {
            if (_presentationSourceEntries.Count > 0)
            {
                return true;
            }

            if (reason == PresentationReloadReason.DataRefresh)
            {
                return false;
            }

            if (_hasMore)
            {
                return false;
            }

            List<EntryViewModel> loadedEntries = GetLoadedEntriesFromCurrentCollection();
            if (loadedEntries.Count == 0)
            {
                return false;
            }

            return _totalEntries == 0 || loadedEntries.Count >= _totalEntries;
        }

        private void EnsurePresentationSourceCacheFromCurrentEntries()
        {
            if (_presentationSourceEntries.Count > 0)
            {
                return;
            }

            List<EntryViewModel> loadedEntries = GetLoadedEntriesFromCurrentCollection();
            if (loadedEntries.Count == 0)
            {
                return;
            }

            SetPresentationSourceEntries(loadedEntries);
        }

        private List<EntryViewModel> GetLoadedEntriesFromCurrentCollection()
        {
            List<EntryViewModel> loadedEntries = _entries
                .Where(entry => entry.IsLoaded && !entry.IsGroupHeader)
                .ToList();
            return loadedEntries;
        }

        private bool TryUseGroupedColumnsCache()
        {
            return _groupedColumnsCacheSourceVersion == _presentationSourceVersion
                && _groupedColumnsCacheSortField == _currentSortField
                && _groupedColumnsCacheSortDirection == _currentSortDirection
                && _groupedColumnsCacheGroupField == _currentGroupField
                && _groupedListRowsPerColumn > 0
                && _groupedColumnsCacheRowsPerColumn == _groupedListRowsPerColumn
                && _groupedColumnsProjectionCache is { Count: > 0 };
        }

        private int _groupedColumnsCacheRowsPerColumn = -1;

        private void UpdateGroupedColumnsCacheStamp()
        {
            _groupedColumnsCacheSourceVersion = _presentationSourceVersion;
            _groupedColumnsCacheSortField = _currentSortField;
            _groupedColumnsCacheSortDirection = _currentSortDirection;
            _groupedColumnsCacheGroupField = _currentGroupField;
            _groupedColumnsCacheRowsPerColumn = _groupedListRowsPerColumn;
        }

        private void InvalidateProjectionCaches()
        {
            _groupedColumnsCacheSourceVersion = -1;
            _groupedColumnsCacheRowsPerColumn = -1;
            _groupedColumnsProjectionCache = null;
        }

        private void InvalidatePresentationSourceCache()
        {
            _presentationSourceEntries.Clear();
            _presentationSourceVersion++;
            InvalidateProjectionCaches();
        }

        private void SetPresentationSourceEntries(IReadOnlyList<EntryViewModel> entries)
        {
            _presentationSourceEntries.Clear();
            _presentationSourceEntries.AddRange(entries);
            _presentationSourceVersion++;
            InvalidateProjectionCaches();
        }

        private List<EntryViewModel> BuildPresentedEntries(IReadOnlyList<EntryViewModel> orderedEntries)
        {
            if (_currentGroupField == EntryGroupField.None)
            {
                ApplyEntryViewState(orderedEntries);
                return orderedEntries.ToList();
            }

            var buckets = new Dictionary<string, EntryGroupBucket>(StringComparer.OrdinalIgnoreCase);
            foreach (EntryViewModel entry in orderedEntries)
            {
                EntryGroupDescriptor descriptor = GetGroupDescriptor(entry);
                if (!buckets.TryGetValue(descriptor.BucketKey, out EntryGroupBucket? bucket))
                {
                    bucket = new EntryGroupBucket { Descriptor = descriptor };
                    buckets.Add(descriptor.BucketKey, bucket);
                }

                bucket.Items.Add(entry);
            }

            List<EntryGroupBucket> orderedBuckets = buckets.Values
                .OrderBy(bucket => bucket.Descriptor.OrderKey, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(bucket => bucket.Descriptor.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var presentedEntries = new List<EntryViewModel>(orderedEntries.Count + orderedBuckets.Count);
            foreach (EntryGroupBucket bucket in orderedBuckets)
            {
                AppendGroup(presentedEntries, bucket.Descriptor, bucket.Items);
            }

            return presentedEntries;
        }

        private void AppendGroup(ICollection<EntryViewModel> presentedEntries, EntryGroupDescriptor descriptor, IReadOnlyList<EntryViewModel> groupEntries)
        {
            if (groupEntries.Count == 0)
            {
                return;
            }

            bool isExpanded = !_groupExpansionStates.TryGetValue(descriptor.StateKey, out bool expanded) || expanded;
            EntryViewModel headerEntry = EntryViewModel.CreateGroupHeader(descriptor.StateKey, descriptor.Label, groupEntries.Count, isExpanded);
            headerEntry.DetailsGroupHeaderMargin = presentedEntries.Count == 0 ? new Thickness(0) : new Thickness(0, 6, 0, 0);
            ApplyEntryLayoutState(headerEntry);
            presentedEntries.Add(headerEntry);

            if (!isExpanded)
            {
                return;
            }

            ApplyEntryViewState(groupEntries);
            foreach (EntryViewModel entry in groupEntries)
            {
                presentedEntries.Add(entry);
            }
        }

        private void ApplyEntryViewState(IReadOnlyList<EntryViewModel> entries)
        {
            foreach (EntryViewModel entry in entries)
            {
                entry.IsGroupHeader = false;
                entry.GroupKey = string.Empty;
                entry.GroupItemCount = 0;
                entry.IsGroupExpanded = false;
                entry.GroupHeaderText = string.Empty;
                ApplyEntryLayoutState(entry);
            }
        }

        private void ApplyEntryLayoutState(EntryViewModel entry)
        {
            entry.HeaderRowVisibility = entry.IsGroupHeader ? Visibility.Visible : Visibility.Collapsed;
            entry.DetailsRowVisibility = !entry.IsGroupHeader && _currentViewMode == EntryViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
            entry.ListRowVisibility = !entry.IsGroupHeader && _currentViewMode == EntryViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateEntrySelectionVisuals()
        {
            var seen = new HashSet<EntryViewModel>();
            IEnumerable<EntryViewModel> allEntries = _presentationSourceEntries.Concat(_entries);
            foreach (EntryViewModel entry in allEntries)
            {
                if (!seen.Add(entry))
                {
                    continue;
                }

                entry.IsExplicitlySelected = !entry.IsGroupHeader &&
                    !string.IsNullOrWhiteSpace(_selectedEntryPath) &&
                    string.Equals(entry.FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase);
                entry.IsKeyboardAnchor = !entry.IsGroupHeader &&
                    !string.IsNullOrWhiteSpace(_focusedEntryPath) &&
                    string.Equals(entry.FullPath, _focusedEntryPath, StringComparison.OrdinalIgnoreCase);
                entry.IsSelectionActive = _isEntriesSelectionActive;
            }
        }

        private int CompareEntriesForPresentation(EntryViewModel left, EntryViewModel right)
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            int result = _currentSortField switch
            {
                EntrySortField.ModifiedDate => Nullable.Compare(left.ModifiedAt, right.ModifiedAt),
                EntrySortField.Type => StringComparer.CurrentCultureIgnoreCase.Compare(left.Type, right.Type),
                EntrySortField.Size => Nullable.Compare(left.SizeBytes, right.SizeBytes),
                _ => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name)
            };

            if (result == 0 && _currentSortField != EntrySortField.Name)
            {
                result = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
            }

            if (_currentSortDirection == SortDirection.Descending)
            {
                result = -result;
            }

            if (result == 0)
            {
                result = StringComparer.CurrentCultureIgnoreCase.Compare(left.FullPath, right.FullPath);
            }

            return result;
        }

        private EntryGroupDescriptor GetGroupDescriptor(EntryViewModel entry)
        {
            return _currentGroupField switch
            {
                EntryGroupField.Name => GetNameGroupDescriptor(entry),
                EntryGroupField.Type => GetTypeGroupDescriptor(entry),
                EntryGroupField.ModifiedDate => GetModifiedDateGroupDescriptor(entry),
                _ => new EntryGroupDescriptor(string.Empty, string.Empty, string.Empty, string.Empty)
            };
        }

        private static EntryGroupDescriptor GetNameGroupDescriptor(EntryViewModel entry)
        {
            string label = string.IsNullOrWhiteSpace(entry.Name) ? "#" : char.ToUpperInvariant(entry.Name[0]).ToString();
            return new EntryGroupDescriptor($"name:{label}", $"name:{label}", label, label);
        }

        private static EntryGroupDescriptor GetTypeGroupDescriptor(EntryViewModel entry)
        {
            string label = string.IsNullOrWhiteSpace(entry.Type) ? "-" : entry.Type;
            string orderKey = entry.IsDirectory
                ? $"0000:{label}"
                : $"1000:{label}";
            return new EntryGroupDescriptor($"type:{label}", $"type:{label}", label, orderKey);
        }

        private static EntryGroupDescriptor GetModifiedDateGroupDescriptor(EntryViewModel entry)
        {
            string label = entry.ModifiedAt?.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? "-";
            return new EntryGroupDescriptor($"modified:{label}", $"modified:{label}", label, label);
        }

        private static string GetEntryTypeText(string name, bool isDirectory, bool isLink)
        {
            if (isDirectory)
            {
                return S(isLink ? "FileTypeFolderLink" : "FileTypeFolder");
            }

            if (isLink)
            {
                return S("FileTypeFileLink");
            }

            return GetFileTypeText(name);
        }

        private static string GetEntryIconGlyph(bool isDirectory, bool isLink, string? name = null)
        {
            EntryIconKind kind = GetEntryIconKind(name, isDirectory, isLink);
            return kind switch
            {
                EntryIconKind.Folder => "\uE8B7",
                EntryIconKind.FolderLink => "\uE8F0",
                EntryIconKind.File => "\uE8A5",
                EntryIconKind.FileLink => "\uE71B",
                EntryIconKind.Text => "\uF000",
                EntryIconKind.Archive => "\uF012",
                EntryIconKind.Image => "\uEB9F",
                EntryIconKind.Video => "\uEC0D",
                EntryIconKind.Audio => "\uEC4F",
                EntryIconKind.Pdf => "\uEA90",
                EntryIconKind.Word => "\uF1C2",
                EntryIconKind.Excel => "\uF1C3",
                EntryIconKind.PowerPoint => "\uF1C4",
                EntryIconKind.Code => "\uE943",
                EntryIconKind.Executable => "\uE756",
                EntryIconKind.Shortcut => "\uE71B",
                EntryIconKind.DiskImage => "\uE7F8",
                _ => "\uE8A5"
            };
        }

        private static Brush GetEntryIconBrush(bool isDirectory, bool isLink, string? name = null)
        {
            EntryIconKind kind = GetEntryIconKind(name, isDirectory, isLink);
            return kind switch
            {
                EntryIconKind.Folder => FolderIconBrush,
                EntryIconKind.FolderLink => FolderLinkIconBrush,
                EntryIconKind.File => FileIconBrush,
                EntryIconKind.FileLink => FileLinkIconBrush,
                EntryIconKind.Text => TextIconBrush,
                EntryIconKind.Archive => ArchiveIconBrush,
                EntryIconKind.Image => ImageIconBrush,
                EntryIconKind.Video => VideoIconBrush,
                EntryIconKind.Audio => AudioIconBrush,
                EntryIconKind.Pdf => PdfIconBrush,
                EntryIconKind.Word => WordIconBrush,
                EntryIconKind.Excel => ExcelIconBrush,
                EntryIconKind.PowerPoint => PowerPointIconBrush,
                EntryIconKind.Code => CodeIconBrush,
                EntryIconKind.Executable => ExecutableIconBrush,
                EntryIconKind.Shortcut => ShortcutIconBrush,
                EntryIconKind.DiskImage => DiskImageIconBrush,
                _ => FileIconBrush
            };
        }

        private static Brush CreateBrush(byte r, byte g, byte b) =>
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, r, g, b));

        private static EntryIconKind GetEntryIconKind(string? name, bool isDirectory, bool isLink)
        {
            if (isDirectory)
            {
                return isLink ? EntryIconKind.FolderLink : EntryIconKind.Folder;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return isLink ? EntryIconKind.FileLink : EntryIconKind.File;
            }

            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                return isLink ? EntryIconKind.FileLink : EntryIconKind.File;
            }

            if (ext is ".lnk" or ".url")
            {
                return EntryIconKind.Shortcut;
            }

            if (ext is ".txt" or ".log" or ".md" or ".ini" or ".cfg" or ".conf" or ".nfo")
            {
                return EntryIconKind.Text;
            }

            if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".cab")
            {
                return EntryIconKind.Archive;
            }

            if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".heic")
            {
                return EntryIconKind.Image;
            }

            if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v")
            {
                return EntryIconKind.Video;
            }

            if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma")
            {
                return EntryIconKind.Audio;
            }

            if (ext == ".pdf")
            {
                return EntryIconKind.Pdf;
            }

            if (ext is ".doc" or ".docx" or ".rtf" or ".odt")
            {
                return EntryIconKind.Word;
            }

            if (ext is ".xls" or ".xlsx" or ".csv" or ".ods")
            {
                return EntryIconKind.Excel;
            }

            if (ext is ".ppt" or ".pptx" or ".odp")
            {
                return EntryIconKind.PowerPoint;
            }

            if (ext is ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".com")
            {
                return EntryIconKind.Executable;
            }

            if (ext is ".iso" or ".img" or ".vhd" or ".vhdx")
            {
                return EntryIconKind.DiskImage;
            }

            if (ext is ".rs" or ".cs" or ".cpp" or ".c" or ".h" or ".hpp" or ".py" or ".js" or ".ts" or ".tsx"
                or ".jsx" or ".java" or ".kt" or ".go" or ".php" or ".swift" or ".json" or ".xml" or ".yaml"
                or ".yml" or ".toml" or ".md" or ".sql" or ".html" or ".css" or ".scss" or ".sh")
            {
                return EntryIconKind.Code;
            }

            return isLink ? EntryIconKind.FileLink : EntryIconKind.File;
        }

        private static string GetFileTypeText(string name)
        {
            string ext = Path.GetExtension(name);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return S("FileTypeGeneric");
            }

            return SF("FileTypeWithExtension", ext.TrimStart('.').ToUpperInvariant());
        }

        private static string GetFileSizeText(string fullPath)
        {
            return TryGetFileSize(fullPath).SizeText;
        }

        private static (long? SizeBytes, string SizeText) TryGetFileSize(string fullPath)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                if (!fi.Exists)
                {
                    return (null, "-");
                }

                return (fi.Length, FormatBytes(fi.Length));
            }
            catch
            {
                return (null, "-");
            }
        }

        private static string GetModifiedTimeText(string fullPath, bool isDirectory)
        {
            return FormatModifiedTime(TryGetModifiedTime(fullPath, isDirectory));
        }

        private static DateTime? TryGetModifiedTime(string fullPath, bool isDirectory)
        {
            try
            {
                DateTime dt = isDirectory
                    ? new DirectoryInfo(fullPath).LastWriteTime
                    : new FileInfo(fullPath).LastWriteTime;
                if (dt == DateTime.MinValue)
                {
                    return null;
                }

                return dt;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatModifiedTime(DateTime? modifiedAt)
        {
            return modifiedAt?.ToString("g", CultureInfo.CurrentCulture) ?? "-";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "-";
            }

            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:F1} {units[unit]}";
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit)
                {
                    return hit;
                }

                T? nested = FindDescendant<T>(child);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (T nested in FindDescendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed && string.Equals(typed.Name, name, StringComparison.Ordinal))
                {
                    return typed;
                }

                T? nested = FindDescendantByName<T>(child, name);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            DependencyObject? current = start;
            while (current is not null)
            {
                if (current is T hit)
                {
                    return hit;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private TreeViewItem? FindTreeViewItemForNode(TreeViewNode targetNode)
        {
            return _sidebarTreeView is null ? null : FindTreeViewItemForNode(_sidebarTreeView, targetNode);
        }

        private static TreeViewItem? FindTreeViewItemForNode(DependencyObject root, TreeViewNode targetNode)
        {
            if (root is TreeViewItem item &&
                (ReferenceEquals(item.DataContext, targetNode) || ReferenceEquals(item.Content, targetNode)))
            {
                return item;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                TreeViewItem? hit = FindTreeViewItemForNode(child, targetNode);
                if (hit is not null)
                {
                    return hit;
                }
            }

            return null;
        }

        private void SetPathInputInvalid()
        {
            PathTextBox.BorderBrush = new SolidColorBrush(Colors.IndianRed);
        }

        private void SetPathInputValid()
        {
            PathTextBox.BorderBrush = _pathDefaultBorderBrush;
        }

        private EntryViewModel CreateLocalCreatedEntryModel(string name, bool isDirectory)
        {
            return new EntryViewModel
            {
                Name = name,
                PendingName = name,
                FullPath = Path.Combine(_currentPath, name),
                Type = GetEntryTypeText(name, isDirectory, isLink: false),
                IconGlyph = GetEntryIconGlyph(isDirectory, isLink: false, name),
                IconForeground = GetEntryIconBrush(isDirectory, isLink: false, name),
                MftRef = 0,
                SizeText = isDirectory ? string.Empty : "0 B",
                ModifiedText = DateTime.Now.ToString("g", CultureInfo.CurrentCulture),
                IsDirectory = isDirectory,
                IsLink = false,
                IsLoaded = true,
                IsMetadataLoaded = true
            };
        }

        private EntryViewModel InsertLocalCreatedEntry(EntryViewModel entry, int insertIndex)
        {
            _entries.Insert(insertIndex, entry);
            _totalEntries++;
            _hasMore = _nextCursor < _totalEntries;
            _lastTitleWasReadFailed = false;
            UpdateWindowTitle();
            return entry;
        }

        private void SuppressNextWatcherRefresh(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _suppressedWatcherRefreshPaths.Add(path);
            }
        }

        private bool ConsumeSuppressedWatcherRefresh(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && _suppressedWatcherRefreshPaths.Remove(path);
        }

        private void SelectEntryInList(EntryViewModel entry, bool ensureVisible)
        {
            if (entry.IsGroupHeader)
            {
                return;
            }

            _selectedEntryPath = entry.FullPath;
            _focusedEntryPath = entry.FullPath;
            if (ensureVisible)
            {
                _ = DispatcherQueue.TryEnqueue(() => ScrollEntryIntoView(entry));
            }

            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private void RestoreListSelectionByPath(bool ensureVisible)
        {
            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return;
            }

            SelectEntryByPath(_selectedEntryPath, ensureVisible);
        }

        private void RestoreListSelectionByPathRespectingViewport()
        {
            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return;
            }

            ScrollViewer viewer = _currentViewMode == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;

            for (int i = 0; i < _entries.Count; i++)
            {
                EntryViewModel entry = _entries[i];
                if (!string.Equals(entry.FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool ensureVisible = !IsEntryFullyVisible(entry, viewer);
                SelectEntryInList(entry, ensureVisible);
                return;
            }
        }

        private void CaptureCurrentDirectoryViewState()
        {
            if (string.IsNullOrWhiteSpace(_currentPath) ||
                string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _directoryViewStates[_currentPath] = new DirectoryViewState
            {
                DetailsVerticalOffset = double.IsNaN(_lastDetailsVerticalOffset)
                    ? Math.Max(0, DetailsEntriesScrollViewer.VerticalOffset)
                    : Math.Max(0, _lastDetailsVerticalOffset),
                SelectedEntryPath = _selectedEntryPath,
            };
        }

        private bool RestoreHistoryViewStateIfPending()
        {
            string? path = _pendingHistoryStateRestorePath;
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _pendingHistoryStateRestorePath = null;
            if (!_directoryViewStates.TryGetValue(path, out DirectoryViewState? state))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(state.SelectedEntryPath))
            {
                _selectedEntryPath = state.SelectedEntryPath;
                RestoreListSelectionByPath(ensureVisible: false);
            }

            if (_currentViewMode == EntryViewMode.Details)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    DetailsEntriesScrollViewer.UpdateLayout();
                    double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                    DetailsEntriesScrollViewer.ChangeView(
                        null,
                        Math.Min(maxOffset, Math.Max(0, state.DetailsVerticalOffset)),
                        null,
                        disableAnimation: true);
                });
            }

            return true;
        }

        private void RestoreParentReturnAnchorIfPending()
        {
            string? targetPath = _pendingParentReturnAnchorPath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            _pendingParentReturnAnchorPath = null;
            for (int i = 0; i < _entries.Count; i++)
            {
                EntryViewModel entry = _entries[i];
                if (!string.Equals(entry.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SelectEntryInList(entry, ensureVisible: false);
                if (_currentViewMode == EntryViewMode.Details)
                {
                    _ = DispatcherQueue.TryEnqueue(() => ScrollEntryNearViewportBottom(i));
                }
                return;
            }
        }

        private void SelectEntryByPath(string targetPath, bool ensureVisible)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    SelectEntryInList(_entries[i], ensureVisible);
                    return;
                }
            }
        }

        private int GetCreateInsertIndex()
        {
            if (_currentViewMode == EntryViewMode.Details && _entries.Count > 0)
            {
                int visibleEnd = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
                return Math.Min(Math.Max(0, visibleEnd + 1), _entries.Count);
            }

            return _entries.Count;
        }

        private async Task EnsureCreateInsertVisibleAsync(int insertIndex)
        {
            if (_entries.Count == 0 || _currentViewMode != EntryViewMode.Details)
            {
                return;
            }

            await EnsureDataForViewportAsync(insertIndex, insertIndex, preferMinimalPage: false);
            DetailsEntriesScrollViewer.UpdateLayout();

            int visibleCount = Math.Max(1, (int)Math.Ceiling(DetailsEntriesScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int visibleEnd = Math.Min(_entries.Count - 1, visibleStart + visibleCount - 1);

            if (insertIndex >= visibleStart && insertIndex <= visibleEnd)
            {
                return;
            }

            if (insertIndex > visibleEnd)
            {
                double targetOffset = Math.Max(0, ((insertIndex + 1) * _estimatedItemHeight) - DetailsEntriesScrollViewer.ViewportHeight);
                DetailsEntriesScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            }
            else
            {
                double targetOffset = insertIndex * _estimatedItemHeight;
                DetailsEntriesScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            }
            await Task.Delay(16);
            DetailsEntriesScrollViewer.UpdateLayout();
        }

        private void ScrollEntryNearViewportBottom(int index)
        {
            if (_currentViewMode != EntryViewMode.Details || index < 0 || index >= _entries.Count)
            {
                return;
            }

            DetailsEntriesScrollViewer.UpdateLayout();
            double viewportHeight = DetailsEntriesScrollViewer.ViewportHeight > 0
                ? DetailsEntriesScrollViewer.ViewportHeight
                : DetailsEntriesScrollViewer.ActualHeight;
            double itemExtent = Math.Max(1.0, _estimatedItemHeight);
            double targetOffset = Math.Max(0, ((index + 1) * itemExtent) - viewportHeight);
            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, targetOffset), null, disableAnimation: true);
        }

        private bool IsIndexInCurrentViewport(int index)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return false;
            }

            if (_currentViewMode != EntryViewMode.Details)
            {
                return false;
            }

            DetailsEntriesScrollViewer.UpdateLayout();
            int visibleCount = Math.Max(1, (int)Math.Ceiling(DetailsEntriesScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int visibleEnd = Math.Min(_entries.Count - 1, visibleStart + visibleCount - 1);
            return index >= visibleStart && index <= visibleEnd;
        }

        private void ClearListSelection()
        {
            _selectedEntryPath = null;
            SyncActivePanelPresentationState();
            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private void ClearListSelectionAndAnchor()
        {
            _selectedEntryPath = null;
            _focusedEntryPath = null;
            SyncActivePanelPresentationState();
            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private void ClearExplicitSelectionKeepAnchor()
        {
            _selectedEntryPath = null;
            SyncActivePanelPresentationState();
            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private async Task<bool> StartRenameForCreatedEntryAsync(EntryViewModel entry, int insertIndex)
        {
            await Task.Delay(16);
            GetVisibleEntriesRoot().UpdateLayout();

            bool renameStarted = await BeginRenameOverlayAsync(entry, ensureVisible: false, updateSelection: false);
            if (renameStarted)
            {
                return true;
            }

            ScrollEntryIntoView(entry);
            await Task.Delay(16);
            GetVisibleEntriesRoot().UpdateLayout();
            bool retryStarted = await BeginRenameOverlayAsync(entry, ensureVisible: false, updateSelection: false);
            return retryStarted;
        }

        private int FindInsertIndexForEntry(EntryViewModel entry)
        {
            return _entriesPresentationBuilder.FindInsertIndex(_entries, entry, _currentSortMode);
        }

        private int CompareEntries(EntryViewModel left, EntryViewModel right)
        {
            return _entriesPresentationBuilder.Compare(left, right, _currentSortMode);
        }

        private async Task<bool> BeginRenameOverlayAsync(EntryViewModel entry, bool ensureVisible = true, bool updateSelection = true)
        {
            _inlineEditCoordinator.CancelActiveSession();
            HideRenameOverlay();
            bool alreadySelected =
                string.Equals(_selectedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

            if (updateSelection && !alreadySelected)
            {
                SelectEntryInList(entry, ensureVisible);
            }
            bool scrolledIntoViewForRetry = false;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(16);
                GetVisibleEntriesRoot().UpdateLayout();
                if (!TryPositionRenameOverlay(entry))
                {
                    if (!scrolledIntoViewForRetry && attempt >= 1)
                    {
                        scrolledIntoViewForRetry = true;
                        ScrollEntryIntoView(entry);
                    }
                    continue;
                }

                _activeRenameOverlayEntry = entry;
                RenameOverlayTextBox.Text = entry.Name;
                RenameOverlayBorder.Visibility = Visibility.Visible;
                _entriesRenameInlineSession ??= new InlineEditSession(
                    () => CommitRenameOverlayIfActiveAsync(),
                    () =>
                    {
                        HideRenameOverlay();
                        FocusEntriesList();
                    },
                    source => IsDescendantOf(source, RenameOverlayBorder));
                _inlineEditCoordinator.BeginSession(_entriesRenameInlineSession);
                RenameOverlayTextBox.Focus(FocusState.Programmatic);
                SelectRenameTargetText(RenameOverlayTextBox, entry);
                return true;
            }

            UpdateStatusKey("StatusRenameFailedCouldNotStartInlineEditor");
            return false;
        }

        private bool TryPositionRenameOverlay(EntryViewModel entry)
        {
            if (!TryGetEntryAnchor(entry, out EntryNameCell? anchor))
            {
                return false;
            }

            FrameworkElement textAnchor = anchor.NameTextElement;
            GeneralTransform cellTransform = anchor.TransformToVisual(RenameOverlayCanvas);
            Rect cellBounds = cellTransform.TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            GeneralTransform textTransform = textAnchor.TransformToVisual(RenameOverlayCanvas);
            Rect textBounds = textTransform.TransformBounds(new Rect(0, 0, textAnchor.ActualWidth, textAnchor.ActualHeight));
            if (cellBounds.Width <= 0 || cellBounds.Height <= 0 || textBounds.Width <= 0 || textBounds.Height <= 0)
            {
                return false;
            }

            double overlayHeight = RenameOverlayBorder.ActualHeight > 0
                ? RenameOverlayBorder.ActualHeight
                : (RenameOverlayBorder.Height > 0 ? RenameOverlayBorder.Height : textBounds.Height);

            double left = Math.Max(0, textBounds.X - 1);
            double top = Math.Max(0, cellBounds.Y + ((cellBounds.Height - overlayHeight) / 2));
            const double renameRightMargin = 12;
            const double renameWidthPadding = 24;
            const double renameMinWidth = 88;
            double canvasAvailableWidth = RenameOverlayCanvas.ActualWidth > 0
                ? RenameOverlayCanvas.ActualWidth - left - renameRightMargin
                : cellBounds.Right - left - renameRightMargin;
            double availableWidth = Math.Max(renameMinWidth, canvasAvailableWidth);
            double desiredWidth = textBounds.Width + renameWidthPadding;

            Canvas.SetLeft(RenameOverlayBorder, left);
            Canvas.SetTop(RenameOverlayBorder, top);
            RenameOverlayBorder.Width = Math.Min(availableWidth, Math.Max(renameMinWidth, desiredWidth));
            return true;
        }

        private void UpdateRenameOverlayPosition()
        {
            if (_activeRenameOverlayEntry is null)
            {
                return;
            }

            if (!TryPositionRenameOverlay(_activeRenameOverlayEntry))
            {
                HideRenameOverlay();
            }
        }

        private bool TryGetEntryAnchor<T>(EntryViewModel entry, [NotNullWhen(true)] out T? anchor) where T : FrameworkElement
        {
            anchor = null;
            if (entry is null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return false;
            }

            IEntriesViewHost? host = GetActiveEntriesViewHost();
            if (host is null)
            {
                return false;
            }

            GetVisibleEntriesRoot().UpdateLayout();

            FrameworkElement? found = typeof(T) == typeof(EntryNameCell)
                ? host.FindEntryNameCell(entry.FullPath)
                : host.FindEntryContainer(entry.FullPath);
            if (found is T typed)
            {
                anchor = typed;
                return true;
            }

            return false;
        }

        private void ScrollEntryIntoView(EntryViewModel entry)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return;
            }

            if (GetActiveEntriesViewHost()?.ScrollEntryIntoView(entry.FullPath) == true)
            {
                return;
            }
        }

        private void EntriesView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_inlineEditCoordinator.HasActiveSession)
            {
                return;
            }

            e.Handled = HandleEntriesNavigationKey(e.Key);
        }

        private bool HandleGlobalShortcutKey(Windows.System.VirtualKey key)
        {
            bool controlPressed = IsControlPressed();

            if (controlPressed)
            {
                switch (key)
                {
                    case Windows.System.VirtualKey.C:
                        if (CanCopySelectedEntry())
                        {
                            ExecuteCopy();
                            return true;
                        }
                        break;
                    case Windows.System.VirtualKey.X:
                        if (CanCutSelectedEntry())
                        {
                            ExecuteCut();
                            return true;
                        }
                        break;
                    case Windows.System.VirtualKey.V:
                        if (CanPasteIntoCurrentDirectory() && _fileManagementCoordinator.HasAvailablePasteItems())
                        {
                            _ = ExecutePasteAsync();
                            return true;
                        }
                        break;
                    case Windows.System.VirtualKey.L:
                        EnterAddressEditMode(selectAll: true);
                        return true;
                }
            }

            switch (key)
            {
                case Windows.System.VirtualKey.Delete:
                    if (CanDeleteSelectedEntry())
                    {
                        _ = ExecuteDeleteSelectedAsync();
                        return true;
                    }
                    break;
                case Windows.System.VirtualKey.F2:
                    if (TryHandleRenameShortcut())
                    {
                        return true;
                    }
                    break;
                case Windows.System.VirtualKey.F5:
                    if (!string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _ = RefreshCurrentDirectoryInBackgroundAsync();
                        return true;
                    }
                    break;
            }

            return false;
        }

        private bool TryHandleRenameShortcut()
        {
            if (IsSidebarTreeFocused() && TryGetSelectedSidebarTreeEntry(out SidebarTreeEntry? treeEntry))
            {
                _ = BeginSidebarTreeRenameAsync(treeEntry);
                return true;
            }

            if (CanRenameSelectedEntry())
            {
                _ = ExecuteRenameSelectedAsync();
                return true;
            }

            return false;
        }

        private static bool IsControlPressed()
        {
            Windows.UI.Core.CoreVirtualKeyStates state =
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        private bool IsSidebarTreeFocused()
        {
            if (_sidebarTreeView is null || Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            return IsDescendantOf(focused, _sidebarTreeView);
        }

        private bool TryGetSelectedSidebarTreeEntry([NotNullWhen(true)] out SidebarTreeEntry? entry)
        {
            entry = _sidebarTreeView?.SelectedNode?.Content as SidebarTreeEntry;
            if (entry is null || string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                entry = null;
                return false;
            }

            string root = Path.GetPathRoot(entry.FullPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(root.TrimEnd('\\'), entry.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                entry = null;
                return false;
            }

            return true;
        }

        private static bool IsTextInputSource(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is TextBox or AutoSuggestBox or PasswordBox or RichEditBox)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private bool HandleEntriesNavigationKey(Windows.System.VirtualKey key)
        {
            switch (key)
            {
                case Windows.System.VirtualKey.Up:
                    if (!TryMoveSelectionVertically(-1))
                    {
                        TryMoveSelectionBy(-1);
                    }
                    return true;
                case Windows.System.VirtualKey.Down:
                    if (!TryMoveSelectionVertically(1))
                    {
                        TryMoveSelectionBy(1);
                    }
                    return true;
                case Windows.System.VirtualKey.Left:
                    if (_currentViewMode == EntryViewMode.List)
                    {
                        TryMoveSelectionHorizontally(-1);
                        return true;
                    }
                    return false;
                case Windows.System.VirtualKey.Right:
                    if (_currentViewMode == EntryViewMode.List)
                    {
                        TryMoveSelectionHorizontally(1);
                        return true;
                    }
                    return false;
                case Windows.System.VirtualKey.Home:
                    if (_currentViewMode == EntryViewMode.List)
                    {
                        TrySelectBoundaryEntry(first: true);
                    }
                    else
                    {
                        TrySelectBoundaryEntry(first: true);
                    }
                    return true;
                case Windows.System.VirtualKey.End:
                    if (_currentViewMode == EntryViewMode.List)
                    {
                        TrySelectBoundaryEntry(first: false);
                    }
                    else
                    {
                        TrySelectBoundaryEntry(first: false);
                    }
                    return true;
                case Windows.System.VirtualKey.PageUp:
                    TryMoveSelectionByPage(-1);
                    return true;
                case Windows.System.VirtualKey.PageDown:
                    TryMoveSelectionByPage(1);
                    return true;
                case Windows.System.VirtualKey.Enter:
                    TryActivateSelectedEntry();
                    return true;
                default:
                    return false;
            }
        }

        private bool TryMoveSelectionBy(int delta)
        {
            List<EntryViewModel> selectableEntries = GetSelectableEntriesInPresentationOrder();
            if (selectableEntries.Count == 0)
            {
                return false;
            }

            int currentIndex = GetSelectedPresentedEntryIndex(selectableEntries);
            int targetIndex = currentIndex < 0
                ? (delta >= 0 ? 0 : selectableEntries.Count - 1)
                : Math.Clamp(currentIndex + delta, 0, selectableEntries.Count - 1);

            SelectEntryFromKeyboard(selectableEntries[targetIndex]);
            return true;
        }

        private bool TryMoveSelectionVertically(int delta)
        {
            if (_currentViewMode != EntryViewMode.List)
            {
                return false;
            }

            return TryMoveSelectionInListColumns(0, delta);
        }

        private bool TryMoveSelectionHorizontally(int delta)
        {
            if (_currentViewMode != EntryViewMode.List)
            {
                return false;
            }

            return TryMoveSelectionInListColumns(delta, 0);
        }

        private bool TryMoveSelectionInListColumns(int columnDelta, int rowDelta)
        {
            List<IReadOnlyList<EntryViewModel>> columns = GetListNavigationColumns();
            if (columns.Count == 0)
            {
                return false;
            }

            if (!TryGetSelectedListColumnPosition(columns, out int currentColumnIndex, out int currentRowIndex))
            {
                IReadOnlyList<EntryViewModel> edgeColumn = columnDelta < 0 || rowDelta < 0
                    ? columns[^1]
                    : columns[0];
                if (edgeColumn.Count == 0)
                {
                    return false;
                }

                EntryViewModel initialEntry = columnDelta < 0 || rowDelta < 0
                    ? edgeColumn[^1]
                    : edgeColumn[0];
                SelectEntryFromKeyboard(initialEntry);
                return true;
            }

            int targetColumnIndex = Math.Clamp(currentColumnIndex + columnDelta, 0, columns.Count - 1);
            IReadOnlyList<EntryViewModel> targetColumn = columns[targetColumnIndex];
            if (targetColumn.Count == 0)
            {
                return false;
            }

            int targetRowIndex = columnDelta == 0
                ? Math.Clamp(currentRowIndex + rowDelta, 0, targetColumn.Count - 1)
                : Math.Min(currentRowIndex, targetColumn.Count - 1);

            SelectEntryFromKeyboard(targetColumn[targetRowIndex]);
            return true;
        }

        private bool TryGetSelectedListColumnPosition(
            IReadOnlyList<IReadOnlyList<EntryViewModel>> columns,
            out int columnIndex,
            out int rowIndex)
        {
            columnIndex = -1;
            rowIndex = -1;

            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return false;
            }

            for (int c = 0; c < columns.Count; c++)
            {
                IReadOnlyList<EntryViewModel> column = columns[c];
                for (int r = 0; r < column.Count; r++)
                {
                    if (string.Equals(column[r].FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        columnIndex = c;
                        rowIndex = r;
                        return true;
                    }
                }
            }

            return false;
        }

        private List<IReadOnlyList<EntryViewModel>> GetListNavigationColumns()
        {
            var columns = new List<IReadOnlyList<EntryViewModel>>();
            foreach (GroupedEntryColumnViewModel groupColumn in _groupedEntryColumns)
            {
                foreach (GroupedEntryItemColumnViewModel itemColumn in groupColumn.ItemColumns)
                {
                    List<EntryViewModel> items = itemColumn.Items
                        .Where(entry => entry.IsLoaded && !entry.IsGroupHeader)
                        .ToList();
                    if (items.Count > 0)
                    {
                        columns.Add(items);
                    }
                }
            }

            if (columns.Count > 0)
            {
                return columns;
            }

            return GetSelectableEntriesInPresentationOrder()
                .Chunk(Math.Max(1, GetGroupedListRowsPerColumn()))
                .Select(chunk => (IReadOnlyList<EntryViewModel>)chunk.ToList())
                .ToList();
        }

        private bool TrySelectBoundaryEntry(bool first)
        {
            List<EntryViewModel> selectableEntries = _currentViewMode == EntryViewMode.List
                ? GetListNavigationColumns()
                    .SelectMany(column => column)
                    .ToList()
                : GetSelectableEntriesInPresentationOrder();
            if (selectableEntries.Count == 0)
            {
                return false;
            }

            SelectEntryFromKeyboard(first ? selectableEntries[0] : selectableEntries[^1]);
            return true;
        }

        private bool TrySelectListColumnBoundary(bool first)
        {
            List<IReadOnlyList<EntryViewModel>> columns = GetListNavigationColumns();
            if (columns.Count == 0)
            {
                return false;
            }

            if (!TryGetSelectedListColumnPosition(columns, out int currentColumnIndex, out _))
            {
                IReadOnlyList<EntryViewModel> edgeColumn = first ? columns[0] : columns[^1];
                if (edgeColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(first ? edgeColumn[0] : edgeColumn[^1]);
                return true;
            }

            IReadOnlyList<EntryViewModel> currentColumn = columns[currentColumnIndex];
            if (currentColumn.Count == 0)
            {
                return false;
            }

            SelectEntryFromKeyboard(first ? currentColumn[0] : currentColumn[^1]);
            return true;
        }

        private bool TryMoveSelectionByPage(int direction)
        {
            if (_currentViewMode == EntryViewMode.List)
            {
                return TryMoveSelectionByPageInListColumns(direction);
            }

            int step = Math.Max(1, GetKeyboardPageStep());
            return TryMoveSelectionBy(direction * step);
        }

        private int GetKeyboardPageStep()
        {
            if (_currentViewMode == EntryViewMode.List)
            {
                return GetGroupedListRowsPerColumn();
            }

            double viewportHeight = DetailsEntriesScrollViewer.ViewportHeight > 0
                ? DetailsEntriesScrollViewer.ViewportHeight
                : DetailsEntriesScrollViewer.ActualHeight;
            if (viewportHeight <= 0 || _estimatedItemHeight <= 0)
            {
                return 8;
            }

            return Math.Max(1, (int)Math.Floor(viewportHeight / _estimatedItemHeight));
        }

        private bool TryMoveSelectionByPageInListColumns(int direction)
        {
            List<IReadOnlyList<EntryViewModel>> columns = GetListNavigationColumns();
            if (columns.Count == 0)
            {
                return false;
            }

            if (!TryGetSelectedListColumnPosition(columns, out int currentColumnIndex, out int currentRowIndex))
            {
                IReadOnlyList<EntryViewModel> edgeColumn = direction < 0 ? columns[0] : columns[^1];
                if (edgeColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(direction < 0 ? edgeColumn[0] : edgeColumn[^1]);
                return true;
            }

            int targetColumnIndex = currentColumnIndex + direction;
            if (targetColumnIndex < 0)
            {
                IReadOnlyList<EntryViewModel> currentColumn = columns[currentColumnIndex];
                if (currentColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(currentColumn[0]);
                return true;
            }

            if (targetColumnIndex >= columns.Count)
            {
                IReadOnlyList<EntryViewModel> currentColumn = columns[currentColumnIndex];
                if (currentColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(currentColumn[^1]);
                return true;
            }

            IReadOnlyList<EntryViewModel> targetColumn = columns[targetColumnIndex];
            if (targetColumn.Count == 0)
            {
                return false;
            }

            int targetRowIndex = currentRowIndex < targetColumn.Count
                ? currentRowIndex
                : targetColumn.Count - 1;

            SelectEntryFromKeyboard(targetColumn[targetRowIndex]);
            return true;
        }

        private void SelectEntryFromKeyboard(EntryViewModel entry)
        {
            ScrollViewer viewer = _currentViewMode == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;
            double originalHorizontalOffset = viewer.HorizontalOffset;
            double originalVerticalOffset = viewer.VerticalOffset;

            bool wasVisible = IsEntryFullyVisible(entry, viewer);
            SelectEntryInList(entry, ensureVisible: false);
            viewer.ChangeView(originalHorizontalOffset, originalVerticalOffset, null, disableAnimation: true);

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (wasVisible)
                {
                    viewer.ChangeView(originalHorizontalOffset, originalVerticalOffset, null, disableAnimation: true);
                    return;
                }

                ScrollEntryIntoView(entry);
            });
        }

        private bool IsEntryFullyVisible(EntryViewModel entry, ScrollViewer viewer)
        {
            if (!TryGetEntryAnchor<FrameworkElement>(entry, out FrameworkElement? element) ||
                viewer.Content is not UIElement content)
            {
                return false;
            }

            GeneralTransform transform = element.TransformToVisual(content);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (_currentViewMode == EntryViewMode.Details)
            {
                double viewportTop = viewer.VerticalOffset;
                double viewportBottom = viewportTop + viewer.ViewportHeight;
                return bounds.Y >= viewportTop && (bounds.Y + bounds.Height) <= viewportBottom;
            }

            double viewportLeft = viewer.HorizontalOffset;
            double viewportRight = viewportLeft + viewer.ViewportWidth;
            return bounds.X >= viewportLeft && (bounds.X + bounds.Width) <= viewportRight;
        }

        private List<EntryViewModel> GetSelectableEntriesInPresentationOrder()
        {
            return _entries
                .Where(entry => !entry.IsGroupHeader && entry.IsLoaded)
                .ToList();
        }

        private int GetSelectedPresentedEntryIndex(IReadOnlyList<EntryViewModel> entries)
        {
            string? activePath = !string.IsNullOrWhiteSpace(_selectedEntryPath)
                ? _selectedEntryPath
                : _focusedEntryPath;
            if (string.IsNullOrWhiteSpace(activePath))
            {
                return -1;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].FullPath, activePath, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryActivateSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                return false;
            }

            _ = ActivateEntryAsync(entry);
            return true;
        }

        private async Task ActivateEntryAsync(EntryViewModel row)
        {
            if (row is null || !row.IsLoaded)
            {
                return;
            }

            await OpenEntryAsync(row, clearSelectionBeforeDirectoryNavigation: true);
        }

        private async Task CommitRenameOverlayAsync()
        {
            if (_isCommittingRenameOverlay || _activeRenameOverlayEntry is null)
            {
                return;
            }

            _isCommittingRenameOverlay = true;
            try
            {
                EntryViewModel entry = _activeRenameOverlayEntry;
                string proposedName = RenameOverlayTextBox.Text?.Trim() ?? string.Empty;
                int index = _entries.IndexOf(entry);

                if (entry.IsPendingCreate)
                {
                    if (string.IsNullOrWhiteSpace(proposedName))
                    {
                        CancelPendingCreateEntry(index);
                        HideRenameOverlay();
                        FocusEntriesList();
                        return;
                    }

                    if (!_fileManagementCoordinator.TryValidateName(_currentPath, entry.Name, proposedName, out string pendingValidationError))
                    {
                        UpdateStatus(pendingValidationError);
                        RenameOverlayTextBox.Focus(FocusState.Programmatic);
                        SelectRenameTargetText(RenameOverlayTextBox, entry);
                        return;
                    }

                    await CommitPendingCreateAsync(entry, index, proposedName);
                    return;
                }

                if (string.Equals(proposedName, entry.Name, StringComparison.Ordinal))
                {
                    HideRenameOverlay();
                    if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                    {
                        CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                    }
                    else
                    {
                        _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                    }
                    return;
                }

                if (!_fileManagementCoordinator.TryValidateName(_currentPath, entry.Name, proposedName, out string validationError))
                {
                    UpdateStatus(validationError);
                    RenameOverlayTextBox.Focus(FocusState.Programmatic);
                    SelectRenameTargetText(RenameOverlayTextBox, entry);
                    return;
                }

                if (index < 0)
                {
                    HideRenameOverlay();
                    return;
                }

                await RenameEntryAsync(entry, index, proposedName);
            }
            finally
            {
                _isCommittingRenameOverlay = false;
            }
        }

        private async Task CommitRenameOverlayIfActiveAsync(bool focusEntriesList = true)
        {
            if (RenameOverlayBorder.Visibility != Visibility.Visible || _activeRenameOverlayEntry is null)
            {
                return;
            }

            await CommitRenameOverlayAsync();

            if (focusEntriesList && RenameOverlayBorder.Visibility != Visibility.Visible)
            {
                FocusEntriesList();
            }
        }

        private async Task CommitPendingCreateAsync(EntryViewModel entry, int index, string proposedName)
        {
            string targetPath = Path.Combine(_currentPath, proposedName);
            try
            {
                if (entry.PendingCreateIsDirectory)
                {
                    await _explorerService.CreateDirectoryAsync(targetPath);
                }
                else
                {
                    await _explorerService.CreateEmptyFileAsync(targetPath);
                }

                try
                {
                    _explorerService.MarkPathChanged(_currentPath);
                }
                catch
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, entry.PendingCreateIsDirectory ? "create-folder" : "create-file");
                }

                HideRenameOverlay();

                if (index < 0 || index >= _entries.Count)
                {
                    return;
                }

                entry.Name = proposedName;
                entry.PendingName = proposedName;
                entry.FullPath = targetPath;
                entry.Type = GetEntryTypeText(proposedName, entry.PendingCreateIsDirectory, isLink: false);
                entry.IconGlyph = GetEntryIconGlyph(entry.PendingCreateIsDirectory, isLink: false, proposedName);
                entry.IconForeground = GetEntryIconBrush(entry.PendingCreateIsDirectory, isLink: false, proposedName);
                entry.SizeText = entry.PendingCreateIsDirectory ? string.Empty : "0 B";
                entry.ModifiedText = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
                entry.IsPendingCreate = false;
                entry.MftRef = 0;
                entry.IsLoaded = true;
                entry.IsMetadataLoaded = true;
                InvalidatePresentationSourceCache();

                if (entry.PendingCreateIsDirectory && FindSidebarTreeNodeByPath(_currentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                {
                    await PopulateSidebarTreeChildrenAsync(parentNode, _currentPath, CancellationToken.None, expandAfterLoad: true);
                }

                UpdateStatusKey("StatusCreateSuccess", proposedName);
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    SelectEntryInList(entry, ensureVisible: true);
                    FocusEntriesList();
                });
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCreateFailed", FileOperationErrors.ToUserMessage(ex));
                RenameOverlayTextBox.Focus(FocusState.Programmatic);
                SelectRenameTargetText(RenameOverlayTextBox, entry);
            }
        }

        private void CancelPendingCreateEntry(int index)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return;
            }

            _entries.RemoveAt(index);
            InvalidatePresentationSourceCache();
        }

        private void HideRenameOverlay()
        {
            if (_entriesRenameInlineSession is not null)
            {
                _inlineEditCoordinator.ClearSession(_entriesRenameInlineSession);
            }
            _activeRenameOverlayEntry = null;
            RenameOverlayBorder.Visibility = Visibility.Collapsed;
        }

        private bool IsFocusedElementWithinRenameOverlay()
        {
            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focused is not null)
            {
                if (ReferenceEquals(focused, RenameOverlayBorder) || ReferenceEquals(focused, RenameOverlayTextBox))
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }

        private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void FocusEntriesList()
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            GetVisibleEntriesRoot().Focus(FocusState.Programmatic);
        }

        private static void SelectRenameTargetText(TextBox textBox, EntryViewModel entry)
        {
            string text = textBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (entry.IsDirectory)
            {
                textBox.SelectAll();
                return;
            }

            int extensionStart = text.LastIndexOf('.');
            if (extensionStart > 0)
            {
                textBox.SelectionStart = 0;
                textBox.SelectionLength = extensionStart;
                return;
            }

            textBox.SelectAll();
        }

        private void CompleteCreatedEntrySelectionIfPending(EntryViewModel entry, bool ensureVisible)
        {
            if (!ReferenceEquals(_pendingCreatedEntrySelection, entry))
            {
                return;
            }

            _pendingCreatedEntrySelection = null;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                SelectEntryInList(entry, ensureVisible);
                FocusEntriesList();
            });
        }

        private void FocusSidebarTree()
        {
            _sidebarTreeView?.Focus(FocusState.Pointer);
        }

        private void FocusSidebarSurface()
        {
            SetActiveSelectionSurface(SelectionSurfaceId.Sidebar);
            if (!StyledSidebarView.Focus(FocusState.Pointer))
            {
                SidebarNavView?.Focus(FocusState.Pointer);
            }
        }

        private void SetActiveSelectionSurface(SelectionSurfaceId surface)
        {
            _selectionSurfaceCoordinator.SetActiveSurface(surface);
            UpdateSelectionActivityState();
        }

        private void UpdateSelectionActivityState()
        {
            bool entriesSelectionActive = _selectionSurfaceCoordinator.IsSurfaceActive(SelectionSurfaceId.PrimaryPane);
            bool sidebarSelectionActive = _selectionSurfaceCoordinator.IsSurfaceActive(SelectionSurfaceId.Sidebar);

            StyledSidebarView.SetSelectionActive(sidebarSelectionActive);
            if (_isSidebarSelectionActive != sidebarSelectionActive)
            {
                _isSidebarSelectionActive = sidebarSelectionActive;
                RefreshSidebarTreeSelectionVisuals();
            }

            if (_isEntriesSelectionActive == entriesSelectionActive)
            {
                return;
            }

            _isEntriesSelectionActive = entriesSelectionActive;
            UpdateEntrySelectionVisuals();
        }

        private void RefreshSidebarTreeSelectionVisuals()
        {
            if (_sidebarTreeView?.SelectedNode is not TreeViewNode selectedNode)
            {
                return;
            }

            _suppressSidebarTreeSelection = true;
            _sidebarTreeView.SelectedNode = null;
            _sidebarTreeView.SelectedNode = selectedNode;
            _suppressSidebarTreeSelection = false;
        }

        private async Task BeginSidebarTreeRenameAsync(SidebarTreeEntry entry)
        {
            if (_sidebarTreeView is null)
            {
                return;
            }

            _inlineEditCoordinator.CancelActiveSession();

            TreeViewNode? node = FindSidebarTreeNodeByPath(entry.FullPath);
            TreeViewItem? item = node is not null ? FindTreeViewItemForNode(node) : null;
            if (item is null)
            {
                UpdateStatusKey("StatusRenameFailedTreeItemUnavailable");
                return;
            }

            EnsureSidebarTreeRenameOverlay();
            if (_sidebarTreeRenameOverlayCanvas is null || _sidebarTreeRenameOverlayBorder is null || _sidebarTreeRenameTextBox is null)
            {
                UpdateStatusKey("StatusRenameFailedTreeOverlayUnavailable");
                return;
            }

            _activeSidebarTreeRenameEntry = entry;
            _sidebarTreeRenameTextBox!.Text = entry.Name;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(16);
                item.UpdateLayout();
                _sidebarTreeView.UpdateLayout();
                _sidebarTreeRenameOverlayCanvas.UpdateLayout();

                if (FindDescendantByName<TextBlock>(item, "SidebarTreeNameTextBlock") is not TextBlock anchor)
                {
                    continue;
                }

                GeneralTransform transform = anchor.TransformToVisual(_sidebarTreeRenameOverlayCanvas);
                Rect bounds = transform.TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    continue;
                }

                double overlayHeight = _sidebarTreeRenameOverlayBorder!.ActualHeight > 0
                    ? _sidebarTreeRenameOverlayBorder.ActualHeight
                    : (_sidebarTreeRenameOverlayBorder.Height > 0 ? _sidebarTreeRenameOverlayBorder.Height : bounds.Height);
                double left = Math.Max(0, bounds.X + SidebarTreeRenameOffsetX);
                double targetWidth = Math.Max(SidebarTreeRenameMinWidth, bounds.Width + SidebarTreeRenameWidthPadding);
                if (_sidebarTreeRenameOverlayCanvas.ActualWidth > 0)
                {
                    double availableWidth = Math.Max(0, _sidebarTreeRenameOverlayCanvas.ActualWidth - left - SidebarTreeRenameRightMargin);
                    if (availableWidth <= 0)
                    {
                        continue;
                    }

                    targetWidth = Math.Min(targetWidth, availableWidth);
                }

                Canvas.SetLeft(_sidebarTreeRenameOverlayBorder, left);
                Canvas.SetTop(_sidebarTreeRenameOverlayBorder, Math.Max(0, bounds.Y + ((bounds.Height - overlayHeight) / 2) + SidebarTreeRenameOffsetY));
                _sidebarTreeRenameOverlayBorder.Width = targetWidth;
                _sidebarTreeRenameOverlayBorder.Visibility = Visibility.Visible;
                _sidebarTreeRenameInlineSession ??= new InlineEditSession(
                    CommitSidebarTreeRenameIfActiveAsync,
                    CancelSidebarTreeRename,
                    source => _sidebarTreeRenameOverlayBorder is not null &&
                        IsDescendantOf(source, _sidebarTreeRenameOverlayBorder));
                _inlineEditCoordinator.BeginSession(_sidebarTreeRenameInlineSession);
                _sidebarTreeRenameTextBox.Focus(FocusState.Programmatic);
                _sidebarTreeRenameTextBox.SelectAll();
                return;
            }

            UpdateStatusKey("StatusRenameFailedTreeTextAnchorUnavailable");
        }

        private void EnsureSidebarTreeRenameOverlay()
        {
            if (_sidebarTreeRenameOverlayCanvas is not null && _sidebarTreeRenameOverlayBorder is not null && _sidebarTreeRenameTextBox is not null)
            {
                return;
            }

            if (_sidebarTreeView?.Parent is not Panel hostPanel)
            {
                return;
            }

            _sidebarTreeRenameOverlayCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Visible
            };
            Canvas.SetZIndex(_sidebarTreeRenameOverlayCanvas, 20);

            _sidebarTreeRenameTextBox = new TextBox
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                FontSize = 14,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, -1, 0, 0),
                MinHeight = 24,
                Padding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            if (GetRenameOverlayTextBoxTemplate() is ControlTemplate renameOverlayTextBoxTemplate)
            {
                _sidebarTreeRenameTextBox.Template = renameOverlayTextBoxTemplate;
            }

            _sidebarTreeRenameTextBox.Resources["TextControlBackground"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBackgroundPointerOver"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBackgroundFocused"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBorderBrush"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.KeyDown += SidebarTreeRenameTextBox_KeyDown;
            _sidebarTreeRenameTextBox.LostFocus += SidebarTreeRenameTextBox_LostFocus;

            Brush selectionBrush = (Brush)Application.Current.Resources["ListViewItemSelectionIndicatorBrush"];
            _sidebarTreeRenameOverlayBorder = new Border
            {
                Width = 160,
                Height = 24,
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = selectionBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Padding = new Thickness(1, 0, 0, 1),
                Child = _sidebarTreeRenameTextBox,
                Visibility = Visibility.Collapsed
            };

            _sidebarTreeRenameOverlayCanvas.Children.Add(_sidebarTreeRenameOverlayBorder);
            hostPanel.Children.Add(_sidebarTreeRenameOverlayCanvas);
        }

        private async void SidebarTreeRenameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await CommitSidebarTreeRenameAsync();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CancelSidebarTreeRename();
            }
        }

        private async void SidebarTreeRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await Task.Yield();
            if (IsFocusedElementWithinSidebarTreeRenameFlyout())
            {
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private bool IsFocusedElementWithinSidebarTreeRenameFlyout()
        {
            if (_sidebarTreeRenameTextBox is null || this.Content is not FrameworkElement root)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focused is not null)
            {
                if (ReferenceEquals(focused, _sidebarTreeRenameTextBox))
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }

        private async Task CommitSidebarTreeRenameAsync()
        {
            if (_isCommittingSidebarTreeRename || _activeSidebarTreeRenameEntry is null || _sidebarTreeRenameTextBox is null)
            {
                return;
            }

            _isCommittingSidebarTreeRename = true;
            try
            {
                SidebarTreeEntry entry = _activeSidebarTreeRenameEntry;
                string newName = _sidebarTreeRenameTextBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, entry.Name, StringComparison.Ordinal))
                {
                    CancelSidebarTreeRename();
                    return;
                }

                if (!TryValidateTreeRename(entry, newName, out string validationError))
                {
                    UpdateStatus(validationError);
                    await Task.Yield();
                    _sidebarTreeRenameTextBox.Focus(FocusState.Programmatic);
                    _sidebarTreeRenameTextBox.SelectAll();
                    return;
                }

                CancelSidebarTreeRename();
                await RenameSidebarTreeEntryAsync(entry, newName);
            }
            finally
            {
                _isCommittingSidebarTreeRename = false;
            }
        }

        private Task CommitSidebarTreeRenameIfActiveAsync()
        {
            if (_activeSidebarTreeRenameEntry is null || _sidebarTreeRenameOverlayBorder?.Visibility != Visibility.Visible)
            {
                return Task.CompletedTask;
            }

            return CommitSidebarTreeRenameAsync();
        }

        private void CancelSidebarTreeRename()
        {
            if (_sidebarTreeRenameInlineSession is not null)
            {
                _inlineEditCoordinator.ClearSession(_sidebarTreeRenameInlineSession);
            }
            _activeSidebarTreeRenameEntry = null;
            if (_sidebarTreeRenameOverlayBorder is not null)
            {
                _sidebarTreeRenameOverlayBorder.Visibility = Visibility.Collapsed;
            }
            _ = DispatcherQueue.TryEnqueue(FocusSidebarSurface);
        }

        private void ApplyLocalRename(int index, string newName)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return;
            }

            EntryViewModel current = _entries[index];
            current.Name = newName;
            current.PendingName = newName;
            current.FullPath = Path.Combine(_currentPath, newName);
            current.SizeText = string.Empty;
            current.ModifiedText = string.Empty;
            current.IsLoaded = true;
            current.IsMetadataLoaded = false;
            InvalidatePresentationSourceCache();
            RequestMetadataForCurrentViewport();
        }

        private void UpdateListEntryNameForCurrentDirectory(string sourcePath, string newName)
        {
            string parentPath = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            if (!string.Equals(parentPath, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int index = -1;
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].FullPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            EntryViewModel current = _entries[index];
            bool wasSelected = string.Equals(_selectedEntryPath, current.FullPath, StringComparison.OrdinalIgnoreCase);
            current.Name = newName;
            current.PendingName = newName;
            current.FullPath = Path.Combine(_currentPath, newName);
            InvalidatePresentationSourceCache();

            if (wasSelected)
            {
                _ = DispatcherQueue.TryEnqueue(() => SelectEntryInList(current, ensureVisible: true));
            }
        }

        private void ApplyLocalDelete(int index)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return;
            }

            if (string.Equals(_selectedEntryPath, _entries[index].FullPath, StringComparison.OrdinalIgnoreCase))
            {
                _selectedEntryPath = null;
            }
            if (string.Equals(_focusedEntryPath, _entries[index].FullPath, StringComparison.OrdinalIgnoreCase))
            {
                _focusedEntryPath = null;
            }

            _entries.RemoveAt(index);
            if (_totalEntries > 0)
            {
                _totalEntries--;
            }
            if (_entries.Count > _totalEntries)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }
            _hasMore = _nextCursor < _totalEntries;
            InvalidatePresentationSourceCache();
            UpdateFileCommandStates();
        }

        private async Task RefreshCurrentDirectoryInBackgroundAsync(bool preserveViewport = false)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                PopulateMyComputerEntries();
                return;
            }

            double detailsVerticalOffset = DetailsEntriesScrollViewer.VerticalOffset;
            double groupedHorizontalOffset = GroupedEntriesScrollViewer.HorizontalOffset;

            try
            {
                UpdateUsnCapability(_currentPath);
                ConfigureDirectoryWatcher(_currentPath);
                EnsureRefreshFallbackInvalidation(_currentPath, "background_refresh");
                await LoadPageAsync(_currentPath, cursor: 0, append: false);
                if (preserveViewport)
                {
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_currentViewMode == EntryViewMode.Details)
                        {
                            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, detailsVerticalOffset), null, disableAnimation: true);
                        }
                        else
                        {
                            double maxOffset = Math.Max(0, GroupedEntriesScrollViewer.ScrollableWidth);
                            GroupedEntriesScrollViewer.ChangeView(Math.Min(maxOffset, groupedHorizontalOffset), null, null, disableAnimation: true);
                        }
                    });
                }
            }
            catch
            {
                // Keep local state if background refresh fails; next manual load can recover.
            }
        }

        private async void GroupedEntriesView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            await HandleEntriesViewDoubleTappedAsync(e);
        }

        private async Task HandleEntriesViewDoubleTappedAsync(DoubleTappedRoutedEventArgs e)
        {
            EntryViewModel? row = GetActiveEntriesViewHost()?.ResolveDoubleTappedEntry(e);
            if (row is not null)
            {
                if (row.IsGroupHeader)
                {
                    _groupExpansionStates[row.GroupKey] = !row.IsGroupExpanded;
                    ApplyCurrentPresentation();
                    e.Handled = true;
                    return;
                }

                SelectEntryInList(row, ensureVisible: false);
            }

            if (row is null || !row.IsLoaded)
            {
                return;
            }

            await OpenEntryAsync(row, clearSelectionBeforeDirectoryNavigation: false);
        }

        private bool IsEntryAlreadySelected(EntryViewModel entry)
        {
            if (entry.IsGroupHeader)
            {
                return false;
            }

            return string.Equals(_selectedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        private void EntryRow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement row || row.DataContext is not EntryViewModel entry)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            var point = e.GetCurrentPoint(row);
            if (!IsEntryAlreadySelected(entry))
            {
                SelectEntryInList(entry, ensureVisible: false);
            }

            FocusEntriesList();

            if (point.Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                return;
            }

        }

        private void GroupHeaderBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not EntryViewModel entry ||
                !entry.IsGroupHeader ||
                string.IsNullOrWhiteSpace(entry.GroupKey))
            {
                return;
            }

            _groupExpansionStates[entry.GroupKey] = !entry.IsGroupExpanded;
            ApplyCurrentPresentation();
            e.Handled = true;
        }

        private IEntriesViewHost? GetActiveEntriesViewHost()
        {
            return GetVisibleEntriesViewHost();
        }

        private void GroupedEntriesView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HandleEntriesViewRightTapped(e);
        }

        private void GroupedListHeader_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void GroupedListHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void GroupedListHeader_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void HandleEntriesViewRightTapped(RightTappedRoutedEventArgs e)
        {
            EntriesViewHitResult? hit = GetActiveEntriesViewHost()?.ResolveRightTappedHit(e);
            if (hit is null)
            {
                if (IsEntriesGroupHeaderSource(e.OriginalSource as DependencyObject))
                {
                    e.Handled = true;
                }
                return;
            }

            if (hit.Entry?.IsGroupHeader == true)
            {
                FrameworkElement root = GetVisibleEntriesRoot();
                hit = new EntriesViewHitResult(root, e.GetPosition(root), null, false);
            }

            _lastEntriesContextItem = hit.Entry;
            if (hit.Entry is not null && !IsEntryAlreadySelected(hit.Entry))
            {
                SelectEntryInList(hit.Entry, ensureVisible: false);
            }

            ShowEntriesContextFlyout(new EntriesContextRequest(
                hit.Anchor,
                hit.Position,
                hit.Entry,
                hit.IsItemTarget));
            e.Handled = true;
        }

        private static bool IsEntriesGroupHeaderSource(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is EntryGroupHeader)
                {
                    return true;
                }

                if (current is FrameworkElement element &&
                    element.DataContext is EntryViewModel entry &&
                    entry.IsGroupHeader)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void ShowEntriesContextFlyout(EntriesContextRequest request)
        {
            CommandMenuFlyout flyout = SelectEntriesContextFlyout(request);
            // `MenuFlyout.IsOpen` can be false during transition while `Opening` has
            // already marked `_entriesFlyoutOpen = true`. Treat either as "open-ish"
            // and route through pending-reopen path to avoid losing context.
            bool flyoutActive = _entriesFlyoutOpen || (_activeEntriesContextFlyout?.IsOpen ?? false);
            if (flyoutActive)
            {
                // During one right-click chain, multiple handlers can race to switch the flyout.
                // Keep the item-target request if it is already pending; do not let a later
                // background request override it.
                if (_pendingEntriesContextRequest is { IsItemTarget: true } &&
                    !request.IsItemTarget)
                {
                    return;
                }

                _pendingEntriesContextRequest = request;
                if (_activeEntriesContextFlyout?.IsOpen == true)
                {
                    _activeEntriesContextFlyout.Hide();
                }
                return;
            }

            _entriesContextRequest = request;
            _lastEntriesContextItem = request.Entry ?? _lastEntriesContextItem;
            _activeEntriesContextFlyout = flyout;
            flyout.SetInvocationContext(request.Anchor, request.Position);
            flyout.ShowAt(request.Anchor, new FlyoutShowOptions
            {
                Position = request.Position
            });
        }

        private CommandMenuFlyout SelectEntriesContextFlyout(EntriesContextRequest request)
        {
            if (!request.IsItemTarget || request.Entry is null)
            {
                return BackgroundEntriesContextFlyout;
            }

            return request.Entry.IsDirectory
                ? FolderEntriesContextFlyout
                : FileEntriesContextFlyout;
        }

        private void HideActiveEntriesContextFlyout()
        {
            if (_entriesFlyoutOpen && _activeEntriesContextFlyout?.IsOpen == true)
            {
                _activeEntriesContextFlyout.Hide();
            }
        }

        private void EntriesContextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not string commandId)
            {
                return;
            }

            ExecuteEntriesContextCommand(commandId);
        }

        private void ExecuteEntriesContextCommand(object? parameter)
        {
            if (parameter is not string commandId || !TryBuildActiveEntriesContextTarget(out FileCommandTarget target))
            {
                return;
            }

            if (!CanExecuteEntriesContextCommand(commandId, target))
            {
                return;
            }

            bool flyoutActive = _entriesFlyoutOpen || (_activeEntriesContextFlyout?.IsOpen ?? false);
            if (flyoutActive)
            {
                _pendingEntriesContextCommand = new PendingEntriesContextCommand(commandId, target);
                HideActiveEntriesContextFlyout();
                return;
            }

            _ = ExecuteEntriesContextCommandAsync(commandId, target);
        }

        private async Task ExecuteEntriesContextCommandAsync(string commandId, FileCommandTarget target)
        {
            switch (commandId)
            {
                case FileCommandIds.Open:
                    await ExecuteOpenEntriesContextTargetAsync(target);
                    break;
                case FileCommandIds.Copy:
                    ExecuteCopy();
                    break;
                case FileCommandIds.Cut:
                    ExecuteCut();
                    break;
                case FileCommandIds.Paste:
                    await ExecutePasteForTargetAsync(target);
                    break;
                case FileCommandIds.Rename:
                    await ExecuteRenameSelectedAsync();
                    break;
                case FileCommandIds.Delete:
                    await ExecuteDeleteSelectedAsync();
                    break;
                case FileCommandIds.NewFile:
                    await ExecuteNewFileAsync();
                    break;
                case FileCommandIds.NewFolder:
                    await ExecuteNewFolderAsync();
                    break;
                case FileCommandIds.Refresh:
                    await RefreshCurrentDirectoryInBackgroundAsync();
                    break;
                case FileCommandIds.CopyPath:
                    ExecuteCopyPathCommand(target);
                    break;
                case FileCommandIds.Share:
                    ExecuteShareCommand(target);
                    break;
                case FileCommandIds.CreateShortcut:
                    await ExecuteCreateShortcutCommandAsync(target);
                    break;
                case FileCommandIds.CompressZip:
                    await ExecuteCompressZipCommandAsync(target);
                    break;
                case FileCommandIds.ExtractSmart:
                    await ExecuteExtractZipSmartCommandAsync(target);
                    break;
                case FileCommandIds.ExtractHere:
                    await ExecuteExtractZipHereCommandAsync(target);
                    break;
                case FileCommandIds.ExtractToFolder:
                    await ExecuteExtractZipToFolderCommandAsync(target);
                    break;
                case FileCommandIds.OpenWith:
                    ExecuteOpenWithCommand(target);
                    break;
                case FileCommandIds.OpenTarget:
                    await ExecuteOpenTargetCommandAsync(target);
                    break;
                case FileCommandIds.RunAsAdministrator:
                    ExecuteRunAsAdministratorCommand(target);
                    break;
                case FileCommandIds.OpenInNewWindow:
                    ExecuteOpenInNewWindowCommand(target);
                    break;
                case FileCommandIds.OpenInTerminal:
                    ExecuteOpenInTerminalCommand(target);
                    break;
                case FileCommandIds.Properties:
                    ExecuteShowPropertiesCommand(target);
                    break;
            }
        }

        private bool TryBuildActiveEntriesContextTarget(out FileCommandTarget target)
        {
            EntryViewModel? contextEntry = _entriesContextRequest?.Entry ?? _lastEntriesContextItem;
            target = ResolveEntriesContextTarget(contextEntry);
            return target.Kind != FileCommandTargetKind.None;
        }

        private bool CanExecuteEntriesContextCommand(string commandId, FileCommandTarget target)
        {
            IReadOnlyList<FileCommandDescriptor> descriptors = _fileCommandCatalog.BuildCommands(target);
            bool supported = descriptors.Any(descriptor => string.Equals(descriptor.Id, commandId, StringComparison.Ordinal));
            if (!supported)
            {
                return false;
            }

            return commandId switch
            {
                FileCommandIds.Open => !string.IsNullOrWhiteSpace(target.Path),
                FileCommandIds.Copy => CanCopySelectedEntry(),
                FileCommandIds.Cut => CanCutSelectedEntry(),
                FileCommandIds.Paste => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                    _fileManagementCoordinator.HasAvailablePasteItems(),
                FileCommandIds.Rename => CanRenameSelectedEntry(),
                FileCommandIds.Delete => CanDeleteSelectedEntry(),
                FileCommandIds.NewFile or FileCommandIds.NewFolder =>
                    !string.IsNullOrWhiteSpace(target.Path) &&
                    string.Equals(target.Path, _currentPath, StringComparison.OrdinalIgnoreCase) &&
                    CanCreateInCurrentDirectory(),
                FileCommandIds.Refresh =>
                    string.Equals(target.Path, _currentPath, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase),
                FileCommandIds.CopyPath => !string.IsNullOrWhiteSpace(target.Path),
                FileCommandIds.Share => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry,
                FileCommandIds.CreateShortcut => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                    _explorerService.DirectoryExists(_currentPath),
                FileCommandIds.CompressZip => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry &&
                    _explorerService.PathExists(target.Path),
                FileCommandIds.ExtractSmart or FileCommandIds.ExtractHere or FileCommandIds.ExtractToFolder =>
                    !string.IsNullOrWhiteSpace(target.Path) &&
                    target.Kind == FileCommandTargetKind.FileEntry &&
                    string.Equals(Path.GetExtension(target.Path), ".zip", StringComparison.OrdinalIgnoreCase) &&
                    _explorerService.PathExists(target.Path),
                FileCommandIds.OpenWith => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.OpenTarget => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.RunAsAdministrator => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.OpenInNewWindow => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.IsDirectory,
                FileCommandIds.OpenInTerminal => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase),
                FileCommandIds.Properties => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private async Task ExecuteOpenEntriesContextTargetAsync(FileCommandTarget target)
        {
            if (target.IsDirectory)
            {
                await NavigateToPathAsync(target.Path ?? ShellMyComputerPath, pushHistory: true);
                return;
            }

            if ((target.Traits & FileEntryTraits.Shortcut) != 0)
            {
                await ExecuteOpenTargetCommandAsync(target);
                return;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = target.Path,
                    UseShellExecute = true
                });
                UpdateStatusKey("StatusOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task OpenEntryAsync(EntryViewModel row, bool clearSelectionBeforeDirectoryNavigation)
        {
            string targetPath = string.IsNullOrWhiteSpace(row.FullPath)
                ? Path.Combine(_currentPath, row.Name)
                : row.FullPath;

            if (row.IsDirectory)
            {
                if (clearSelectionBeforeDirectoryNavigation)
                {
                    ClearListSelection();
                }

                await NavigateToPathAsync(targetPath, pushHistory: true);
                return;
            }

            FileCommandTarget target = FileCommandTargetResolver.ResolveEntry(targetPath, isDirectory: false);
            if ((target.Traits & FileEntryTraits.Shortcut) != 0)
            {
                await ExecuteOpenTargetCommandAsync(target);
                return;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                });
                UpdateStatusKey("StatusOpened", row.Name);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private Task ExecutePasteForTargetAsync(FileCommandTarget target)
        {
            string targetPath = target.Path ?? string.Empty;
            bool selectPastedEntry = string.Equals(targetPath, _currentPath, StringComparison.OrdinalIgnoreCase);
            return PasteIntoDirectoryAsync(targetPath, selectPastedEntry);
        }

        private void ExecuteCopyPathCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                var package = new DataPackage();
                package.SetText(target.Path);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                UpdateStatusKey("StatusCopyPathReady", target.Path);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCopyPathFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteShareCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                if (!EnsureShareDataTransferManager())
                {
                    UpdateStatusKey("StatusShareFailed", S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                _pendingShareTarget = target;
                IntPtr ownerHandle = _windowHandle != IntPtr.Zero
                    ? _windowHandle
                    : WindowNative.GetWindowHandle(this);
                NativeMethods.ShowShareUIForWindow(ownerHandle);
                UpdateStatusKey("StatusShareOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                _pendingShareTarget = null;
                UpdateStatusKey("StatusShareFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task ExecuteCreateShortcutCommandAsync(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            if (!TryEnsureCurrentDirectoryAvailable(out string errorMessage))
            {
                UpdateStatusKey("StatusCreateShortcutFailed", errorMessage);
                return;
            }

            try
            {
                SuppressNextWatcherRefresh(_currentPath);
                FileOperationResult<CreatedEntryInfo> createResult = await _fileManagementCoordinator.TryCreateShortcutAsync(_currentPath, target.Path);
                if (!createResult.Succeeded)
                {
                    UpdateStatusKey("StatusCreateShortcutFailed", createResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                CreatedEntryInfo created = createResult.Value!;
                if (!created.ChangeNotified)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, "create-shortcut");
                }

                _selectedEntryPath = created.FullPath;
                await LoadFirstPageAsync();
                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                UpdateFileCommandStates();
                UpdateStatusKey("StatusCreateShortcutSuccess", created.Name);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCreateShortcutFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task ExecuteCompressZipCommandAsync(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                string sourcePath = target.Path;
                string destinationDirectory = Path.GetDirectoryName(sourcePath.TrimEnd('\\')) ?? _currentPath;
                if (string.IsNullOrWhiteSpace(destinationDirectory) || !_explorerService.DirectoryExists(destinationDirectory))
                {
                    UpdateStatusKey("StatusCompressZipFailed", S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                string archiveName = _explorerService.GenerateUniqueZipArchiveName(destinationDirectory, sourcePath);
                string archivePath = Path.Combine(destinationDirectory, archiveName);

                bool destinationIsCurrentDirectory =
                    string.Equals(destinationDirectory, _currentPath, StringComparison.OrdinalIgnoreCase);
                double detailsVerticalOffset = DetailsEntriesScrollViewer.VerticalOffset;
                double groupedHorizontalOffset = GroupedEntriesScrollViewer.HorizontalOffset;
                if (destinationIsCurrentDirectory)
                {
                    SuppressNextWatcherRefresh(_currentPath);
                }

                FileOperationResult<string> zipResult = await _fileManagementCoordinator.TryCreateZipArchiveAsync(sourcePath, archivePath);
                if (!zipResult.Succeeded)
                {
                    UpdateStatusKey("StatusCompressZipFailed", zipResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }

                if (destinationIsCurrentDirectory)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, "compress-zip");
                    _selectedEntryPath = archivePath;
                    await LoadFirstPageAsync();
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_currentViewMode == EntryViewMode.Details)
                        {
                            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, detailsVerticalOffset), null, disableAnimation: true);
                        }
                        else
                        {
                            double maxOffset = Math.Max(0, GroupedEntriesScrollViewer.ScrollableWidth);
                            GroupedEntriesScrollViewer.ChangeView(Math.Min(maxOffset, groupedHorizontalOffset), null, null, disableAnimation: true);
                        }

                        RestoreListSelectionByPathRespectingViewport();
                        FocusEntriesList();
                    });
                    UpdateFileCommandStates();
                }

                UpdateStatusKey("StatusCompressZipSuccess", archiveName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCompressZipFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private Task ExecuteExtractZipSmartCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipCommandCoreAsync(
                target,
                archivePath => _fileManagementCoordinator.TryExtractZipSmartAsync(archivePath),
                "extract-smart");
        }

        private Task ExecuteExtractZipHereCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipCommandCoreAsync(
                target,
                archivePath => _fileManagementCoordinator.TryExtractZipHereAsync(archivePath),
                "extract-here");
        }

        private Task ExecuteExtractZipToFolderCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipCommandCoreAsync(
                target,
                archivePath => _fileManagementCoordinator.TryExtractZipToFolderAsync(archivePath),
                "extract-to-folder");
        }

        private async Task ExecuteExtractZipCommandCoreAsync(
            FileCommandTarget target,
            Func<string, Task<FileOperationResult<ZipExtractionInfo>>> extractAsync,
            string invalidationReason)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                string archivePath = target.Path;
                string destinationDirectory = Path.GetDirectoryName(archivePath.TrimEnd('\\')) ?? _currentPath;
                if (string.IsNullOrWhiteSpace(destinationDirectory) || !_explorerService.DirectoryExists(destinationDirectory))
                {
                    UpdateStatusKey("StatusExtractZipFailed", S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                bool destinationIsCurrentDirectory =
                    string.Equals(destinationDirectory, _currentPath, StringComparison.OrdinalIgnoreCase);
                double detailsVerticalOffset = DetailsEntriesScrollViewer.VerticalOffset;
                double groupedHorizontalOffset = GroupedEntriesScrollViewer.HorizontalOffset;
                if (destinationIsCurrentDirectory)
                {
                    SuppressNextWatcherRefresh(_currentPath);
                }

                FileOperationResult<ZipExtractionInfo> extractResult = await extractAsync(archivePath);
                if (!extractResult.Succeeded)
                {
                    UpdateStatusKey("StatusExtractZipFailed", extractResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }

                ZipExtractionInfo extracted = extractResult.Value!;
                string? extractedName = string.IsNullOrWhiteSpace(extracted.PrimarySelectionPath)
                    ? null
                    : Path.GetFileName(extracted.PrimarySelectionPath.TrimEnd('\\'));

                if (destinationIsCurrentDirectory)
                {
                    if (!extracted.ChangeNotified)
                    {
                        EnsurePersistentRefreshFallbackInvalidation(_currentPath, invalidationReason);
                    }

                    _selectedEntryPath = extracted.PrimarySelectionPath;
                    await LoadFirstPageAsync();
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_currentViewMode == EntryViewMode.Details)
                        {
                            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, detailsVerticalOffset), null, disableAnimation: true);
                        }
                        else
                        {
                            double maxOffset = Math.Max(0, GroupedEntriesScrollViewer.ScrollableWidth);
                            GroupedEntriesScrollViewer.ChangeView(Math.Min(maxOffset, groupedHorizontalOffset), null, null, disableAnimation: true);
                        }

                        if (!string.IsNullOrWhiteSpace(_selectedEntryPath))
                        {
                            RestoreListSelectionByPathRespectingViewport();
                        }

                        FocusEntriesList();
                    });
                    UpdateFileCommandStates();
                }

                UpdateStatusKey("StatusExtractZipSuccess", extractedName ?? target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusExtractZipFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteOpenInNewWindowCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || !target.IsDirectory)
            {
                return;
            }

            try
            {
                if (Application.Current is App app)
                {
                    app.CreateWindow(target.Path);
                    UpdateStatusKey("StatusOpenedInNewWindow", target.DisplayName);
                }
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenInNewWindowFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task ExecuteOpenTargetCommandAsync(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || target.IsDirectory)
            {
                return;
            }

            try
            {
                string resolvedTargetPath = await Task.Run(() => _explorerService.ResolveShortcutTargetPath(target.Path));
                if (Uri.TryCreate(resolvedTargetPath, UriKind.Absolute, out Uri? uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    _ = Process.Start(new ProcessStartInfo
                    {
                        FileName = resolvedTargetPath,
                        UseShellExecute = true
                    });
                    UpdateStatusKey("StatusOpened", target.DisplayName);
                    return;
                }

                if (_explorerService.DirectoryExists(resolvedTargetPath))
                {
                    await NavigateToPathAsync(resolvedTargetPath, pushHistory: true);
                    return;
                }

                if (_explorerService.PathExists(resolvedTargetPath))
                {
                    _ = Process.Start(new ProcessStartInfo
                    {
                        FileName = resolvedTargetPath,
                        UseShellExecute = true
                    });
                    UpdateStatusKey("StatusOpened", Path.GetFileName(resolvedTargetPath));
                    return;
                }

                UpdateStatusKey("StatusOpenTargetFailed", resolvedTargetPath);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenTargetFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteRunAsAdministratorCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || target.IsDirectory)
            {
                return;
            }

            try
            {
                _explorerService.RunAsAdministrator(target.Path);
                UpdateStatusKey("StatusRunAsAdministratorStarted", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusRunAsAdministratorFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteOpenWithCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || target.IsDirectory)
            {
                return;
            }

            try
            {
                IntPtr ownerHandle = _windowHandle != IntPtr.Zero
                    ? _windowHandle
                    : WindowNative.GetWindowHandle(this);
                var openAsInfo = new NativeMethods.OpenAsInfo
                {
                    FilePath = target.Path,
                    ClassName = null,
                    Flags = NativeMethods.OAIF_EXEC | NativeMethods.OAIF_HIDE_REGISTRATION
                };
                int hr = NativeMethods.SHOpenWithDialog(ownerHandle, ref openAsInfo);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                UpdateStatusKey("StatusOpenWithOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenWithFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteOpenInTerminalCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                _explorerService.OpenPathInTerminal(target.Path);
                UpdateStatusKey("StatusOpenTerminalSuccess", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenTerminalFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteShowPropertiesCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                _explorerService.ShowProperties(target.Path);
                UpdateStatusKey("StatusPropertiesOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPropertiesFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private bool EnsureShareDataTransferManager()
        {
            if (_shareDataTransferManager is not null)
            {
                return true;
            }

            IntPtr ownerHandle = _windowHandle != IntPtr.Zero
                ? _windowHandle
                : WindowNative.GetWindowHandle(this);
            if (ownerHandle == IntPtr.Zero)
            {
                return false;
            }

            _shareDataTransferManager = NativeMethods.GetDataTransferManagerForWindow(ownerHandle);
            _shareDataTransferManager.DataRequested += ShareDataTransferManager_DataRequested;
            return true;
        }

        private async void ShareDataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            FileCommandTarget? target = _pendingShareTarget;
            _pendingShareTarget = null;

            if (target is null || string.IsNullOrWhiteSpace(target.Path))
            {
                args.Request.FailWithDisplayText("Share target is unavailable.");
                return;
            }

            DataRequestDeferral deferral = args.Request.GetDeferral();
            try
            {
                DataRequest request = args.Request;
                request.Data.Properties.Title = target.DisplayName;
                request.Data.Properties.Description = target.Path;

                IStorageItem storageItem = target.IsDirectory
                    ? (IStorageItem)await StorageFolder.GetFolderFromPathAsync(target.Path)
                    : await StorageFile.GetFileFromPathAsync(target.Path);

                request.Data.SetStorageItems(new[] { storageItem });
            }
            catch (Exception ex)
            {
                string message = FileOperationErrors.ToUserMessage(ex);
                args.Request.FailWithDisplayText(message);
                UpdateStatusKey("StatusShareFailed", message);
            }
            finally
            {
                deferral.Complete();
            }
        }

        private void UpdateConditionalCommandVisibility(CommandMenuFlyout flyout, string commandId, string label, int insertIndex, bool shouldShow)
        {
            int existingIndex = -1;
            for (int i = 0; i < flyout.Commands.Count; i++)
            {
                if (string.Equals(flyout.Commands[i].Label, label, StringComparison.Ordinal))
                {
                    existingIndex = i;
                    break;
                }
            }

            if (shouldShow)
            {
                if (existingIndex >= 0)
                {
                    return;
                }

                flyout.Commands.Insert(insertIndex, CreateCommandBarItem(commandId, label));
                return;
            }

            if (existingIndex >= 0)
            {
                flyout.Commands.RemoveAt(existingIndex);
            }
        }

        private void UpdateEntriesContextFlyoutState(CommandMenuFlyout flyout)
        {
            if (!TryBuildActiveEntriesContextTarget(out FileCommandTarget target))
            {
                return;
            }

            var availableCommandIds = _fileCommandCatalog.BuildCommands(target)
                .Select(descriptor => descriptor.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry)
            {
                availableCommandIds.Add(FileCommandIds.CompressZip);
                availableCommandIds.Add(FileCommandIds.Share);
            }

            foreach (MenuFlyoutItemBase item in flyout.Items)
            {
                UpdateEntriesContextMenuItemState(item, target, availableCommandIds);
            }

            if (ReferenceEquals(flyout, FileEntriesContextFlyout))
            {
                UpdateFileArchiveActionsSubMenuState(target, availableCommandIds);
            }

            RefreshEntriesContextDynamicLabels(flyout, target);

            foreach (CommandMenuFlyoutItem item in flyout.Commands)
            {
                string commandId = item.CommandId;
                bool enabled = !string.IsNullOrWhiteSpace(commandId) &&
                    availableCommandIds.Contains(commandId) &&
                    CanExecuteEntriesContextCommand(commandId, target);
                item.Command = enabled ? _entriesContextCommand : null;
                item.CommandParameter = enabled ? commandId : null;
                item.IsEnabled = enabled;
            }

            _entriesContextCommand.RaiseCanExecuteChanged();
        }

        private void UpdateFileArchiveActionsSubMenuState(FileCommandTarget target, HashSet<string> availableCommandIds)
        {
            bool showExtractSmart = availableCommandIds.Contains(FileCommandIds.ExtractSmart);
            bool showExtractHere = availableCommandIds.Contains(FileCommandIds.ExtractHere);
            bool showExtractToFolder = availableCommandIds.Contains(FileCommandIds.ExtractToFolder);
            bool showAnyExtract = showExtractSmart || showExtractHere || showExtractToFolder;
            bool showCompress = availableCommandIds.Contains(FileCommandIds.CompressZip);

            FileExtractSmartMenuItem.Visibility = showExtractSmart ? Visibility.Visible : Visibility.Collapsed;
            FileExtractHereMenuItem.Visibility = showExtractHere ? Visibility.Visible : Visibility.Collapsed;
            FileExtractToFolderMenuItem.Visibility = showExtractToFolder ? Visibility.Visible : Visibility.Collapsed;
            FileArchiveExtractSeparator.Visibility = showAnyExtract && showCompress ? Visibility.Visible : Visibility.Collapsed;
            FileCompressZipMenuItem.Visibility = showCompress ? Visibility.Visible : Visibility.Collapsed;
            FileArchiveActionsSubMenu.Visibility = showAnyExtract || showCompress ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshEntriesContextDynamicLabels(CommandMenuFlyout flyout, FileCommandTarget target)
        {
            foreach (MenuFlyoutItemBase item in EnumerateMenuItemsRecursive(flyout.Items))
            {
                if (item is MenuFlyoutItem menuItem && menuItem.Tag is string commandId)
                {
                    menuItem.Text = GetEntriesContextMenuItemLabel(commandId, target, menuItem.Text);
                }
            }
        }

        private static IEnumerable<MenuFlyoutItemBase> EnumerateMenuItemsRecursive(IList<MenuFlyoutItemBase> items)
        {
            foreach (MenuFlyoutItemBase item in items)
            {
                yield return item;

                if (item is MenuFlyoutSubItem subItem)
                {
                    foreach (MenuFlyoutItemBase child in EnumerateMenuItemsRecursive(subItem.Items))
                    {
                        yield return child;
                    }
                }
            }
        }

        private void UpdateEntriesContextMenuItemState(
            MenuFlyoutItemBase item,
            FileCommandTarget target,
            HashSet<string> availableCommandIds)
        {
            switch (item)
            {
                case MenuFlyoutItem menuItem when menuItem.Tag is string commandId:
                    menuItem.Text = GetEntriesContextMenuItemLabel(commandId, target, menuItem.Text);
                    bool visible = availableCommandIds.Contains(commandId);
                    menuItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    menuItem.IsEnabled = visible && CanExecuteEntriesContextCommand(commandId, target);
                    break;

                case MenuFlyoutSubItem subItem:
                    bool hasTaggedChildren = false;
                    bool anyVisibleChild = false;
                    foreach (MenuFlyoutItemBase child in subItem.Items)
                    {
                        if (child is MenuFlyoutItem childMenuItem && childMenuItem.Tag is string)
                        {
                            hasTaggedChildren = true;
                        }

                        UpdateEntriesContextMenuItemState(child, target, availableCommandIds);
                        anyVisibleChild |= child.Visibility == Visibility.Visible;
                    }

                    if (hasTaggedChildren)
                    {
                        subItem.Visibility = anyVisibleChild ? Visibility.Visible : Visibility.Collapsed;
                    }
                    break;
            }
        }

        private string GetEntriesContextMenuItemLabel(string commandId, FileCommandTarget target, string currentLabel)
        {
            if (string.Equals(commandId, FileCommandIds.ExtractToFolder, StringComparison.Ordinal))
            {
                return SF("CommonExtractToNamedFolder", GetDefaultExtractedFolderDisplayName(target));
            }

            return commandId switch
            {
                FileCommandIds.ExtractSmart => S("CommonExtractSmart"),
                FileCommandIds.ExtractHere => S("CommonExtractHere"),
                _ => currentLabel
            };
        }

        private string GetDefaultExtractedFolderDisplayName(FileCommandTarget target)
        {
            string? sourcePath = target.Path;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                string fileName = Path.GetFileName(sourcePath.TrimEnd('\\'));
                string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (!string.IsNullOrWhiteSpace(withoutExtension))
                {
                    return withoutExtension;
                }
            }

            string displayName = target.DisplayName ?? string.Empty;
            string fallback = Path.GetFileNameWithoutExtension(displayName);
            return string.IsNullOrWhiteSpace(fallback) ? displayName : fallback;
        }

        private CommandMenuFlyoutItem CreateCommandBarItem(string commandId, string label)
        {
            string glyph = label switch
            {
                var value when string.Equals(value, S("CommonPaste"), StringComparison.Ordinal) => "\uE77F",
                var value when string.Equals(value, S("CommonProperties"), StringComparison.Ordinal) => "\uE946",
                var value when string.Equals(value, S("CommonCut"), StringComparison.Ordinal) => "\uE8C6",
                var value when string.Equals(value, S("CommonCopy"), StringComparison.Ordinal) => "\uE8C8",
                var value when string.Equals(value, S("CommonRename"), StringComparison.Ordinal) => "\uE8AC",
                var value when string.Equals(value, S("CommonDelete"), StringComparison.Ordinal) => "\uE74D",
                var value when string.Equals(value, S("CommonOpenInTerminal"), StringComparison.Ordinal) => "\uE756",
                var value when string.Equals(value, S("CommonRefresh"), StringComparison.Ordinal) => "\uE72C",
                _ => "\uE8A5"
            };

            return new CommandMenuFlyoutItem
            {
                CommandId = commandId,
                Command = _entriesContextCommand,
                CommandParameter = commandId,
                Label = label,
                Icon = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = glyph
                }
            };
        }

        private void GroupedEntriesView_PointerPressedPreview(object sender, PointerRoutedEventArgs e)
        {
            HandleEntriesViewPointerPressedPreview(e);
        }

        private void HandleEntriesViewPointerPressedPreview(PointerRoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            if (!_entriesFlyoutOpen)
            {
                if (GetActiveEntriesViewHost()?.ResolvePressedEntry(e) is null &&
                    !IsEntriesGroupHeaderSource(e.OriginalSource as DependencyObject) &&
                    !string.IsNullOrWhiteSpace(_selectedEntryPath))
                {
                    ClearExplicitSelectionKeepAnchor();
                    FocusEntriesList();
                }

                return;
            }

            FrameworkElement root = GetVisibleEntriesRoot();
            var point = e.GetCurrentPoint(root);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            EntryViewModel? entry = GetActiveEntriesViewHost()?.ResolvePressedEntry(e);
            if (entry is not null)
            {
                if (entry.IsGroupHeader)
                {
                    _activeEntriesContextFlyout?.Hide();
                    e.Handled = true;
                    return;
                }

                if (!IsEntryAlreadySelected(entry))
                {
                    SelectEntryInList(entry, ensureVisible: false);
                }
            }
            else if (!IsEntriesGroupHeaderSource(e.OriginalSource as DependencyObject) &&
                !string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                ClearExplicitSelectionKeepAnchor();
                FocusEntriesList();
            }

            _activeEntriesContextFlyout?.Hide();
        }

        private void StaticEntriesContextFlyout_Opening(object sender, object e)
        {
            _entriesFlyoutOpen = true;
            _activeEntriesContextFlyout = sender as CommandMenuFlyout;
            ResetColumnSplitterCursorState();
            UpdateViewCommandStates();

            if (ReferenceEquals(sender, FileEntriesContextFlyout) &&
                _entriesContextRequest?.Entry is EntryViewModel fileEntry &&
                !fileEntry.IsDirectory)
            {
                string displayName = GetDefaultExtractedFolderDisplayName(FileCommandTargetResolver.ResolveEntry(fileEntry.FullPath, isDirectory: false));
                FileExtractToFolderMenuItem.Text = SF("CommonExtractToNamedFolder", displayName);
            }

            if (ReferenceEquals(sender, FolderEntriesContextFlyout))
            {
                UpdateConditionalCommandVisibility(FolderEntriesContextFlyout, FileCommandIds.Paste, S("CommonPaste"), 2, _fileManagementCoordinator.HasAvailablePasteItems());
            }
            else if (ReferenceEquals(sender, BackgroundEntriesContextFlyout))
            {
                UpdateConditionalCommandVisibility(BackgroundEntriesContextFlyout, FileCommandIds.Paste, S("CommonPaste"), 0, _fileManagementCoordinator.HasAvailablePasteItems());
            }

            if (sender is CommandMenuFlyout flyout)
            {
                UpdateEntriesContextFlyoutState(flyout);
            }
        }

        private void StaticEntriesContextFlyout_Closed(object sender, object e)
        {
            _entriesFlyoutOpen = false;

            if (_pendingEntriesContextCommand is not null)
            {
                PendingEntriesContextCommand pendingCommand = _pendingEntriesContextCommand;
                _pendingEntriesContextCommand = null;
                _pendingEntriesContextRequest = null;
                _entriesContextRequest = null;
                _lastEntriesContextItem = null;
                _activeEntriesContextFlyout = null;
                _ = ExecuteEntriesContextCommandAsync(pendingCommand.CommandId, pendingCommand.Target);
                return;
            }

            if (_pendingContextRenameEntry is not null)
            {
                EntryViewModel pendingRenameEntry = _pendingContextRenameEntry;
                _pendingContextRenameEntry = null;
                _pendingEntriesContextRequest = null;
                _entriesContextRequest = null;
                _lastEntriesContextItem = null;
                _activeEntriesContextFlyout = null;
                _ = BeginRenameOverlayAsync(pendingRenameEntry);
                return;
            }

            if (_pendingEntriesContextRequest is not null)
            {
                EntriesContextRequest pendingRequest = _pendingEntriesContextRequest;
                _pendingEntriesContextRequest = null;

                _entriesContextRequest = pendingRequest;
                _lastEntriesContextItem = pendingRequest.Entry ?? _lastEntriesContextItem;
                _activeEntriesContextFlyout = SelectEntriesContextFlyout(pendingRequest);
                _activeEntriesContextFlyout.SetInvocationContext(pendingRequest.Anchor, pendingRequest.Position);
                _activeEntriesContextFlyout.ShowAt(pendingRequest.Anchor, new FlyoutShowOptions
                {
                    Position = pendingRequest.Position
                });
                return;
            }

            _entriesContextRequest = null;
            _lastEntriesContextItem = null;
            _activeEntriesContextFlyout = null;
            _pendingContextRenameEntry = null;
        }

        private void ResetColumnSplitterCursorState()
        {
            _splitterHoverCount = 0;
            _activeSplitterElement = null;
            _activeSplitterDragMode = SplitterDragMode.None;
            _activeColumnSplitterKind = null;
            _activeColumnResizeState = null;
            _sidebarDragStartWidth = null;
        }

        private FileCommandTarget ResolveEntriesContextTarget(EntryViewModel? contextEntry)
        {
            if (contextEntry is { IsLoaded: true })
            {
                if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) && contextEntry.IsDirectory)
                {
                    return FileCommandTargetResolver.ResolveDriveRoot(contextEntry.FullPath);
                }

                return FileCommandTargetResolver.ResolveEntry(contextEntry.FullPath, contextEntry.IsDirectory);
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return FileCommandTargetResolver.ResolveVirtualNode(ShellMyComputerPath, S("SidebarMyComputer"));
            }

            return FileCommandTargetResolver.ResolveListBackground(_currentPath);
        }

        private async void UpButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusAlreadyAtRoot");
                return;
            }

            if (IsDriveRoot(_currentPath))
            {
                await NavigateToPathAsync(ShellMyComputerPath, pushHistory: true);
                return;
            }

            string? parent = _explorerService.GetParentPath(_currentPath);
            if (string.IsNullOrEmpty(parent))
            {
                await NavigateToPathAsync(ShellMyComputerPath, pushHistory: true);
                return;
            }

            _pendingParentReturnAnchorPath = _currentPath;
            await NavigateToPathAsync(parent, pushHistory: true);
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_backStack.Count == 0)
            {
                UpdateNavButtonsState();
                return;
            }

            string prev = _backStack.Pop();
            _forwardStack.Push(_currentPath);
            UpdateNavButtonsState();
            await NavigateToPathAsync(prev, pushHistory: false);
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_forwardStack.Count == 0)
            {
                UpdateNavButtonsState();
                return;
            }

            string next = _forwardStack.Pop();
            _backStack.Push(_currentPath);
            UpdateNavButtonsState();
            await NavigateToPathAsync(next, pushHistory: false);
        }

        private async void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string target)
            {
                return;
            }

            await NavigateToPathAsync(target, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private void BreadcrumbChevronButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string basePath)
            {
                return;
            }

            var flyout = new MenuFlyout();
            if (Application.Current.Resources.TryGetValue("BreadcrumbMenuFlyoutPresenterStyle", out object styleObj)
                && styleObj is Style presenterStyle)
            {
                flyout.MenuFlyoutPresenterStyle = presenterStyle;
            }
            _activeBreadcrumbFlyout = flyout;
            flyout.Closed -= BreadcrumbSplitFlyout_Closed;
            flyout.Closed += BreadcrumbSplitFlyout_Closed;

            try
            {
                if (string.Equals(basePath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (DriveInfo drive in _explorerService.GetReadyDrives())
                    {
                        flyout.Items.Add(new MenuFlyoutItem
                        {
                            Text = drive.Name,
                            Tag = drive.Name
                        });
                    }
                }
                else if (_explorerService.DirectoryExists(basePath))
                {
                    string[] dirs = _explorerService.GetDirectories(basePath);
                    Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                    int shown = 0;
                    foreach (string dir in dirs)
                    {
                        string name = Path.GetFileName(dir.TrimEnd('\\'));
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = dir;
                        }

                        flyout.Items.Add(new MenuFlyoutItem
                        {
                            Text = name,
                            Tag = dir
                        });
                        if (flyout.Items[^1] is MenuFlyoutItem item)
                        {
                            item.Click += BreadcrumbSubdirMenuItem_Click;
                        }
                        shown++;
                        if (shown >= 80)
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Fallback item below.
            }

            if (flyout.Items.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = S("CommonEmpty"),
                    IsEnabled = false
                });
            }

            ShowBreadcrumbFlyout(flyout, btn);
        }

        private void OverflowBreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
            {
                return;
            }

            var flyout = new MenuFlyout();
            if (Application.Current.Resources.TryGetValue("BreadcrumbMenuFlyoutPresenterStyle", out object styleObj)
                && styleObj is Style presenterStyle)
            {
                flyout.MenuFlyoutPresenterStyle = presenterStyle;
            }

            _activeBreadcrumbFlyout = flyout;
            flyout.Closed -= BreadcrumbSplitFlyout_Closed;
            flyout.Closed += BreadcrumbSplitFlyout_Closed;

            foreach (BreadcrumbItemViewModel item in _hiddenBreadcrumbItems)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = item.Label,
                    Tag = item.FullPath
                };
                menuItem.Click += OverflowBreadcrumbMenuItem_Click;
                flyout.Items.Add(menuItem);
            }

            if (flyout.Items.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = S("CommonEmpty"),
                    IsEnabled = false
                });
            }

            ShowBreadcrumbFlyout(flyout, btn);
        }

        private void ShowBreadcrumbFlyout(MenuFlyout flyout, FrameworkElement target)
        {
            var options = new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
            };

            flyout.ShowAt(target, options);
        }

        private void BreadcrumbSplitFlyout_Closed(object? sender, object e)
        {
            if (ReferenceEquals(sender, _activeBreadcrumbFlyout))
            {
                _activeBreadcrumbFlyout = null;
            }
        }

        private bool HasChildDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !_explorerService.DirectoryExists(path))
            {
                return false;
            }

            return _explorerService.DirectoryHasChildDirectories(path);
        }

        private bool HasBreadcrumbChildren(string path)
        {
            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return _explorerService.GetReadyDrives().Count > 0;
            }

            return HasChildDirectory(path);
        }

        private async void BreadcrumbSubdirMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not string target)
            {
                return;
            }

            await NavigateToPathAsync(target, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private async void OverflowBreadcrumbMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not string target)
            {
                return;
            }

            await NavigateToPathAsync(target, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private bool TryValidateTreeRename(SidebarTreeEntry entry, string newName, out string error)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = S("ErrorRenameNameEmpty");
                return false;
            }

            if (newName is "." or "..")
            {
                error = SF("ErrorRenameReservedName", newName);
                return false;
            }

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = SF("ErrorRenameInvalidChars", newName);
                return false;
            }

            string parentPath = Path.GetDirectoryName(entry.FullPath.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty;
            string targetPath = Path.Combine(parentPath, newName);
            if (!string.Equals(entry.FullPath, targetPath, StringComparison.OrdinalIgnoreCase) &&
                _explorerService.PathExists(targetPath))
            {
                error = SF("ErrorRenameAlreadyExists", newName);
                return false;
            }

            error = string.Empty;
            return true;
        }

        private async Task RenameSidebarTreeEntryAsync(SidebarTreeEntry entry, string newName)
        {
            string sourcePath = entry.FullPath;
            string parentPath = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty;
            string targetPath = Path.Combine(parentPath, newName);
            bool wasSelectedInTree = _sidebarTreeView?.SelectedNode?.Content is SidebarTreeEntry selectedEntry &&
                string.Equals(selectedEntry.FullPath, sourcePath, StringComparison.OrdinalIgnoreCase);
            try
            {
                FileOperationResult<RenamedEntryInfo> renameResult = await _fileManagementCoordinator.TryRenameEntryAsync(parentPath, entry.Name, newName);
                if (!renameResult.Succeeded)
                {
                    UpdateStatusKey("StatusRenameFailedWithReason", renameResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }

                RenamedEntryInfo renamed = renameResult.Value!;
                try
                {
                    _explorerService.MarkPathChanged(parentPath);
                }
                catch
                {
                    EnsurePersistentRefreshFallbackInvalidation(parentPath, "tree-rename");
                }

                TreeViewNode? renamedNode = FindSidebarTreeNodeByPath(sourcePath);
                if (renamedNode is not null)
                {
                    UpdateSidebarTreeNodePath(renamedNode, renamed.SourcePath, renamed.TargetPath, newName);
                }
                else if (FindSidebarTreeNodeByPath(parentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                {
                    await PopulateSidebarTreeChildrenAsync(parentNode, parentPath, CancellationToken.None, expandAfterLoad: true);
                }

                if (IsPathWithin(_currentPath, renamed.SourcePath))
                {
                    string suffix = _currentPath.Length > renamed.SourcePath.Length
                        ? _currentPath[renamed.SourcePath.Length..]
                        : string.Empty;
                    _currentPath = renamed.TargetPath + suffix;
                    PathTextBox.Text = _currentPath;
                    UpdateBreadcrumbs(_currentPath);
                    UpdateNavButtonsState();
                    StyledSidebarView.SetSelectedPath(_currentPath);
                    await LoadFirstPageAsync();
                }
                else if (wasSelectedInTree)
                {
                    await SelectSidebarTreePathAsync(renamed.TargetPath);
                }

                UpdateListEntryNameForCurrentDirectory(renamed.SourcePath, newName);

                UpdateStatusKey("StatusRenameSuccess", entry.Name, newName);
                _ = DispatcherQueue.TryEnqueue(FocusSidebarSurface);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusRenameFailedWithReason", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task RenameEntryAsync(EntryViewModel entry, int selectedIndex, string newName)
        {
            string src = Path.Combine(_currentPath, entry.Name);
            string oldName = entry.Name;
            TreeViewNode? renamedTreeNode = entry.IsDirectory ? FindSidebarTreeNodeByPath(src) : null;
            try
            {
                FileOperationResult<RenamedEntryInfo> renameResult = await _fileManagementCoordinator.TryRenameEntryAsync(_currentPath, entry.Name, newName);
                if (!renameResult.Succeeded)
                {
                    if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                    {
                        CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                    }
                    UpdateStatusKey("StatusRenameFailedWithReason", renameResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                RenamedEntryInfo renamed = renameResult.Value!;
                RenameTextBox.Text = string.Empty;
                HideRenameOverlay();
                _selectedEntryPath = renamed.TargetPath;
                if (!renamed.ChangeNotified)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, "rename");
                }
                if (entry.IsDirectory)
                {
                    if (renamedTreeNode is not null)
                    {
                        UpdateSidebarTreeNodePath(renamedTreeNode, renamed.SourcePath, renamed.TargetPath, newName);
                    }
                    else if (FindSidebarTreeNodeByPath(_currentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                    {
                        await PopulateSidebarTreeChildrenAsync(parentNode, _currentPath, CancellationToken.None, expandAfterLoad: true);
                    }
                }
                ApplyLocalRename(selectedIndex, newName);
                if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                {
                    CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                }
                else
                {
                    _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                }
                UpdateStatusKey("StatusRenameSuccess", oldName, newName);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                {
                    CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                }
                UpdateStatusKey("StatusRenameFailedWithReason", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task DeleteEntryAsync(EntryViewModel entry, int selectedIndex, string targetPath, bool recursive)
        {
            try
            {
                if (_appSettings.ConfirmDelete && !await ConfirmDeleteAsync(entry.Name, recursive))
                {
                    UpdateStatusKey("StatusDeleteCanceled");
                    return;
                }

                FileOperationResult<bool> deleteResult = await _fileManagementCoordinator.TryDeleteEntryAsync(targetPath, recursive);
                if (!deleteResult.Succeeded)
                {
                    if (deleteResult.Failure?.Error == FileOperationError.Canceled)
                    {
                        UpdateStatusKey("StatusDeleteCanceled");
                        return;
                    }

                    UpdateStatusKey("StatusDeleteFailedWithReason", deleteResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                bool changeNotified = deleteResult.Value;
                if (!changeNotified)
                {
                    string parentPath = Path.GetDirectoryName(targetPath) ?? _currentPath;
                    EnsurePersistentRefreshFallbackInvalidation(parentPath, "delete");
                }
                ApplyLocalDelete(selectedIndex);
                _ = RefreshCurrentDirectoryInBackgroundAsync(preserveViewport: true);
                UpdateStatusKey("StatusDeleteSuccess", entry.Name, recursive);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusDeleteFailedWithReason", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void UpdateBreadcrumbs(string path)
        {
            Breadcrumbs.Clear();
            _breadcrumbWidthsReady = false;
            _breadcrumbVisibleStartIndex = -1;
            _hiddenBreadcrumbItems.Clear();
            VisibleBreadcrumbs.Clear();
            if (BreadcrumbItemsControl is not null)
            {
                BreadcrumbItemsControl.Opacity = 0;
            }
            if (OverflowBreadcrumbButton is not null)
            {
                OverflowBreadcrumbButton.Visibility = Visibility.Collapsed;
            }

            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                Breadcrumbs.Add(new BreadcrumbItemViewModel
                {
                    Label = S("SidebarMyComputer"),
                    FullPath = ShellMyComputerPath,
                    HasChildren = false,
                    IconGlyph = BreadcrumbMyComputerGlyph,
                    ChevronVisibility = Visibility.Collapsed,
                    IsLast = true,
                    MeasuredWidth = 0
                });
                return;
            }

            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;

            Breadcrumbs.Add(new BreadcrumbItemViewModel
            {
                Label = S("SidebarMyComputer"),
                FullPath = ShellMyComputerPath,
                HasChildren = HasBreadcrumbChildren(ShellMyComputerPath),
                IconGlyph = BreadcrumbMyComputerGlyph,
                ChevronVisibility = Visibility.Collapsed
            });

            if (!string.IsNullOrEmpty(root))
            {
                Breadcrumbs.Add(new BreadcrumbItemViewModel
                {
                    Label = root,
                    FullPath = root,
                    HasChildren = HasBreadcrumbChildren(root),
                    ChevronVisibility = Visibility.Collapsed
                });
            }

            string remaining = fullPath.Substring(root.Length).Trim('\\');
            if (!string.IsNullOrEmpty(remaining))
            {
                string current = root;
                string[] parts = remaining.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    current = string.IsNullOrEmpty(current) ? part : Path.Combine(current, part);
                    Breadcrumbs.Add(new BreadcrumbItemViewModel
                    {
                        Label = part,
                        FullPath = current,
                        HasChildren = HasBreadcrumbChildren(current),
                        ChevronVisibility = Visibility.Collapsed
                    });
                }
            }

            for (int i = 0; i < Breadcrumbs.Count; i++)
            {
                BreadcrumbItemViewModel item = Breadcrumbs[i];
                item.IsLast = i == Breadcrumbs.Count - 1;
                item.ChevronVisibility = item.HasChildren && !item.IsLast
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                item.MeasuredWidth = 0;
            }
        }

        private void AddressBreadcrumbBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVisibleBreadcrumbs();
        }

        private void UpdateVisibleBreadcrumbs()
        {
            if (Breadcrumbs.Count == 0)
            {
                _hiddenBreadcrumbItems.Clear();
                VisibleBreadcrumbs.Clear();
                if (OverflowBreadcrumbButton is not null)
                {
                    OverflowBreadcrumbButton.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (!_breadcrumbWidthsReady)
            {
                return;
            }

            int visibleStartIndex = CalculateVisibleBreadcrumbStartIndex();
            if (visibleStartIndex == _breadcrumbVisibleStartIndex)
            {
                return;
            }

            _breadcrumbVisibleStartIndex = visibleStartIndex;
            _hiddenBreadcrumbItems.Clear();
            for (int i = 0; i < visibleStartIndex; i++)
            {
                _hiddenBreadcrumbItems.Add(Breadcrumbs[i]);
            }

            var visibleItems = new List<BreadcrumbItemViewModel>();
            for (int i = visibleStartIndex; i < Breadcrumbs.Count; i++)
            {
                visibleItems.Add(Breadcrumbs[i]);
            }

            ResetVisibleBreadcrumbs(visibleItems);

            if (BreadcrumbItemsControl is not null)
            {
                BreadcrumbItemsControl.Opacity = 1;
            }
            if (OverflowBreadcrumbButton is not null)
            {
                OverflowBreadcrumbButton.Visibility = visibleStartIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private int CalculateVisibleBreadcrumbStartIndex()
        {
            Border? border = AddressBreadcrumbBorder;
            double availableWidth = border?.ActualWidth ?? 0;
            if (border is null || availableWidth <= 0)
            {
                return 0;
            }

            Thickness padding = border.Padding;
            Thickness borderThickness = border.BorderThickness;
            availableWidth -= padding.Left + padding.Right + borderThickness.Left + borderThickness.Right + BreadcrumbWidthReserve;

            double usedWidth = 0;

            for (int i = Breadcrumbs.Count - 1; i >= 0; i--)
            {
                double itemWidth = Breadcrumbs[i].MeasuredWidth;
                double requiredWidth = usedWidth + itemWidth;
                if (i < Breadcrumbs.Count - 1)
                {
                    requiredWidth += BreadcrumbItemSpacing;
                }

                if (i > 0)
                {
                    requiredWidth += BreadcrumbOverflowButtonWidth;
                }

                if (i == Breadcrumbs.Count - 1 || requiredWidth <= availableWidth)
                {
                    usedWidth += itemWidth;
                    if (i < Breadcrumbs.Count - 1)
                    {
                        usedWidth += BreadcrumbItemSpacing;
                    }
                    continue;
                }

                return i + 1;
            }

            return 0;
        }

        private void ResetVisibleBreadcrumbs(IEnumerable<BreadcrumbItemViewModel> items)
        {
            VisibleBreadcrumbs.Clear();
            foreach (BreadcrumbItemViewModel item in items)
            {
                VisibleBreadcrumbs.Add(item);
            }
        }

        private void MeasureBreadcrumbItem_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement);
        }

        private void MeasureBreadcrumbItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement);
        }

        private void UpdateMeasuredBreadcrumbWidth(FrameworkElement? element)
        {
            if (element?.DataContext is not BreadcrumbItemViewModel item || element.ActualWidth <= 0)
            {
                return;
            }

            item.MeasuredWidth = element.ActualWidth;
            if (!_breadcrumbWidthsReady && AreAllBreadcrumbWidthsMeasured())
            {
                _breadcrumbWidthsReady = true;
                UpdateVisibleBreadcrumbs();
            }
        }

        private bool AreAllBreadcrumbWidthsMeasured()
        {
            for (int i = 0; i < Breadcrumbs.Count; i++)
            {
                if (Breadcrumbs[i].MeasuredWidth <= 0)
                {
                    return false;
                }
            }

            return Breadcrumbs.Count > 0;
        }

        private async Task<string?> PromptRenameAsync(string oldName)
        {
            var input = new TextBox
            {
                Text = oldName,
                MinWidth = 280
            };

            var dialog = new ContentDialog
            {
                Title = S("DialogRenameTitle"),
                Content = input,
                PrimaryButtonText = S("DialogRenamePrimaryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };

            if (this.Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return input.Text?.Trim();
        }

        private async Task<bool> ConfirmDeleteAsync(string name, bool recursive)
        {
            var dialog = new ContentDialog
            {
                Title = S("DialogDeleteTitle"),
                Content = SF("DialogDeleteContent", name, recursive),
                PrimaryButtonText = S("DialogDeletePrimaryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Close
            };

            if (this.Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        private void LanguageToggleButton_Click(object sender, RoutedEventArgs e)
        {
            string language = LocalizedStrings.Instance.ToggleDebugLanguage();
            string exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                UpdateStatus(SF("StatusLanguageSwitched", language));
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--lang={language}",
                UseShellExecute = true
            });

            Close();
        }

        private void EnterAddressEditMode(bool selectAll)
        {
            _addressInlineSession ??= new InlineEditSession(
                CommitAddressEditIfActiveAsync,
                CancelAddressEdit,
                source => ReferenceEquals(source, PathTextBox) || IsDescendantOf(source, PathTextBox));

            _inlineEditCoordinator.CancelActiveSession();
            _inlineEditCoordinator.BeginSession(_addressInlineSession);
            AddressBreadcrumbBorder.Visibility = Visibility.Collapsed;
            PathTextBox.Visibility = Visibility.Visible;
            PathTextBox.Text = _currentPath;
            PathTextBox.Focus(FocusState.Programmatic);
            if (selectAll)
            {
                PathTextBox.SelectAll();
            }
            else
            {
                PathTextBox.SelectionStart = PathTextBox.Text.Length;
            }
        }

        private void ExitAddressEditMode(bool commit)
        {
            if (_addressInlineSession is not null)
            {
                _inlineEditCoordinator.ClearSession(_addressInlineSession);
            }

            if (!commit)
            {
                PathTextBox.Text = _currentPath;
            }

            PathTextBox.Visibility = Visibility.Collapsed;
            AddressBreadcrumbBorder.Visibility = Visibility.Visible;
        }

        private async Task CommitAddressEditIfActiveAsync()
        {
            if (PathTextBox.Visibility != Visibility.Visible)
            {
                return;
            }

            string targetPath = PathTextBox.Text.Trim();
            await NavigateToPathAsync(targetPath, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private void CancelAddressEdit()
        {
            ExitAddressEditMode(commit: false);
        }

        private bool IsFocusedElementWithinAddressEdit()
        {
            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            return ReferenceEquals(focused, PathTextBox) || IsDescendantOf(focused, PathTextBox);
        }

        private void UpdateNavButtonsState()
        {
            BackButton.IsEnabled = _backStack.Count > 0;
            ForwardButton.IsEnabled = _forwardStack.Count > 0;
        }
    }

    public sealed class EntryViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _fullPath = string.Empty;
        private string _type = string.Empty;
        private string _iconGlyph = "\uE8A5";
        private Brush _iconForeground = new SolidColorBrush(Colors.DimGray);
        private ulong _mftRef;
        private string _sizeText = string.Empty;
        private string _modifiedText = string.Empty;
        private bool _isDirectory;
        private bool _isLink;
        private bool _isLoaded;
        private bool _isMetadataLoaded;
        private bool _isPendingCreate;
        private bool _pendingCreateIsDirectory;
        private bool _isExplicitlySelected;
        private bool _isKeyboardAnchor;
        private bool _isSelectionActive = true;
        private string _pendingName = string.Empty;
        private bool _isNameEditing;
        private long? _sizeBytes;
        private DateTime? _modifiedAt;
        private bool _isGroupHeader;
        private string _groupKey = string.Empty;
        private int _groupItemCount;
        private bool _isGroupExpanded;
        private string _groupHeaderText = string.Empty;
        private Visibility _headerRowVisibility = Visibility.Collapsed;
        private Visibility _detailsRowVisibility = Visibility.Visible;
        private Visibility _listRowVisibility = Visibility.Collapsed;
        private Thickness _detailsGroupHeaderMargin = new(0);

        public event PropertyChangedEventHandler? PropertyChanged;

        public static EntryViewModel CreateGroupHeader(string groupKey, string groupHeaderText, int groupItemCount, bool isExpanded)
        {
            return new EntryViewModel
            {
                Name = groupHeaderText,
                GroupKey = groupKey,
                GroupHeaderText = groupHeaderText,
                GroupItemCount = groupItemCount,
                IsGroupExpanded = isExpanded,
                IsGroupHeader = true,
                IsLoaded = false,
                Type = string.Empty,
                SizeText = string.Empty,
                ModifiedText = string.Empty,
                IconGlyph = string.Empty
            };
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name == value)
                {
                    return;
                }
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath == value)
                {
                    return;
                }
                _fullPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullPath)));
            }
        }

        public string Type
        {
            get => _type;
            set
            {
                if (_type == value)
                {
                    return;
                }
                _type = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            }
        }

        public string IconGlyph
        {
            get => _iconGlyph;
            set
            {
                if (_iconGlyph == value)
                {
                    return;
                }
                _iconGlyph = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconGlyph)));
            }
        }

        public Brush IconForeground
        {
            get => _iconForeground;
            set
            {
                if (ReferenceEquals(_iconForeground, value))
                {
                    return;
                }
                _iconForeground = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconForeground)));
            }
        }

        public ulong MftRef
        {
            get => _mftRef;
            set
            {
                if (_mftRef == value)
                {
                    return;
                }
                _mftRef = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MftRef)));
            }
        }

        public string SizeText
        {
            get => _sizeText;
            set
            {
                if (_sizeText == value)
                {
                    return;
                }
                _sizeText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
            }
        }

        public long? SizeBytes
        {
            get => _sizeBytes;
            set
            {
                if (_sizeBytes == value)
                {
                    return;
                }
                _sizeBytes = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeBytes)));
            }
        }

        public string ModifiedText
        {
            get => _modifiedText;
            set
            {
                if (_modifiedText == value)
                {
                    return;
                }
                _modifiedText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedText)));
            }
        }

        public DateTime? ModifiedAt
        {
            get => _modifiedAt;
            set
            {
                if (_modifiedAt == value)
                {
                    return;
                }
                _modifiedAt = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ModifiedAt)));
            }
        }

        public bool IsDirectory
        {
            get => _isDirectory;
            set
            {
                if (_isDirectory == value)
                {
                    return;
                }
                _isDirectory = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDirectory)));
            }
        }

        public bool IsLink
        {
            get => _isLink;
            set
            {
                if (_isLink == value)
                {
                    return;
                }
                _isLink = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLink)));
            }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                if (_isLoaded == value)
                {
                    return;
                }
                _isLoaded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLoaded)));
            }
        }

        public bool IsGroupHeader
        {
            get => _isGroupHeader;
            set
            {
                if (_isGroupHeader == value)
                {
                    return;
                }
                _isGroupHeader = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGroupHeader)));
            }
        }

        public string GroupKey
        {
            get => _groupKey;
            set
            {
                if (_groupKey == value)
                {
                    return;
                }
                _groupKey = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupKey)));
            }
        }

        public int GroupItemCount
        {
            get => _groupItemCount;
            set
            {
                if (_groupItemCount == value)
                {
                    return;
                }
                _groupItemCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupItemCount)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupCountText)));
            }
        }

        public bool IsGroupExpanded
        {
            get => _isGroupExpanded;
            set
            {
                if (_isGroupExpanded == value)
                {
                    return;
                }
                _isGroupExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsGroupExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupExpandGlyph)));
            }
        }

        public string GroupHeaderText
        {
            get => _groupHeaderText;
            set
            {
                if (_groupHeaderText == value)
                {
                    return;
                }
                _groupHeaderText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupHeaderText)));
            }
        }

        public Visibility HeaderRowVisibility
        {
            get => _headerRowVisibility;
            set
            {
                if (_headerRowVisibility == value)
                {
                    return;
                }
                _headerRowVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HeaderRowVisibility)));
            }
        }

        public Visibility DetailsRowVisibility
        {
            get => _detailsRowVisibility;
            set
            {
                if (_detailsRowVisibility == value)
                {
                    return;
                }
                _detailsRowVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsRowVisibility)));
            }
        }

        public Visibility ListRowVisibility
        {
            get => _listRowVisibility;
            set
            {
                if (_listRowVisibility == value)
                {
                    return;
                }
                _listRowVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ListRowVisibility)));
            }
        }

        public Thickness DetailsGroupHeaderMargin
        {
            get => _detailsGroupHeaderMargin;
            set
            {
                if (_detailsGroupHeaderMargin.Equals(value))
                {
                    return;
                }
                _detailsGroupHeaderMargin = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsGroupHeaderMargin)));
            }
        }

        public bool IsMetadataLoaded
        {
            get => _isMetadataLoaded;
            set
            {
                if (_isMetadataLoaded == value)
                {
                    return;
                }
                _isMetadataLoaded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMetadataLoaded)));
            }
        }

        public bool IsPendingCreate
        {
            get => _isPendingCreate;
            set
            {
                if (_isPendingCreate == value)
                {
                    return;
                }
                _isPendingCreate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPendingCreate)));
            }
        }

        public bool PendingCreateIsDirectory
        {
            get => _pendingCreateIsDirectory;
            set
            {
                if (_pendingCreateIsDirectory == value)
                {
                    return;
                }
                _pendingCreateIsDirectory = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingCreateIsDirectory)));
            }
        }

        public bool IsExplicitlySelected
        {
            get => _isExplicitlySelected;
            set
            {
                if (_isExplicitlySelected == value)
                {
                    return;
                }
                _isExplicitlySelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExplicitlySelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowBackground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowSelectionIndicatorVisibility)));
            }
        }

        public bool IsKeyboardAnchor
        {
            get => _isKeyboardAnchor;
            set
            {
                if (_isKeyboardAnchor == value)
                {
                    return;
                }

                _isKeyboardAnchor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsKeyboardAnchor)));
            }
        }

        public bool IsSelectionActive
        {
            get => _isSelectionActive;
            set
            {
                if (_isSelectionActive == value)
                {
                    return;
                }

                _isSelectionActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectionActive)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectionIndicatorBrush)));
            }
        }

        public string PendingName
        {
            get => _pendingName;
            set
            {
                if (_pendingName == value)
                {
                    return;
                }
                _pendingName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PendingName)));
            }
        }

        public bool IsNameEditing
        {
            get => _isNameEditing;
            set
            {
                if (_isNameEditing == value)
                {
                    return;
                }
                _isNameEditing = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNameEditing)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsNameReadOnly)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditorBackground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditorBorderThickness)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameDisplayVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameEditorVisibility)));
            }
        }

        public bool IsNameReadOnly => !_isNameEditing;

        public Brush NameEditorBackground => _isNameEditing
            ? new SolidColorBrush(ColorHelper.FromArgb(0x22, 0x80, 0x80, 0x80))
            : new SolidColorBrush(Colors.Transparent);

        public Thickness NameEditorBorderThickness => _isNameEditing ? new Thickness(1) : new Thickness(0);

        public Visibility NameDisplayVisibility => _isNameEditing ? Visibility.Collapsed : Visibility.Visible;

        public Visibility NameEditorVisibility => _isNameEditing ? Visibility.Visible : Visibility.Collapsed;

        public string GroupCountText => _groupItemCount > 0 ? $"({_groupItemCount})" : string.Empty;

        public string GroupExpandGlyph => _isGroupExpanded ? "\uE70D" : "\uE76C";

        public Brush SelectionIndicatorBrush => ResolveSelectionIndicatorBrush(_isSelectionActive);

        public Brush RowBackground => _isExplicitlySelected
            ? new SolidColorBrush(ColorHelper.FromArgb(0x14, 0x80, 0x80, 0x80))
            : new SolidColorBrush(Colors.Transparent);

        public Visibility RowSelectionIndicatorVisibility => _isExplicitlySelected ? Visibility.Visible : Visibility.Collapsed;

        private static Brush ResolveSelectionIndicatorBrush(bool isActive)
        {
            string resourceKey = isActive ? "ListViewItemSelectionIndicatorBrush" : "TextFillColorDisabledBrush";
            if (Application.Current.Resources.TryGetValue(resourceKey, out object? value) && value is Brush brush)
            {
                return brush;
            }

            return new SolidColorBrush(isActive ? ColorHelper.FromArgb(0xFF, 0x00, 0x78, 0xD4) : ColorHelper.FromArgb(0xFF, 0xC8, 0xC8, 0xC8));
        }
    }

    public sealed class BreadcrumbItemViewModel : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _fullPath = string.Empty;
        private string _iconGlyph = string.Empty;
        private bool _hasChildren;
        private bool _isLast;
        private double _measuredWidth;
        private Visibility _chevronVisibility = Visibility.Visible;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label
        {
            get => _label;
            set
            {
                if (_label == value)
                {
                    return;
                }
                _label = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            }
        }

        public string FullPath
        {
            get => _fullPath;
            set
            {
                if (_fullPath == value)
                {
                    return;
                }
                _fullPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullPath)));
            }
        }

        public string IconGlyph
        {
            get => _iconGlyph;
            set
            {
                if (_iconGlyph == value)
                {
                    return;
                }

                _iconGlyph = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconGlyph)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconVisibility)));
            }
        }

        public bool HasChildren
        {
            get => _hasChildren;
            set
            {
                if (_hasChildren == value)
                {
                    return;
                }
                _hasChildren = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasChildren)));
            }
        }

        public bool IsLast
        {
            get => _isLast;
            set
            {
                if (_isLast == value)
                {
                    return;
                }
                _isLast = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLast)));
            }
        }

        public Visibility ChevronVisibility
        {
            get => _chevronVisibility;
            set
            {
                if (_chevronVisibility == value)
                {
                    return;
                }
                _chevronVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ChevronVisibility)));
            }
        }

        public Visibility IconVisibility => string.IsNullOrWhiteSpace(_iconGlyph)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public double MeasuredWidth
        {
            get => _measuredWidth;
            set
            {
                if (Math.Abs(_measuredWidth - value) < 0.1)
                {
                    return;
                }

                _measuredWidth = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MeasuredWidth)));
            }
        }
    }

    public sealed class SidebarTreeEntry
    {
        public SidebarTreeEntry(string name, string fullPath, string iconGlyph = "\uE8B7")
        {
            Name = name;
            FullPath = fullPath;
            IconGlyph = iconGlyph;
        }

        public string Name { get; }
        public string FullPath { get; }
        public string IconGlyph { get; }
    }


    internal static partial class NativeMethods
    {
        internal const uint OAIF_EXEC = 0x00000004;
        internal const uint OAIF_HIDE_REGISTRATION = 0x00000020;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct OpenAsInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            internal string FilePath;

            [MarshalAs(UnmanagedType.LPWStr)]
            internal string? ClassName;

            internal uint Flags;
        }

        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow(IntPtr appWindow, in Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
        internal static partial IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [LibraryImport("user32.dll", EntryPoint = "SetCursor", SetLastError = true)]
        internal static partial IntPtr SetCursor(IntPtr hCursor);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo openAsInfo);

        internal static DataTransferManager GetDataTransferManagerForWindow(IntPtr hwnd)
        {
            const string runtimeClassName = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            Guid iid = new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
            using var factory = ActivationFactory.Get(runtimeClassName);
            var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
            IntPtr result = interop.GetForWindow(hwnd, iid);
            return MarshalInterface<DataTransferManager>.FromAbi(result);
        }

        internal static void ShowShareUIForWindow(IntPtr hwnd)
        {
            const string runtimeClassName = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            using var factory = ActivationFactory.Get(runtimeClassName);
            var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
            interop.ShowShareUIForWindow(hwnd);
        }
    }

}
