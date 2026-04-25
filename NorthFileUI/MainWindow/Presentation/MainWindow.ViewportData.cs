using NorthFileUI.Interop;
using NorthFileUI.Services;
using NorthFileUI.Collections;
using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private BatchObservableCollection<EntryViewModel> GetViewportEntries(WorkspacePanelId panelId)
        {
            return GetPanelEntries(panelId);
        }

        private ScrollViewer GetCurrentViewportScrollViewer()
        {
            return GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;
        }

        private ScrollViewer GetPanelViewportScrollViewer(WorkspacePanelId panelId)
        {
            EntryViewMode viewMode = panelId == WorkspacePanelId.Secondary
                ? EntryViewMode.Details
                : _workspaceLayoutHost.GetPanelState(panelId).ViewMode;
            return GetPanelActiveScrollViewer(panelId, viewMode);
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
            return Math.Max(PrimaryEntries.Count, checked((int)Math.Min(int.MaxValue, GetPanelTotalEntries(WorkspacePanelId.Primary))));
        }

        private int GetLogicalEntryCount(WorkspacePanelId panelId)
        {
            BatchObservableCollection<EntryViewModel> entries = GetViewportEntries(panelId);
            PanelViewState panel = GetPanelState(panelId);
            return Math.Max(entries.Count, checked((int)Math.Min(int.MaxValue, panel.DataSession.TotalEntries)));
        }

        private void UpdateEstimatedItemHeight()
        {
            _estimatedItemHeight = Math.Max(32.0, EntryItemMetrics.RowHeight + 4);
        }

        private void ReplaceEntriesWithLoadedRows(string basePath, IReadOnlyList<FileRow> rows)
        {
            PrimaryEntries.Clear();
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
                PrimaryEntries.Add(CreateLoadedEntryModel(basePath, row));
            }
        }

        private void EnsurePlaceholderCount(int target)
        {
            EnsurePanelPlaceholderCount(WorkspacePanelId.Primary, target);
        }

        private void EnsurePanelPlaceholderCount(WorkspacePanelId panelId, int target)
        {
            GetPanelEntries(panelId).Resize(target, CreatePlaceholderEntryModel);
        }

        private void EnsureLoadedRangeCapacity(int startIndex, int rowCount)
        {
            EnsurePanelLoadedRangeCapacity(WorkspacePanelId.Primary, startIndex, rowCount);
        }

        private void EnsurePanelLoadedRangeCapacity(WorkspacePanelId panelId, int startIndex, int rowCount)
        {
            if (startIndex < 0 || rowCount <= 0)
            {
                return;
            }

            int target = checked(startIndex + rowCount);
            BatchObservableCollection<EntryViewModel> entries = GetPanelEntries(panelId);
            if (target > entries.Count)
            {
                EnsurePanelPlaceholderCount(panelId, target);
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
            FillPanelPageRows(WorkspacePanelId.Primary, startIndex, rows, basePathOverride);
        }

        private void FillPanelPageRows(
            WorkspacePanelId panelId,
            int startIndex,
            IReadOnlyList<FileRow> rows,
            string? basePathOverride = null)
        {
            BatchObservableCollection<EntryViewModel> entries = GetPanelEntries(panelId);
            if (startIndex < 0 || startIndex >= entries.Count)
            {
                return;
            }

            string basePath = string.IsNullOrWhiteSpace(basePathOverride)
                ? GetPanelCurrentPath(panelId)
                : basePathOverride;
            int max = Math.Min(rows.Count, entries.Count - startIndex);
            for (int i = 0; i < max; i++)
            {
                ApplyLoadedEntryRow(entries[startIndex + i], basePath, rows[i]);
            }
        }

        private void FillPanelLoadedEntries(
            WorkspacePanelId panelId,
            int startIndex,
            IReadOnlyList<EntryViewModel> loadedEntries)
        {
            BatchObservableCollection<EntryViewModel> entries = GetPanelEntries(panelId);
            if (startIndex < 0 || startIndex >= entries.Count)
            {
                return;
            }

            int max = Math.Min(loadedEntries.Count, entries.Count - startIndex);
            for (int i = 0; i < max; i++)
            {
                entries[startIndex + i] = loadedEntries[i];
            }
        }

        private List<EntryViewModel> GetLoadedPanelEntries(WorkspacePanelId panelId)
        {
            return GetPanelEntries(panelId)
                .Where(entry => entry.IsLoaded && !entry.IsGroupHeader)
                .ToList();
        }

        private void SetPanelPresentationSourceEntries(
            WorkspacePanelId panelId,
            IReadOnlyList<EntryViewModel> loadedEntries)
        {
            if (panelId == WorkspacePanelId.Primary)
            {
                SetPresentationSourceEntries(loadedEntries);
                return;
            }

            PanelDataSession session = GetPanelDataSession(panelId);
            session.PresentationSourceEntries.Clear();
            session.PresentationSourceEntries.AddRange(loadedEntries);
            session.PresentationSourceInitialized = true;
            session.PresentationSourceVersion++;
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

            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List)
            {
                int rowsPerColumn = Math.Max(1, GetGroupedListRowsPerColumn());
                double columnStride = Math.Max(1, EntryContainerWidth + GroupedListColumnSpacing);
                int columnIndex = (int)Math.Floor(viewer.HorizontalOffset / columnStride);
                return Math.Clamp(columnIndex * rowsPerColumn, 0, logicalCount - 1);
            }

            double itemExtent = Math.Max(1.0, _estimatedItemHeight);
            int index = (int)Math.Floor(Math.Max(0.0, viewer.VerticalOffset) / itemExtent);
            return Math.Clamp(index, 0, logicalCount - 1);
        }

        private int EstimateViewportIndex(WorkspacePanelId panelId, ScrollViewer viewer)
        {
            int logicalCount = GetLogicalEntryCount(panelId);
            if (logicalCount <= 1)
            {
                return 0;
            }

            EntryViewMode viewMode = panelId == WorkspacePanelId.Secondary
                ? EntryViewMode.Details
                : GetPanelViewMode(panelId);

            if (viewMode == EntryViewMode.List)
            {
                int rowsPerColumn = Math.Max(1, GetGroupedListRowsPerColumn());
                double columnStride = Math.Max(1, EntryContainerWidth + GroupedListColumnSpacing);
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
            int visibleCount = GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List
                ? Math.Max(1, GetGroupedListRowsPerColumn() * Math.Max(1, (int)Math.Ceiling(viewer.ViewportWidth / Math.Max(1, EntryContainerWidth + GroupedListColumnSpacing))))
                : Math.Max(1, (int)Math.Ceiling(viewer.ViewportHeight / _estimatedItemHeight));
            int bottom = topIndex + visibleCount;
            return Math.Min(GetLogicalEntryCount() - 1, Math.Max(0, bottom));
        }

        private int EstimateViewportBottomIndex(WorkspacePanelId panelId, ScrollViewer viewer)
        {
            int topIndex = EstimateViewportIndex(panelId, viewer);
            EntryViewMode viewMode = panelId == WorkspacePanelId.Secondary
                ? EntryViewMode.Details
                : GetPanelViewMode(panelId);
            int visibleCount = viewMode == EntryViewMode.List
                ? Math.Max(1, GetGroupedListRowsPerColumn() * Math.Max(1, (int)Math.Ceiling(viewer.ViewportWidth / Math.Max(1, EntryContainerWidth + GroupedListColumnSpacing))))
                : Math.Max(1, (int)Math.Ceiling(viewer.ViewportHeight / _estimatedItemHeight));
            int bottom = topIndex + visibleCount;
            return Math.Min(GetLogicalEntryCount(panelId) - 1, Math.Max(0, bottom));
        }

        private void ResetEntriesViewport()
        {
            ResetPanelViewportState(WorkspacePanelId.Primary);

            if (DetailsHeaderTranslateTransform is not null)
            {
                DetailsHeaderTranslateTransform.X = 0;
            }
        }

        private double GetCurrentDetailsVerticalOffset()
        {
            return double.IsNaN(_lastDetailsVerticalOffset)
                ? Math.Max(0, DetailsEntriesScrollViewer.VerticalOffset)
                : Math.Max(0, _lastDetailsVerticalOffset);
        }

        private double GetCurrentPanelDetailsVerticalOffset(WorkspacePanelId panelId)
        {
            double verticalOffset = GetPanelLastDetailsVerticalOffset(panelId);
            return double.IsNaN(verticalOffset)
                ? Math.Max(0, GetPanelDetailsScrollViewer(panelId).VerticalOffset)
                : Math.Max(0, verticalOffset);
        }

        private double GetCurrentDetailsHorizontalOffset()
        {
            return double.IsNaN(_lastDetailsHorizontalOffset)
                ? Math.Max(0, DetailsEntriesScrollViewer.HorizontalOffset)
                : Math.Max(0, _lastDetailsHorizontalOffset);
        }

        private double GetCurrentPanelDetailsHorizontalOffset(WorkspacePanelId panelId)
        {
            double horizontalOffset = GetPanelLastDetailsHorizontalOffset(panelId);
            return double.IsNaN(horizontalOffset)
                ? Math.Max(0, GetPanelDetailsScrollViewer(panelId).HorizontalOffset)
                : Math.Max(0, horizontalOffset);
        }

        private double GetCurrentGroupedHorizontalOffset()
        {
            return double.IsNaN(_lastGroupedHorizontalOffset)
                ? Math.Max(0, GroupedEntriesScrollViewer.HorizontalOffset)
                : Math.Max(0, _lastGroupedHorizontalOffset);
        }

        private double GetCurrentPanelGroupedHorizontalOffset(WorkspacePanelId panelId)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                return 0;
            }

            double horizontalOffset = GetPanelLastGroupedHorizontalOffset(panelId);
            return double.IsNaN(horizontalOffset)
                ? Math.Max(0, GroupedEntriesScrollViewer.HorizontalOffset)
                : Math.Max(0, horizontalOffset);
        }

        private void RestoreCurrentViewportOffsets(
            double detailsHorizontalOffset,
            double detailsVerticalOffset,
            double groupedHorizontalOffset)
        {
            double safeDetailsHorizontalOffset = NormalizeViewportOffset(detailsHorizontalOffset);
            double safeDetailsVerticalOffset = NormalizeViewportOffset(detailsVerticalOffset);
            double safeGroupedHorizontalOffset = NormalizeViewportOffset(groupedHorizontalOffset);

            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details)
            {
                double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                double maxHorizontalOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableWidth);
                DetailsEntriesScrollViewer.ChangeView(
                    Math.Min(maxHorizontalOffset, safeDetailsHorizontalOffset),
                    Math.Min(maxOffset, safeDetailsVerticalOffset),
                    null,
                    disableAnimation: true);
                return;
            }

            double groupedMaxOffset = Math.Max(0, GroupedEntriesScrollViewer.ScrollableWidth);
            GroupedEntriesScrollViewer.ChangeView(
                Math.Min(groupedMaxOffset, safeGroupedHorizontalOffset),
                null,
                null,
                disableAnimation: true);
        }

        private static double NormalizeViewportOffset(double offset)
        {
            return double.IsNaN(offset) || double.IsInfinity(offset)
                ? 0
                : Math.Max(0, offset);
        }

        private void InvalidateEntriesLayouts()
        {
            DetailsEntriesRepeater.InvalidateMeasure();
            GroupedEntriesRepeater.InvalidateMeasure();
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
                $"start={safeStartIndex} end={safeEndIndex} entries={PrimaryEntries.Count} total={GetPanelTotalEntries(WorkspacePanelId.Primary)} loading={GetPanelIsLoading(WorkspacePanelId.Primary)}");

            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details && PrimaryEntries.Count < logicalCount)
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
            return IsPanelViewportRangeLoaded(WorkspacePanelId.Primary, startIndex, endIndex);
        }

        private bool IsPanelViewportRangeLoaded(WorkspacePanelId panelId, int startIndex, int endIndex)
        {
            if (startIndex < 0 || endIndex < startIndex)
            {
                return true;
            }

            BatchObservableCollection<EntryViewModel> entries = GetPanelEntries(panelId);
            if (startIndex >= entries.Count)
            {
                return false;
            }

            int cappedEnd = Math.Min(endIndex, entries.Count - 1);
            for (int index = startIndex; index <= cappedEnd; index++)
            {
                if (!entries[index].IsLoaded)
                {
                    return false;
                }
            }

            return cappedEnd >= endIndex;
        }

        private async Task EnsureSimplePanelDataForViewportAsync(
            WorkspacePanelId panelId,
            int startIndex,
            int endIndex,
            bool preferMinimalPage = false)
        {
            if (panelId == WorkspacePanelId.Primary)
            {
                await EnsureDataForViewportAsync(startIndex, endIndex, preferMinimalPage);
                return;
            }

            if (panelId == WorkspacePanelId.Secondary && !_isDualPaneEnabled)
            {
                return;
            }

            int logicalCount = GetLogicalEntryCount(panelId);
            if (logicalCount <= 0)
            {
                return;
            }

            int safeStartIndex = Math.Clamp(Math.Min(startIndex, endIndex), 0, logicalCount - 1);
            int safeEndIndex = Math.Clamp(Math.Max(startIndex, endIndex), safeStartIndex, logicalCount - 1);
            BatchObservableCollection<EntryViewModel> entries = GetPanelEntries(panelId);
            if (GetPanelViewMode(panelId) == EntryViewMode.Details && entries.Count < logicalCount)
            {
                EnsurePanelPlaceholderCount(panelId, logicalCount);
                InvalidatePanelDetailsViewportRealization(panelId);
            }

            if (!IsPanelViewportRangeLoaded(panelId, safeStartIndex, safeEndIndex))
            {
                await QueueSparseSimplePanelViewportLoadAsync(panelId, safeStartIndex, preferMinimalPage);
                return;
            }

            if (MaybePrefetchSimplePanelDetailsViewportBlock(panelId, safeStartIndex, safeEndIndex, preferMinimalPage))
            {
                return;
            }
        }

        private bool MaybePrefetchSimplePanelDetailsViewportBlock(
            WorkspacePanelId panelId,
            int startIndex,
            int endIndex,
            bool preferMinimalPage)
        {
            if (panelId == WorkspacePanelId.Primary)
            {
                return MaybePrefetchDetailsViewportBlock(startIndex, endIndex, preferMinimalPage);
            }

            if (GetPanelViewMode(panelId) != EntryViewMode.Details || IsSecondarySparseViewportLoadQueuedOrActive())
            {
                return false;
            }

            int logicalCount = GetLogicalEntryCount(panelId);
            if (logicalCount <= 0 || endIndex < 0)
            {
                return false;
            }

            BatchObservableCollection<EntryViewModel> entries = GetPanelEntries(panelId);
            int visibleCount = Math.Max(1, endIndex - startIndex + 1);
            int prefetchDistance = Math.Max(6, visibleCount / 3);
            int searchStart = Math.Min(logicalCount - 1, endIndex + 1);
            int searchEnd = Math.Min(logicalCount - 1, endIndex + Math.Max(visibleCount * 2, 36));
            int firstUnloadedIndex = -1;
            for (int index = searchStart; index <= searchEnd; index++)
            {
                if (index >= entries.Count || !entries[index].IsLoaded)
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
                return false;
            }

            _ = QueueSparseSimplePanelViewportLoadAsync(panelId, firstUnloadedIndex, preferMinimalPage);
            return true;
        }

        private bool MaybePrefetchDetailsViewportBlock(int startIndex, int endIndex, bool preferMinimalPage)
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
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
                if (index >= PrimaryEntries.Count || !PrimaryEntries[index].IsLoaded)
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
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details || targetIndex < 0 || targetIndex >= PrimaryEntries.Count)
            {
                return false;
            }

            if (PrimaryEntries[targetIndex].IsLoaded)
            {
                return false;
            }

            int loadedThreshold = Math.Max(192, checked((int)GetPanelCurrentPageSize(WorkspacePanelId.Primary)) * 2);
            int loadedCount = 0;
            for (int i = 0; i < PrimaryEntries.Count; i++)
            {
                if (PrimaryEntries[i].IsLoaded)
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
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details ||
                string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
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

            string path = currentPath;
            uint lastFetchMs = GetPanelLastFetchMs(WorkspacePanelId.Primary);
            long snapshotVersion = GetPanelDirectorySnapshotVersion(WorkspacePanelId.Primary);
            EnsureActiveEntryResultSet(path);
            IEntryResultSet? resultSet = GetPanelActiveEntryResultSet(WorkspacePanelId.Primary);
            if (resultSet is null)
            {
                return;
            }

            Stopwatch sw = Stopwatch.StartNew();
            LogDetailsViewportPerf(
                "sparse-fetch.begin",
                $"req={requestId} target={targetIndex} cursor={cursor} pageSize={pageSize} visible={visibleCount} block={blockSize} minimal={preferMinimalPage} entries={PrimaryEntries.Count} total={GetPanelTotalEntries(WorkspacePanelId.Primary)}");

            FileBatchPage page;
            bool ok;
            int rustErrorCode;
            string rustErrorMessage;
            int viewportIndexDelta = GetPanelLastDetailsViewportIndexDelta(WorkspacePanelId.Primary);
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
                $"req={requestId} ok={ok} elapsed={sw.ElapsedMilliseconds}ms cursor={cursor} rows={page.Rows.Count} total={page.TotalEntries} rust={rustErrorCode} sync={useSynchronousRead} delta={GetPanelLastDetailsVerticalDelta(WorkspacePanelId.Primary):F1} indexDelta={viewportIndexDelta} blockDelta={viewportBlockDelta}");

            if (!ok ||
                snapshotVersion != GetPanelDirectorySnapshotVersion(WorkspacePanelId.Primary) ||
                !string.Equals(path, GetPanelCurrentPath(WorkspacePanelId.Primary), StringComparison.OrdinalIgnoreCase))
            {
                LogDetailsViewportPerf(
                    "sparse-fetch.discard",
                    $"req={requestId} ok={ok} snapshotMatch={snapshotVersion == GetPanelDirectorySnapshotVersion(WorkspacePanelId.Primary)} pathMatch={string.Equals(path, GetPanelCurrentPath(WorkspacePanelId.Primary), StringComparison.OrdinalIgnoreCase)}");
                return;
            }

            Stopwatch bindSw = Stopwatch.StartNew();
            SetPanelTotalEntries(WorkspacePanelId.Primary, Math.Max(GetPanelTotalEntries(WorkspacePanelId.Primary), page.TotalEntries));
            EnsurePlaceholderCount(checked((int)Math.Min(int.MaxValue, GetPanelTotalEntries(WorkspacePanelId.Primary))));
            FillPageRows((int)cursor, page.Rows, path);
            InvalidateEntriesLayouts();
            CancelPendingViewportMetadataWork();
            bindSw.Stop();
            LogDetailsViewportPerf(
                "sparse-bind.end",
                $"req={requestId} elapsed={bindSw.ElapsedMilliseconds}ms cursor={cursor} rows={page.Rows.Count} entries={PrimaryEntries.Count} total={GetPanelTotalEntries(WorkspacePanelId.Primary)}");
        }

        private async Task LoadSparseSimplePanelViewportPageAsync(
            WorkspacePanelId panelId,
            int targetIndex,
            bool preferMinimalPage)
        {
            if (panelId == WorkspacePanelId.Primary)
            {
                await LoadSparseViewportPageAsync(targetIndex, preferMinimalPage);
                return;
            }

            PanelViewState panelState = GetPanelState(panelId);
            if (panelState.DataSession.IsLoading)
            {
                return;
            }

            string currentPath = string.IsNullOrWhiteSpace(panelState.CurrentPath)
                ? ShellMyComputerPath
                : panelState.CurrentPath;
            if (GetPanelViewMode(panelId) != EntryViewMode.Details ||
                string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            int logicalCount = GetLogicalEntryCount(panelId);
            if (logicalCount <= 0)
            {
                return;
            }

            ScrollViewer viewer = GetPanelDetailsScrollViewer(panelId);
            double viewportHeight = viewer.ViewportHeight > 0
                ? viewer.ViewportHeight
                : viewer.ActualHeight;
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

            string path = currentPath;
            string query = panelState.QueryText;
            long snapshotVersion = GetPanelDirectorySnapshotVersion(panelId);
            EnsureActiveEntryResultSet(panelId, path, query);
            IEntryResultSet? resultSet = GetPanelActiveEntryResultSet(panelId);
            if (resultSet is null)
            {
                return;
            }

            panelState.DataSession.IsLoading = true;
            RaiseSimplePanelDataStateChanged(panelId);
            try
            {
                uint lastFetchMs = GetPanelLastFetchMs(panelId);
                Stopwatch sw = Stopwatch.StartNew();
                FileBatchPage page;
                bool ok;
                int rustErrorCode;
                string rustErrorMessage;
                int viewportIndexDelta = GetPanelLastDetailsViewportIndexDelta(panelId);
                int viewportBlockDelta = Math.Max(0, (int)Math.Ceiling(viewportIndexDelta / (double)Math.Max(1, blockSize)));
                bool useSynchronousRead = preferMinimalPage && viewportBlockDelta <= 1;

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
                    "simple-sparse-fetch.end",
                    $"panel={panelId} ok={ok} elapsed={sw.ElapsedMilliseconds}ms target={targetIndex} cursor={cursor} rows={page.Rows.Count} total={page.TotalEntries} rust={rustErrorCode} sync={useSynchronousRead} indexDelta={viewportIndexDelta} blockDelta={viewportBlockDelta}");

                if (!ok ||
                    snapshotVersion != GetPanelDirectorySnapshotVersion(panelId) ||
                    !string.Equals(path, GetPanelCurrentPath(panelId), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                SetPanelLastFetchMs(panelId, (uint)Math.Clamp(sw.ElapsedMilliseconds, 0, int.MaxValue));
                SetPanelTotalEntries(panelId, Math.Max(GetPanelTotalEntries(panelId), page.TotalEntries));
                EnsurePanelPlaceholderCount(panelId, checked((int)Math.Min(int.MaxValue, GetPanelTotalEntries(panelId))));
                FillPanelPageRows(panelId, (int)cursor, page.Rows, path);
                SetPanelPresentationSourceEntries(panelId, GetLoadedPanelEntries(panelId));
                SetPanelNextCursor(panelId, page.NextCursor);
                SetPanelHasMore(panelId, page.HasMore);
                MarkPanelDataLoadedForCurrentNavigation(panelId);
                InvalidatePanelDetailsViewportRealization(panelId);
                RefreshPanelStatus(panelId);
                UpdateSimplePanelSelectionVisuals(panelId);
            }
            finally
            {
                panelState.DataSession.IsLoading = false;
                RaiseSimplePanelDataStateChanged(panelId);
            }
        }

        private async Task QueueSparseSimplePanelViewportLoadAsync(
            WorkspacePanelId panelId,
            int targetIndex,
            bool preferMinimalPage = false)
        {
            if (panelId == WorkspacePanelId.Primary)
            {
                await QueueSparseViewportLoadAsync(targetIndex, preferMinimalPage);
                return;
            }

            int logicalCount = GetLogicalEntryCount(panelId);
            if (logicalCount <= 0)
            {
                return;
            }

            bool shouldStartPump = false;
            int clampedTargetIndex = Math.Clamp(targetIndex, 0, logicalCount - 1);
            lock (_secondarySparseViewportGate)
            {
                _pendingSecondarySparseViewportTargetIndex = clampedTargetIndex;
                _pendingSecondarySparseViewportPreferMinimalPage = preferMinimalPage;
                if (!_isSecondarySparseViewportLoadActive)
                {
                    _isSecondarySparseViewportLoadActive = true;
                    shouldStartPump = true;
                }
            }

            if (!shouldStartPump)
            {
                LogDetailsViewportPerf("secondary-sparse-queue.update", $"target={clampedTargetIndex}");
                return;
            }

            try
            {
                while (true)
                {
                    int nextTargetIndex;
                    bool consumeMinimalPage;
                    lock (_secondarySparseViewportGate)
                    {
                        if (_pendingSecondarySparseViewportTargetIndex is null)
                        {
                            _isSecondarySparseViewportLoadActive = false;
                            return;
                        }

                        nextTargetIndex = _pendingSecondarySparseViewportTargetIndex.Value;
                        consumeMinimalPage = _pendingSecondarySparseViewportPreferMinimalPage;
                        _pendingSecondarySparseViewportTargetIndex = null;
                        _pendingSecondarySparseViewportPreferMinimalPage = false;
                    }

                    LogDetailsViewportPerf("secondary-sparse-queue.consume", $"target={nextTargetIndex} minimal={consumeMinimalPage}");
                    await LoadSparseSimplePanelViewportPageAsync(panelId, nextTargetIndex, consumeMinimalPage);
                }
            }
            finally
            {
                lock (_secondarySparseViewportGate)
                {
                    _isSecondarySparseViewportLoadActive = false;
                }
            }
        }

        private static void LogDetailsViewportPerf(string stage, string detail)
        {
            string message = $"[DETAILS-VP] stage={stage} {detail}";
            Debug.WriteLine(message);
            AppendNavigationPerfLog(message);
        }

        private void BeginDirectorySnapshot()
        {
            BeginDirectorySnapshot(WorkspacePanelId.Primary);
        }

        private void BeginDirectorySnapshot(WorkspacePanelId panelId)
        {
            CancellationTokenSource? metadataPrefetchCts = GetPanelMetadataPrefetchCts(panelId);
            CancellationTokenSource? directoryLoadCts = GetPanelDirectoryLoadCts(panelId);
            CancelAndDispose(ref metadataPrefetchCts);
            CancelAndDispose(ref directoryLoadCts);
            SetPanelMetadataPrefetchCts(panelId, null);
            SetPanelDirectoryLoadCts(panelId, null);
            SetPanelDirectoryLoadCts(panelId, new CancellationTokenSource());
            SetPanelDirectorySnapshotVersion(panelId, GetPanelDirectorySnapshotVersion(panelId) + 1);
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
