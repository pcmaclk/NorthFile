using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using System;
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
            _groupedColumnsProjectionCache = projection;
            ApplyGroupedColumnsProjection(projection);
            UpdateGroupedColumnsCacheStamp();
        }

        private void ApplyGroupedColumnsProjection(IReadOnlyList<GroupedEntryColumnViewModel> projection)
        {
            _groupedEntryColumns.Clear();
            foreach (GroupedEntryColumnViewModel column in projection)
            {
                _groupedEntryColumns.Add(column);
            }
        }

        private List<GroupedEntryColumnViewModel> BuildGroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries, bool applyEntryState)
        {
            int rowsPerColumn = GetGroupedListRowsPerColumn();
            if (_currentGroupField == EntryGroupField.None)
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
            double headerHeight = _currentGroupField == EntryGroupField.None ? 0 : EntryItemMetrics.GroupHeaderHeight;
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
            int currentRows = _groupedListRowsPerColumn > 0 ? _groupedListRowsPerColumn : floorRows;

            if (double.IsNaN(_lastGroupedViewportHeight))
            {
                _lastGroupedViewportHeight = viewportHeight;
            }

            bool growing = viewportHeight > _lastGroupedViewportHeight + 0.5;
            bool shrinking = viewportHeight < _lastGroupedViewportHeight - 0.5;
            _lastGroupedViewportHeight = viewportHeight;

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
            if (refreshVersion >= 0 && refreshVersion != _groupedColumnsRefreshVersion)
            {
                return;
            }

            if (!UsesColumnsListPresentation())
            {
                return;
            }

            long nowStamp = Stopwatch.GetTimestamp();
            if (_lastGroupedColumnsRefreshAppliedStamp != 0)
            {
                double elapsedMs = (nowStamp - _lastGroupedColumnsRefreshAppliedStamp) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs < 20)
                {
                    return;
                }
            }

            int rowsPerColumn = GetGroupedListRowsPerColumn();
            int previousRowsPerColumn = _groupedListRowsPerColumn;
            if (!force && rowsPerColumn == _groupedListRowsPerColumn)
            {
                return;
            }

            _groupedListRowsPerColumn = rowsPerColumn;
            if (refreshVersion >= 0 && refreshVersion != _groupedColumnsRefreshVersion)
            {
                return;
            }
            GroupedEntriesRepeater.InvalidateMeasure();
            if (force)
            {
                GroupedEntriesScrollViewer.UpdateLayout();
            }
            _lastGroupedColumnsRefreshAppliedStamp = nowStamp;
            TraceListResize(
                $"refresh-done rows={previousRowsPerColumn}->{rowsPerColumn} force={force} visible={_entries.Count} " +
                $"elapsed={sw.ElapsedMilliseconds}ms");
        }

        private void RequestGroupedColumnsRefresh(bool force = false)
        {
            if (!UsesColumnsListPresentation())
            {
                CancelAndDispose(ref _groupedColumnsResizeDebounceCts);
                _groupedColumnsRefreshQueued = false;
                return;
            }

            if (_groupedColumnsRefreshQueued)
            {
                return;
            }

            int refreshVersion = Interlocked.Increment(ref _groupedColumnsRefreshVersion);
            _groupedColumnsRefreshQueued = true;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                _groupedColumnsRefreshQueued = false;
                RefreshGroupedColumnsForViewport(refreshVersion, force);
            });
        }

        private void RequestGroupedColumnsRefreshDebounced(int delayMs = 90, bool force = true)
        {
            if (!UsesColumnsListPresentation())
            {
                CancelAndDispose(ref _groupedColumnsResizeDebounceCts);
                return;
            }

            CancelAndDispose(ref _groupedColumnsResizeDebounceCts);
            var cts = new CancellationTokenSource();
            _groupedColumnsResizeDebounceCts = cts;
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

                if (cts.IsCancellationRequested || !ReferenceEquals(_groupedColumnsResizeDebounceCts, cts))
                {
                    return;
                }

                CancelAndDispose(ref _groupedColumnsResizeDebounceCts);
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
            if (_presentationSourceInitialized)
            {
                return true;
            }

            if (reason == PresentationReloadReason.DataRefresh)
            {
                return false;
            }

            if (_hasMore)
            {
                return false;
            }

            List<EntryViewModel> loadedEntries = GetLoadedEntriesFromCurrentCollection();
            if (loadedEntries.Count == 0)
            {
                return false;
            }

            return _totalEntries == 0 || loadedEntries.Count >= _totalEntries;
        }

        private void EnsurePresentationSourceCacheFromCurrentEntries()
        {
            if (_presentationSourceInitialized)
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
            var loadedEntries = new List<EntryViewModel>(_entries.Count);
            foreach (EntryViewModel entry in _entries)
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
            return _groupedColumnsCacheSourceVersion == _presentationSourceVersion
                && _groupedColumnsCacheSortField == _currentSortField
                && _groupedColumnsCacheSortDirection == _currentSortDirection
                && _groupedColumnsCacheGroupField == _currentGroupField
                && _groupedListRowsPerColumn > 0
                && _groupedColumnsCacheRowsPerColumn == _groupedListRowsPerColumn
                && _groupedColumnsProjectionCache is { Count: > 0 };
        }

        private void UpdateGroupedColumnsCacheStamp()
        {
            _groupedColumnsCacheSourceVersion = _presentationSourceVersion;
            _groupedColumnsCacheSortField = _currentSortField;
            _groupedColumnsCacheSortDirection = _currentSortDirection;
            _groupedColumnsCacheGroupField = _currentGroupField;
            _groupedColumnsCacheRowsPerColumn = _groupedListRowsPerColumn;
        }

        private void InvalidateProjectionCaches()
        {
            _groupedColumnsCacheSourceVersion = -1;
            _groupedColumnsCacheRowsPerColumn = -1;
            _groupedColumnsProjectionCache = null;
        }

        private void InvalidatePresentationSourceCache()
        {
            _presentationSourceEntries.Clear();
            _presentationSourceInitialized = false;
            _presentationSourceVersion++;
            InvalidateProjectionCaches();
        }

        private void SetPresentationSourceEntries(IReadOnlyList<EntryViewModel> entries)
        {
            _presentationSourceEntries.Clear();
            _presentationSourceEntries.AddRange(entries);
            _presentationSourceInitialized = true;
            _presentationSourceVersion++;
            InvalidateProjectionCaches();
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
