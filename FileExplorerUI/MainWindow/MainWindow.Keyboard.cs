using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void RootElement_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Handled || _inlineEditCoordinator.HasActiveSession)
            {
                return;
            }

            if (IsTextInputSource(e.OriginalSource as DependencyObject))
            {
                return;
            }

            e.Handled = HandleGlobalShortcutKey(e.Key);
        }

        private void RegisterEntriesKeyHandlers(UIElement host)
        {
            host.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(EntriesView_KeyDown), true);
        }

        private void EntriesView_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (_inlineEditCoordinator.HasActiveSession)
            {
                return;
            }

            if (ReferenceEquals(sender, SecondaryEntriesScrollViewer))
            {
                SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
                e.Handled = HandleSecondaryEntriesNavigationKey(e.Key);
                return;
            }

            e.Handled = HandleEntriesNavigationKey(e.Key);
        }

        private bool HandleGlobalShortcutKey(Windows.System.VirtualKey key)
        {
            bool controlPressed = IsControlPressed();
            bool shiftPressed = IsShiftPressed();
            bool altPressed = IsMenuPressed();

            if (controlPressed)
            {
                switch (key)
                {
                    case Windows.System.VirtualKey.C:
                        if (_paneFileCommandController.CanCopyInActivePane())
                        {
                            _paneFileCommandController.ExecuteCopyInActivePane();
                            return true;
                        }
                        break;
                    case Windows.System.VirtualKey.X:
                        if (_paneFileCommandController.CanCutInActivePane())
                        {
                            _paneFileCommandController.ExecuteCutInActivePane();
                            return true;
                        }
                        break;
                    case Windows.System.VirtualKey.V:
                        if (_paneFileCommandController.CanPasteInActivePane())
                        {
                            _ = _paneFileCommandController.ExecutePasteInActivePaneAsync();
                            return true;
                        }
                        break;
                    case Windows.System.VirtualKey.L:
                        FocusActivePaneAddressBox(selectAll: true);
                        return true;
                    case Windows.System.VirtualKey.T:
                        _ = _workspaceTabController.OpenPathInNewTabAsync(
                            _paneFileCommandController.ActivePanel == WorkspacePanelId.Secondary
                                ? SecondaryPanelState.CurrentPath
                                : GetPanelCurrentPath(WorkspacePanelId.Primary));
                        return true;
                    case Windows.System.VirtualKey.Tab:
                        _ = _workspaceChromeCoordinator.ActivateAdjacentAsync(shiftPressed ? -1 : 1);
                        return true;
                    case Windows.System.VirtualKey.W:
                        _ = _workspaceTabController.CloseActiveAsync();
                        return true;
                    case Windows.System.VirtualKey.Enter:
                        if (TryHandleOpenSelectionInNewTabShortcut())
                        {
                            return true;
                        }
                        break;
                }
            }

            if (altPressed)
            {
                switch (key)
                {
                    case Windows.System.VirtualKey.Left:
                        _ = TryNavigateActivePanelBackAsync();
                        return true;
                    case Windows.System.VirtualKey.Right:
                        _ = TryNavigateActivePanelForwardAsync();
                        return true;
                    case Windows.System.VirtualKey.Up:
                        _ = TryNavigateActivePanelUpAsync();
                        return true;
                }
            }

            switch (key)
            {
                case Windows.System.VirtualKey.GoBack:
                case Windows.System.VirtualKey.NavigationLeft:
                    _ = TryNavigateActivePanelBackAsync();
                    return true;
                case Windows.System.VirtualKey.GoForward:
                case Windows.System.VirtualKey.NavigationRight:
                    _ = TryNavigateActivePanelForwardAsync();
                    return true;
                case Windows.System.VirtualKey.Delete:
                    if (_paneFileCommandController.CanDeleteInActivePane())
                    {
                        _ = _paneFileCommandController.ExecuteDeleteInActivePaneAsync();
                        return true;
                    }
                    break;
                case Windows.System.VirtualKey.F2:
                    if (TryHandleRenameShortcut())
                    {
                        return true;
                    }
                    break;
                case Windows.System.VirtualKey.F5:
                    if (TryRefreshActivePane())
                    {
                        return true;
                    }
                    break;
            }

            return false;
        }

        private bool TryHandleRenameShortcut()
        {
            if (IsSidebarTreeFocused() && TryGetSelectedSidebarTreeEntry(out SidebarTreeEntry? treeEntry))
            {
                _ = BeginSidebarTreeRenameAsync(treeEntry);
                return true;
            }

            if (_paneFileCommandController.CanRenameInActivePane())
            {
                _ = _paneFileCommandController.ExecuteRenameInActivePaneAsync();
                return true;
            }

            return false;
        }

        private bool TryHandleOpenSelectionInNewTabShortcut()
        {
            if (_isDualPaneEnabled &&
                _workspaceLayoutHost.ActivePanel == WorkspacePanelId.Secondary &&
                TryGetSelectedLoadedEntryForPane(WorkspacePanelId.Secondary, out EntryViewModel? secondaryEntry) &&
                secondaryEntry.IsDirectory)
            {
                _ = _workspaceTabController.OpenPathInNewTabAsync(secondaryEntry.FullPath);
                return true;
            }

            if (IsSidebarTreeFocused() && TryGetSelectedSidebarTreeEntry(out SidebarTreeEntry? treeEntry))
            {
                _ = _workspaceTabController.OpenPathInNewTabAsync(treeEntry.FullPath);
                return true;
            }

            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry) || !entry.IsDirectory)
            {
                return false;
            }

            string targetPath = string.IsNullOrWhiteSpace(entry.FullPath)
                ? Path.Combine(GetPanelCurrentPath(WorkspacePanelId.Primary), entry.Name)
                : entry.FullPath;
            _ = _workspaceTabController.OpenPathInNewTabAsync(targetPath);
            return true;
        }

        private void FocusActivePaneAddressBox(bool selectAll)
        {
            if (_paneFileCommandController.ActivePanel == WorkspacePanelId.Secondary)
            {
                EnterSecondaryAddressEditMode(selectAll);
                return;
            }

            EnterAddressEditMode(selectAll);
        }

        private Task<bool> TryNavigateActivePanelBackAsync()
        {
            return TryNavigatePanelBackAsync(_paneFileCommandController.ActivePanel);
        }

        private Task<bool> TryNavigateActivePanelForwardAsync()
        {
            return TryNavigatePanelForwardAsync(_paneFileCommandController.ActivePanel);
        }

        private Task<bool> TryNavigateActivePanelUpAsync()
        {
            return TryNavigatePanelUpAsync(_paneFileCommandController.ActivePanel);
        }

        private bool TryRefreshActivePane()
        {
            if (!_paneFileCommandController.CanRefreshInActivePane())
            {
                return false;
            }

            _ = _paneFileCommandController.ExecuteRefreshInActivePaneAsync();
            return true;
        }

        private static bool IsControlPressed()
        {
            Windows.UI.Core.CoreVirtualKeyStates state =
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        private static bool IsShiftPressed()
        {
            Windows.UI.Core.CoreVirtualKeyStates state =
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        private static bool IsMenuPressed()
        {
            Windows.UI.Core.CoreVirtualKeyStates state =
                Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu);
            return (state & Windows.UI.Core.CoreVirtualKeyStates.Down) == Windows.UI.Core.CoreVirtualKeyStates.Down;
        }

        private bool IsSidebarTreeFocused()
        {
            if (_sidebarTreeView is null || Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            return IsDescendantOf(focused, _sidebarTreeView);
        }

        private bool TryGetSelectedSidebarTreeEntry([NotNullWhen(true)] out SidebarTreeEntry? entry)
        {
            entry = _sidebarTreeView?.SelectedNode?.Content as SidebarTreeEntry;
            if (entry is null || string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                entry = null;
                return false;
            }

            string root = Path.GetPathRoot(entry.FullPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(root) &&
                string.Equals(root.TrimEnd('\\'), entry.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                entry = null;
                return false;
            }

            return true;
        }

        private static bool IsTextInputSource(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is TextBox or AutoSuggestBox or PasswordBox or RichEditBox)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private bool HandleEntriesNavigationKey(Windows.System.VirtualKey key)
        {
            switch (key)
            {
                case Windows.System.VirtualKey.Up:
                    if (!TryMoveSelectionVertically(-1))
                    {
                        TryMoveSelectionBy(-1);
                    }
                    return true;
                case Windows.System.VirtualKey.Down:
                    if (!TryMoveSelectionVertically(1))
                    {
                        TryMoveSelectionBy(1);
                    }
                    return true;
                case Windows.System.VirtualKey.Left:
                    if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List)
                    {
                        TryMoveSelectionHorizontally(-1);
                        return true;
                    }
                    return false;
                case Windows.System.VirtualKey.Right:
                    if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List)
                    {
                        TryMoveSelectionHorizontally(1);
                        return true;
                    }
                    return false;
                case Windows.System.VirtualKey.Home:
                    TrySelectBoundaryEntry(first: true);
                    return true;
                case Windows.System.VirtualKey.End:
                    TrySelectBoundaryEntry(first: false);
                    return true;
                case Windows.System.VirtualKey.PageUp:
                    TryMoveSelectionByPage(-1);
                    return true;
                case Windows.System.VirtualKey.PageDown:
                    TryMoveSelectionByPage(1);
                    return true;
                case Windows.System.VirtualKey.Enter:
                    TryActivateSelectedEntry();
                    return true;
                default:
                    return false;
            }
        }

        private bool TryMoveSelectionBy(int delta)
        {
            List<EntryViewModel> selectableEntries = GetSelectableEntriesInPresentationOrder();
            if (selectableEntries.Count == 0)
            {
                return false;
            }

            int currentIndex = GetSelectedPresentedEntryIndex(selectableEntries);
            int targetIndex = currentIndex < 0
                ? (delta >= 0 ? 0 : selectableEntries.Count - 1)
                : Math.Clamp(currentIndex + delta, 0, selectableEntries.Count - 1);

            SelectEntryFromKeyboard(selectableEntries[targetIndex]);
            return true;
        }

        private bool TryMoveSelectionVertically(int delta)
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.List)
            {
                return false;
            }

            return TryMoveSelectionInListColumns(0, delta);
        }

        private bool TryMoveSelectionHorizontally(int delta)
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.List)
            {
                return false;
            }

            return TryMoveSelectionInListColumns(delta, 0);
        }

        private bool TryMoveSelectionInListColumns(int columnDelta, int rowDelta)
        {
            List<IReadOnlyList<EntryViewModel>> columns = GetListNavigationColumns();
            if (columns.Count == 0)
            {
                return false;
            }

            if (!TryGetSelectedListColumnPosition(columns, out int currentColumnIndex, out int currentRowIndex))
            {
                IReadOnlyList<EntryViewModel> edgeColumn = columnDelta < 0 || rowDelta < 0
                    ? columns[^1]
                    : columns[0];
                if (edgeColumn.Count == 0)
                {
                    return false;
                }

                EntryViewModel initialEntry = columnDelta < 0 || rowDelta < 0
                    ? edgeColumn[^1]
                    : edgeColumn[0];
                SelectEntryFromKeyboard(initialEntry);
                return true;
            }

            int targetColumnIndex = Math.Clamp(currentColumnIndex + columnDelta, 0, columns.Count - 1);
            IReadOnlyList<EntryViewModel> targetColumn = columns[targetColumnIndex];
            if (targetColumn.Count == 0)
            {
                return false;
            }

            int targetRowIndex = columnDelta == 0
                ? Math.Clamp(currentRowIndex + rowDelta, 0, targetColumn.Count - 1)
                : Math.Min(currentRowIndex, targetColumn.Count - 1);

            SelectEntryFromKeyboard(targetColumn[targetRowIndex]);
            return true;
        }

        private bool TryGetSelectedListColumnPosition(
            IReadOnlyList<IReadOnlyList<EntryViewModel>> columns,
            out int columnIndex,
            out int rowIndex)
        {
            columnIndex = -1;
            rowIndex = -1;

            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return false;
            }

            for (int c = 0; c < columns.Count; c++)
            {
                IReadOnlyList<EntryViewModel> column = columns[c];
                for (int r = 0; r < column.Count; r++)
                {
                    if (string.Equals(column[r].FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase))
                    {
                        columnIndex = c;
                        rowIndex = r;
                        return true;
                    }
                }
            }

            return false;
        }

        private List<IReadOnlyList<EntryViewModel>> GetListNavigationColumns()
        {
            var columns = new List<IReadOnlyList<EntryViewModel>>();

            int rowsPerColumn = Math.Max(1, GetGroupedListRowsPerColumn());
            var currentGroupItems = new List<EntryViewModel>();
            void FlushCurrentGroup()
            {
                if (currentGroupItems.Count == 0)
                {
                    return;
                }

                foreach (EntryViewModel[] chunk in currentGroupItems.Chunk(rowsPerColumn))
                {
                    columns.Add(chunk);
                }

                currentGroupItems.Clear();
            }

            foreach (EntryViewModel entry in PrimaryEntries)
            {
                if (entry.IsGroupHeader)
                {
                    FlushCurrentGroup();
                    continue;
                }

                if (entry.IsLoaded)
                {
                    currentGroupItems.Add(entry);
                }
            }

            FlushCurrentGroup();

            if (columns.Count > 0)
            {
                return columns;
            }

            return GetSelectableEntriesInPresentationOrder()
                .Chunk(rowsPerColumn)
                .Select(chunk => (IReadOnlyList<EntryViewModel>)chunk.ToList())
                .ToList();
        }

        private bool TrySelectBoundaryEntry(bool first)
        {
            List<EntryViewModel> selectableEntries = GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List
                ? GetListNavigationColumns()
                    .SelectMany(column => column)
                    .ToList()
                : GetSelectableEntriesInPresentationOrder();
            if (selectableEntries.Count == 0)
            {
                return false;
            }

            SelectEntryFromKeyboard(first ? selectableEntries[0] : selectableEntries[^1]);
            return true;
        }

        private bool TrySelectListColumnBoundary(bool first)
        {
            List<IReadOnlyList<EntryViewModel>> columns = GetListNavigationColumns();
            if (columns.Count == 0)
            {
                return false;
            }

            if (!TryGetSelectedListColumnPosition(columns, out int currentColumnIndex, out _))
            {
                IReadOnlyList<EntryViewModel> edgeColumn = first ? columns[0] : columns[^1];
                if (edgeColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(first ? edgeColumn[0] : edgeColumn[^1]);
                return true;
            }

            IReadOnlyList<EntryViewModel> currentColumn = columns[currentColumnIndex];
            if (currentColumn.Count == 0)
            {
                return false;
            }

            SelectEntryFromKeyboard(first ? currentColumn[0] : currentColumn[^1]);
            return true;
        }

        private bool TryMoveSelectionByPage(int direction)
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List)
            {
                return TryMoveSelectionByPageInListColumns(direction);
            }

            int step = Math.Max(1, GetKeyboardPageStep());
            return TryMoveSelectionBy(direction * step);
        }

        private int GetKeyboardPageStep()
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List)
            {
                return GetGroupedListRowsPerColumn();
            }

            double viewportHeight = DetailsEntriesScrollViewer.ViewportHeight > 0
                ? DetailsEntriesScrollViewer.ViewportHeight
                : DetailsEntriesScrollViewer.ActualHeight;
            if (viewportHeight <= 0 || _estimatedItemHeight <= 0)
            {
                return 8;
            }

            return Math.Max(1, (int)Math.Floor(viewportHeight / _estimatedItemHeight));
        }

        private bool TryMoveSelectionByPageInListColumns(int direction)
        {
            List<IReadOnlyList<EntryViewModel>> columns = GetListNavigationColumns();
            if (columns.Count == 0)
            {
                return false;
            }

            if (!TryGetSelectedListColumnPosition(columns, out int currentColumnIndex, out int currentRowIndex))
            {
                IReadOnlyList<EntryViewModel> edgeColumn = direction < 0 ? columns[0] : columns[^1];
                if (edgeColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(direction < 0 ? edgeColumn[0] : edgeColumn[^1]);
                return true;
            }

            int targetColumnIndex = currentColumnIndex + direction;
            if (targetColumnIndex < 0)
            {
                IReadOnlyList<EntryViewModel> currentColumn = columns[currentColumnIndex];
                if (currentColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(currentColumn[0]);
                return true;
            }

            if (targetColumnIndex >= columns.Count)
            {
                IReadOnlyList<EntryViewModel> currentColumn = columns[currentColumnIndex];
                if (currentColumn.Count == 0)
                {
                    return false;
                }

                SelectEntryFromKeyboard(currentColumn[^1]);
                return true;
            }

            IReadOnlyList<EntryViewModel> targetColumn = columns[targetColumnIndex];
            if (targetColumn.Count == 0)
            {
                return false;
            }

            int targetRowIndex = currentRowIndex < targetColumn.Count
                ? currentRowIndex
                : targetColumn.Count - 1;

            SelectEntryFromKeyboard(targetColumn[targetRowIndex]);
            return true;
        }

        private void SelectEntryFromKeyboard(EntryViewModel entry)
        {
            ScrollViewer viewer = GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;
            double originalHorizontalOffset = viewer.HorizontalOffset;
            double originalVerticalOffset = viewer.VerticalOffset;

            bool wasVisible = IsEntryFullyVisible(entry, viewer);
            SelectEntryInList(entry, ensureVisible: false);
            viewer.ChangeView(originalHorizontalOffset, originalVerticalOffset, null, disableAnimation: true);

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (wasVisible)
                {
                    viewer.ChangeView(originalHorizontalOffset, originalVerticalOffset, null, disableAnimation: true);
                    return;
                }

                ScrollEntryIntoView(entry);
            });
        }

        private bool IsEntryFullyVisible(EntryViewModel entry, ScrollViewer viewer)
        {
            if (!TryGetEntryAnchor<FrameworkElement>(entry, WorkspacePanelId.Primary, out FrameworkElement? element) ||
                viewer.Content is not UIElement content)
            {
                return false;
            }

            GeneralTransform transform = element.TransformToVisual(content);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details)
            {
                double viewportTop = viewer.VerticalOffset;
                double viewportBottom = viewportTop + viewer.ViewportHeight;
                return bounds.Y >= viewportTop && (bounds.Y + bounds.Height) <= viewportBottom;
            }

            double viewportLeft = viewer.HorizontalOffset;
            double viewportRight = viewportLeft + viewer.ViewportWidth;
            return bounds.X >= viewportLeft && (bounds.X + bounds.Width) <= viewportRight;
        }

        private List<EntryViewModel> GetSelectableEntriesInPresentationOrder()
        {
            return PrimaryEntries
                .Where(entry => !entry.IsGroupHeader && entry.IsLoaded)
                .ToList();
        }

        private int GetSelectedPresentedEntryIndex(IReadOnlyList<EntryViewModel> entries)
        {
            string? activePath = !string.IsNullOrWhiteSpace(_selectedEntryPath)
                ? _selectedEntryPath
                : _focusedEntryPath;
            if (string.IsNullOrWhiteSpace(activePath))
            {
                return -1;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].FullPath, activePath, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryActivateSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                return false;
            }

            _ = ActivateEntryAsync(entry);
            return true;
        }

        private async Task ActivateEntryAsync(EntryViewModel row)
        {
            if (row is null || !row.IsLoaded)
            {
                return;
            }

            await OpenEntryAsync(row, clearSelectionBeforeDirectoryNavigation: true);
        }

        private bool HandleSecondaryEntriesNavigationKey(Windows.System.VirtualKey key)
        {
            switch (key)
            {
                case Windows.System.VirtualKey.Up:
                    return TryMoveSecondarySelectionBy(-1);
                case Windows.System.VirtualKey.Down:
                    return TryMoveSecondarySelectionBy(1);
                case Windows.System.VirtualKey.Home:
                    return TrySelectSecondaryBoundaryEntry(first: true);
                case Windows.System.VirtualKey.End:
                    return TrySelectSecondaryBoundaryEntry(first: false);
                case Windows.System.VirtualKey.PageUp:
                    return TryMoveSecondarySelectionByPage(-1);
                case Windows.System.VirtualKey.PageDown:
                    return TryMoveSecondarySelectionByPage(1);
                case Windows.System.VirtualKey.Enter:
                    return TryActivateSecondarySelectedEntry();
                default:
                    return false;
            }
        }

        private bool TryMoveSecondarySelectionBy(int delta)
        {
            List<EntryViewModel> selectableEntries = GetSecondarySelectableEntriesInPresentationOrder();
            if (selectableEntries.Count == 0)
            {
                return false;
            }

            int currentIndex = GetSelectedSecondaryPresentedEntryIndex(selectableEntries);
            int targetIndex = currentIndex < 0
                ? (delta >= 0 ? 0 : selectableEntries.Count - 1)
                : Math.Clamp(currentIndex + delta, 0, selectableEntries.Count - 1);

            SelectSecondaryEntryFromKeyboard(selectableEntries[targetIndex]);
            return true;
        }

        private bool TrySelectSecondaryBoundaryEntry(bool first)
        {
            List<EntryViewModel> selectableEntries = GetSecondarySelectableEntriesInPresentationOrder();
            if (selectableEntries.Count == 0)
            {
                return false;
            }

            SelectSecondaryEntryFromKeyboard(first ? selectableEntries[0] : selectableEntries[^1]);
            return true;
        }

        private bool TryMoveSecondarySelectionByPage(int direction)
        {
            int step = Math.Max(1, GetSecondaryKeyboardPageStep());
            return TryMoveSecondarySelectionBy(direction * step);
        }

        private int GetSecondaryKeyboardPageStep()
        {
            double viewportHeight = SecondaryEntriesScrollViewer.ViewportHeight > 0
                ? SecondaryEntriesScrollViewer.ViewportHeight
                : SecondaryEntriesScrollViewer.ActualHeight;
            if (viewportHeight <= 0 || _estimatedItemHeight <= 0)
            {
                return 8;
            }

            return Math.Max(1, (int)Math.Floor(viewportHeight / _estimatedItemHeight));
        }

        private void SelectSecondaryEntryFromKeyboard(EntryViewModel entry)
        {
            double originalVerticalOffset = SecondaryEntriesScrollViewer.VerticalOffset;
            bool wasVisible = IsEntryFullyVisible(entry, SecondaryEntriesScrollViewer);
            SecondarySelectEntryInList(entry);
            SecondaryEntriesScrollViewer.ChangeView(null, originalVerticalOffset, null, disableAnimation: true);

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (wasVisible)
                {
                    SecondaryEntriesScrollViewer.ChangeView(null, originalVerticalOffset, null, disableAnimation: true);
                    return;
                }

                SecondaryScrollEntryIntoView(entry);
            });
        }

        private List<EntryViewModel> GetSecondarySelectableEntriesInPresentationOrder()
        {
            return SecondaryPanelState.DataSession.Entries
                .Where(entry => !entry.IsGroupHeader && entry.IsLoaded)
                .ToList();
        }

        private int GetSelectedSecondaryPresentedEntryIndex(IReadOnlyList<EntryViewModel> entries)
        {
            string? activePath = !string.IsNullOrWhiteSpace(SecondaryPanelState.SelectedEntryPath)
                ? SecondaryPanelState.SelectedEntryPath
                : SecondaryPanelState.FocusedEntryPath;
            if (string.IsNullOrWhiteSpace(activePath))
            {
                return -1;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].FullPath, activePath, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryActivateSecondarySelectedEntry()
        {
            if (!TryGetSelectedLoadedEntryForPane(WorkspacePanelId.Secondary, out EntryViewModel? entry))
            {
                return false;
            }

            _ = ActivateSecondaryEntryAsync(entry);
            return true;
        }
    }
}
