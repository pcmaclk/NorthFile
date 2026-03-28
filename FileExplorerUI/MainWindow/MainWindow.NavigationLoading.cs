using FileExplorerUI.Interop;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async Task LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            NavigationPerfSession? perf = TryGetCurrentNavigationPerfSession();
            perf?.Mark("load-first-page.enter");
            _currentPath = string.IsNullOrWhiteSpace(PathTextBox.Text) ? ShellMyComputerPath : NormalizeAddressInputPath(PathTextBox.Text);
            PersistLastOpenedPathIfNeeded();
            if (!_sidebarInitialized)
            {
                BuildSidebarItems();
                _sidebarInitialized = true;
            }
            else
            {
                UpdateSidebarSelectionOnly();
            }
            RefreshSidebarFavorites(refreshSelection: false);
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
                await LoadAllEntriesForPresentationAsync(_currentPath, perf, cancellationToken);
                return;
            }
            await LoadPageAsync(_currentPath, cursor: 0, append: false, perf, cancellationToken);
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
                    _directorySessionController.ApplyPushHistoryIfNeeded(
                        _backStack,
                        _forwardStack,
                        _currentPath,
                        target,
                        pushHistory);

                _currentPath = ShellMyComputerPath;
                _pendingHistoryStateRestorePath = pushHistory ? null : ShellMyComputerPath;
                PathTextBox.Text = GetDisplayPathText(ShellMyComputerPath);
                _currentQuery = string.Empty;
                SearchTextBox.Text = string.Empty;
                UpdateBreadcrumbs(_currentPath);
                UpdateNavButtonsState();
                _ = SelectSidebarTreePathAsync(_currentPath);
                await WaitForLoadIdleAsync();
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

                _directorySessionController.ApplyPushHistoryIfNeeded(
                    _backStack,
                    _forwardStack,
                    _currentPath,
                    target,
                    pushHistory);

                _currentPath = target;
                _pendingHistoryStateRestorePath = pushHistory ? null : target;
                PathTextBox.Text = GetDisplayPathText(target);
                _currentQuery = string.Empty;
                SearchTextBox.Text = string.Empty;
                UpdateBreadcrumbs(target);
                UpdateNavButtonsState();
                _ = SelectSidebarTreePathAsync(_currentPath);
                await WaitForLoadIdleAsync();
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

        private CancellationTokenSource BeginNavigationLoadTransition()
        {
            CancelAndDispose(ref _navigationLoadCts);
            _navigationLoadCts = new CancellationTokenSource();
            return _navigationLoadCts;
        }

        private async Task WaitForLoadIdleAsync()
        {
            while (_isLoading)
            {
                await Task.Delay(16);
            }
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

        private async Task LoadPageAsync(string path, ulong cursor, bool append, NavigationPerfSession? perf = null, CancellationToken cancellationToken = default)
        {
            if (_isLoading)
            {
                perf?.Mark("load-page.skipped", "already-loading");
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                perf?.Mark("load-page.skipped", "cancelled-before-start");
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

                if (cancellationToken.IsCancellationRequested)
                {
                    perf?.Mark("load-page.skipped", "cancelled-or-stale");
                    return;
                }

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
                List<FileRow> visibleRows = page.Rows
                    .Where(row => ShouldIncludeEntry(Path.Combine(path, row.Name), row.Name))
                    .ToList();

                if (!append)
                {
                    ResetEntriesViewport();
                    _entries.Clear();
                    EnsureLoadedRangeCapacity(0, visibleRows.Count);
                    FillPageRows(0, visibleRows, path);
                    perf?.Mark("load-page.visible-entries-updated", $"count={_entries.Count}");
                }
                else
                {
                    EnsureLoadedRangeCapacity(_entries.Count, visibleRows.Count);
                    FillPageRows(_entries.Count, visibleRows, path);
                    perf?.Mark("load-page.visible-entries-updated", $"count={_entries.Count}");
                }
                _totalEntries = (uint)_entries.Count;
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
                if (cancellationToken.IsCancellationRequested)
                {
                    perf?.Mark("load-page.cancelled", ex.Message);
                    return;
                }

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

        private async Task LoadAllEntriesForPresentationAsync(string path, NavigationPerfSession? perf = null, CancellationToken cancellationToken = default)
        {
            if (_isLoading)
            {
                perf?.Mark("load-all.skipped", "already-loading");
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                perf?.Mark("load-all.skipped", "cancelled-before-start");
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

                    if (cancellationToken.IsCancellationRequested)
                    {
                        perf?.Mark("load-all.skipped", "cancelled-or-stale");
                        return;
                    }

                    if (!ok)
                    {
                        throw new InvalidOperationException($"Rust error {rustErrorCode}: {rustErrorMessage}");
                    }

                    totalEntries = page.TotalEntries;
                    foreach (FileRow row in page.Rows)
                    {
                        if (!ShouldIncludeEntry(Path.Combine(path, row.Name), row.Name))
                        {
                            continue;
                        }

                        EntryViewModel entry = CreateLoadedEntryModel(path, row);
                        PopulateEntryMetadata(entry);
                        loadedEntries.Add(entry);
                    }

                    cursor = page.NextCursor;
                    hasMore = page.HasMore;
                    perf?.Mark("load-all.batch", $"loaded={loadedEntries.Count} total={totalEntries} hasMore={hasMore}");
                } while (hasMore);

                ResetEntriesViewport();
                SetPresentationSourceEntries(loadedEntries);
                perf?.Mark("load-all.fetch-completed", $"loaded={loadedEntries.Count}");
                ApplyCurrentPresentation(perf);
                _totalEntries = (uint)loadedEntries.Count;
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
                if (cancellationToken.IsCancellationRequested)
                {
                    perf?.Mark("load-all.cancelled", ex.Message);
                    return;
                }

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

    }
}
