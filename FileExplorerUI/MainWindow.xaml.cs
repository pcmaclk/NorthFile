using FileExplorerUI.Interop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow : Window
    {
        [DllImport("rust_engine.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int test_connection();

        private const uint InitialPageSize = 96;
        private const uint MinPageSize = 64;
        private const uint MaxPageSize = 1000;
        private readonly ObservableCollection<EntryViewModel> _entries = new();
        public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = new();
        private ulong _nextCursor;
        private bool _hasMore;
        private bool _isLoading;
        private string _currentPath = @"C:\";
        private int _statusCode;
        private uint _currentPageSize = InitialPageSize;
        private uint _lastFetchMs;
        private uint _totalEntries;
        private ScrollViewer? _listScrollViewer;
        private double _estimatedItemHeight = 32.0;
        private Brush? _pathDefaultBorderBrush;
        private readonly Stack<string> _backStack = new();
        private string _currentQuery = string.Empty;
        private readonly string _engineVersion;
        private RustUsnCapability _usnCapability;

        public MainWindow()
        {
            InitializeComponent();
            EntriesListView.ItemsSource = _entries;
            EntriesListView.SizeChanged += EntriesListView_SizeChanged;
            this.SizeChanged += MainWindow_SizeChanged;
            _pathDefaultBorderBrush = PathTextBox.BorderBrush;
            _engineVersion = RustBatchInterop.GetEngineVersion();

            _statusCode = test_connection();
            BackButton.IsEnabled = false;
            this.AppWindow.Title = $"FileExplorerUI | Engine {_engineVersion}";
            _ = LoadFirstPageAsync();
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateToPathAsync(PathTextBox.Text.Trim(), pushHistory: true);
        }

        private async void PathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await NavigateToPathAsync(PathTextBox.Text.Trim(), pushHistory: true);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadNextPageAsync();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            _currentQuery = SearchTextBox.Text?.Trim() ?? string.Empty;
            await LoadPageAsync(_currentPath, cursor: 0, append: false);
        }

        private async void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            _currentQuery = string.Empty;
            SearchTextBox.Text = string.Empty;
            await LoadPageAsync(_currentPath, cursor: 0, append: false);
        }

        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            _currentQuery = SearchTextBox.Text?.Trim() ?? string.Empty;
            await LoadPageAsync(_currentPath, cursor: 0, append: false);
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
            UpdateUsnCapability(_currentPath);
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
            }

            _currentPath = target;
            PathTextBox.Text = target;
            UpdateBreadcrumbs(target);
            BackButton.IsEnabled = _backStack.Count > 0;
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

            try
            {
                uint requestedPageSize = _currentPageSize;
                Stopwatch sw = Stopwatch.StartNew();
                FileBatchPage page;
                if (string.IsNullOrWhiteSpace(_currentQuery))
                {
                    page = await Task.Run(
                        () => RustBatchInterop.ReadDirectoryRowsAuto(path, cursor, requestedPageSize, _lastFetchMs)
                    );
                }
                else
                {
                    string query = _currentQuery;
                    page = await Task.Run(
                        () => RustBatchInterop.SearchDirectoryRowsAuto(path, query, cursor, requestedPageSize, _lastFetchMs)
                    );
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

                this.AppWindow.Title = $"连接:{_statusCode} 项:{_entries.Count}";
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
                this.AppWindow.Title = $"连接:{_statusCode} 目录读取失败";
                UpdateStatus($"Path: {path} | Error: {ex.Message}");
            }
            finally
            {
                _isLoading = false;
                LoadButton.IsEnabled = true;
                NextButton.IsEnabled = _hasMore;
            }
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = $"连接状态: {_statusCode}\n{message}";
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
                    MftRef = 0,
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
                    Type = row.IsDirectory ? "DIR" : "FILE",
                    MftRef = row.MftRef,
                    IsLoaded = true
                };
            }
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
                MftRef = current.MftRef,
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
            if (row.Type == "DIR")
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
                BackButton.IsEnabled = false;
                return;
            }

            string prev = _backStack.Pop();
            BackButton.IsEnabled = _backStack.Count > 0;
            await NavigateToPathAsync(prev, pushHistory: false);
        }

        private async void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string target)
            {
                return;
            }

            await NavigateToPathAsync(target, pushHistory: true);
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
                    FullPath = root
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
                    FullPath = current
                });
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
    }

    public sealed class EntryViewModel
    {
        public string Name { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public ulong MftRef { get; init; }
        public bool IsLoaded { get; init; }
    }

    public sealed class BreadcrumbItemViewModel
    {
        public string Label { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }
}
