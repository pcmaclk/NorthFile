using FileExplorerUI.Interop;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private ScrollViewer GetCurrentViewportScrollViewer()
        {
            return _currentViewMode == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;
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
                DisplayName = string.Empty,
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
            entry.DisplayName = GetEntryDisplayName(row.Name, row.IsDirectory);
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
            ApplyEntryVisibilityStyling(entry);
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
                entry.ModifiedText = FormatModifiedTime(entry.ModifiedAt);
            }
            catch
            {
                entry.ModifiedAt = null;
                entry.ModifiedText = "-";
            }

            entry.IsMetadataLoaded = true;
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
    }
}
