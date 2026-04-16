using FileExplorerUI.Interop;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using FileExplorerUI.Collections;
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
        private readonly record struct PanelPageLoadCoreResult(
            uint RequestedPageSize,
            long ElapsedMilliseconds,
            bool Ok,
            FileBatchPage Page,
            int RustErrorCode,
            string RustErrorMessage,
            List<FileRow> VisibleRows,
            string Source);

        private void ApplyPanelPageRows(
            WorkspacePanelId panelId,
            string path,
            IReadOnlyList<FileRow> visibleRows,
            bool append)
        {
            BatchObservableCollection<EntryViewModel> entries = GetPanelEntries(panelId);
            if (panelId == WorkspacePanelId.Primary)
            {
                if (!append)
                {
                    ResetEntriesViewport();
                    entries.Clear();
                    EnsureLoadedRangeCapacity(0, visibleRows.Count);
                    FillPageRows(0, visibleRows, path);
                }
                else
                {
                    EnsureLoadedRangeCapacity(entries.Count, visibleRows.Count);
                    FillPageRows(entries.Count, visibleRows, path);
                }

                List<EntryViewModel> primaryLoadedEntries = entries
                    .Where(entry => entry.IsLoaded && !entry.IsGroupHeader)
                    .ToList();
                SetPresentationSourceEntries(primaryLoadedEntries);
                SetPanelTotalEntries(panelId, (uint)entries.Count);
                MarkPanelDataLoadedForCurrentNavigation(panelId);
                InvalidateEntriesLayouts();
                LogPrimaryTabDataState($"ApplyPanelPageRows(primary, append={append})");
                return;
            }

            List<EntryViewModel> loadedEntries = append
                ? entries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList()
                : [];

            foreach (FileRow row in visibleRows)
            {
                loadedEntries.Add(CreateLoadedEntryModel(path, row));
            }

            entries.ReplaceAll(loadedEntries);
            PanelViewState panelState = _workspaceLayoutHost.GetPanelState(panelId);
            panelState.DataSession.PresentationSourceEntries.Clear();
            panelState.DataSession.PresentationSourceEntries.AddRange(loadedEntries);
            SetPanelTotalEntries(panelId, (uint)entries.Count);
            MarkPanelDataLoadedForCurrentNavigation(panelId);
        }

        private Task LoadPanelDataAsync(WorkspacePanelId panelId, CancellationToken cancellationToken = default)
        {
            return panelId == WorkspacePanelId.Primary
                ? LoadPrimaryPanelDataAsync(_workspaceLayoutHost.ShellState.Primary, cancellationToken: cancellationToken)
                : ReloadPanelDataAsync(
                    panelId,
                    preserveViewport: false,
                    ensureSelectionVisible: false,
                    focusEntries: false);
        }

        private async Task LoadFirstPageAsync(CancellationToken cancellationToken = default)
        {
            NavigationPerfSession? perf = TryGetCurrentNavigationPerfSession();
            perf?.Mark("load-first-page.enter");
            SetPrimaryPanelNavigationState(
                string.IsNullOrWhiteSpace(PathTextBox.Text) ? ShellMyComputerPath : NormalizeAddressInputPath(PathTextBox.Text),
                queryText: GetPanelQueryText(WorkspacePanelId.Primary),
                addressText: PathTextBox.Text,
                syncEditors: true);
            await LoadPrimaryPanelDataAsync(
                _workspaceLayoutHost.ShellState.Primary,
                perf,
                cancellationToken);
        }

        private async Task LoadPrimaryPanelDataAsync(
            PanelViewState panelState,
            NavigationPerfSession? perf = null,
            CancellationToken cancellationToken = default)
        {
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
            panelState.CurrentPageSize = InitialPageSize;
            panelState.DataSession.LastFetchMs = 0;
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            ClearPanelEntriesIfNavigationIsStale(WorkspacePanelId.Primary);
            LogPrimaryTabDataState("LoadPrimaryPanelDataAsync.after-clear-stale");
            UpdateBreadcrumbs(currentPath);
            UpdateDetailsHeaders();
            if (string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                _usnCapability = default;
                ConfigureDirectoryWatcher(string.Empty);
                PopulateMyComputerEntries();
                ApplyCurrentPresentation();
                LogPrimaryTabDataState("LoadPrimaryPanelDataAsync.my-computer.after-apply");
                UpdateFileCommandStates();
                if (perf is not null)
                {
                    ScheduleNavigationPerfFirstFrameMark(perf, "load-first-page.first-frame");
                }
                perf?.Mark("load-first-page.my-computer.completed");
                return;
            }

            UpdateUsnCapability(currentPath);
            ConfigureDirectoryWatcher(currentPath);
            EnsureRefreshFallbackInvalidation(currentPath, "manual_load");
            SyncActivePanelPresentationState();
            perf?.Mark("load-first-page.pipeline-selected", UsesClientPresentationPipeline() ? "client" : "paged");
            if (UsesClientPresentationPipeline())
            {
                await LoadAllEntriesForPresentationAsync(currentPath, perf, cancellationToken);
                return;
            }
            await LoadPageAsync(currentPath, cursor: 0, append: false, perf, cancellationToken);
        }

        private async Task NavigateToPathAsync(string path, bool pushHistory, bool focusEntriesAfterNavigation = true)
        {
            HideRenameOverlay();

            string target = string.IsNullOrWhiteSpace(path) ? @"C:\" : path.Trim();
            CaptureCurrentDirectoryViewState();
            NavigationPerfSession perf = BeginNavigationPerfSession(target, pushHistory ? "navigate" : "history");
            try
            {
                string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
                if (string.Equals(target, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                {
                    _directorySessionController.ApplyPushHistoryIfNeeded(
                        _backStack,
                        _forwardStack,
                        currentPath,
                        target,
                        pushHistory);

                    SetPrimaryPanelNavigationState(ShellMyComputerPath, queryText: string.Empty, syncEditors: true);
                    _pendingHistoryStateRestorePath = pushHistory ? null : ShellMyComputerPath;
                    UpdateBreadcrumbs(GetPanelCurrentPath(WorkspacePanelId.Primary));
                    UpdateNavButtonsState();
                    _ = SelectSidebarTreePathAsync(GetPanelCurrentPath(WorkspacePanelId.Primary));
                    await WaitForLoadIdleAsync();
                    await LoadPanelDataAsync(WorkspacePanelId.Primary);
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
                    perf.Mark("navigate.path-missing");
                    return;
                }
                perf.Mark("navigate.validated");

                _directorySessionController.ApplyPushHistoryIfNeeded(
                    _backStack,
                    _forwardStack,
                    currentPath,
                    target,
                    pushHistory);

                SetPrimaryPanelNavigationState(target, queryText: string.Empty, syncEditors: true);
                _pendingHistoryStateRestorePath = pushHistory ? null : target;
                UpdateBreadcrumbs(target);
                UpdateNavButtonsState();
                _ = SelectSidebarTreePathAsync(GetPanelCurrentPath(WorkspacePanelId.Primary));
                await WaitForLoadIdleAsync();
                await LoadPanelDataAsync(WorkspacePanelId.Primary);
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
            await LoadNextPanelPageAsync(WorkspacePanelId.Primary);
        }

        private async Task LoadNextPanelPageAsync(WorkspacePanelId panelId)
        {
            if (!GetPanelHasMore(panelId))
            {
                if (panelId == WorkspacePanelId.Primary)
                {
                    UpdateStatusKey("StatusNoMoreEntries");
                }

                return;
            }

            if (panelId == WorkspacePanelId.Primary)
            {
                await LoadPageAsync(GetPanelCurrentPath(WorkspacePanelId.Primary), GetPanelNextCursor(panelId), append: true);
                return;
            }

            await LoadNextSimplePanelPageAsync(panelId);
        }

        private CancellationTokenSource BeginNavigationLoadTransition()
        {
            CancellationTokenSource? navigationLoadCts = GetPanelNavigationLoadCts(WorkspacePanelId.Primary);
            CancelAndDispose(ref navigationLoadCts);
            SetPanelNavigationLoadCts(WorkspacePanelId.Primary, new CancellationTokenSource());
            return GetPanelNavigationLoadCts(WorkspacePanelId.Primary)!;
        }

        private async Task WaitForLoadIdleAsync()
        {
            while (GetPanelIsLoading(WorkspacePanelId.Primary))
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
                            GetPanelLastFetchMs(WorkspacePanelId.Primary),
                            GetPanelDirectorySortMode(WorkspacePanelId.Primary),
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
            EnsureActiveEntryResultSet(WorkspacePanelId.Primary, path, GetPanelQueryText(WorkspacePanelId.Primary));
        }

        private void EnsureActiveEntryResultSet(WorkspacePanelId panelId, string path, string query)
        {
            IEntryResultSet? activeEntryResultSet = GetPanelActiveEntryResultSet(panelId);
            DirectorySortMode sortMode = GetPanelDirectorySortMode(panelId);
            if (activeEntryResultSet is not null &&
                string.Equals(activeEntryResultSet.Path, path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(activeEntryResultSet.Query, query, StringComparison.Ordinal) &&
                activeEntryResultSet.SortMode == sortMode)
            {
                return;
            }

            SetPanelActiveEntryResultSet(
                panelId,
                string.IsNullOrWhiteSpace(query)
                    ? _explorerService.CreateDirectoryResultSet(path, sortMode)
                    : _explorerService.CreateSearchResultSet(path, query, sortMode));
        }

        private async Task LoadPageAsync(string path, ulong cursor, bool append, NavigationPerfSession? perf = null, CancellationToken cancellationToken = default)
        {
            if (GetPanelIsLoading(WorkspacePanelId.Primary))
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

            SetPanelIsLoading(WorkspacePanelId.Primary, true);
            LoadButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            StyledSidebarView.IsEnabled = false;

            try
            {
                PanelPageLoadCoreResult loadResult = await LoadPanelPageCoreAsync(
                    WorkspacePanelId.Primary,
                    path,
                    GetPanelQueryText(WorkspacePanelId.Primary),
                    cursor,
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    perf?.Mark("load-page.skipped", "cancelled-or-stale");
                    return;
                }

                if (!loadResult.Ok)
                {
                    if (IsRustAccessDenied(loadResult.RustErrorCode, loadResult.RustErrorMessage))
                    {
                        SetPanelHasMore(WorkspacePanelId.Primary, false);
                        SetPanelNextCursor(WorkspacePanelId.Primary, 0);
                        if (!append)
                        {
                            ResetEntriesViewport();
                            PrimaryEntries.Clear();
                            SetPanelTotalEntries(WorkspacePanelId.Primary, 0);
                            InvalidateEntriesLayouts();
                        }

                        UpdateStatusKey("StatusPathAccessDeniedSkip", path);
                        return;
                    }

                    throw new InvalidOperationException($"Rust error {loadResult.RustErrorCode}: {loadResult.RustErrorMessage}");
                }

                perf?.Mark("load-page.fetch-completed", $"rows={loadResult.Page.Rows.Count} total={loadResult.Page.TotalEntries} source={loadResult.Source}");
                SetPanelLastFetchMs(WorkspacePanelId.Primary, (uint)Math.Clamp(loadResult.ElapsedMilliseconds, 0, int.MaxValue));

                if (!append)
                {
                    ApplyPanelPageRows(WorkspacePanelId.Primary, path, loadResult.VisibleRows, append: false);
                    perf?.Mark("load-page.visible-entries-updated", $"count={PrimaryEntries.Count}");
                }
                else
                {
                    ApplyPanelPageRows(WorkspacePanelId.Primary, path, loadResult.VisibleRows, append: true);
                    perf?.Mark("load-page.visible-entries-updated", $"count={PrimaryEntries.Count}");
                }
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
                SetPanelNextCursor(WorkspacePanelId.Primary, loadResult.Page.NextCursor);
                SetPanelHasMore(WorkspacePanelId.Primary, loadResult.Page.HasMore);
                SetPanelCurrentPageSize(WorkspacePanelId.Primary, ClampPageSize(loadResult.Page.SuggestedNextLimit, loadResult.RequestedPageSize));
                string source = loadResult.Source;
                perf?.Mark("load-page.bind-completed", $"visible={PrimaryEntries.Count} hasMore={GetPanelHasMore(WorkspacePanelId.Primary)}");

                void FinalizeLoadedPageUi()
                {
                    perf?.Mark("load-page.ui-finalize.begin");
                    UpdateFileCommandStates();
                    if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details)
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
                    UpdateStatus(SF("StatusCurrentFolderItems", GetPanelTotalEntries(WorkspacePanelId.Primary)));
                    LogPerfSnapshot(
                        mode: string.IsNullOrWhiteSpace(GetPanelQueryText(WorkspacePanelId.Primary)) ? "browse" : "search",
                        path: path,
                        query: GetPanelQueryText(WorkspacePanelId.Primary),
                        source: source,
                        loaded: loadResult.Page.Rows.Count,
                        total: loadResult.Page.TotalEntries,
                        scanned: loadResult.Page.ScannedEntries,
                        matched: loadResult.Page.MatchedEntries,
                        fetchMs: loadResult.ElapsedMilliseconds,
                        batch: GetPanelCurrentPageSize(WorkspacePanelId.Primary),
                        hasMore: GetPanelHasMore(WorkspacePanelId.Primary),
                        nextCursor: GetPanelNextCursor(WorkspacePanelId.Primary),
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
                SetPanelIsLoading(WorkspacePanelId.Primary, false);
                LoadButton.IsEnabled = true;
                NextButton.IsEnabled = GetPanelHasMore(WorkspacePanelId.Primary);
                StyledSidebarView.IsEnabled = true;
                perf?.Mark("load-page.exit");
            }
        }

        private async Task<PanelPageLoadCoreResult> LoadPanelPageCoreAsync(
            WorkspacePanelId panelId,
            string path,
            string query,
            ulong cursor,
            CancellationToken cancellationToken)
        {
            EnsureActiveEntryResultSet(panelId, path, query);
            uint requestedPageSize = GetPanelCurrentPageSize(panelId);
            Stopwatch sw = Stopwatch.StartNew();
            IEntryResultSet? resultSet = GetPanelActiveEntryResultSet(panelId);
            if (resultSet is null)
            {
                throw new InvalidOperationException("active entry result set was not initialized");
            }

            (bool ok, FileBatchPage page, int rustErrorCode, string rustErrorMessage) = await Task.Run(
                () =>
                {
                    bool success = resultSet.TryReadRange(
                        cursor,
                        requestedPageSize,
                        GetPanelLastFetchMs(panelId),
                        out FileBatchPage p,
                        out int code,
                        out string msg);
                    return (success, p, code, msg);
                },
                cancellationToken);

            sw.Stop();
            List<FileRow> visibleRows = ok
                ? page.Rows
                    .Where(row => ShouldIncludeEntry(Path.Combine(path, row.Name), row.Name))
                    .ToList()
                : [];

            return new PanelPageLoadCoreResult(
                RequestedPageSize: requestedPageSize,
                ElapsedMilliseconds: sw.ElapsedMilliseconds,
                Ok: ok,
                Page: page,
                RustErrorCode: rustErrorCode,
                RustErrorMessage: rustErrorMessage,
                VisibleRows: visibleRows,
                Source: ok ? _explorerService.DescribeBatchSource(page.SourceKind) : string.Empty);
        }

        private async Task LoadAllEntriesForPresentationAsync(string path, NavigationPerfSession? perf = null, CancellationToken cancellationToken = default)
        {
            if (GetPanelIsLoading(WorkspacePanelId.Primary))
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
            SetPanelIsLoading(WorkspacePanelId.Primary, true);
            LoadButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            StyledSidebarView.IsEnabled = false;

            try
            {
                PanelEntriesLoadResult loadResult = await LoadPanelEntriesSnapshotAsync(
                    path,
                    GetPanelQueryText(WorkspacePanelId.Primary),
                    GetPanelLastFetchMs(WorkspacePanelId.Primary),
                    cancellationToken,
                    perf,
                    perfPrefix: "load-all");

                ResetEntriesViewport();
                SetPresentationSourceEntries(loadResult.Entries);
                SetPanelActiveEntryResultSet(WorkspacePanelId.Primary, loadResult.ActiveEntryResultSet);
                perf?.Mark("load-all.fetch-completed", $"loaded={loadResult.Entries.Count}");
                ApplyCurrentPresentation(perf);
                SetPanelTotalEntries(WorkspacePanelId.Primary, (uint)loadResult.Entries.Count);
                MarkPanelDataLoadedForCurrentNavigation(WorkspacePanelId.Primary);
                InvalidateEntriesLayouts();
                SetPanelNextCursor(WorkspacePanelId.Primary, 0);
                SetPanelHasMore(WorkspacePanelId.Primary, false);
                SetPanelCurrentPageSize(WorkspacePanelId.Primary, InitialPageSize);
                _lastTitleWasReadFailed = false;
                UpdateWindowTitle();
                UpdateStatus(SF("StatusCurrentFolderItems", GetPanelTotalEntries(WorkspacePanelId.Primary)));
                if (perf is not null)
                {
                    ScheduleNavigationPerfFirstFrameMark(perf, "load-all.first-frame");
                }
                perf?.Mark("load-all.completed", $"visible={PrimaryEntries.Count}");
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
                SetPanelIsLoading(WorkspacePanelId.Primary, false);
                LoadButton.IsEnabled = true;
                NextButton.IsEnabled = GetPanelHasMore(WorkspacePanelId.Primary);
                StyledSidebarView.IsEnabled = true;
                UpdateFileCommandStates();
                perf?.Mark("load-all.exit");
            }
        }

    }
}
