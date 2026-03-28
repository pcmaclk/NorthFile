using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void DetailsEntriesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer)
            {
                return;
            }

            int previousViewportStartIndex = _lastDetailsViewportStartIndex;
            int viewportStartIndex = -1;
            int viewportBottomIndex = -1;

            double previousVerticalOffset = _lastDetailsVerticalOffset;
            bool scrolled = HasScrollOffsetChanged(viewer, ref _lastDetailsHorizontalOffset, ref _lastDetailsVerticalOffset);
            _lastDetailsVerticalDelta = double.IsNaN(previousVerticalOffset)
                ? 0.0
                : Math.Abs(viewer.VerticalOffset - previousVerticalOffset);
            if (scrolled && _entriesFlyoutOpen && (_activeEntriesContextFlyout?.IsOpen ?? false))
            {
                HideActiveEntriesContextFlyout();
            }

            if (scrolled && RenameOverlayBorder.Visibility == Visibility.Visible)
            {
                HideRenameOverlay();
            }

            if (scrolled)
            {
                _lastDetailsScrollInteractionTick = Environment.TickCount64;
                InvalidateDetailsViewportRealization();
            }

            if (DetailsHeaderTranslateTransform is not null)
            {
                DetailsHeaderTranslateTransform.X = -viewer.HorizontalOffset;
            }

            UpdateEstimatedItemHeight();
            int logicalCount = GetLogicalEntryCount();
            if (logicalCount > 0)
            {
                viewportStartIndex = EstimateViewportIndex(viewer);
                viewportBottomIndex = EstimateViewportBottomIndex(viewer);
                LogDetailsViewportPerf(
                    "view-changed",
                    $"intermediate={e.IsIntermediate} offset={viewer.VerticalOffset:F1} scrollable={viewer.ScrollableHeight:F1} start={viewportStartIndex} end={viewportBottomIndex} entries={_entries.Count} total={_totalEntries}");
                _ = EnsureDataForViewportAsync(viewportStartIndex, viewportBottomIndex, preferMinimalPage: e.IsIntermediate);
            }

            if (viewportStartIndex >= 0)
            {
                _lastDetailsViewportIndexDelta = previousViewportStartIndex < 0
                    ? 0
                    : Math.Abs(viewportStartIndex - previousViewportStartIndex);
                _lastDetailsViewportStartIndex = viewportStartIndex;
            }

            if (e.IsIntermediate)
            {
                unchecked
                {
                    _metadataViewportRequestVersion++;
                }

                return;
            }

            RequestMetadataForCurrentViewportDeferred(48);
        }

        private void GroupedEntriesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer)
            {
                return;
            }

            bool scrolled = HasScrollOffsetChanged(viewer, ref _lastGroupedHorizontalOffset, ref _lastGroupedVerticalOffset);
            if (scrolled && RenameOverlayBorder.Visibility == Visibility.Visible)
            {
                HideRenameOverlay();
            }
        }

        private static bool HasScrollOffsetChanged(ScrollViewer viewer, ref double lastHorizontalOffset, ref double lastVerticalOffset)
        {
            bool changed =
                double.IsNaN(lastHorizontalOffset) ||
                double.IsNaN(lastVerticalOffset) ||
                Math.Abs(viewer.HorizontalOffset - lastHorizontalOffset) > 0.1 ||
                Math.Abs(viewer.VerticalOffset - lastVerticalOffset) > 0.1;

            lastHorizontalOffset = viewer.HorizontalOffset;
            lastVerticalOffset = viewer.VerticalOffset;
            return changed;
        }

        private void GroupedEntriesScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!UsesColumnsListPresentation())
            {
                return;
            }

            GroupedEntriesRepeater.InvalidateMeasure();
        }

        private void InvalidateDetailsViewportRealization(bool preferMinimalBuffer = false, bool forceSynchronous = false)
        {
            DetailsEntriesRepeater.InvalidateMeasure();
            if (forceSynchronous)
            {
                DetailsEntriesScrollViewer.UpdateLayout();
            }
        }

        private bool IsDetailsViewportInteractionHot()
        {
            if (_currentViewMode != EntryViewMode.Details)
            {
                return false;
            }

            return Environment.TickCount64 - Interlocked.Read(ref _lastDetailsScrollInteractionTick) < 180;
        }

        private bool IsSparseViewportLoadQueuedOrActive()
        {
            lock (_sparseViewportGate)
            {
                return _isSparseViewportLoadActive || _pendingSparseViewportTargetIndex is not null;
            }
        }

        private bool RangeHasPendingMetadata(int startIndex, int endIndex)
        {
            if (_entries.Count == 0 || startIndex < 0 || endIndex < startIndex)
            {
                return false;
            }

            int cappedEnd = Math.Min(endIndex, _entries.Count - 1);
            for (int i = Math.Max(0, startIndex); i <= cappedEnd; i++)
            {
                EntryViewModel entry = _entries[i];
                if (entry.IsLoaded && !entry.IsMetadataLoaded)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ViewportHasPendingMetadata()
        {
            if (_entries.Count == 0)
            {
                return false;
            }

            int startIndex;
            int endIndex;
            if (_currentViewMode != EntryViewMode.Details)
            {
                startIndex = 0;
                endIndex = _entries.Count - 1;
            }
            else
            {
                startIndex = EstimateViewportIndex(DetailsEntriesScrollViewer);
                endIndex = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
            }

            return RangeHasPendingMetadata(startIndex, endIndex);
        }

        private void CancelPendingViewportMetadataWork()
        {
            unchecked
            {
                _metadataViewportRequestVersion++;
            }

            CancelAndDispose(ref _metadataPrefetchCts);
        }

        private void RequestViewportWork()
        {
            if (_currentViewMode != EntryViewMode.Details)
            {
                return;
            }

            RequestPrefetchForCurrentViewport();
        }

        private void RequestPrefetchForCurrentViewport()
        {
            if (!_hasMore)
            {
                return;
            }

            int startIndex = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int endIndex = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
            _ = EnsureDataForViewportAsync(startIndex, endIndex);
        }

        private void RequestMetadataForCurrentViewportDeferred(int delayMs = 1)
        {
            int requestVersion = unchecked(++_metadataViewportRequestVersion);
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                if (requestVersion != _metadataViewportRequestVersion)
                {
                    return;
                }

                RequestMetadataForCurrentViewport();
            });
        }

        private void RequestMetadataForCurrentViewport()
        {
            unchecked
            {
                _metadataViewportRequestVersion++;
            }

            if (IsDetailsViewportInteractionHot() || IsSparseViewportLoadQueuedOrActive())
            {
                RequestMetadataForCurrentViewportDeferred(96);
                return;
            }

            if (_entries.Count == 0)
            {
                CancelAndDispose(ref _metadataPrefetchCts);
                return;
            }

            int visibleStart;
            int visibleEnd;
            if (_currentViewMode != EntryViewMode.Details)
            {
                visibleStart = 0;
                visibleEnd = _entries.Count - 1;
            }
            else
            {
                visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
                visibleEnd = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
            }

            if (visibleEnd < visibleStart)
            {
                return;
            }

            int visibleCount = Math.Max(1, visibleEnd - visibleStart + 1);
            bool throttleMetadataPrefetch = _entries.Count > 1024;
            int lookahead = throttleMetadataPrefetch
                ? 0
                : Math.Max(visibleCount, (int)_currentPageSize);
            int prefetchEnd = Math.Min(_entries.Count - 1, visibleEnd + lookahead);

            List<MetadataWorkItem> visibleItems = CollectMetadataWorkItems(visibleStart, visibleEnd);
            List<MetadataWorkItem> prefetchItems = throttleMetadataPrefetch
                ? []
                : CollectMetadataWorkItems(visibleEnd + 1, prefetchEnd);
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
    }
}
