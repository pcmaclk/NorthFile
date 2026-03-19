using FileExplorerUI.Commands;
using FileExplorerUI.Controls;
using FileExplorerUI.Interop;
using FileExplorerUI.Services;
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
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Globalization;
using Windows.Foundation;
using WinRT.Interop;

namespace FileExplorerUI
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        private sealed record EntriesContextRequest(
            UIElement Anchor,
            Point Position,
            EntryViewModel? Entry,
            bool IsItemTarget);

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
        private readonly FileCommandCatalog _fileCommandCatalog = new();
        private MenuFlyout? _sidebarTreeContextFlyout;
        private SidebarTreeEntry? _pendingSidebarTreeContextEntry;
        private Canvas? _sidebarTreeRenameOverlayCanvas;
        private Border? _sidebarTreeRenameOverlayBorder;
        private TextBox? _sidebarTreeRenameTextBox;
        private ControlTemplate? _renameOverlayTextBoxTemplate;
        private SidebarTreeEntry? _activeSidebarTreeRenameEntry;
        private bool _isCommittingSidebarTreeRename;

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
        private static string SF(string key, params object[] args) => string.Format(S(key), args);
        private void UpdateStatusKey(string key, params object[] args) => UpdateStatus(SF(key, args));
        private static string CreateKindLabel(bool isDirectory) => S(isDirectory ? "CreateKindFolder" : "CreateKindFile");

        public event PropertyChangedEventHandler? PropertyChanged;

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
                DetailsRowWidth = value + 20;
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
            }
        }

        private const uint InitialPageSize = 96;
        private const uint MinPageSize = 64;
        private const uint MaxPageSize = 1000;
        private readonly ObservableCollection<EntryViewModel> _entries = new();
        public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = new();
        public ObservableCollection<BreadcrumbItemViewModel> VisibleBreadcrumbs { get; } = new();
        private ulong _nextCursor;
        private bool _hasMore;
        private bool _isLoading;
        private bool _entriesFlyoutOpen;
        private CommandMenuFlyout? _activeEntriesContextFlyout;
        private bool _entriesPointerHooked;
        private string _currentPath = ShellMyComputerPath;
        private string? _selectedEntryPath;
        private uint _currentPageSize = InitialPageSize;
        private uint _lastFetchMs;
        private uint _totalEntries;
        private DirectorySortMode _currentSortMode = DirectorySortMode.FolderFirstNameAsc;
        private ScrollViewer? _listScrollViewer;
        private double _lastListHorizontalOffset = double.NaN;
        private double _lastListVerticalOffset = double.NaN;
        private double _estimatedItemHeight = 32.0;
        private Brush? _pathDefaultBorderBrush;
        private readonly Stack<string> _backStack = new();
        private readonly Stack<string> _forwardStack = new();
        private string _currentQuery = string.Empty;
        private readonly ExplorerService _explorerService = new();
        private readonly FileManagementCoordinator _fileManagementCoordinator;
        private readonly HashSet<string> _suppressedWatcherRefreshPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _engineVersion;
        private EntryViewModel? _activeRenameOverlayEntry;
        private EntryViewModel? _pendingCreatedEntrySelection;
        private bool _isCommittingRenameOverlay;
        private RustUsnCapability _usnCapability;
        private FileSystemWatcher? _dirWatcher;
        private CancellationTokenSource? _watcherDebounceCts;
        private CancellationTokenSource? _directoryLoadCts;
        private CancellationTokenSource? _metadataPrefetchCts;
        private long _directorySnapshotVersion;
        private CommandDockSide _commandDockSide = CommandDockSide.Top;
        private bool _showCommandDock = false;
        private bool _sidebarInitialized;
        private long _lastWatcherRefreshTick;
        private readonly Dictionary<string, NavigationViewItem> _sidebarPathButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sidebarQuickAccessPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sidebarDrivePaths = new(StringComparer.OrdinalIgnoreCase);
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

        public MainWindow()
        {
            InitializeComponent();
            _fileManagementCoordinator = new FileManagementCoordinator(_explorerService);
            if (Content is FrameworkElement rootElement)
            {
                rootElement.ActualThemeChanged += MainWindowRoot_ActualThemeChanged;
                rootElement.PointerEntered += RootElement_PointerEnteredOrMoved;
                rootElement.PointerMoved += RootElement_PointerEnteredOrMoved;
            }
            PathTextBox.Text = ShellMyComputerPath;
            RegisterColumnSplitterHandlers(HeaderSplitter1);
            RegisterColumnSplitterHandlers(HeaderSplitter2);
            RegisterColumnSplitterHandlers(HeaderSplitter3);
            RegisterColumnSplitterHandlers(HeaderSplitter4);
            RegisterSidebarSplitterHandlers(SidebarSplitter);
            EntriesListView.ItemsSource = _entries;
            FileEntriesContextFlyout.OverlayInputPassThroughElement = EntriesListView;
            FolderEntriesContextFlyout.OverlayInputPassThroughElement = EntriesListView;
            BackgroundEntriesContextFlyout.OverlayInputPassThroughElement = EntriesListView;
            EntriesListView.SizeChanged += EntriesListView_SizeChanged;
            this.SizeChanged += MainWindow_SizeChanged;
            this.Activated += MainWindow_Activated;
            _pathDefaultBorderBrush = PathTextBox.BorderBrush;
            _engineVersion = _explorerService.GetEngineVersion();
            LocalizedStrings.Instance.PropertyChanged += LocalizedStrings_PropertyChanged;
#if !DEBUG
            LanguageToggleButton.Visibility = Visibility.Collapsed;
#endif

            UpdateNavButtonsState();
            UpdateWindowTitle();
            ApplyTitleBarTheme();
            StyledSidebarView.NavigateRequested += StyledSidebarView_NavigateRequested;
            BuildSidebarItems();
            _sidebarInitialized = true;
            ApplyCommandDockLayout();
            _ = LoadFirstPageAsync();
        }

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

        private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            TryResetSystemCursorToArrow();
        }

        private void RootElement_PointerEnteredOrMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitterElement is null)
            {
                TryResetSystemCursorToArrow();
            }
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
                ExitAddressEditMode(commit: false);
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await NavigateToPathAsync(PathTextBox.Text.Trim(), pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private void PathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            ExitAddressEditMode(commit: false);
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
                await LoadPageAsync(_currentPath, cursor: 0, append: false);
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            _currentQuery = SearchTextBox.Text?.Trim() ?? string.Empty;
            await LoadPageAsync(_currentPath, cursor: 0, append: false);
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

        private bool TryGetSelectedLoadedEntry(out EntryViewModel entry)
        {
            if (EntriesListView.SelectedItem is EntryViewModel selected && selected.IsLoaded)
            {
                entry = selected;
                return true;
            }

            entry = null!;
            return false;
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

            int selectedIndex = EntriesListView.SelectedIndex;
            return selectedIndex >= 0 && selectedIndex < _entries.Count;
        }

        private bool CanDeleteSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out _))
            {
                return false;
            }

            int selectedIndex = EntriesListView.SelectedIndex;
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
            if (!TryGetSelectedLoadedEntry(out EntryViewModel entry))
            {
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return Task.CompletedTask;
            }

            int selectedIndex = EntriesListView.SelectedIndex;
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
            if (!TryGetSelectedLoadedEntry(out EntryViewModel entry))
            {
                UpdateStatusKey("StatusDeleteFailedSelectLoaded");
                return;
            }

            int selectedIndex = EntriesListView.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatusKey("StatusDeleteFailedInvalidIndex");
                return;
            }

            bool recursive = RecursiveDeleteCheckBox.IsChecked == true;
            bool confirmed = await ConfirmDeleteAsync(entry.Name, recursive);
            if (!confirmed)
            {
                UpdateStatusKey("StatusDeleteCanceled");
                return;
            }

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
            return PasteIntoCurrentDirectoryAsync();
        }

        private void CopySelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel entry))
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
            if (!TryGetSelectedLoadedEntry(out EntryViewModel entry))
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

        private async Task PasteIntoCurrentDirectoryAsync()
        {
            if (!CanPasteIntoCurrentDirectory())
            {
                UpdateStatusKey("StatusPasteFailedOpenFolderFirst");
                return;
            }

            if (!TryEnsureCurrentDirectoryAvailable(out string pasteError))
            {
                UpdateStatusKey("StatusPasteFailedWithReason", pasteError);
                return;
            }

            if (!_fileManagementCoordinator.HasAvailablePasteItems())
            {
                UpdateStatusKey("StatusPasteFailedClipboardEmpty");
                return;
            }

            try
            {
                FilePasteResult result = await _fileManagementCoordinator.PasteAsync(_currentPath);
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
                    EnsureRefreshFallbackInvalidation(_currentPath, result.Mode == FileTransferMode.Cut ? "cut-paste" : "copy-paste");
                }

                if (appliedCount == 1 && !string.IsNullOrWhiteSpace(firstAppliedPath))
                {
                    _selectedEntryPath = firstAppliedPath;
                }

                await LoadFirstPageAsync();

                if (appliedDirectory && FindSidebarTreeNodeByPath(_currentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                {
                    await PopulateSidebarTreeChildrenAsync(parentNode, _currentPath, CancellationToken.None, expandAfterLoad: true);
                }

                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);

                UpdateFileCommandStates();
                string modeText = result.Mode == FileTransferMode.Cut ? S("OperationMove") : S("OperationPaste");
                UpdateStatusKey("StatusTransferSuccess", modeText, appliedCount, conflictCount, samePathCount, failureCount);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPasteFailedWithReason", ex.Message);
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
                CreatedEntryInfo created = await _fileManagementCoordinator.CreateEntryAsync(_currentPath, isDirectory);
                if (!created.ChangeNotified)
                {
                    EnsureRefreshFallbackInvalidation(_currentPath, isDirectory ? "create-folder" : "create-file");
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
                UpdateStatusKey("StatusCreateFailed", ex.Message);
            }
        }

        private void EntriesListView_Loaded(object sender, RoutedEventArgs e)
        {
            _listScrollViewer ??= FindDescendant<ScrollViewer>(EntriesListView);
            if (_listScrollViewer is not null)
            {
                _listScrollViewer.ViewChanged -= ListScrollViewer_ViewChanged;
                _listScrollViewer.ViewChanged += ListScrollViewer_ViewChanged;
                _lastListHorizontalOffset = _listScrollViewer.HorizontalOffset;
                _lastListVerticalOffset = _listScrollViewer.VerticalOffset;
            }
            if (!_entriesPointerHooked)
            {
                EntriesListView.AddHandler(
                    UIElement.PointerPressedEvent,
                    new PointerEventHandler(EntriesListView_PointerPressedPreview),
                    true);
                _entriesPointerHooked = true;
            }
            UpdateEstimatedItemHeight();
            RequestViewportWork();
        }

        private async void RenameOverlayTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await Task.Delay(1);
            if (IsFocusedElementWithinRenameOverlay())
            {
                return;
            }

            await CommitRenameOverlayAsync();
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
            }
        }

        private async void ListScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer)
            {
                return;
            }

            bool scrolled =
                double.IsNaN(_lastListHorizontalOffset) ||
                double.IsNaN(_lastListVerticalOffset) ||
                Math.Abs(viewer.HorizontalOffset - _lastListHorizontalOffset) > 0.1 ||
                Math.Abs(viewer.VerticalOffset - _lastListVerticalOffset) > 0.1;

            _lastListHorizontalOffset = viewer.HorizontalOffset;
            _lastListVerticalOffset = viewer.VerticalOffset;

            if (scrolled && _entriesFlyoutOpen && (_activeEntriesContextFlyout?.IsOpen ?? false))
            {
                HideActiveEntriesContextFlyout();
            }

            if (DetailsHeaderTranslateTransform is not null)
            {
                DetailsHeaderTranslateTransform.X = -viewer.HorizontalOffset;
            }

            UpdateEstimatedItemHeight();
            int estimatedIndex = EstimateViewportBottomIndex(viewer);
            if (_hasMore)
            {
                await EnsureDataForIndexAsync(estimatedIndex);
            }
            RequestMetadataForCurrentViewport();
            UpdateRenameOverlayPosition();
        }

        private void EntriesListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateEstimatedItemHeight();
            RequestViewportWork();
            UpdateRenameOverlayPosition();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            ApplySidebarWidthLayout();
            UpdateEstimatedItemHeight();
            RequestViewportWork();
            UpdateVisibleBreadcrumbs();
            UpdateRenameOverlayPosition();
            TryResetSystemCursorToArrow();
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
                UpdateFileCommandStates();
                return;
            }

            UpdateUsnCapability(_currentPath);
            ConfigureDirectoryWatcher(_currentPath);
            EnsureRefreshFallbackInvalidation(_currentPath, "manual_load");
            await LoadPageAsync(_currentPath, cursor: 0, append: false);
        }

        private async Task NavigateToPathAsync(string path, bool pushHistory)
        {
            HideRenameOverlay();

            string target = string.IsNullOrWhiteSpace(path) ? @"C:\" : path.Trim();
            if (string.Equals(target, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                if (pushHistory && !string.Equals(_currentPath, target, StringComparison.OrdinalIgnoreCase))
                {
                    _backStack.Push(_currentPath);
                    _forwardStack.Clear();
                }

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

            if (!_explorerService.DirectoryExists(target))
            {
                SetPathInputInvalid();
                UpdateStatusKey("StatusPathNotFound", target);
                return;
            }
            SetPathInputValid();

            if (!await CanReadDirectoryAsync(target))
            {
                return;
            }

            if (pushHistory && !string.Equals(_currentPath, target, StringComparison.OrdinalIgnoreCase))
            {
                _backStack.Push(_currentPath);
                _forwardStack.Clear();
            }

            _currentPath = target;
            PathTextBox.Text = target;
            _currentQuery = string.Empty;
            SearchTextBox.Text = string.Empty;
            UpdateBreadcrumbs(target);
            UpdateNavButtonsState();
            _ = SelectSidebarTreePathAsync(_currentPath);
            await LoadFirstPageAsync();
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

        private async Task LoadPageAsync(string path, ulong cursor, bool append)
        {
            if (_isLoading)
            {
                return;
            }

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
                uint requestedPageSize = _currentPageSize;
                Stopwatch sw = Stopwatch.StartNew();
                FileBatchPage page;
                bool ok;
                int rustErrorCode;
                string rustErrorMessage;
                if (string.IsNullOrWhiteSpace(_currentQuery))
                {
                    (ok, page, rustErrorCode, rustErrorMessage) = await Task.Run(
                        () =>
                        {
                            bool success = _explorerService.TryReadDirectoryRowsAuto(
                                path,
                                cursor,
                                requestedPageSize,
                                _lastFetchMs,
                                _currentSortMode,
                                out FileBatchPage p,
                                out int code,
                                out string msg
                            );
                            return (success, p, code, msg);
                        }
                    );
                }
                else
                {
                    string query = _currentQuery;
                    (ok, page, rustErrorCode, rustErrorMessage) = await Task.Run(
                        () =>
                        {
                            bool success = _explorerService.TrySearchDirectoryRowsAuto(
                                path,
                                query,
                                cursor,
                                requestedPageSize,
                                _lastFetchMs,
                                _currentSortMode,
                                out FileBatchPage p,
                                out int code,
                                out string msg
                            );
                            return (success, p, code, msg);
                        }
                    );
                }

                if (!ok)
                {
                    if (IsRustAccessDenied(rustErrorCode, rustErrorMessage))
                    {
                        _hasMore = false;
                        _nextCursor = 0;
                        if (!append)
                        {
                            _entries.Clear();
                            _totalEntries = 0;
                        }

                        UpdateStatusKey("StatusPathAccessDeniedSkip", path);
                        return;
                    }

                    throw new InvalidOperationException($"Rust error {rustErrorCode}: {rustErrorMessage}");
                }

                sw.Stop();
                _lastFetchMs = (uint)Math.Clamp(sw.ElapsedMilliseconds, 0, int.MaxValue);
                if (!append)
                {
                    _entries.Clear();
                }

                _totalEntries = page.TotalEntries;
                EnsurePlaceholderCount((int)_totalEntries);
                FillPageRows((int)cursor, page.Rows);
                if (!append)
                {
                    RestoreListSelectionByPath(ensureVisible: false);
                }
                UpdateFileCommandStates();
                RequestMetadataForCurrentViewport();

                _nextCursor = page.NextCursor;
                _hasMore = page.HasMore;
                _currentPageSize = ClampPageSize(page.SuggestedNextLimit, requestedPageSize);
                string source = _explorerService.DescribeBatchSource(page.SourceKind);

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
            }
            catch (Exception ex)
            {
                _lastTitleWasReadFailed = true;
                UpdateWindowTitle();
                UpdateStatusKey("StatusPathError", path, ex.Message);
            }
            finally
            {
                _isLoading = false;
                LoadButton.IsEnabled = true;
                NextButton.IsEnabled = _hasMore;
                SidebarNavView.IsEnabled = true;
                StyledSidebarView.IsEnabled = true;
                UpdateFileCommandStates();
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

        private void EnsureRefreshFallbackInvalidation(string path, string reason)
        {
            // Week4 strategy: if USN capability is unavailable, force invalidate to keep consistency.
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
                UpdateStatusKey("StatusPathInvalidateWarning", path, reason, ex.Message);
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

            ApplySidebarCompactState(_isSidebarCompact);
            UpdateSidebarSelectionOnly();
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
                UpdateStatusKey("StatusPathError", path, ex.Message);
                return false;
            }
        }

        private void PopulateMyComputerEntries()
        {
            _entries.Clear();
            _selectedEntryPath = null;
            foreach (DriveInfo drive in _explorerService.GetReadyDrives())
            {
                string root = drive.RootDirectory.FullName;
                string label = drive.Name.TrimEnd('\\');
                string type = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? S("DriveTypeLocalDisk")
                    : SF("DriveTypeVolumeFormat", drive.VolumeLabel, drive.DriveFormat);

                _entries.Add(new EntryViewModel
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

            _totalEntries = (uint)_entries.Count;
            _nextCursor = 0;
            _hasMore = false;
            EntriesListView.SelectedItem = null;
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
                UpdateStatusKey("StatusSidebarTreeExpandFailed", ex.Message);
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
                await NavigateToPathAsync(entry.FullPath, pushHistory: true);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarTreeNavFailed", ex.Message);
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

            if (IsCurrentPath(target))
            {
                return;
            }

            try
            {
                await NavigateToPathAsync(target, pushHistory: true);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", ex.Message);
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

            if (IsCurrentPath(e.Path))
            {
                StyledSidebarView.SetSelectedPath(_currentPath);
                return;
            }

            try
            {
                await NavigateToPathAsync(e.Path, pushHistory: true);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", ex.Message);
                StyledSidebarView.SetSelectedPath(_currentPath);
            }
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

                        EnsureRefreshFallbackInvalidation(_currentPath, $"watcher_{reason}");
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

        private int EstimateViewportIndex(ScrollViewer viewer)
        {
            if (_entries.Count <= 1)
            {
                return 0;
            }

            double scrollable = Math.Max(1.0, viewer.ScrollableHeight);
            double progress = viewer.VerticalOffset / scrollable;
            progress = Math.Clamp(progress, 0.0, 1.0);
            return (int)Math.Round(progress * (_entries.Count - 1));
        }

        private int EstimateViewportBottomIndex(ScrollViewer viewer)
        {
            int topIndex = EstimateViewportIndex(viewer);
            int visibleCount = Math.Max(1, (int)Math.Ceiling(viewer.ViewportHeight / _estimatedItemHeight));
            int bottom = topIndex + visibleCount;
            return Math.Min(_entries.Count - 1, Math.Max(0, bottom));
        }

        private async Task EnsureDataForIndexAsync(int index)
        {
            if (!_hasMore)
            {
                return;
            }

            int dynamicWindow = Math.Max(32, (int)Math.Ceiling((_listScrollViewer?.ViewportHeight ?? 0) / _estimatedItemHeight) * 2);
            int prefetchWindow = (int)Math.Max(dynamicWindow, _currentPageSize * 2);
            int rounds = 0;
            while (!_isLoading && _hasMore && index >= ((int)_nextCursor - prefetchWindow) && rounds < 6)
            {
                await LoadNextPageAsync();
                rounds++;
            }
        }

        private void RequestViewportWork()
        {
            RequestPrefetchForCurrentViewport();
            RequestMetadataForCurrentViewport();
        }

        private void RequestPrefetchForCurrentViewport()
        {
            if (_listScrollViewer is null || !_hasMore)
            {
                return;
            }

            int idx = EstimateViewportBottomIndex(_listScrollViewer);
            _ = EnsureDataForIndexAsync(idx);
        }

        private void RequestMetadataForCurrentViewport()
        {
            if (_entries.Count == 0)
            {
                CancelAndDispose(ref _metadataPrefetchCts);
                return;
            }

            int visibleStart;
            int visibleEnd;
            if (_listScrollViewer is null)
            {
                visibleStart = 0;
                visibleEnd = Math.Min(_entries.Count - 1, (int)_currentPageSize - 1);
            }
            else
            {
                visibleStart = EstimateViewportIndex(_listScrollViewer);
                visibleEnd = EstimateViewportBottomIndex(_listScrollViewer);
            }

            if (visibleEnd < visibleStart)
            {
                return;
            }

            int visibleCount = Math.Max(1, visibleEnd - visibleStart + 1);
            int lookahead = Math.Max(visibleCount, (int)_currentPageSize);
            int prefetchEnd = Math.Min(_entries.Count - 1, visibleEnd + lookahead);

            List<MetadataWorkItem> visibleItems = CollectMetadataWorkItems(visibleStart, visibleEnd);
            List<MetadataWorkItem> prefetchItems = CollectMetadataWorkItems(visibleEnd + 1, prefetchEnd);
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
            foreach (MetadataWorkItem item in items)
            {
                token.ThrowIfCancellationRequested();

                MetadataPayload payload = BuildMetadataPayload(path, item.Name, item.IsDirectory, item.IsLink, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    ApplyMetadataPayload(snapshotVersion, item, payload);
                });
            }
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
            const double fallback = 32.0;
            double found = 0;
            int sample = Math.Min(_entries.Count, 16);
            for (int i = 0; i < sample; i++)
            {
                if (EntriesListView.ContainerFromIndex(i) is ListViewItem item && item.ActualHeight > 4)
                {
                    found = item.ActualHeight;
                    break;
                }
            }

            _estimatedItemHeight = found > 0 ? found : fallback;
        }

        private void EnsurePlaceholderCount(int target)
        {
            if (target < 0)
            {
                target = 0;
            }

            while (_entries.Count < target)
            {
                _entries.Add(new EntryViewModel
                {
                    Name = "...",
                    PendingName = "...",
                    FullPath = string.Empty,
                    Type = "",
                    IconGlyph = "\uE9CE",
                    IconForeground = FileIconBrush,
                    MftRef = 0,
                    SizeText = "",
                    ModifiedText = "",
                    IsDirectory = false,
                    IsLink = false,
                    IsLoaded = false,
                    IsMetadataLoaded = false
                });
            }

            while (_entries.Count > target)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }
        }

        private void FillPageRows(int startIndex, IReadOnlyList<FileRow> rows)
        {
            if (startIndex < 0 || startIndex >= _entries.Count)
            {
                return;
            }

            int max = Math.Min(rows.Count, _entries.Count - startIndex);
            for (int i = 0; i < max; i++)
            {
                FileRow row = rows[i];
                EntryViewModel current = _entries[startIndex + i];
                current.Name = row.Name;
                current.PendingName = row.Name;
                current.FullPath = Path.Combine(_currentPath, row.Name);
                current.Type = GetEntryTypeText(row.Name, row.IsDirectory, row.IsLink);
                current.IconGlyph = GetEntryIconGlyph(row.IsDirectory, row.IsLink, row.Name);
                current.IconForeground = GetEntryIconBrush(row.IsDirectory, row.IsLink, row.Name);
                current.MftRef = row.MftRef;
                current.SizeText = string.Empty;
                current.ModifiedText = string.Empty;
                current.IsDirectory = row.IsDirectory;
                current.IsLink = row.IsLink;
                current.IsPendingCreate = false;
                current.PendingCreateIsDirectory = false;
                current.IsLoaded = true;
                current.IsMetadataLoaded = false;
            }
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
            try
            {
                var fi = new FileInfo(fullPath);
                if (!fi.Exists)
                {
                    return "-";
                }

                return FormatBytes(fi.Length);
            }
            catch
            {
                return "-";
            }
        }

        private static string GetModifiedTimeText(string fullPath, bool isDirectory)
        {
            try
            {
                DateTime dt = isDirectory
                    ? new DirectoryInfo(fullPath).LastWriteTime
                    : new FileInfo(fullPath).LastWriteTime;
                if (dt == DateTime.MinValue)
                {
                    return "-";
                }

                return dt.ToString("g", CultureInfo.CurrentCulture);
            }
            catch
            {
                return "-";
            }
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
            _selectedEntryPath = entry.FullPath;
            EntriesListView.SelectedItem = entry;
            if (ensureVisible)
            {
                EntriesListView.ScrollIntoView(entry);
            }
        }

        private void RestoreListSelectionByPath(bool ensureVisible)
        {
            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                EntriesListView.SelectedItem = null;
                return;
            }

            SelectEntryByPath(_selectedEntryPath, ensureVisible);
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

        private void EntriesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EntriesListView.SelectedItem is EntryViewModel entry)
            {
                _selectedEntryPath = entry.FullPath;
            }
            else if (!_isLoading)
            {
                _selectedEntryPath = null;
            }

            UpdateFileCommandStates();
        }

        private int GetCreateInsertIndex()
        {
            if (_listScrollViewer is not null && _entries.Count > 0)
            {
                int visibleEnd = EstimateViewportBottomIndex(_listScrollViewer);
                return Math.Min(Math.Max(0, visibleEnd + 1), _entries.Count);
            }

            int loadedCount = 0;
            while (loadedCount < _entries.Count && _entries[loadedCount].IsLoaded)
            {
                loadedCount++;
            }

            return loadedCount;
        }

        private async Task EnsureCreateInsertVisibleAsync(int insertIndex)
        {
            const double createRevealBottomPadding = 0;

            if (_entries.Count == 0)
            {
                return;
            }

            _listScrollViewer ??= FindDescendant<ScrollViewer>(EntriesListView);
            if (_listScrollViewer is null)
            {
                int anchorIndex = Math.Clamp(insertIndex <= 0 ? 0 : insertIndex - 1, 0, _entries.Count - 1);
                EntriesListView.ScrollIntoView(_entries[anchorIndex]);
                await Task.Delay(16);
                EntriesListView.UpdateLayout();
                return;
            }

            await EnsureDataForIndexAsync(insertIndex);
            EntriesListView.UpdateLayout();

            int visibleCount = Math.Max(1, (int)Math.Ceiling(_listScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(_listScrollViewer);
            int visibleEnd = Math.Min(_entries.Count - 1, visibleStart + visibleCount - 1);

            if (insertIndex >= visibleStart && insertIndex <= visibleEnd)
            {
                return;
            }

            if (insertIndex > visibleEnd)
            {
                double targetOffset = Math.Max(
                    0,
                    ((insertIndex + 1) * _estimatedItemHeight) - _listScrollViewer.ViewportHeight + createRevealBottomPadding);
                _listScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            }
            else
            {
                double targetOffset = insertIndex * _estimatedItemHeight;
                _listScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            }
            await Task.Delay(16);
            EntriesListView.UpdateLayout();
        }

        private bool IsIndexInCurrentViewport(int index)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return false;
            }

            _listScrollViewer ??= FindDescendant<ScrollViewer>(EntriesListView);
            if (_listScrollViewer is null)
            {
                return false;
            }

            EntriesListView.UpdateLayout();
            int visibleCount = Math.Max(1, (int)Math.Ceiling(_listScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(_listScrollViewer);
            int visibleEnd = Math.Min(_entries.Count - 1, visibleStart + visibleCount - 1);
            return index >= visibleStart && index <= visibleEnd;
        }

        private void ClearListSelection()
        {
            _selectedEntryPath = null;
            EntriesListView.SelectedItem = null;
        }

        private async Task<bool> StartRenameForCreatedEntryAsync(EntryViewModel entry, int insertIndex)
        {
            await Task.Delay(16);
            EntriesListView.UpdateLayout();

            bool renameStarted = await BeginRenameOverlayAsync(entry, ensureVisible: false, updateSelection: false);
            if (renameStarted)
            {
                return true;
            }

            EntriesListView.ScrollIntoView(entry);
            await Task.Delay(16);
            EntriesListView.UpdateLayout();
            bool retryStarted = await BeginRenameOverlayAsync(entry, ensureVisible: false, updateSelection: false);
            return retryStarted;
        }

        private int FindInsertIndexForEntry(EntryViewModel entry)
        {
            int loadedCount = 0;
            while (loadedCount < _entries.Count && _entries[loadedCount].IsLoaded)
            {
                loadedCount++;
            }

            int index = 0;
            for (; index < loadedCount; index++)
            {
                if (CompareEntries(entry, _entries[index]) < 0)
                {
                    break;
                }
            }

            return index;
        }

        private int CompareEntries(EntryViewModel left, EntryViewModel right)
        {
            if (_currentSortMode == DirectorySortMode.FolderFirstNameAsc)
            {
                if (left.IsDirectory != right.IsDirectory)
                {
                    return left.IsDirectory ? -1 : 1;
                }

                return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        }

        private async Task<bool> BeginRenameOverlayAsync(EntryViewModel entry, bool ensureVisible = true, bool updateSelection = true)
        {
            HideRenameOverlay();
            bool alreadySelected =
                ReferenceEquals(EntriesListView.SelectedItem, entry) ||
                string.Equals(_selectedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

            if (updateSelection && !alreadySelected)
            {
                SelectEntryInList(entry, ensureVisible);
            }
            bool scrolledIntoViewForRetry = false;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(16);
                EntriesListView.UpdateLayout();
                if (!TryPositionRenameOverlay(entry))
                {
                    if (!scrolledIntoViewForRetry && attempt >= 1)
                    {
                        scrolledIntoViewForRetry = true;
                        EntriesListView.ScrollIntoView(entry);
                    }
                    continue;
                }

                _activeRenameOverlayEntry = entry;
                RenameOverlayTextBox.Text = entry.Name;
                RenameOverlayBorder.Visibility = Visibility.Visible;
                RenameOverlayTextBox.Focus(FocusState.Programmatic);
                SelectRenameTargetText(RenameOverlayTextBox, entry);
                return true;
            }

            UpdateStatusKey("StatusRenameFailedCouldNotStartInlineEditor");
            return false;
        }

        private bool TryPositionRenameOverlay(EntryViewModel entry)
        {
            if (EntriesListView.ContainerFromItem(entry) is not ListViewItem item)
            {
                return false;
            }

            if (FindDescendantByName<TextBlock>(item, "EntryNameTextBlock") is not TextBlock anchor)
            {
                return false;
            }

            GeneralTransform transform = anchor.TransformToVisual(RenameOverlayCanvas);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            double overlayHeight = RenameOverlayBorder.ActualHeight > 0
                ? RenameOverlayBorder.ActualHeight
                : (RenameOverlayBorder.Height > 0 ? RenameOverlayBorder.Height : bounds.Height);

            Canvas.SetLeft(RenameOverlayBorder, bounds.X - 2);
            Canvas.SetTop(RenameOverlayBorder, Math.Max(0, bounds.Y + ((bounds.Height - overlayHeight) / 2)));
            RenameOverlayBorder.Width = Math.Max(140, Math.Min(NameColumnWidth.Value - 18, bounds.Width + 12));
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
                    EnsureRefreshFallbackInvalidation(_currentPath, entry.PendingCreateIsDirectory ? "create-folder" : "create-file");
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
                UpdateStatusKey("StatusCreateFailed", ex.Message);
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
        }

        private void HideRenameOverlay()
        {
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

        private void FocusEntriesList()
        {
            EntriesListView.Focus(FocusState.Pointer);
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
            if (!StyledSidebarView.Focus(FocusState.Pointer))
            {
                SidebarNavView?.Focus(FocusState.Pointer);
            }
        }

        private async Task BeginSidebarTreeRenameAsync(SidebarTreeEntry entry)
        {
            if (_sidebarTreeView is null)
            {
                return;
            }

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

            await CommitSidebarTreeRenameAsync();
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

        private void CancelSidebarTreeRename()
        {
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
            bool wasSelected = ReferenceEquals(EntriesListView.SelectedItem, current);
            current.Name = newName;
            current.PendingName = newName;
            current.FullPath = Path.Combine(_currentPath, newName);

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

            if (ReferenceEquals(EntriesListView.SelectedItem, _entries[index]))
            {
                _selectedEntryPath = null;
                EntriesListView.SelectedItem = null;
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
            UpdateFileCommandStates();
        }

        private async Task RefreshCurrentDirectoryInBackgroundAsync()
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                PopulateMyComputerEntries();
                return;
            }

            try
            {
                UpdateUsnCapability(_currentPath);
                ConfigureDirectoryWatcher(_currentPath);
                EnsureRefreshFallbackInvalidation(_currentPath, "background_refresh");
                await LoadPageAsync(_currentPath, cursor: 0, append: false);
            }
            catch
            {
                // Keep local state if background refresh fails; next manual load can recover.
            }
        }

        private async void EntriesListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            EntryViewModel? row = null;
            ListViewItem? item = null;
            if (e.OriginalSource is DependencyObject source)
            {
                item = FindAncestor<ListViewItem>(source);
            }

            if (item is null)
            {
                Point pos = e.GetPosition(EntriesListView);
                item = FindListViewItemAt(pos);
            }

            if (item?.DataContext is EntryViewModel tappedEntry)
            {
                row = tappedEntry;
                SelectEntryInList(tappedEntry, ensureVisible: false);
            }
            else
            {
                row = EntriesListView.SelectedItem as EntryViewModel;
            }

            if (row is null || !row.IsLoaded)
            {
                return;
            }

            string targetPath = string.IsNullOrWhiteSpace(row.FullPath)
                ? Path.Combine(_currentPath, row.Name)
                : row.FullPath;
            if (row.IsDirectory)
            {
                await NavigateToPathAsync(targetPath, pushHistory: true);
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
                UpdateStatusKey("StatusOpenFailed", ex.Message);
            }
        }

        private bool IsEntryAlreadySelected(EntryViewModel entry)
        {
            return ReferenceEquals(EntriesListView.SelectedItem, entry) ||
                   string.Equals(_selectedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        private void EntryRow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement row || row.DataContext is not EntryViewModel entry)
            {
                return;
            }

            var point = e.GetCurrentPoint(row);
            if (!IsEntryAlreadySelected(entry))
            {
                return;
            }

            if (point.Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                return;
            }

        }

        private void EntriesListView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is not ListViewItem item)
            {
                return;
            }

            item.RightTapped -= EntryContainer_RightTapped;
            item.RightTapped += EntryContainer_RightTapped;
        }

        private void EntryContainer_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not ListViewItem item || item.Content is not EntryViewModel entry)
            {
                return;
            }

            _lastEntriesContextItem = entry;

            if (!IsEntryAlreadySelected(entry))
            {
                SelectEntryInList(entry, ensureVisible: false);
            }

            ShowEntriesContextFlyout(new EntriesContextRequest(
                item,
                e.GetPosition(item),
                entry,
                IsItemTarget: true));
            e.Handled = true;
        }

        private void EntriesListView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            Point position = e.GetPosition(EntriesListView);
            if (e.OriginalSource is DependencyObject source &&
                FindAncestor<ListViewItem>(source) is not null)
            {
                e.Handled = true;
                return;
            }

            if (TryFindListViewItemBoundsAt(position, out ListViewItem? hitItem, out _) &&
                hitItem is not null)
            {
                e.Handled = true;
                return;
            }

            _lastEntriesContextItem = null;
            ShowEntriesContextFlyout(new EntriesContextRequest(
                EntriesListView,
                position,
                Entry: null,
                IsItemTarget: false));
            e.Handled = true;
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

        private void UpdateConditionalCommandVisibility(CommandMenuFlyout flyout, string label, int insertIndex, bool shouldShow)
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

                flyout.Commands.Insert(insertIndex, CreateCommandBarItem(label));
                return;
            }

            if (existingIndex >= 0)
            {
                flyout.Commands.RemoveAt(existingIndex);
            }
        }

        private static CommandMenuFlyoutItem CreateCommandBarItem(string label)
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
                Label = label,
                Icon = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = glyph
                }
            };
        }

        private void EntriesListView_PointerPressedPreview(object sender, PointerRoutedEventArgs e)
        {
            if (!_entriesFlyoutOpen)
            {
                return;
            }

            var point = e.GetCurrentPoint(EntriesListView);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            ListViewItem? item = FindListViewItemAt(point.Position)
                                 ?? FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
            if (item?.DataContext is EntryViewModel entry)
            {
                if (!IsEntryAlreadySelected(entry))
                {
                    SelectEntryInList(entry, ensureVisible: false);
                }
            }

            _activeEntriesContextFlyout?.Hide();
        }

        private void StaticEntriesContextFlyout_Opening(object sender, object e)
        {
            _entriesFlyoutOpen = true;
            _activeEntriesContextFlyout = sender as CommandMenuFlyout;
            ResetColumnSplitterCursorState();

            if (ReferenceEquals(sender, FolderEntriesContextFlyout))
            {
                UpdateConditionalCommandVisibility(FolderEntriesContextFlyout, S("CommonPaste"), 2, _fileManagementCoordinator.HasAvailablePasteItems());
            }
            else if (ReferenceEquals(sender, BackgroundEntriesContextFlyout))
            {
                UpdateConditionalCommandVisibility(BackgroundEntriesContextFlyout, S("CommonPaste"), 0, _fileManagementCoordinator.HasAvailablePasteItems());
            }
        }

        private void StaticEntriesContextFlyout_Closed(object sender, object e)
        {
            _entriesFlyoutOpen = false;

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

        private ListViewItem? FindListViewItemAt(Point position)
        {
            foreach (UIElement hit in VisualTreeHelper.FindElementsInHostCoordinates(position, EntriesListView, includeAllElements: true))
            {
                if (hit is ListViewItem direct)
                {
                    return direct;
                }

                if (hit is DependencyObject dep)
                {
                    ListViewItem? ancestor = FindAncestor<ListViewItem>(dep);
                    if (ancestor is not null)
                    {
                        return ancestor;
                    }
                }
            }

            if (position.X > 0)
            {
                Point leftAlignedPosition = new(1, position.Y);
                foreach (UIElement hit in VisualTreeHelper.FindElementsInHostCoordinates(leftAlignedPosition, EntriesListView, includeAllElements: true))
                {
                    if (hit is ListViewItem direct)
                    {
                        return direct;
                    }

                    if (hit is DependencyObject dep)
                    {
                        ListViewItem? ancestor = FindAncestor<ListViewItem>(dep);
                        if (ancestor is not null)
                        {
                            return ancestor;
                        }
                    }
                }
            }

            ListViewItem? verticalMatch = FindListViewItemByVerticalPosition(position.Y);
            if (verticalMatch is not null)
            {
                return verticalMatch;
            }

            return null;
        }

        private bool TryFindListViewItemBoundsAt(Point position, out ListViewItem? item, out Rect bounds)
        {
            item = null;
            bounds = default;

            foreach (UIElement hit in VisualTreeHelper.FindElementsInHostCoordinates(position, EntriesListView, includeAllElements: true))
            {
                if (hit is ListViewItem direct)
                {
                    Rect directBounds = GetListViewItemBounds(direct);
                    if (directBounds.Contains(position))
                    {
                        item = direct;
                        bounds = directBounds;
                        return true;
                    }
                }

                if (hit is DependencyObject dep)
                {
                    ListViewItem? ancestor = FindAncestor<ListViewItem>(dep);
                    if (ancestor is not null)
                    {
                        Rect ancestorBounds = GetListViewItemBounds(ancestor);
                        if (ancestorBounds.Contains(position))
                        {
                            item = ancestor;
                            bounds = ancestorBounds;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private Rect GetListViewItemBounds(ListViewItem item)
        {
            GeneralTransform transform = item.TransformToVisual(EntriesListView);
            return transform.TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
        }

        private ListViewItem? FindListViewItemByVerticalPosition(double y)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (EntriesListView.ContainerFromIndex(i) is not ListViewItem item)
                {
                    continue;
                }

                GeneralTransform? transform = item.TransformToVisual(EntriesListView);
                Rect bounds = transform.TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                if (y >= bounds.Top && y <= bounds.Bottom)
                {
                    return item;
                }
            }

            return null;
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
                await _explorerService.RenamePathAsync(sourcePath, targetPath);
                try
                {
                    _explorerService.MarkPathChanged(parentPath);
                }
                catch
                {
                    EnsureRefreshFallbackInvalidation(parentPath, "tree-rename");
                }

                TreeViewNode? renamedNode = FindSidebarTreeNodeByPath(sourcePath);
                if (renamedNode is not null)
                {
                    UpdateSidebarTreeNodePath(renamedNode, sourcePath, targetPath, newName);
                }
                else if (FindSidebarTreeNodeByPath(parentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                {
                    await PopulateSidebarTreeChildrenAsync(parentNode, parentPath, CancellationToken.None, expandAfterLoad: true);
                }

                if (IsPathWithin(_currentPath, sourcePath))
                {
                    string suffix = _currentPath.Length > sourcePath.Length
                        ? _currentPath[sourcePath.Length..]
                        : string.Empty;
                    _currentPath = targetPath + suffix;
                    PathTextBox.Text = _currentPath;
                    UpdateBreadcrumbs(_currentPath);
                    UpdateNavButtonsState();
                    StyledSidebarView.SetSelectedPath(_currentPath);
                    await LoadFirstPageAsync();
                }
                else if (wasSelectedInTree)
                {
                    await SelectSidebarTreePathAsync(targetPath);
                }

                UpdateListEntryNameForCurrentDirectory(sourcePath, newName);

                UpdateStatusKey("StatusRenameSuccess", entry.Name, newName);
                _ = DispatcherQueue.TryEnqueue(FocusSidebarSurface);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusRenameFailedWithReason", ex.Message);
            }
        }

        private async Task RenameEntryAsync(EntryViewModel entry, int selectedIndex, string newName)
        {
            string src = Path.Combine(_currentPath, entry.Name);
            string oldName = entry.Name;
            TreeViewNode? renamedTreeNode = entry.IsDirectory ? FindSidebarTreeNodeByPath(src) : null;
            try
            {
                RenamedEntryInfo renamed = await _fileManagementCoordinator.RenameEntryAsync(_currentPath, entry.Name, newName);
                RenameTextBox.Text = string.Empty;
                HideRenameOverlay();
                _selectedEntryPath = renamed.TargetPath;
                if (!renamed.ChangeNotified)
                {
                    EnsureRefreshFallbackInvalidation(_currentPath, "rename");
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
                UpdateStatusKey("StatusRenameFailedWithReason", ex.Message);
            }
        }

        private async Task DeleteEntryAsync(EntryViewModel entry, int selectedIndex, string targetPath, bool recursive)
        {
            try
            {
                bool changeNotified = await _fileManagementCoordinator.DeleteEntryAsync(targetPath, recursive);
                if (!changeNotified)
                {
                    string parentPath = Path.GetDirectoryName(targetPath) ?? _currentPath;
                    EnsureRefreshFallbackInvalidation(parentPath, "delete");
                }
                ApplyLocalDelete(selectedIndex);
                _ = RefreshCurrentDirectoryInBackgroundAsync();
                UpdateStatusKey("StatusDeleteSuccess", entry.Name, recursive);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusDeleteFailedWithReason", ex.Message);
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
            if (!commit)
            {
                PathTextBox.Text = _currentPath;
            }

            PathTextBox.Visibility = Visibility.Collapsed;
            AddressBreadcrumbBorder.Visibility = Visibility.Visible;
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
        private string _pendingName = string.Empty;
        private bool _isNameEditing;

        public event PropertyChangedEventHandler? PropertyChanged;

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

        public Brush RowBackground => _isExplicitlySelected
            ? new SolidColorBrush(ColorHelper.FromArgb(0x14, 0x80, 0x80, 0x80))
            : new SolidColorBrush(Colors.Transparent);

        public Visibility RowSelectionIndicatorVisibility => _isExplicitlySelected ? Visibility.Visible : Visibility.Collapsed;
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
        [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
        internal static partial IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [LibraryImport("user32.dll", EntryPoint = "SetCursor", SetLastError = true)]
        internal static partial IntPtr SetCursor(IntPtr hCursor);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

}
