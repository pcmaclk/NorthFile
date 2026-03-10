using FileExplorerUI.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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
        private readonly List<SidebarGroupControl> _sidebarGroups = new();
        private readonly List<SidebarItemButton> _sidebarItems = new();

        private const double SidebarExpandedDefaultWidth = 220;
        private const double SidebarExpandedMinWidth = 32;
        private const double SidebarExpandedMaxWidth = 360;
        private const double SidebarCompactWidth = 32;
        private const double SidebarCompactThreshold = SidebarCompactWidth;
        private const double SidebarSplitterWidth = 0;
        private const double SidebarMinContentWidth = 520;

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
        private string _currentPath = @"C:\";
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
        private readonly string _engineVersion;
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
        private long _lastNavInvokeTick;
        private readonly Brush _sidebarSelectedBorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1E, 0xA7, 0xFF));
        private readonly Brush _sidebarTransparentBrush = new SolidColorBrush(Colors.Transparent);
        private readonly Brush _sidebarSelectedBackgroundBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        private readonly Brush _sidebarHoverBackgroundBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xEA, 0xEA, 0xEA));
        private readonly Brush _sidebarTextBrush = new SolidColorBrush(Colors.DimGray);
        private readonly Brush _sidebarItemTextBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x28, 0x28, 0x28));
        private readonly Brush _sidebarNormalBackgroundBrush = new SolidColorBrush(Colors.Transparent);
        private readonly Dictionary<string, SidebarItemButton> _sidebarPathButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sidebarQuickAccessPaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _sidebarDrivePaths = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<SidebarGroupState> _sidebarGroupStates = new();
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

        private sealed class SidebarGroupState
        {
            public required string Key { get; init; }
            public required SidebarGroupControl Control { get; init; }
            public List<string> Paths { get; } = new();
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

        public MainWindow()
        {
            InitializeComponent();
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
            _engineVersion = RustBatchInterop.GetEngineVersion();

            UpdateNavButtonsState();
            this.AppWindow.Title = $"NorthFile | Engine {_engineVersion}";
            BuildSidebarItems();
            _sidebarInitialized = true;
            ApplyCommandDockLayout();
            InstallWindowHook();
            _ = LoadFirstPageAsync();
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

        private async void SidebarItemButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is not Button btn || btn.Tag is not string target)
                {
                    return;
                }
                if (_isLoading)
                {
                    UpdateStatus("Sidebar nav ignored: loading in progress.");
                    return;
                }

                long now = Environment.TickCount64;
                if (now - _lastNavInvokeTick < 180)
                {
                    return;
                }
                _lastNavInvokeTick = now;

                string normTarget = Path.GetFullPath(target).TrimEnd('\\');
                string normCurrent = Path.GetFullPath(_currentPath).TrimEnd('\\');
                if (string.Equals(normTarget, normCurrent, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                ApplySidebarSelectionImmediate(target);
                await NavigateToPathAsync(target, pushHistory: true);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Sidebar nav failed: {ex.Message}");
            }
        }

        private void ApplySidebarSelectionImmediate(string target)
        {
            string selectedPath = Path.GetFullPath(target).TrimEnd('\\');
            foreach ((string path, SidebarItemButton btn) in _sidebarPathButtons)
            {
                string currentPath = Path.GetFullPath(path).TrimEnd('\\');
                btn.ApplySelection(string.Equals(currentPath, selectedPath, StringComparison.OrdinalIgnoreCase));
            }
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

        private async void RenameButton_Click(object sender, RoutedEventArgs e)
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

            string newName = RenameTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newName))
            {
                UpdateStatus("Rename failed: new name is empty.");
                return;
            }

            await RenameEntryAsync(entry, selectedIndex, newName);
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
        }

        private void EntriesListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateEstimatedItemHeight();
            RequestViewportWork();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            ApplySidebarWidthLayout();
            UpdateEstimatedItemHeight();
            RequestViewportWork();
            UpdateVisibleBreadcrumbs();
        }

        private async Task LoadFirstPageAsync()
        {
            _currentPath = string.IsNullOrWhiteSpace(PathTextBox.Text) ? @"C:\" : PathTextBox.Text.Trim();
            if (!_sidebarInitialized)
            {
                BuildSidebarItems();
                _sidebarInitialized = true;
            }
            else
            {
                UpdateSidebarSelectionOnly();
            }
            UpdateUsnCapability(_currentPath);
            ConfigureDirectoryWatcher(_currentPath);
            EnsureRefreshFallbackInvalidation(_currentPath, "manual_load");
            _currentPageSize = InitialPageSize;
            _lastFetchMs = 0;
            UpdateBreadcrumbs(_currentPath);
            await LoadPageAsync(_currentPath, cursor: 0, append: false);
        }

        private async Task NavigateToPathAsync(string path, bool pushHistory)
        {
            string target = string.IsNullOrWhiteSpace(path) ? @"C:\" : path.Trim();
            if (!Directory.Exists(target))
            {
                SetPathInputInvalid();
                UpdateStatus($"Path not found: {target}");
                return;
            }
            SetPathInputValid();

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
            SidebarScrollViewer.IsEnabled = false;

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
                            bool success = RustBatchInterop.TryReadDirectoryRowsAuto(
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
                            bool success = RustBatchInterop.TrySearchDirectoryRowsAuto(
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
                string source = RustBatchInterop.DescribeBatchSource(page.SourceKind);

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
                SidebarScrollViewer.IsEnabled = true;
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
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
                RustBatchInterop.InvalidateMemoryDirectory(path);
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
                _usnCapability = RustBatchInterop.ProbeUsnCapability(path);
            }
            catch
            {
                _usnCapability = default;
            }
        }

        private void BuildSidebarItems()
        {
            if (SidebarGroupsHost is null)
            {
                return;
            }

            SidebarGroupsHost.Children.Clear();
            _sidebarPathButtons.Clear();
            _sidebarQuickAccessPaths.Clear();
            _sidebarDrivePaths.Clear();
            _sidebarGroups.Clear();
            _sidebarItems.Clear();
            _sidebarGroupStates.Clear();

            SidebarGroupState AddGroup(string key, string text, bool expanded = true)
            {
                var group = new SidebarGroupControl(
                    text,
                    _sidebarNormalBackgroundBrush,
                    _sidebarHoverBackgroundBrush,
                    _sidebarSelectedBackgroundBrush,
                    _sidebarSelectedBorderBrush,
                    _sidebarTextBrush,
                    expanded,
                    headerHeight: 28)
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                group.ExpandedChanged += SidebarGroup_ExpandedChanged;
                _sidebarGroups.Add(group);
                SidebarGroupsHost.Children.Add(group);
                var state = new SidebarGroupState
                {
                    Key = key,
                    Control = group
                };
                _sidebarGroupStates.Add(state);
                return state;
            }

            void AddItem(SidebarGroupState groupState, string label, string path)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }
                string iconGlyph = label switch
                {
                    "桌面" => "\uE770",
                    "文档" => "\uE8A5",
                    "下载" => "\uE896",
                    _ => "\uE8B7"
                };
                var item = new SidebarItemButton(
                    label,
                    iconGlyph,
                    path,
                    _sidebarNormalBackgroundBrush,
                    _sidebarHoverBackgroundBrush,
                    _sidebarSelectedBackgroundBrush,
                    _sidebarItemTextBrush,
                    _sidebarTransparentBrush,
                    _sidebarSelectedBorderBrush);
                item.Click += SidebarItemButton_Click;
                groupState.Control.Body.Children.Add(item);
                _sidebarPathButtons[path] = item;
                _sidebarItems.Add(item);
                groupState.Paths.Add(path);
                if (string.Equals(groupState.Key, "quick_access", StringComparison.Ordinal))
                {
                    _sidebarQuickAccessPaths.Add(path);
                }
                else if (string.Equals(groupState.Key, "drives", StringComparison.Ordinal))
                {
                    _sidebarDrivePaths.Add(path);
                }
            }

            SidebarGroupState quickAccess = AddGroup("quick_access", "固定", expanded: true);
            AddItem(quickAccess, "桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddItem(quickAccess, "文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddItem(quickAccess, "下载", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

            SidebarGroupState drives = AddGroup("drives", "驱动器", expanded: true);

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    continue;
                }
                string letter = drive.Name.TrimEnd('\\');
                AddItem(drives, $"本地磁盘 ({letter})", drive.RootDirectory.FullName);
            }

            _ = AddGroup("cloud", "云盘", expanded: false);
            _ = AddGroup("network", "网络", expanded: false);
            _ = AddGroup("tags", "标签", expanded: false);
            ApplySidebarCompactState(_isSidebarCompact);
            UpdateSidebarSelectionOnly();
        }

        private void SidebarGroup_ExpandedChanged(object? sender, EventArgs e)
        {
            UpdateSidebarSelectionOnly();
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
            if (ExplorerBodyGrid is null)
            {
                return;
            }

            double bodyWidth = ExplorerBodyGrid.ActualWidth;
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
                    _sidebarPreferredExpandedWidth = Math.Clamp(dragWidth, SidebarExpandedMinWidth, SidebarExpandedMaxWidth);
                }
            }

            bool forcedCompact = maxSidebarWidth <= SidebarCompactThreshold;
            bool compact = forcedCompact || _sidebarPinnedCompact;
            double clampedWidth = Math.Min(_sidebarPreferredExpandedWidth, maxSidebarWidth);
            double finalWidth = compact
                ? SidebarCompactWidth
                : Math.Clamp(clampedWidth, SidebarExpandedMinWidth, SidebarExpandedMaxWidth);

            SidebarColumnWidth = new GridLength(finalWidth);
            ApplySidebarCompactState(compact);
        }

        private void ApplySidebarCompactState(bool compact)
        {
            if (_isSidebarCompact == compact && _sidebarGroups.Count > 0 && _sidebarItems.Count > 0)
            {
                return;
            }

            _isSidebarCompact = compact;

            foreach (SidebarGroupControl group in _sidebarGroups)
            {
                group.SetCompact(compact);
            }

            foreach (SidebarItemButton item in _sidebarItems)
            {
                item.SetCompact(compact);
            }
        }

        private void UpdateSidebarSelectionOnly()
        {
            bool quickAccessSelected = false;
            foreach (string quickPath in _sidebarQuickAccessPaths)
            {
                if (IsCurrentPath(quickPath))
                {
                    quickAccessSelected = true;
                    break;
                }
            }

            foreach ((string path, SidebarItemButton btn) in _sidebarPathButtons)
            {
                bool selected = IsCurrentPath(path);
                if (quickAccessSelected && _sidebarDrivePaths.Contains(path))
                {
                    selected = false;
                }
                btn.ApplySelection(selected);
            }

            foreach (SidebarGroupState groupState in _sidebarGroupStates)
            {
                if (quickAccessSelected && string.Equals(groupState.Key, "drives", StringComparison.Ordinal))
                {
                    groupState.Control.ApplySelection(false);
                    continue;
                }

                bool groupSelected = false;
                foreach (string path in groupState.Paths)
                {
                    if (IsCurrentPath(path))
                    {
                        groupSelected = true;
                        break;
                    }
                }

                groupState.Control.ApplySelection(groupSelected);
            }
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

            string curr = Path.GetFullPath(_currentPath).TrimEnd('\\');
            string cand = Path.GetFullPath(candidate).TrimEnd('\\');
            if (string.Equals(curr, cand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            // Highlight sidebar parent items too (e.g. inside Downloads subtree).
            return curr.StartsWith(cand + "\\", StringComparison.OrdinalIgnoreCase);
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
            if (!Directory.Exists(path))
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
                            RustBatchInterop.MarkPathChanged(_currentPath);
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

        private void SetPathInputInvalid()
        {
            PathTextBox.BorderBrush = new SolidColorBrush(Colors.IndianRed);
        }

        private void SetPathInputValid()
        {
            PathTextBox.BorderBrush = _pathDefaultBorderBrush;
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
            RequestMetadataForCurrentViewport();
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
            if (EntriesListView.SelectedItem is not EntryViewModel row || !row.IsLoaded)
            {
                return;
            }

            string targetPath = Path.Combine(_currentPath, row.Name);
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
            string? parent = Directory.GetParent(_currentPath)?.FullName;
            if (string.IsNullOrEmpty(parent))
            {
                UpdateStatus("Already at root.");
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
                if (Directory.Exists(basePath))
                {
                    string[] dirs = Directory.GetDirectories(basePath);
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

        private static bool HasChildDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return false;
            }

            try
            {
                using IEnumerator<string> it = Directory.EnumerateDirectories(path).GetEnumerator();
                return it.MoveNext();
            }
            catch
            {
                return false;
            }
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

        private async void ContextRename_Click(object sender, RoutedEventArgs e)
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

            string? newName = await PromptRenameAsync(entry.Name);
            if (string.IsNullOrWhiteSpace(newName))
            {
                return;
            }

            await RenameEntryAsync(entry, selectedIndex, newName);
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

        private async Task RenameEntryAsync(EntryViewModel entry, int selectedIndex, string newName)
        {
            string src = Path.Combine(_currentPath, entry.Name);
            string dst = Path.Combine(_currentPath, newName);
            try
            {
                await Task.Run(() => RustBatchInterop.RenamePath(src, dst));
                RenameTextBox.Text = string.Empty;
                ApplyLocalRename(selectedIndex, newName);
                _ = RefreshCurrentDirectoryInBackgroundAsync();
                UpdateStatus($"Rename success: {entry.Name} -> {newName}");
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
                await Task.Run(() => RustBatchInterop.DeletePath(targetPath, recursive));
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


    internal static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

}
