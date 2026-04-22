using Microsoft.UI.Xaml.Controls;
using FileExplorerUI.Workspace;
using System;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void SelectEntryInList(EntryViewModel entry, bool ensureVisible)
        {
            if (entry.IsGroupHeader)
            {
                return;
            }

            _selectedEntryPath = entry.FullPath;
            _focusedEntryPath = entry.FullPath;
            if (ensureVisible)
            {
                _ = DispatcherQueue.TryEnqueue(() => ScrollEntryIntoView(entry));
            }

            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private void SelectPanelEntryInList(WorkspacePanelId panelId, EntryViewModel entry, bool ensureVisible)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                SecondarySelectEntryInList(entry);
                if (ensureVisible)
                {
                    _ = DispatcherQueue.TryEnqueue(() => SecondaryScrollEntryIntoView(entry));
                }
                return;
            }

            SelectEntryInList(entry, ensureVisible);
        }

        private void RestoreListSelectionByPath(bool ensureVisible)
        {
            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return;
            }

            SelectEntryByPath(_selectedEntryPath, ensureVisible);
        }

        private void RestoreListSelectionByPathRespectingViewport()
        {
            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return;
            }

            ScrollViewer viewer = GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;

            for (int i = 0; i < PrimaryEntries.Count; i++)
            {
                EntryViewModel entry = PrimaryEntries[i];
                if (!string.Equals(entry.FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool ensureVisible = !IsEntryFullyVisible(entry, viewer);
                SelectEntryInList(entry, ensureVisible);
                return;
            }
        }

        private void CaptureCurrentDirectoryViewState()
        {
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            if (string.IsNullOrWhiteSpace(currentPath) ||
                string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            GetMutablePrimaryDirectoryViewStates()[currentPath] = new DirectoryViewState
            {
                DetailsHorizontalOffset = GetCurrentDetailsHorizontalOffset(),
                DetailsVerticalOffset = GetCurrentDetailsVerticalOffset(),
                GroupedHorizontalOffset = GetCurrentGroupedHorizontalOffset(),
                SelectedEntryPath = _selectedEntryPath,
            };
        }

        private bool RestoreHistoryViewStateIfPending()
        {
            string? path = _pendingHistoryStateRestorePath;
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _pendingHistoryStateRestorePath = null;
            if (!GetPrimaryDirectoryViewStates().TryGetValue(path, out DirectoryViewState? state))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(state.SelectedEntryPath))
            {
                _selectedEntryPath = state.SelectedEntryPath;
                RestoreListSelectionByPath(ensureVisible: false);
            }

            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    DetailsEntriesScrollViewer.UpdateLayout();
                    RestoreCurrentViewportOffsets(
                        state.DetailsHorizontalOffset,
                        state.DetailsVerticalOffset,
                        state.GroupedHorizontalOffset);
                });
            }
            else
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    GroupedEntriesScrollViewer.UpdateLayout();
                    RestoreCurrentViewportOffsets(
                        state.DetailsHorizontalOffset,
                        state.DetailsVerticalOffset,
                        state.GroupedHorizontalOffset);
                });
            }

            return true;
        }

        private void RestoreParentReturnAnchorIfPending()
        {
            string? targetPath = _pendingParentReturnAnchorPath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return;
            }

            _pendingParentReturnAnchorPath = null;
            for (int i = 0; i < PrimaryEntries.Count; i++)
            {
                EntryViewModel entry = PrimaryEntries[i];
                if (!string.Equals(entry.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SelectEntryInList(entry, ensureVisible: false);
                if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details)
                {
                    _ = DispatcherQueue.TryEnqueue(() => ScrollEntryNearViewportBottom(i));
                }
                return;
            }
        }

        private void SelectEntryByPath(string targetPath, bool ensureVisible)
        {
            for (int i = 0; i < PrimaryEntries.Count; i++)
            {
                if (string.Equals(PrimaryEntries[i].FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    SelectEntryInList(PrimaryEntries[i], ensureVisible);
                    return;
                }
            }
        }

        private int GetCreateInsertIndex()
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details && PrimaryEntries.Count > 0)
            {
                int visibleEnd = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
                return Math.Min(Math.Max(0, visibleEnd + 1), PrimaryEntries.Count);
            }

            return PrimaryEntries.Count;
        }

        private async Task EnsureCreateInsertVisibleAsync(int insertIndex)
        {
            if (PrimaryEntries.Count == 0 || GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
            {
                return;
            }

            await EnsureDataForViewportAsync(insertIndex, insertIndex, preferMinimalPage: false);
            DetailsEntriesScrollViewer.UpdateLayout();

            int visibleCount = Math.Max(1, (int)Math.Ceiling(DetailsEntriesScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int visibleEnd = Math.Min(PrimaryEntries.Count - 1, visibleStart + visibleCount - 1);

            if (insertIndex >= visibleStart && insertIndex <= visibleEnd)
            {
                return;
            }

            if (insertIndex > visibleEnd)
            {
                double targetOffset = Math.Max(0, ((insertIndex + 1) * _estimatedItemHeight) - DetailsEntriesScrollViewer.ViewportHeight);
                DetailsEntriesScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            }
            else
            {
                double targetOffset = insertIndex * _estimatedItemHeight;
                DetailsEntriesScrollViewer.ChangeView(null, targetOffset, null, disableAnimation: true);
            }
            await Task.Delay(16);
            DetailsEntriesScrollViewer.UpdateLayout();
        }

        private void ScrollEntryNearViewportBottom(int index)
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details || index < 0 || index >= PrimaryEntries.Count)
            {
                return;
            }

            DetailsEntriesScrollViewer.UpdateLayout();
            double viewportHeight = DetailsEntriesScrollViewer.ViewportHeight > 0
                ? DetailsEntriesScrollViewer.ViewportHeight
                : DetailsEntriesScrollViewer.ActualHeight;
            double itemExtent = Math.Max(1.0, _estimatedItemHeight);
            double targetOffset = Math.Max(0, ((index + 1) * itemExtent) - viewportHeight);
            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, targetOffset), null, disableAnimation: true);
        }

        private bool IsIndexInCurrentViewport(int index)
        {
            if (index < 0 || index >= PrimaryEntries.Count)
            {
                return false;
            }

            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
            {
                return false;
            }

            DetailsEntriesScrollViewer.UpdateLayout();
            int visibleCount = Math.Max(1, (int)Math.Ceiling(DetailsEntriesScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int visibleEnd = Math.Min(PrimaryEntries.Count - 1, visibleStart + visibleCount - 1);
            return index >= visibleStart && index <= visibleEnd;
        }
    }
}
