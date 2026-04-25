using NorthFileUI.Services;
using NorthFileUI.Workspace;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private PanelDataSession GetPanelDataSession(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).DataSession;
        }

        private double GetPanelLastDetailsHorizontalOffset(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).LastDetailsHorizontalOffset;
        }

        private void SetPanelLastDetailsHorizontalOffset(WorkspacePanelId panelId, double value)
        {
            _workspaceLayoutHost.GetPanelState(panelId).LastDetailsHorizontalOffset = value;
        }

        private double GetPanelLastDetailsVerticalOffset(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).LastDetailsVerticalOffset;
        }

        private void SetPanelLastDetailsVerticalOffset(WorkspacePanelId panelId, double value)
        {
            _workspaceLayoutHost.GetPanelState(panelId).LastDetailsVerticalOffset = value;
        }

        private double GetPanelLastGroupedHorizontalOffset(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).LastGroupedHorizontalOffset;
        }

        private void SetPanelLastGroupedHorizontalOffset(WorkspacePanelId panelId, double value)
        {
            _workspaceLayoutHost.GetPanelState(panelId).LastGroupedHorizontalOffset = value;
        }

        private double GetPanelLastGroupedVerticalOffset(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).LastGroupedVerticalOffset;
        }

        private void SetPanelLastGroupedVerticalOffset(WorkspacePanelId panelId, double value)
        {
            _workspaceLayoutHost.GetPanelState(panelId).LastGroupedVerticalOffset = value;
        }

        private double GetPanelLastDetailsVerticalDelta(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastDetailsVerticalDelta;
        }

        private void SetPanelLastDetailsVerticalDelta(WorkspacePanelId panelId, double value)
        {
            GetPanelDataSession(panelId).LastDetailsVerticalDelta = value;
        }

        private int GetPanelLastDetailsViewportStartIndex(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastDetailsViewportStartIndex;
        }

        private void SetPanelLastDetailsViewportStartIndex(WorkspacePanelId panelId, int value)
        {
            GetPanelDataSession(panelId).LastDetailsViewportStartIndex = value;
        }

        private int GetPanelLastDetailsViewportIndexDelta(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastDetailsViewportIndexDelta;
        }

        private void SetPanelLastDetailsViewportIndexDelta(WorkspacePanelId panelId, int value)
        {
            GetPanelDataSession(panelId).LastDetailsViewportIndexDelta = value;
        }

        private long GetPanelLastDetailsScrollInteractionTick(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastDetailsScrollInteractionTick;
        }

        private void SetPanelLastDetailsScrollInteractionTick(WorkspacePanelId panelId, long value)
        {
            GetPanelDataSession(panelId).LastDetailsScrollInteractionTick = value;
        }

        private int GetPanelMetadataViewportRequestVersion(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).MetadataViewportRequestVersion;
        }

        private int IncrementPanelMetadataViewportRequestVersion(WorkspacePanelId panelId)
        {
            return unchecked(++GetPanelDataSession(panelId).MetadataViewportRequestVersion);
        }

        private void SetPanelMetadataViewportRequestVersion(WorkspacePanelId panelId, int value)
        {
            GetPanelDataSession(panelId).MetadataViewportRequestVersion = value;
        }

        private void ResetPanelViewportState(WorkspacePanelId panelId)
        {
            ScrollViewer detailsViewer = GetPanelDetailsScrollViewer(panelId);
            detailsViewer.ChangeView(0, 0, null, disableAnimation: true);
            SetPanelLastDetailsHorizontalOffset(panelId, double.NaN);
            SetPanelLastDetailsVerticalOffset(panelId, double.NaN);
            SetPanelLastDetailsVerticalDelta(panelId, 0);
            SetPanelLastDetailsViewportStartIndex(panelId, -1);
            SetPanelLastDetailsViewportIndexDelta(panelId, 0);

            ScrollViewer? groupedViewer = GetPanelGroupedScrollViewer(panelId);
            if (groupedViewer is not null)
            {
                groupedViewer.ChangeView(0, 0, null, disableAnimation: true);
                SetPanelLastGroupedHorizontalOffset(panelId, double.NaN);
                SetPanelLastGroupedVerticalOffset(panelId, double.NaN);
            }
        }

        private void InvalidatePanelDetailsViewportRealization(
            WorkspacePanelId panelId,
            bool preferMinimalBuffer = false,
            bool forceSynchronous = false)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                SecondaryEntriesRepeater.InvalidateMeasure();
                if (forceSynchronous)
                {
                    SecondaryEntriesScrollViewer.UpdateLayout();
                }

                return;
            }

            InvalidateDetailsViewportRealization(preferMinimalBuffer, forceSynchronous);
        }

        private ulong GetPanelNextCursor(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).NextCursor;
        }

        private void SetPanelNextCursor(WorkspacePanelId panelId, ulong value)
        {
            GetPanelDataSession(panelId).NextCursor = value;
        }

        private bool GetPanelHasMore(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).HasMore;
        }

        private void SetPanelHasMore(WorkspacePanelId panelId, bool value)
        {
            GetPanelDataSession(panelId).HasMore = value;
        }

        private bool GetPanelIsLoading(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).IsLoading;
        }

        private void SetPanelIsLoading(WorkspacePanelId panelId, bool value)
        {
            GetPanelDataSession(panelId).IsLoading = value;
        }

        private uint GetPanelCurrentPageSize(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).CurrentPageSize;
        }

        private void SetPanelCurrentPageSize(WorkspacePanelId panelId, uint value)
        {
            _workspaceLayoutHost.GetPanelState(panelId).CurrentPageSize = value;
        }

        private uint GetPanelLastFetchMs(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastFetchMs;
        }

        private void SetPanelLastFetchMs(WorkspacePanelId panelId, uint value)
        {
            GetPanelDataSession(panelId).LastFetchMs = value;
        }

        private uint GetPanelTotalEntries(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).TotalEntries;
        }

        private void SetPanelTotalEntries(WorkspacePanelId panelId, uint value)
        {
            GetPanelDataSession(panelId).TotalEntries = value;
        }

        private IEntryResultSet? GetPanelActiveEntryResultSet(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).ActiveEntryResultSet;
        }

        private void SetPanelActiveEntryResultSet(WorkspacePanelId panelId, IEntryResultSet? value)
        {
            GetPanelDataSession(panelId).ActiveEntryResultSet = value;
        }

        private CancellationTokenSource? GetPanelNavigationLoadCts(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).NavigationLoadCts;
        }

        private void SetPanelNavigationLoadCts(WorkspacePanelId panelId, CancellationTokenSource? value)
        {
            GetPanelDataSession(panelId).NavigationLoadCts = value;
        }

        private CancellationTokenSource? GetPanelDirectoryLoadCts(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).DirectoryLoadCts;
        }

        private void SetPanelDirectoryLoadCts(WorkspacePanelId panelId, CancellationTokenSource? value)
        {
            GetPanelDataSession(panelId).DirectoryLoadCts = value;
        }

        private CancellationTokenSource? GetPanelMetadataPrefetchCts(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).MetadataPrefetchCts;
        }

        private void SetPanelMetadataPrefetchCts(WorkspacePanelId panelId, CancellationTokenSource? value)
        {
            GetPanelDataSession(panelId).MetadataPrefetchCts = value;
        }

        private long GetPanelDirectorySnapshotVersion(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).DirectorySnapshotVersion;
        }

        private void SetPanelDirectorySnapshotVersion(WorkspacePanelId panelId, long value)
        {
            GetPanelDataSession(panelId).DirectorySnapshotVersion = value;
        }

        private bool GetPanelGroupedColumnsRefreshQueued(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).GroupedColumnsRefreshQueued;
        }

        private void SetPanelGroupedColumnsRefreshQueued(WorkspacePanelId panelId, bool value)
        {
            GetPanelDataSession(panelId).GroupedColumnsRefreshQueued = value;
        }

        private CancellationTokenSource? GetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).GroupedColumnsResizeDebounceCts;
        }

        private void SetPanelGroupedColumnsResizeDebounceCts(WorkspacePanelId panelId, CancellationTokenSource? value)
        {
            GetPanelDataSession(panelId).GroupedColumnsResizeDebounceCts = value;
        }

        private int GetPanelGroupedColumnsRefreshVersion(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).GroupedColumnsRefreshVersion;
        }

        private void SetPanelGroupedColumnsRefreshVersion(WorkspacePanelId panelId, int value)
        {
            GetPanelDataSession(panelId).GroupedColumnsRefreshVersion = value;
        }

        private long GetPanelLastGroupedColumnsRefreshAppliedStamp(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastGroupedColumnsRefreshAppliedStamp;
        }

        private void SetPanelLastGroupedColumnsRefreshAppliedStamp(WorkspacePanelId panelId, long value)
        {
            GetPanelDataSession(panelId).LastGroupedColumnsRefreshAppliedStamp = value;
        }

        private double GetPanelLastGroupedViewportHeight(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastGroupedViewportHeight;
        }

        private void SetPanelLastGroupedViewportHeight(WorkspacePanelId panelId, double value)
        {
            GetPanelDataSession(panelId).LastGroupedViewportHeight = value;
        }

        private long GetPanelLastGroupedColumnsLiveResizeRefreshTick(WorkspacePanelId panelId)
        {
            return GetPanelDataSession(panelId).LastGroupedColumnsLiveResizeRefreshTick;
        }

        private void SetPanelLastGroupedColumnsLiveResizeRefreshTick(WorkspacePanelId panelId, long value)
        {
            GetPanelDataSession(panelId).LastGroupedColumnsLiveResizeRefreshTick = value;
        }
    }
}
