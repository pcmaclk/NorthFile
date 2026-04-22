using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void RebuildGroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries, bool applyEntryState = true)
        {
            List<GroupedEntryColumnViewModel> projection = BuildGroupedEntryColumns(orderedEntries, applyEntryState);
            SetPrimaryGroupedColumnsProjectionCache(projection);
            ApplyGroupedColumnsProjection(projection);
            UpdateGroupedColumnsCacheStamp();
        }

        private void ApplyGroupedColumnsProjection(IReadOnlyList<GroupedEntryColumnViewModel> projection)
        {
            ObservableCollection<GroupedEntryColumnViewModel> groupedEntryColumns = GetPrimaryGroupedEntryColumns();
            groupedEntryColumns.Clear();
            foreach (GroupedEntryColumnViewModel column in projection)
            {
                groupedEntryColumns.Add(column);
            }
        }

        private List<GroupedEntryColumnViewModel> BuildGroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries, bool applyEntryState)
        {
            int rowsPerColumn = GetGroupedListRowsPerColumn();
            if (GetPanelGroupField(WorkspacePanelId.Primary) == EntryGroupField.None)
            {
                return BuildUngroupedEntryColumns(orderedEntries, rowsPerColumn, applyEntryState);
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
                .Select(bucket => CreateGroupedEntryColumn(bucket, rowsPerColumn, applyEntryState))
                .ToList();
        }

        private List<GroupedEntryColumnViewModel> BuildUngroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries, int rowsPerColumn, bool applyEntryState)
        {
            IReadOnlyList<EntryViewModel> items = orderedEntries.ToList();
            if (applyEntryState)
            {
                ApplyEntryViewState(items);
            }

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

        private GroupedEntryColumnViewModel CreateGroupedEntryColumn(EntryGroupBucket bucket, int rowsPerColumn, bool applyEntryState)
        {
            IReadOnlyList<EntryViewModel> items = bucket.Items.ToList();
            if (applyEntryState)
            {
                ApplyEntryViewState(items);
            }
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
                int count = Math.Min(safeRowsPerColumn, items.Count - i);
                columns.Add(new GroupedEntryItemColumnViewModel
                {
                    Items = new EntryRangeList(items, i, count)
                });
            }

            return columns;
        }

        private int GetGroupedListRowsPerColumn()
        {
            double rowPitch = EntryItemMetrics.RowHeight + 4;
            double verticalPadding = GroupedEntriesScrollViewer.Padding.Top + GroupedEntriesScrollViewer.Padding.Bottom;
            double headerHeight = GetPanelGroupField(WorkspacePanelId.Primary) == EntryGroupField.None ? 0 : EntryItemMetrics.GroupHeaderHeight;
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
            int floorRows = Math.Max(1, (int)Math.Floor(availableHeight / rowPitch));
            int ceilRows = Math.Max(1, (int)Math.Ceiling(availableHeight / rowPitch));

            // Keep a small hysteresis band so resize feels responsive without oscillation.
            const double hysteresisFactor = 0.35;
            double hysteresis = rowPitch * hysteresisFactor;
            int groupedListRowsPerColumn = GetPrimaryGroupedListRowsPerColumn();
            int currentRows = groupedListRowsPerColumn > 0 ? groupedListRowsPerColumn : floorRows;

            double lastGroupedViewportHeight = GetPanelLastGroupedViewportHeight(WorkspacePanelId.Primary);
            if (double.IsNaN(lastGroupedViewportHeight))
            {
                lastGroupedViewportHeight = viewportHeight;
                SetPanelLastGroupedViewportHeight(WorkspacePanelId.Primary, viewportHeight);
            }

            bool growing = viewportHeight > lastGroupedViewportHeight + 0.5;
            bool shrinking = viewportHeight < lastGroupedViewportHeight - 0.5;
            SetPanelLastGroupedViewportHeight(WorkspacePanelId.Primary, viewportHeight);

            int targetRows = currentRows;
            if (currentRows < floorRows)
            {
                targetRows = floorRows;
            }
            else if (currentRows > ceilRows)
            {
                targetRows = ceilRows;
            }

            if (growing && currentRows < ceilRows)
            {
                double nextRowsThreshold = currentRows * rowPitch + hysteresis;
                if (availableHeight >= nextRowsThreshold)
                {
                    targetRows = Math.Min(ceilRows, currentRows + 1);
                }
            }

            if (shrinking && currentRows > floorRows)
            {
                double keepRowsThreshold = currentRows * rowPitch - hysteresis;
                if (availableHeight <= keepRowsThreshold)
                {
                    targetRows = Math.Max(floorRows, currentRows - 1);
                }
            }

            return targetRows;
        }

        private void RefreshGroupedColumnsForViewport(int refreshVersion = -1, bool force = false)
        {
            var sw = Stopwatch.StartNew();
            if (refreshVersion >= 0 && refreshVersion != GetPanelGroupedColumnsRefreshVersion(WorkspacePanelId.Primary))
            {
                return;
            }

            if (!UsesColumnsListPresentation())
            {
                return;
            }

            long nowStamp = Stopwatch.GetTimestamp();
            long lastGroupedColumnsRefreshAppliedStamp = GetPanelLastGroupedColumnsRefreshAppliedStamp(WorkspacePanelId.Primary);
            if (lastGroupedColumnsRefreshAppliedStamp != 0)
            {
                double elapsedMs = (nowStamp - lastGroupedColumnsRefreshAppliedStamp) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs < 20)
                {
                    return;
                }
            }

            int rowsPerColumn = GetGroupedListRowsPerColumn();
            int previousRowsPerColumn = GetPrimaryGroupedListRowsPerColumn();
            if (!force && rowsPerColumn == GetPrimaryGroupedListRowsPerColumn())
            {
                return;
            }

            SetPrimaryGroupedListRowsPerColumn(rowsPerColumn);
            if (refreshVersion >= 0 && refreshVersion != GetPanelGroupedColumnsRefreshVersion(WorkspacePanelId.Primary))
            {
                return;
            }
            GroupedEntriesRepeater.InvalidateMeasure();
            if (force)
            {
                GroupedEntriesScrollViewer.UpdateLayout();
            }
            SetPanelLastGroupedColumnsRefreshAppliedStamp(WorkspacePanelId.Primary, nowStamp);
            TraceListResize(
                $"refresh-done rows={previousRowsPerColumn}->{rowsPerColumn} force={force} visible={PrimaryEntries.Count} " +
                $"elapsed={sw.ElapsedMilliseconds}ms");
        }

        private void RequestGroupedColumnsRefresh(bool force = false)
        {
            if (!UsesColumnsListPresentation())
            {
                CancellationTokenSource? groupedColumnsResizeDebounceCts = GetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary);
                CancelAndDispose(ref groupedColumnsResizeDebounceCts);
                SetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary, null);
                SetPanelGroupedColumnsRefreshQueued(WorkspacePanelId.Primary, false);
                return;
            }

            if (GetPanelGroupedColumnsRefreshQueued(WorkspacePanelId.Primary))
            {
                return;
            }

            int refreshVersion = GetPanelGroupedColumnsRefreshVersion(WorkspacePanelId.Primary) + 1;
            SetPanelGroupedColumnsRefreshVersion(WorkspacePanelId.Primary, refreshVersion);
            SetPanelGroupedColumnsRefreshQueued(WorkspacePanelId.Primary, true);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                SetPanelGroupedColumnsRefreshQueued(WorkspacePanelId.Primary, false);
                RefreshGroupedColumnsForViewport(refreshVersion, force);
            });
        }

        private void RequestGroupedColumnsRefreshDebounced(int delayMs = 90, bool force = true)
        {
            if (!UsesColumnsListPresentation())
            {
                CancellationTokenSource? groupedColumnsResizeDebounceCts = GetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary);
                CancelAndDispose(ref groupedColumnsResizeDebounceCts);
                SetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary, null);
                return;
            }

            CancellationTokenSource? activeGroupedColumnsResizeDebounceCts = GetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary);
            CancelAndDispose(ref activeGroupedColumnsResizeDebounceCts);
            SetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary, null);
            var cts = new CancellationTokenSource();
            SetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary, cts);
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await Task.Delay(Math.Max(40, delayMs), cts.Token);
                }
                catch (TaskCanceledException)
                {
                    return;
                }

                if (cts.IsCancellationRequested || !ReferenceEquals(GetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary), cts))
                {
                    return;
                }

                CancellationTokenSource? completedGroupedColumnsResizeDebounceCts = GetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary);
                CancelAndDispose(ref completedGroupedColumnsResizeDebounceCts);
                SetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId.Primary, null);
                RequestGroupedColumnsRefresh(force);
            });
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
            if (GetPrimaryPresentationSourceInitialized())
            {
                return true;
            }

            if (reason == PresentationReloadReason.DataRefresh)
            {
                return false;
            }

            if (GetPanelHasMore(WorkspacePanelId.Primary))
            {
                return false;
            }

            List<EntryViewModel> loadedEntries = GetLoadedEntriesFromCurrentCollection();
            if (loadedEntries.Count == 0)
            {
                return false;
            }

            uint totalEntries = GetPanelTotalEntries(WorkspacePanelId.Primary);
            return totalEntries == 0 || loadedEntries.Count >= totalEntries;
        }

        private void EnsurePresentationSourceCacheFromCurrentEntries()
        {
            if (GetPrimaryPresentationSourceInitialized())
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
            var loadedEntries = new List<EntryViewModel>(PrimaryEntries.Count);
            foreach (EntryViewModel entry in PrimaryEntries)
            {
                if (entry.IsLoaded && !entry.IsGroupHeader)
                {
                    loadedEntries.Add(entry);
                }
            }
            return loadedEntries;
        }

        private bool TryUseGroupedColumnsCache()
        {
            return GetPrimaryGroupedColumnsCacheSourceVersion() == GetPrimaryPresentationSourceVersion()
                && GetPrimaryGroupedColumnsCacheSortField() == GetPanelSortField(WorkspacePanelId.Primary)
                && GetPrimaryGroupedColumnsCacheSortDirection() == GetPanelSortDirection(WorkspacePanelId.Primary)
                && GetPrimaryGroupedColumnsCacheGroupField() == GetPanelGroupField(WorkspacePanelId.Primary)
                && GetPrimaryGroupedListRowsPerColumn() > 0
                && _groupedColumnsCacheRowsPerColumn == GetPrimaryGroupedListRowsPerColumn()
                && GetPrimaryGroupedColumnsProjectionCache() is { Count: > 0 };
        }

        private void UpdateGroupedColumnsCacheStamp()
        {
            SetPrimaryGroupedColumnsCacheSourceVersion(GetPrimaryPresentationSourceVersion());
            SetPrimaryGroupedColumnsCacheSortField(GetPanelSortField(WorkspacePanelId.Primary));
            SetPrimaryGroupedColumnsCacheSortDirection(GetPanelSortDirection(WorkspacePanelId.Primary));
            SetPrimaryGroupedColumnsCacheGroupField(GetPanelGroupField(WorkspacePanelId.Primary));
            _groupedColumnsCacheRowsPerColumn = GetPrimaryGroupedListRowsPerColumn();
        }

        private void InvalidateProjectionCaches()
        {
            SetPrimaryGroupedColumnsCacheSourceVersion(-1);
            _groupedColumnsCacheRowsPerColumn = -1;
            SetPrimaryGroupedColumnsProjectionCache(null);
        }

        private void InvalidatePresentationSourceCache()
        {
            GetPrimaryPresentationSourceEntries().Clear();
            SetPrimaryPresentationSourceInitialized(false);
            IncrementPrimaryPresentationSourceVersion();
            InvalidateProjectionCaches();
        }

        private void SetPresentationSourceEntries(IReadOnlyList<EntryViewModel> entries)
        {
            List<EntryViewModel> presentationSourceEntries = GetPrimaryPresentationSourceEntries();
            presentationSourceEntries.Clear();
            presentationSourceEntries.AddRange(entries);
            SetPrimaryPresentationSourceInitialized(true);
            IncrementPrimaryPresentationSourceVersion();
            InvalidateProjectionCaches();
            LogPrimaryTabDataState("SetPresentationSourceEntries");
        }

        private sealed class EntryRangeList : IReadOnlyList<EntryViewModel>
        {
            private readonly IReadOnlyList<EntryViewModel> _source;
            private readonly int _start;

            public EntryRangeList(IReadOnlyList<EntryViewModel> source, int start, int count)
            {
                _source = source;
                _start = start;
                Count = count;
            }

            public int Count { get; }

            public EntryViewModel this[int index] => _source[_start + index];

            public IEnumerator<EntryViewModel> GetEnumerator()
            {
                for (int i = 0; i < Count; i++)
                {
                    yield return _source[_start + i];
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static void TraceListResize(string message)
        {
            AppendNavigationPerfLog($"[LIST-RESIZE] {message}");
        }
    }
}
