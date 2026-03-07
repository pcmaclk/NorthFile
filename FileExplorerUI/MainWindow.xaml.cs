using FileExplorerUI.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public sealed partial class MainWindow : Window
    {
        private const uint InitialPageSize = 96;
        private const uint MinPageSize = 64;
        private const uint MaxPageSize = 1000;
        private readonly ObservableCollection<EntryViewModel> _entries = new();
        public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = new();
        private ulong _nextCursor;
        private bool _hasMore;
        private bool _isLoading;
        private bool _entriesFlyoutOpen;
        private bool _entriesPointerHooked;
        private string _currentPath = @"C:\";
        private uint _currentPageSize = InitialPageSize;
        private uint _lastFetchMs;
        private uint _totalEntries;
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
        private CommandDockSide _commandDockSide = CommandDockSide.Top;
        private bool _showCommandDock = false;
        private bool _sidebarInitialized;
        private long _lastWatcherRefreshTick;
        private long _lastNavInvokeTick;
        private readonly Brush _sidebarSelectedBorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x1E, 0xA7, 0xFF));
        private readonly Brush _sidebarTransparentBrush = new SolidColorBrush(Colors.Transparent);
        private readonly Brush _sidebarSelectedBackgroundBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        private readonly Brush _sidebarHoverBackgroundBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xCC, 0xCC, 0xCC));
        private readonly Brush _sidebarTextBrush = new SolidColorBrush(Colors.DimGray);
        private readonly Brush _sidebarNormalBackgroundBrush = new SolidColorBrush(Colors.Transparent);
        private readonly Dictionary<string, SidebarItemButton> _sidebarPathButtons = new(StringComparer.OrdinalIgnoreCase);
        private IntPtr _windowHandle;
        private IntPtr _originalWndProc;
        private WndProcDelegate? _wndProcDelegate;
        private MenuFlyout? _activeBreadcrumbFlyout;

        private enum CommandDockSide
        {
            Top,
            Right,
            Bottom
        }

        private const int GWL_WNDPROC = -4;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public MainWindow()
        {
            InitializeComponent();
            EntriesListView.ItemsSource = _entries;
            EntriesContextFlyout.OverlayInputPassThroughElement = EntriesListView;
            EntriesListView.SizeChanged += EntriesListView_SizeChanged;
            this.SizeChanged += MainWindow_SizeChanged;
            _pathDefaultBorderBrush = PathTextBox.BorderBrush;
            _engineVersion = RustBatchInterop.GetEngineVersion();

            UpdateNavButtonsState();
            this.AppWindow.Title = $"FileExplorerUI | Engine {_engineVersion}";
            BuildSidebarItems();
            _sidebarInitialized = true;
            ApplyCommandDockLayout();
            InstallWindowHook();
            _ = LoadFirstPageAsync();
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
            RequestPrefetchForCurrentViewport();
        }

        private async void ListScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer || !_hasMore)
            {
                return;
            }

            UpdateEstimatedItemHeight();
            int estimatedIndex = EstimateViewportBottomIndex(viewer);
            await EnsureDataForIndexAsync(estimatedIndex);
        }

        private void EntriesListView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateEstimatedItemHeight();
            RequestPrefetchForCurrentViewport();
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            UpdateEstimatedItemHeight();
            RequestPrefetchForCurrentViewport();
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

                _nextCursor = page.NextCursor;
                _hasMore = page.HasMore;
                _currentPageSize = ClampPageSize(page.SuggestedNextLimit, requestedPageSize);
                string source = RustBatchInterop.DescribeBatchSource(page.SourceKind);
                string searchInfo = string.IsNullOrWhiteSpace(_currentQuery) ? string.Empty : $" | Query: {_currentQuery}";
                string hitInfo = page.ScannedEntries > 0
                    ? $" | Match: {page.MatchedEntries}/{page.ScannedEntries} ({(page.MatchedEntries * 100.0 / page.ScannedEntries):F1}%)"
                    : string.Empty;
                string usnInfo = $" | USN: {DescribeUsnCapability(_usnCapability)}";

                this.AppWindow.Title = $"FileExplorerUI | Engine {_engineVersion} | Items: {_entries.Count}";
                UpdateStatus(
                    $"Path: {path}{searchInfo}{hitInfo}{usnInfo} | Loaded: {page.Rows.Count} | Total: {_totalEntries} | Source: {source} | HasMore: {_hasMore} | NextCursor: {_nextCursor} | Fetch: {sw.ElapsedMilliseconds}ms | Batch: {_currentPageSize}"
                );
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
                this.AppWindow.Title = $"FileExplorerUI | Engine {_engineVersion} | Read Failed";
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

            StackPanel AddGroup(string text, bool expanded = true)
            {
                var group = new SidebarGroupControl(
                    text,
                    _sidebarNormalBackgroundBrush,
                    _sidebarHoverBackgroundBrush,
                    _sidebarTextBrush,
                    expanded,
                    headerHeight: 28)
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                SidebarGroupsHost.Children.Add(group);
                return group.Body;
            }

            void AddItem(StackPanel parent, string label, string path)
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
                    _sidebarTextBrush,
                    _sidebarTransparentBrush,
                    _sidebarSelectedBorderBrush);
                item.Click += SidebarItemButton_Click;
                parent.Children.Add(item);
                _sidebarPathButtons[path] = item;
            }

            StackPanel quickAccess = AddGroup("固定", expanded: true);
            AddItem(quickAccess, "桌面", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddItem(quickAccess, "文档", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddItem(quickAccess, "下载", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

            StackPanel drives = AddGroup("驱动器", expanded: true);

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                {
                    continue;
                }
                string letter = drive.Name.TrimEnd('\\');
                AddItem(drives, $"本地磁盘 ({letter})", drive.RootDirectory.FullName);
            }

            AddGroup("云盘", expanded: false);
            AddGroup("网络", expanded: false);
            AddGroup("标签", expanded: false);
            UpdateSidebarSelectionOnly();
        }

        private void UpdateSidebarSelectionOnly()
        {
            foreach ((string path, SidebarItemButton btn) in _sidebarPathButtons)
            {
                bool selected = IsCurrentPath(path);
                btn.ApplySelection(selected);
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

        private void RequestPrefetchForCurrentViewport()
        {
            if (_listScrollViewer is null || !_hasMore)
            {
                return;
            }

            int idx = EstimateViewportBottomIndex(_listScrollViewer);
            _ = EnsureDataForIndexAsync(idx);
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
                    MftRef = 0,
                    SizeText = "",
                    ModifiedText = "",
                    IsDirectory = false,
                    IsLoaded = false
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
                    Type = row.IsDirectory ? "文件夹" : GetFileTypeText(row.Name),
                    IconGlyph = row.IsDirectory ? "\uE8B7" : "\uE8A5",
                    MftRef = row.MftRef,
                    SizeText = row.IsDirectory ? "" : GetFileSizeText(Path.Combine(_currentPath, row.Name)),
                    ModifiedText = GetModifiedTimeText(Path.Combine(_currentPath, row.Name), row.IsDirectory),
                    IsDirectory = row.IsDirectory,
                    IsLoaded = true
                };
            }
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
                MftRef = current.MftRef,
                SizeText = current.SizeText,
                ModifiedText = current.ModifiedText,
                IsDirectory = current.IsDirectory,
                IsLoaded = true
            };
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

            flyout.ShowAt(btn);
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
            }
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

    public sealed class EntryViewModel
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public string IconGlyph { get; init; } = "\uE8A5";
        public ulong MftRef { get; init; }
        public string SizeText { get; init; } = string.Empty;
        public string ModifiedText { get; init; } = string.Empty;
        public bool IsDirectory { get; init; }
        public bool IsLoaded { get; init; }
    }

    public sealed class BreadcrumbItemViewModel : INotifyPropertyChanged
    {
        private string _label = string.Empty;
        private string _fullPath = string.Empty;
        private bool _hasChildren;
        private bool _isLast;
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
    }


    internal static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

}
