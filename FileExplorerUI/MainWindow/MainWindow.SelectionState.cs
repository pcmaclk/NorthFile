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

            ScrollViewer viewer = _currentViewMode == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;

            for (int i = 0; i < _entries.Count; i++)
            {
                EntryViewModel entry = _entries[i];
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
            if (string.IsNullOrWhiteSpace(_currentPath) ||
                string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _directoryViewStates[_currentPath] = new DirectoryViewState
            {
                DetailsVerticalOffset = double.IsNaN(_lastDetailsVerticalOffset)
                    ? Math.Max(0, DetailsEntriesScrollViewer.VerticalOffset)
                    : Math.Max(0, _lastDetailsVerticalOffset),
                SelectedEntryPath = _selectedEntryPath,
            };
        }

        private bool RestoreHistoryViewStateIfPending()
        {
            string? path = _pendingHistoryStateRestorePath;
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _pendingHistoryStateRestorePath = null;
            if (!_directoryViewStates.TryGetValue(path, out DirectoryViewState? state))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(state.SelectedEntryPath))
            {
                _selectedEntryPath = state.SelectedEntryPath;
                RestoreListSelectionByPath(ensureVisible: false);
            }

            if (_currentViewMode == EntryViewMode.Details)
            {
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    DetailsEntriesScrollViewer.UpdateLayout();
                    double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                    DetailsEntriesScrollViewer.ChangeView(
                        null,
                        Math.Min(maxOffset, Math.Max(0, state.DetailsVerticalOffset)),
                        null,
                        disableAnimation: true);
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
            for (int i = 0; i < _entries.Count; i++)
            {
                EntryViewModel entry = _entries[i];
                if (!string.Equals(entry.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SelectEntryInList(entry, ensureVisible: false);
                if (_currentViewMode == EntryViewMode.Details)
                {
                    _ = DispatcherQueue.TryEnqueue(() => ScrollEntryNearViewportBottom(i));
                }
                return;
            }
        }

        private void SelectEntryByPath(string targetPath, bool ensureVisible)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    SelectEntryInList(_entries[i], ensureVisible);
                    return;
                }
            }
        }

        private int GetCreateInsertIndex()
        {
            if (_currentViewMode == EntryViewMode.Details && _entries.Count > 0)
            {
                int visibleEnd = EstimateViewportBottomIndex(DetailsEntriesScrollViewer);
                return Math.Min(Math.Max(0, visibleEnd + 1), _entries.Count);
            }

            return _entries.Count;
        }

        private async Task EnsureCreateInsertVisibleAsync(int insertIndex)
        {
            if (_entries.Count == 0 || _currentViewMode != EntryViewMode.Details)
            {
                return;
            }

            await EnsureDataForViewportAsync(insertIndex, insertIndex, preferMinimalPage: false);
            DetailsEntriesScrollViewer.UpdateLayout();

            int visibleCount = Math.Max(1, (int)Math.Ceiling(DetailsEntriesScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int visibleEnd = Math.Min(_entries.Count - 1, visibleStart + visibleCount - 1);

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
            if (_currentViewMode != EntryViewMode.Details || index < 0 || index >= _entries.Count)
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
            if (index < 0 || index >= _entries.Count)
            {
                return false;
            }

            if (_currentViewMode != EntryViewMode.Details)
            {
                return false;
            }

            DetailsEntriesScrollViewer.UpdateLayout();
            int visibleCount = Math.Max(1, (int)Math.Ceiling(DetailsEntriesScrollViewer.ViewportHeight / _estimatedItemHeight));
            int visibleStart = EstimateViewportIndex(DetailsEntriesScrollViewer);
            int visibleEnd = Math.Min(_entries.Count - 1, visibleStart + visibleCount - 1);
            return index >= visibleStart && index <= visibleEnd;
        }
    }
}
