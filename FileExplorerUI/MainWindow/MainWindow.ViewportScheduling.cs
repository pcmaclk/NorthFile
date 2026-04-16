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
            HandleDetailsEntriesScrollViewerViewChanged(WorkspacePanelId.Primary, sender, e);
        }

        private void SecondaryEntriesScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            HandleDetailsEntriesScrollViewerViewChanged(WorkspacePanelId.Secondary, sender, e);
        }

        private void HandleDetailsEntriesScrollViewerViewChanged(
            WorkspacePanelId panelId,
            object? sender,
            ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer viewer)
            {
                return;
            }

            int previousViewportStartIndex = GetPanelLastDetailsViewportStartIndex(panelId);
            int viewportStartIndex = -1;
            int viewportBottomIndex = -1;

            double previousVerticalOffset = GetPanelLastDetailsVerticalOffset(panelId);
            (bool scrolled, double horizontalOffset, double verticalOffset) = GetScrollOffsetChange(
                viewer,
                GetPanelLastDetailsHorizontalOffset(panelId),
                GetPanelLastDetailsVerticalOffset(panelId));
            SetPanelLastDetailsHorizontalOffset(panelId, horizontalOffset);
            SetPanelLastDetailsVerticalOffset(panelId, verticalOffset);
            SetPanelLastDetailsVerticalDelta(
                panelId,
                double.IsNaN(previousVerticalOffset)
                    ? 0.0
                    : Math.Abs(viewer.VerticalOffset - previousVerticalOffset));

            if (panelId == WorkspacePanelId.Primary &&
                scrolled &&
                _entriesFlyoutOpen &&
                (_activeEntriesContextFlyout?.IsOpen ?? false))
            {
                HideActiveEntriesContextFlyout();
            }

            if (scrolled &&
                RenameOverlayBorder.Visibility == Visibility.Visible &&
                _activeRenameOverlayPanelId == panelId)
            {
                HideRenameOverlay();
            }

            if (scrolled)
            {
                SetPanelLastDetailsScrollInteractionTick(panelId, Environment.TickCount64);
                InvalidatePanelDetailsViewportRealization(panelId);
            }

            if (panelId == WorkspacePanelId.Primary && DetailsHeaderTranslateTransform is not null)
            {
                DetailsHeaderTranslateTransform.X = -viewer.HorizontalOffset;
            }

            UpdateEstimatedItemHeight();
            int logicalCount = GetLogicalEntryCount(panelId);
            if (logicalCount > 0)
            {
                viewportStartIndex = EstimateViewportIndex(panelId, viewer);
                viewportBottomIndex = EstimateViewportBottomIndex(panelId, viewer);
                LogDetailsViewportPerf(
                    panelId == WorkspacePanelId.Primary ? "view-changed" : "view-changed.secondary",
                    $"intermediate={e.IsIntermediate} offset={viewer.VerticalOffset:F1} scrollable={viewer.ScrollableHeight:F1} start={viewportStartIndex} end={viewportBottomIndex} entries={GetViewportEntries(panelId).Count} total={GetPanelDataSession(panelId).TotalEntries}");

                if (panelId == WorkspacePanelId.Primary)
                {
                    _ = EnsureDataForViewportAsync(viewportStartIndex, viewportBottomIndex, preferMinimalPage: e.IsIntermediate);
                }
                else if (ShouldPrefetchSecondaryPanePage(viewer, e.IsIntermediate))
                {
                    _ = LoadNextSimplePanelPageAsync(panelId);
                }
            }

            if (viewportStartIndex >= 0)
            {
                SetPanelLastDetailsViewportIndexDelta(
                    panelId,
                    previousViewportStartIndex < 0
                        ? 0
                        : Math.Abs(viewportStartIndex - previousViewportStartIndex));
                SetPanelLastDetailsViewportStartIndex(panelId, viewportStartIndex);
            }

            if (panelId != WorkspacePanelId.Primary)
            {
                return;
            }

            if (e.IsIntermediate)
            {
                IncrementPanelMetadataViewportRequestVersion(panelId);
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

            (bool scrolled, double horizontalOffset, double verticalOffset) = GetScrollOffsetChange(
                viewer,
                GetPanelLastGroupedHorizontalOffset(WorkspacePanelId.Primary),
                GetPanelLastGroupedVerticalOffset(WorkspacePanelId.Primary));
            SetPanelLastGroupedHorizontalOffset(WorkspacePanelId.Primary, horizontalOffset);
            SetPanelLastGroupedVerticalOffset(WorkspacePanelId.Primary, verticalOffset);
            if (scrolled && RenameOverlayBorder.Visibility == Visibility.Visible)
            {
                HideRenameOverlay();
            }
        }

        private static (bool Changed, double HorizontalOffset, double VerticalOffset) GetScrollOffsetChange(
            ScrollViewer viewer,
            double lastHorizontalOffset,
            double lastVerticalOffset)
        {
            bool changed =
                double.IsNaN(lastHorizontalOffset) ||
                double.IsNaN(lastVerticalOffset) ||
                Math.Abs(viewer.HorizontalOffset - lastHorizontalOffset) > 0.1 ||
                Math.Abs(viewer.VerticalOffset - lastVerticalOffset) > 0.1;

            return (changed, viewer.HorizontalOffset, viewer.VerticalOffset);
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
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
            {
                return false;
            }

            return IsPanelDetailsViewportInteractionHot(WorkspacePanelId.Primary);
        }

        private bool IsPanelDetailsViewportInteractionHot(WorkspacePanelId panelId)
        {
            return Environment.TickCount64 - GetPanelLastDetailsScrollInteractionTick(panelId) < 180;
        }

        private bool ShouldPrefetchSecondaryPanePage(ScrollViewer viewer, bool isIntermediate)
        {
            if (SecondaryPanelState.DataSession.IsLoading || !SecondaryPanelState.DataSession.HasMore)
            {
                return false;
            }

            double scrollableHeight = Math.Max(0, viewer.ScrollableHeight);
            if (scrollableHeight <= 0)
            {
                return false;
            }

            double remaining = scrollableHeight - viewer.VerticalOffset;
            double threshold = isIntermediate
                ? Math.Max(160, viewer.ViewportHeight * 1.5)
                : Math.Max(96, viewer.ViewportHeight);
            return remaining <= threshold;
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
            if (PrimaryEntries.Count == 0 || startIndex < 0 || endIndex < startIndex)
            {
                return false;
            }

            int cappedEnd = Math.Min(endIndex, PrimaryEntries.Count - 1);
            for (int i = Math.Max(0, startIndex); i <= cappedEnd; i++)
            {
                EntryViewModel entry = PrimaryEntries[i];
                if (entry.IsLoaded && !entry.IsMetadataLoaded)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ViewportHasPendingMetadata()
        {
            if (PrimaryEntries.Count == 0)
            {
                return false;
            }

            int startIndex;
            int endIndex;
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
            {
                startIndex = 0;
                endIndex = PrimaryEntries.Count - 1;
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
            IncrementPanelMetadataViewportRequestVersion(WorkspacePanelId.Primary);

            CancellationTokenSource? activeMetadataPrefetchCts = GetPanelMetadataPrefetchCts(WorkspacePanelId.Primary);
            CancelAndDispose(ref activeMetadataPrefetchCts);
            SetPanelMetadataPrefetchCts(WorkspacePanelId.Primary, null);
        }

        private void RequestViewportWork()
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
            {
                return;
            }

            RequestPrefetchForCurrentViewport();
        }

        private void RequestPrefetchForCurrentViewport()
        {
            if (!GetPanelHasMore(WorkspacePanelId.Primary))
            {
                return;
            }

            int startIndex = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int endIndex = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
            _ = EnsureDataForViewportAsync(startIndex, endIndex);
        }

        private void RequestMetadataForCurrentViewportDeferred(int delayMs = 1)
        {
            int requestVersion = IncrementPanelMetadataViewportRequestVersion(WorkspacePanelId.Primary);
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs);
                }

                if (requestVersion != GetPanelMetadataViewportRequestVersion(WorkspacePanelId.Primary))
                {
                    return;
                }

                RequestMetadataForCurrentViewport();
            });
        }

        private void RequestMetadataForCurrentViewport()
        {
            IncrementPanelMetadataViewportRequestVersion(WorkspacePanelId.Primary);

            if (IsDetailsViewportInteractionHot() || IsSparseViewportLoadQueuedOrActive())
            {
                RequestMetadataForCurrentViewportDeferred(96);
                return;
            }

            if (PrimaryEntries.Count == 0)
            {
                CancellationTokenSource? metadataPrefetchCts = GetPanelMetadataPrefetchCts(WorkspacePanelId.Primary);
                CancelAndDispose(ref metadataPrefetchCts);
                SetPanelMetadataPrefetchCts(WorkspacePanelId.Primary, null);
                return;
            }

            int visibleStart;
            int visibleEnd;
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
            {
                visibleStart = 0;
                visibleEnd = PrimaryEntries.Count - 1;
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
            bool throttleMetadataPrefetch = PrimaryEntries.Count > 1024;
            int lookahead = throttleMetadataPrefetch
                ? 0
                : Math.Max(visibleCount, (int)GetPanelCurrentPageSize(WorkspacePanelId.Primary));
            int prefetchEnd = Math.Min(PrimaryEntries.Count - 1, visibleEnd + lookahead);

            List<MetadataWorkItem> visibleItems = CollectMetadataWorkItems(visibleStart, visibleEnd);
            List<MetadataWorkItem> prefetchItems = throttleMetadataPrefetch
                ? []
                : CollectMetadataWorkItems(visibleEnd + 1, prefetchEnd);
            if (visibleItems.Count == 0 && prefetchItems.Count == 0)
            {
                return;
            }

            CancellationTokenSource? baseCts = GetPanelDirectoryLoadCts(WorkspacePanelId.Primary);
            if (baseCts is null)
            {
                return;
            }

            CancellationTokenSource? activeMetadataPrefetchCts = GetPanelMetadataPrefetchCts(WorkspacePanelId.Primary);
            CancelAndDispose(ref activeMetadataPrefetchCts);
            SetPanelMetadataPrefetchCts(WorkspacePanelId.Primary, null);
            SetPanelMetadataPrefetchCts(WorkspacePanelId.Primary, CancellationTokenSource.CreateLinkedTokenSource(baseCts.Token));
            CancellationToken token = GetPanelMetadataPrefetchCts(WorkspacePanelId.Primary)!.Token;
            long snapshotVersion = GetPanelDirectorySnapshotVersion(WorkspacePanelId.Primary);
            string path = GetPanelCurrentPath(WorkspacePanelId.Primary);

            _ = Task.Run(async () =>
            {
                await HydrateMetadataBatchAsync(path, snapshotVersion, visibleItems, token);
                await HydrateMetadataBatchAsync(path, snapshotVersion, prefetchItems, token);
            }, token);
        }
    }
}
