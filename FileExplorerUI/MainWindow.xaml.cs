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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Foundation;
using WinRT.Interop;

namespace FileExplorerUI
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {
        private GridLength _nameColumnWidth = new(220);
        private GridLength _typeColumnWidth = new(150);
        private GridLength _sizeColumnWidth = new(120);
        private GridLength _modifiedColumnWidth = new(180);
        private GridLength _sidebarColumnWidth = new(220);
        private double _detailsContentWidth = 694;
        private double _detailsRowWidth = 714;
        private UIElement? _activeColumnSplitter;
        private int _activeSplitterTag;
        private double _dragStartX;
        private double _dragStartName;
        private double _dragStartType;
        private double _dragStartSize;
        private double _dragStartModified;
        private double _dragStartContent;
        private double _sidebarDragStartWidth;
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
        private MenuFlyout? _sidebarTreeContextFlyout;
        private SidebarTreeEntry? _pendingSidebarTreeContextEntry;
        private Canvas? _sidebarTreeRenameOverlayCanvas;
        private Border? _sidebarTreeRenameOverlayBorder;
        private TextBox? _sidebarTreeRenameTextBox;
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
        private static readonly DataTemplate SidebarTreeItemTemplate = CreateSidebarTreeItemTemplate();

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
        private bool _entriesPointerHooked;
        private string _currentPath = ShellMyComputerPath;
        private uint _currentPageSize = InitialPageSize;
        private uint _lastFetchMs;
        private uint _totalEntries;
        private DirectorySortMode _currentSortMode = DirectorySortMode.FolderFirstNameAsc;
        private ScrollViewer? _listScrollViewer;
        private double _estimatedItemHeight = 32.0;
        private Brush? _pathDefaultBorderBrush;
        private readonly Stack<string> _backStack = new();
        private readonly Stack<string> _forwardStack = new();
        private string _currentQuery = string.Empty;
        private readonly ExplorerService _explorerService = new();
        private readonly string _engineVersion;
        private EntryViewModel? _activeRenameOverlayEntry;
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

        private enum CommandDockSide
        {
            Top,
            Right,
            Bottom
        }

        private const int GWL_WNDPROC = -4;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int WM_SETCURSOR = 0x0020;

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

        public MainWindow()
        {
            InitializeComponent();
            if (Content is FrameworkElement rootElement)
            {
                rootElement.ActualThemeChanged += MainWindowRoot_ActualThemeChanged;
            }
            PathTextBox.Text = ShellMyComputerPath;
            RegisterColumnSplitterHandlers(HeaderSplitter1);
            RegisterColumnSplitterHandlers(HeaderSplitter2);
            RegisterColumnSplitterHandlers(HeaderSplitter3);
            RegisterColumnSplitterHandlers(HeaderSplitter4);
            RegisterSidebarSplitterHandlers(SidebarSplitter);
            EntriesListView.ItemsSource = _entries;
            EntriesContextFlyout.OverlayInputPassThroughElement = EntriesListView;
            EntriesListView.SizeChanged += EntriesListView_SizeChanged;
            this.SizeChanged += MainWindow_SizeChanged;
            _pathDefaultBorderBrush = PathTextBox.BorderBrush;
            _engineVersion = _explorerService.GetEngineVersion();

            UpdateNavButtonsState();
            this.AppWindow.Title = $"NorthFile | Engine {_engineVersion}";
            ApplyTitleBarTheme();
            StyledSidebarView.NavigateRequested += StyledSidebarView_NavigateRequested;
            BuildSidebarItems();
            _sidebarInitialized = true;
            ApplyCommandDockLayout();
            InstallWindowHook();
            _ = LoadFirstPageAsync();
        }

        private void MainWindowRoot_ActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyTitleBarTheme();
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
            else if (msg == WM_SETCURSOR && (_splitterHoverCount > 0 || _activeColumnSplitter is not null))
            {
                if (ColumnSplitter.TryApplyResizeCursor())
                {
                    return new IntPtr(1);
                }
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
            if (sender is not FrameworkElement splitter || splitter.Tag is not string tagText || !int.TryParse(tagText, out int tag))
            {
                return;
            }

            ColumnSplitter.TryApplyResizeCursor();
            _activeColumnSplitter = splitter;
            _activeSplitterTag = tag;
            _dragStartX = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            _dragStartName = NameColumnWidth.Value;
            _dragStartType = TypeColumnWidth.Value;
            _dragStartSize = SizeColumnWidth.Value;
            _dragStartModified = ModifiedColumnWidth.Value;
            _dragStartContent = DetailsContentWidth;
            if (splitter is UIElement uiElement)
            {
                uiElement.CapturePointer(e.Pointer);
            }
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
            if (sender is UIElement)
            {
                ColumnSplitter.TryApplyResizeCursor();
            }

            if (sender is not UIElement splitter || !ReferenceEquals(splitter, _activeColumnSplitter))
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double x = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            double delta = x - _dragStartX;
            if (Math.Abs(delta) < 0.5)
            {
                return;
            }

            const double minType = 90;
            const double minSize = 80;
            const double minName = 120;
            const double minModified = 120;

            double name = _dragStartName;
            double type = _dragStartType;
            double size = _dragStartSize;
            double modified = _dragStartModified;
            double content = _dragStartContent;

            switch (_activeSplitterTag)
            {
                case 1:
                    {
                        name = Math.Max(minName, _dragStartName + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case 2:
                    {
                        type = Math.Max(minType, _dragStartType + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case 3:
                    {
                        size = Math.Max(minSize, _dragStartSize + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case 4:
                    {
                        modified = Math.Max(minModified, _dragStartModified + delta);
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
            if (sender is UIElement splitter)
            {
                splitter.ReleasePointerCaptures();
            }

            _activeColumnSplitter = null;
            _activeSplitterTag = 0;
            _dragStartX = 0;
            e.Handled = true;
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
            if (EntriesListView.SelectedItem is not EntryViewModel entry || !entry.IsLoaded)
            {
                UpdateStatus("Rename failed: select a loaded entry first.");
                return;
            }
            int selectedIndex = EntriesListView.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatus("Rename failed: invalid selected index.");
                return;
            }

            _ = BeginRenameOverlayAsync(entry);
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesListView.SelectedItem is not EntryViewModel entry || !entry.IsLoaded)
            {
                UpdateStatus("Delete failed: select a loaded entry first.");
                return;
            }
            int selectedIndex = EntriesListView.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatus("Delete failed: invalid selected index.");
                return;
            }

            string target = Path.Combine(_currentPath, entry.Name);
            bool recursive = RecursiveDeleteCheckBox.IsChecked == true;
            bool confirmed = await ConfirmDeleteAsync(entry.Name, recursive);
            if (!confirmed)
            {
                UpdateStatus("Delete canceled by user.");
                return;
            }

            await DeleteEntryAsync(entry, selectedIndex, target, recursive);
        }

        private async void NewFileButton_Click(object sender, RoutedEventArgs e)
        {
            await CreateNewFileAsync();
        }

        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            await CreateNewFolderAsync();
        }

        private async Task CreateNewFileAsync()
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus("New file failed: open a folder first.");
                return;
            }

            if (!_explorerService.DirectoryExists(_currentPath))
            {
                UpdateStatus("New file failed: current folder is not available.");
                return;
            }

            string initialName = GenerateUniqueNewFileName();
            string fullPath = Path.Combine(_currentPath, initialName);

            try
            {
                await _explorerService.CreateEmptyFileAsync(fullPath);
                EntryViewModel entry = InsertLocalCreatedEntry(initialName, isDirectory: false);
                await PromptRenameCreatedEntryAsync(entry);
                UpdateStatus($"Create success: {initialName}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"New file failed: {ex.Message}");
            }
        }

        private async Task CreateNewFolderAsync()
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus("New folder failed: open a folder first.");
                return;
            }

            if (!_explorerService.DirectoryExists(_currentPath))
            {
                UpdateStatus("New folder failed: current folder is not available.");
                return;
            }

            string initialName = GenerateUniqueNewFolderName();
            string fullPath = Path.Combine(_currentPath, initialName);

            try
            {
                await _explorerService.CreateDirectoryAsync(fullPath);
                EntryViewModel entry = InsertLocalCreatedEntry(initialName, isDirectory: true);
                await PromptRenameCreatedEntryAsync(entry);
                _ = SelectSidebarTreePathAsync(_currentPath);
                UpdateStatus($"Create success: {initialName}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"New folder failed: {ex.Message}");
            }
        }

        private void EntriesListView_Loaded(object sender, RoutedEventArgs e)
        {
            _listScrollViewer ??= FindDescendant<ScrollViewer>(EntriesListView);
            if (_listScrollViewer is not null)
            {
                _listScrollViewer.ViewChanged -= ListScrollViewer_ViewChanged;
                _listScrollViewer.ViewChanged += ListScrollViewer_ViewChanged;
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
                UpdateStatus($"Path not found: {target}");
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
                UpdateStatus("No more entries.");
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

                        UpdateStatus($"Path: {path} | Access denied. Skip current directory.");
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
                RequestMetadataForCurrentViewport();

                _nextCursor = page.NextCursor;
                _hasMore = page.HasMore;
                _currentPageSize = ClampPageSize(page.SuggestedNextLimit, requestedPageSize);
                string source = _explorerService.DescribeBatchSource(page.SourceKind);

                this.AppWindow.Title = $"NorthFile | Engine {_engineVersion} | Items: {_entries.Count}";
                UpdateStatus($"当前文件夹下 {_totalEntries} 个项目");
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
                this.AppWindow.Title = $"NorthFile | Engine {_engineVersion} | Read Failed";
                UpdateStatus($"Path: {path} | Error: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
                LoadButton.IsEnabled = true;
                NextButton.IsEnabled = _hasMore;
                SidebarNavView.IsEnabled = true;
                StyledSidebarView.IsEnabled = true;
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

            NameHeaderTextBlock.Text = "名称";
            TypeHeaderTextBlock.Text = "类型";
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                SizeHeaderTextBlock.Text = "总大小";
                ModifiedHeaderTextBlock.Text = "可用空间";
                return;
            }

            SizeHeaderTextBlock.Text = "大小";
            ModifiedHeaderTextBlock.Text = "修改日期";
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
                UpdateStatus($"Path: {path} | Invalidate warning({reason}): {ex.Message}");
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
                Content = "固定",
                Icon = new SymbolIcon(Symbol.Favorite),
                SelectsOnInvoked = false
            };
            quickAccess.MenuItems.Add(CreateLeaf("桌面", desktopPath, Symbol.Home, true, false));
            quickAccess.MenuItems.Add(CreateLeaf("文档", documentsPath, Symbol.Document, true, false));
            quickAccess.MenuItems.Add(CreateLeaf("下载", downloadsPath, Symbol.Download, true, false));
            SidebarNavView.MenuItems.Add(quickAccess);

            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = "云盘", Icon = new SymbolIcon(Symbol.World), SelectsOnInvoked = false });
            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = "网络", Icon = new SymbolIcon(Symbol.Globe), SelectsOnInvoked = false });
            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = "标签", Icon = new SymbolIcon(Symbol.Tag), SelectsOnInvoked = false });

            StyledSidebarView.ConfigurePinnedPaths(desktopPath, documentsPath, downloadsPath, picturesPath);
            StyledSidebarView.SetExtraItems(new[]
            {
                new SidebarNavItemModel("cloud", "云盘", null, "\uE753", selectable: false),
                new SidebarNavItemModel("network", "网络", null, "\uE774", selectable: false),
                new SidebarNavItemModel("tags", "标签", null, "\uE8EC", selectable: false)
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
            var renameItem = new MenuFlyoutItem { Text = "Rename" };
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
            var computerEntry = new SidebarTreeEntry("我的电脑", "shell:mycomputer", "\uE7F4");
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
                    UpdateStatus($"Path: {path} | Access denied.");
                    return false;
                }

                UpdateStatus($"Path: {path} | Error: Rust error {rustErrorCode}: {rustErrorMessage}");
                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"Path: {path} | Error: {ex.Message}");
                return false;
            }
        }

        private void PopulateMyComputerEntries()
        {
            _entries.Clear();
            foreach (DriveInfo drive in _explorerService.GetReadyDrives())
            {
                string root = drive.RootDirectory.FullName;
                string label = drive.Name.TrimEnd('\\');
                string type = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? "本地磁盘"
                    : $"{drive.VolumeLabel} ({drive.DriveFormat})";

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
            this.AppWindow.Title = $"NorthFile | Engine {_engineVersion} | Drives: {_entries.Count}";
            UpdateStatus($"我的电脑下 {_entries.Count} 个驱动器");
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
                UpdateStatus($"Sidebar tree expand failed: {ex.Message}");
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
            _suppressSidebarTreeSelection = true;
            _sidebarTreeView.SelectedNode = node;
            _suppressSidebarTreeSelection = false;

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
                UpdateStatus("Rename failed: select a tree node first.");
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
                UpdateStatus("Sidebar tree nav ignored: loading in progress.");
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
                UpdateStatus($"Sidebar tree nav failed: {ex.Message}");
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
                UpdateStatus("Sidebar nav ignored: loading in progress.");
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
                UpdateStatus($"Sidebar nav failed: {ex.Message}");
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
                UpdateStatus("Sidebar nav ignored: loading in progress.");
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
                UpdateStatus($"Sidebar nav failed: {ex.Message}");
                StyledSidebarView.SetSelectedPath(_currentPath);
            }
        }

        private void SidebarSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter)
            {
                return;
            }

            ColumnSplitter.TryApplyResizeCursor();
            _activeColumnSplitter = splitter;
            _activeSplitterTag = 100;
            _dragStartX = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            _sidebarDragStartWidth = SidebarColumnWidth.Value;
            splitter.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void SidebarSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is UIElement)
            {
                ColumnSplitter.TryApplyResizeCursor();
            }

            if (sender is not UIElement splitter || !ReferenceEquals(splitter, _activeColumnSplitter) || _activeSplitterTag != 100)
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double x = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            double delta = x - _dragStartX;
            double requestedWidth = _sidebarDragStartWidth + delta;
            ApplySidebarWidthLayout(requestedWidth, fromUserDrag: true);
            e.Handled = true;
        }

        private void SidebarSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitterTag != 100)
            {
                return;
            }

            if (sender is UIElement splitter)
            {
                splitter.ReleasePointerCaptures();
            }

            _activeColumnSplitter = null;
            _activeSplitterTag = 0;
            _dragStartX = 0;
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
                return $"Error({c.error_code})";
            }
            if (c.available != 0)
            {
                return "Available";
            }
            if (c.is_ntfs_local == 0)
            {
                return "NotNTFS";
            }
            if (c.access_denied != 0)
            {
                return "Denied";
            }
            return "Unavailable";
        }

        private static string DescribeSourceDetail(byte sourceKind, RustUsnCapability c)
        {
            if (sourceKind != 1)
            {
                return string.Empty;
            }
            if (c.error_code != 0)
            {
                return $" (NTFS fallback: probe error {c.error_code})";
            }
            if (c.is_ntfs_local == 0)
            {
                return " (NTFS fallback: not local NTFS)";
            }
            if (c.access_denied != 0)
            {
                return " (NTFS fallback: access denied)";
            }
            if (c.available != 0)
            {
                return " (NTFS fallback: probe succeeded, batch path unavailable)";
            }
            return " (NTFS fallback: unavailable)";
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
                _entries[startIndex + i] = new EntryViewModel
                {
                    Name = row.Name,
                    PendingName = row.Name,
                    FullPath = Path.Combine(_currentPath, row.Name),
                    Type = GetEntryTypeText(row.Name, row.IsDirectory, row.IsLink),
                    IconGlyph = GetEntryIconGlyph(row.IsDirectory, row.IsLink, row.Name),
                    IconForeground = GetEntryIconBrush(row.IsDirectory, row.IsLink, row.Name),
                    MftRef = row.MftRef,
                    SizeText = "",
                    ModifiedText = "",
                    IsDirectory = row.IsDirectory,
                    IsLink = row.IsLink,
                    IsLoaded = true,
                    IsMetadataLoaded = false
                };
            }
        }

        private static string GetEntryTypeText(string name, bool isDirectory, bool isLink)
        {
            if (isDirectory)
            {
                return isLink ? "文件夹链接" : "文件夹";
            }

            if (isLink)
            {
                return "文件链接";
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
                return "文件";
            }

            return $"{ext.TrimStart('.').ToUpperInvariant()} 文件";
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

                return dt.ToString("yyyy-MM-dd HH:mm");
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

        private string GenerateUniqueNewFileName()
        {
            const string baseName = "New File";
            const string extension = ".txt";
            string candidate = baseName + extension;
            int suffix = 2;

            while (_explorerService.PathExists(Path.Combine(_currentPath, candidate)))
            {
                candidate = $"{baseName} ({suffix}){extension}";
                suffix++;
            }

            return candidate;
        }

        private string GenerateUniqueNewFolderName()
        {
            const string baseName = "New Folder";
            string candidate = baseName;
            int suffix = 2;

            while (_explorerService.PathExists(Path.Combine(_currentPath, candidate)))
            {
                candidate = $"{baseName} ({suffix})";
                suffix++;
            }

            return candidate;
        }

        private EntryViewModel InsertLocalCreatedEntry(string name, bool isDirectory)
        {
            var entry = new EntryViewModel
            {
                Name = name,
                PendingName = name,
                FullPath = Path.Combine(_currentPath, name),
                Type = GetEntryTypeText(name, isDirectory, isLink: false),
                IconGlyph = GetEntryIconGlyph(isDirectory, isLink: false, name),
                IconForeground = GetEntryIconBrush(isDirectory, isLink: false, name),
                MftRef = 0,
                SizeText = isDirectory ? string.Empty : "0 B",
                ModifiedText = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                IsDirectory = isDirectory,
                IsLink = false,
                IsLoaded = true,
                IsMetadataLoaded = true
            };

            int insertIndex = FindInsertIndexForEntry(entry);
            _entries.Insert(insertIndex, entry);
            _totalEntries++;
            _hasMore = _nextCursor < _totalEntries;
            this.AppWindow.Title = $"NorthFile | Engine {_engineVersion} | Items: {_entries.Count}";
            EntriesListView.SelectedItem = entry;
            EntriesListView.ScrollIntoView(entry);
            return entry;
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

        private async Task BeginRenameOverlayAsync(EntryViewModel entry)
        {
            HideRenameOverlay();
            EntriesListView.SelectedItem = entry;
            EntriesListView.ScrollIntoView(entry);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(16);
                EntriesListView.UpdateLayout();
                if (!TryPositionRenameOverlay(entry))
                {
                    continue;
                }

                _activeRenameOverlayEntry = entry;
                RenameOverlayTextBox.Text = entry.Name;
                RenameOverlayBorder.Visibility = Visibility.Visible;
                RenameOverlayTextBox.Focus(FocusState.Programmatic);
                RenameOverlayTextBox.SelectAll();
                return;
            }

            UpdateStatus("Rename failed: could not start inline editor.");
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
                if (string.Equals(proposedName, entry.Name, StringComparison.Ordinal))
                {
                    HideRenameOverlay();
                    FocusEntriesList();
                    return;
                }

                if (!TryValidateCreateOrRenameName(entry, proposedName, out string validationError))
                {
                    UpdateStatus(validationError);
                    RenameOverlayTextBox.Focus(FocusState.Programmatic);
                    RenameOverlayTextBox.SelectAll();
                    return;
                }

                int index = _entries.IndexOf(entry);
                if (index < 0)
                {
                    HideRenameOverlay();
                    FocusEntriesList();
                    return;
                }

                await RenameEntryAsync(entry, index, proposedName);
            }
            finally
            {
                _isCommittingRenameOverlay = false;
            }
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
                UpdateStatus("Rename failed: tree item is not available.");
                return;
            }

            EnsureSidebarTreeRenameOverlay();
            if (_sidebarTreeRenameOverlayCanvas is null || _sidebarTreeRenameOverlayBorder is null || _sidebarTreeRenameTextBox is null)
            {
                UpdateStatus("Rename failed: tree rename overlay is not available.");
                return;
            }

            _activeSidebarTreeRenameEntry = entry;
            _sidebarTreeRenameTextBox!.Text = entry.Name;
            if (FindDescendantByName<TextBlock>(item, "SidebarTreeNameTextBlock") is not TextBlock anchor)
            {
                UpdateStatus("Rename failed: tree text anchor is not available.");
                return;
            }

            GeneralTransform transform = anchor.TransformToVisual(_sidebarTreeRenameOverlayCanvas);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            double overlayHeight = _sidebarTreeRenameOverlayBorder!.ActualHeight > 0
                ? _sidebarTreeRenameOverlayBorder.ActualHeight
                : (_sidebarTreeRenameOverlayBorder.Height > 0 ? _sidebarTreeRenameOverlayBorder.Height : bounds.Height);
            Canvas.SetLeft(_sidebarTreeRenameOverlayBorder, bounds.X + SidebarTreeRenameOffsetX);
            Canvas.SetTop(_sidebarTreeRenameOverlayBorder, Math.Max(0, bounds.Y + ((bounds.Height - overlayHeight) / 2) + SidebarTreeRenameOffsetY));
            _sidebarTreeRenameOverlayBorder.Width = Math.Max(140, bounds.Width + 12);
            _sidebarTreeRenameOverlayBorder.Visibility = Visibility.Visible;
            await Task.Yield();
            _sidebarTreeRenameTextBox.Focus(FocusState.Programmatic);
            _sidebarTreeRenameTextBox.SelectAll();
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

        private bool TryValidateCreateOrRenameName(EntryViewModel entry, string name, out string error)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Name failed: name is empty.";
                return false;
            }

            if (name is "." or "..")
            {
                error = $"Name failed: '{name}' is reserved.";
                return false;
            }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = $"Name failed: '{name}' contains invalid characters.";
                return false;
            }

            string targetPath = Path.Combine(_currentPath, name);
            if (!string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase) && _explorerService.PathExists(targetPath))
            {
                error = $"Name failed: '{name}' already exists.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private async Task PromptRenameCreatedEntryAsync(EntryViewModel entry)
        {
            string? proposedName = await PromptRenameAsync(entry.Name);
            if (string.IsNullOrWhiteSpace(proposedName) || string.Equals(proposedName, entry.Name, StringComparison.Ordinal))
            {
                return;
            }

            if (!TryValidateCreateOrRenameName(entry, proposedName, out string validationError))
            {
                UpdateStatus(validationError);
                return;
            }

            int index = _entries.IndexOf(entry);
            if (index < 0)
            {
                return;
            }

            await RenameEntryAsync(entry, index, proposedName);
        }

        private void ApplyLocalRename(int index, string newName)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return;
            }

            EntryViewModel current = _entries[index];
            _entries[index] = new EntryViewModel
            {
                Name = newName,
                PendingName = newName,
                FullPath = Path.Combine(_currentPath, newName),
                Type = current.Type,
                IconGlyph = current.IconGlyph,
                IconForeground = current.IconForeground,
                MftRef = current.MftRef,
                SizeText = string.Empty,
                ModifiedText = string.Empty,
                IsDirectory = current.IsDirectory,
                IsLink = current.IsLink,
                IsLoaded = true,
                IsMetadataLoaded = false
            };
            HideRenameOverlay();
            EntriesListView.SelectedItem = _entries[index];
            EntriesListView.ScrollIntoView(_entries[index]);
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
            _entries[index] = new EntryViewModel
            {
                Name = newName,
                PendingName = newName,
                FullPath = Path.Combine(_currentPath, newName),
                Type = current.Type,
                IconGlyph = current.IconGlyph,
                IconForeground = current.IconForeground,
                MftRef = current.MftRef,
                SizeText = current.SizeText,
                ModifiedText = current.ModifiedText,
                IsDirectory = current.IsDirectory,
                IsLink = current.IsLink,
                IsLoaded = current.IsLoaded,
                IsMetadataLoaded = current.IsMetadataLoaded
            };

            if (wasSelected)
            {
                EntriesListView.SelectedItem = _entries[index];
                EntriesListView.ScrollIntoView(_entries[index]);
            }
        }

        private void ApplyLocalDelete(int index)
        {
            if (index < 0 || index >= _entries.Count)
            {
                return;
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
                EntriesListView.SelectedItem = tappedEntry;
                item.IsSelected = true;
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
                UpdateStatus($"Opened: {row.Name}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Open failed: {ex.Message}");
            }
        }

        private void EntriesListView_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            ListViewItem? item = null;
            if (e.OriginalSource is DependencyObject source)
            {
                item = FindAncestor<ListViewItem>(source);
            }

            if (item is null && e.TryGetPosition(EntriesListView, out Point pos))
            {
                item = FindListViewItemAt(pos);
            }

            if (item?.DataContext is EntryViewModel entry)
            {
                EntriesListView.SelectedItem = entry;
                item.IsSelected = true;
            }
        }

        private void EntryRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement row || row.DataContext is not EntryViewModel entry)
            {
                return;
            }

            EntriesListView.SelectedItem = entry;

            if (FindAncestor<ListViewItem>(row) is ListViewItem item)
            {
                item.IsSelected = true;
            }
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
                EntriesListView.SelectedItem = entry;
                item.IsSelected = true;
            }

            EntriesContextFlyout.Hide();
        }

        private void EntriesContextFlyout_Opening(object sender, object e)
        {
            _entriesFlyoutOpen = true;
        }

        private void EntriesContextFlyout_Closed(object sender, object e)
        {
            _entriesFlyoutOpen = false;
            EntriesListView.Focus(FocusState.Programmatic);
            if (_pendingContextRenameEntry is not null)
            {
                EntryViewModel entry = _pendingContextRenameEntry;
                _pendingContextRenameEntry = null;
                _ = BeginRenameOverlayAsync(entry);
            }
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

            return null;
        }

        private async void UpButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus("Already at root.");
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
                if (_explorerService.DirectoryExists(basePath))
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
                    Text = "(empty)",
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
                    Text = "(empty)",
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

        private void ContextRename_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesListView.SelectedItem is not EntryViewModel entry || !entry.IsLoaded)
            {
                UpdateStatus("Rename failed: select a loaded entry first.");
                return;
            }

            int selectedIndex = EntriesListView.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatus("Rename failed: invalid selected index.");
                return;
            }

            if (_entriesFlyoutOpen)
            {
                _pendingContextRenameEntry = entry;
                return;
            }

            _ = BeginRenameOverlayAsync(entry);
        }

        private async void ContextNewFile_Click(object sender, RoutedEventArgs e)
        {
            await CreateNewFileAsync();
        }

        private async void ContextNewFolder_Click(object sender, RoutedEventArgs e)
        {
            await CreateNewFolderAsync();
        }

        private async void ContextDelete_Click(object sender, RoutedEventArgs e)
        {
            if (EntriesListView.SelectedItem is not EntryViewModel entry || !entry.IsLoaded)
            {
                UpdateStatus("Delete failed: select a loaded entry first.");
                return;
            }

            int selectedIndex = EntriesListView.SelectedIndex;
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatus("Delete failed: invalid selected index.");
                return;
            }

            bool recursive = RecursiveDeleteCheckBox.IsChecked == true;
            bool confirmed = await ConfirmDeleteAsync(entry.Name, recursive);
            if (!confirmed)
            {
                return;
            }

            string target = Path.Combine(_currentPath, entry.Name);
            await DeleteEntryAsync(entry, selectedIndex, target, recursive);
        }

        private async void ContextRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadFirstPageAsync();
        }

        private bool TryValidateTreeRename(SidebarTreeEntry entry, string newName, out string error)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = "Rename failed: name is empty.";
                return false;
            }

            if (newName is "." or "..")
            {
                error = $"Rename failed: '{newName}' is reserved.";
                return false;
            }

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = $"Rename failed: '{newName}' contains invalid characters.";
                return false;
            }

            string parentPath = Path.GetDirectoryName(entry.FullPath.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty;
            string targetPath = Path.Combine(parentPath, newName);
            if (!string.Equals(entry.FullPath, targetPath, StringComparison.OrdinalIgnoreCase) &&
                _explorerService.PathExists(targetPath))
            {
                error = $"Rename failed: '{newName}' already exists.";
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

                UpdateStatus($"Rename success: {entry.Name} -> {newName}");
                _ = DispatcherQueue.TryEnqueue(FocusSidebarSurface);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Rename failed: {ex.Message}");
            }
        }

        private async Task RenameEntryAsync(EntryViewModel entry, int selectedIndex, string newName)
        {
            string src = Path.Combine(_currentPath, entry.Name);
            string dst = Path.Combine(_currentPath, newName);
            TreeViewNode? renamedTreeNode = entry.IsDirectory ? FindSidebarTreeNodeByPath(src) : null;
            try
            {
                await _explorerService.RenamePathAsync(src, dst);
                RenameTextBox.Text = string.Empty;
                HideRenameOverlay();
                ApplyLocalRename(selectedIndex, newName);
                try
                {
                    _explorerService.MarkPathChanged(_currentPath);
                }
                catch
                {
                    EnsureRefreshFallbackInvalidation(_currentPath, "rename");
                }
                if (entry.IsDirectory)
                {
                    if (renamedTreeNode is not null)
                    {
                        UpdateSidebarTreeNodePath(renamedTreeNode, src, dst, newName);
                    }
                    else if (FindSidebarTreeNodeByPath(_currentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                    {
                        await PopulateSidebarTreeChildrenAsync(parentNode, _currentPath, CancellationToken.None, expandAfterLoad: true);
                    }
                }
                UpdateStatus($"Rename success: {entry.Name} -> {newName}");
                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Rename failed: {ex.Message}");
            }
        }

        private async Task DeleteEntryAsync(EntryViewModel entry, int selectedIndex, string targetPath, bool recursive)
        {
            try
            {
                await _explorerService.DeletePathAsync(targetPath, recursive);
                ApplyLocalDelete(selectedIndex);
                _ = RefreshCurrentDirectoryInBackgroundAsync();
                UpdateStatus($"Delete success: {entry.Name} (recursive: {recursive})");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Delete failed: {ex.Message}");
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
                    Label = "我的电脑",
                    FullPath = ShellMyComputerPath,
                    HasChildren = false,
                    ChevronVisibility = Visibility.Collapsed,
                    IsLast = true,
                    MeasuredWidth = 0
                });
                return;
            }

            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(root))
            {
                Breadcrumbs.Add(new BreadcrumbItemViewModel
                {
                    Label = root,
                    FullPath = root,
                    HasChildren = HasChildDirectory(root),
                    ChevronVisibility = Visibility.Collapsed
                });
            }

            string remaining = fullPath.Substring(root.Length).Trim('\\');
            if (string.IsNullOrEmpty(remaining))
            {
                return;
            }

            string current = root;
            string[] parts = remaining.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                current = string.IsNullOrEmpty(current) ? part : Path.Combine(current, part);
                Breadcrumbs.Add(new BreadcrumbItemViewModel
                {
                    Label = part,
                    FullPath = current,
                    HasChildren = HasChildDirectory(current),
                    ChevronVisibility = Visibility.Collapsed
                });
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
                Title = "Rename",
                Content = input,
                PrimaryButtonText = "Rename",
                CloseButtonText = "Cancel",
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
                Title = "Confirm Delete",
                Content = $"Delete '{name}'?\nRecursive: {recursive}",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            if (this.Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
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
    }

    public sealed class BreadcrumbItemViewModel : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _fullPath = string.Empty;
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
        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

}
