using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void RebuildGroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries)
        {
            List<GroupedEntryColumnViewModel> projection = BuildGroupedEntryColumns(orderedEntries);
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

        private List<GroupedEntryColumnViewModel> BuildGroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries)
        {
            int rowsPerColumn = GetGroupedListRowsPerColumn();
            if (_currentGroupField == EntryGroupField.None)
            {
                return BuildUngroupedEntryColumns(orderedEntries, rowsPerColumn);
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
                .Select(bucket => CreateGroupedEntryColumn(bucket, rowsPerColumn))
                .ToList();
        }

        private List<GroupedEntryColumnViewModel> BuildUngroupedEntryColumns(IReadOnlyList<EntryViewModel> orderedEntries, int rowsPerColumn)
        {
            IReadOnlyList<EntryViewModel> items = orderedEntries.ToList();
            ApplyEntryViewState(items);

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

        private GroupedEntryColumnViewModel CreateGroupedEntryColumn(EntryGroupBucket bucket, int rowsPerColumn)
        {
            IReadOnlyList<EntryViewModel> items = bucket.Items.ToList();
            ApplyEntryViewState(items);
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
                columns.Add(new GroupedEntryItemColumnViewModel
                {
                    Items = items.Skip(i).Take(safeRowsPerColumn).ToList()
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
            return Math.Max(1, (int)Math.Floor(availableHeight / rowPitch));
        }

        private void RefreshGroupedColumnsForViewport()
        {
            if (!UsesColumnsListPresentation())
            {
                return;
            }

            int rowsPerColumn = GetGroupedListRowsPerColumn();
            if (rowsPerColumn == _groupedListRowsPerColumn)
            {
                return;
            }

            _groupedListRowsPerColumn = rowsPerColumn;
            RebuildGroupedEntryColumns(_entries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList());
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
            List<EntryViewModel> loadedEntries = _entries
                .Where(entry => entry.IsLoaded && !entry.IsGroupHeader)
                .ToList();
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
    }
}
