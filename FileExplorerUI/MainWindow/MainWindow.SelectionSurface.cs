using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void ClearListSelection()
        {
            _selectedEntryPath = null;
            SyncActivePanelPresentationState();
            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private void ClearListSelectionAndAnchor()
        {
            _selectedEntryPath = null;
            _focusedEntryPath = null;
            SyncActivePanelPresentationState();
            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private void ClearExplicitSelectionKeepAnchor()
        {
            _selectedEntryPath = null;
            SyncActivePanelPresentationState();
            UpdateEntrySelectionVisuals();
            UpdateFileCommandStates();
        }

        private void FocusEntriesList()
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            GetVisibleEntriesRoot().Focus(FocusState.Programmatic);
        }

        private void FocusSidebarTree()
        {
            _sidebarTreeView?.Focus(FocusState.Pointer);
        }

        private void FocusSidebarSurface()
        {
            SetActiveSelectionSurface(SelectionSurfaceId.Sidebar);
            if (!StyledSidebarView.Focus(FocusState.Pointer))
            {
                SidebarNavView?.Focus(FocusState.Pointer);
            }
        }

        private void SetActiveSelectionSurface(SelectionSurfaceId surface)
        {
            _selectionSurfaceCoordinator.SetActiveSurface(surface);
            UpdateSelectionActivityState();
        }

        private void UpdateSelectionActivityState()
        {
            bool entriesSelectionActive = _selectionSurfaceCoordinator.IsSurfaceActive(SelectionSurfaceId.PrimaryPane);
            bool sidebarSelectionActive = _selectionSurfaceCoordinator.IsSurfaceActive(SelectionSurfaceId.Sidebar);

            StyledSidebarView.SetSelectionActive(sidebarSelectionActive);
            if (_isSidebarSelectionActive != sidebarSelectionActive)
            {
                _isSidebarSelectionActive = sidebarSelectionActive;
                RefreshSidebarTreeSelectionVisuals();
            }

            if (_isEntriesSelectionActive == entriesSelectionActive)
            {
                return;
            }

            _isEntriesSelectionActive = entriesSelectionActive;
            UpdateEntrySelectionVisuals();
        }

        private void RefreshSidebarTreeSelectionVisuals()
        {
            if (_sidebarTreeView?.SelectedNode is not TreeViewNode selectedNode)
            {
                return;
            }

            _suppressSidebarTreeSelection = true;
            _sidebarTreeView.SelectedNode = null;
            _sidebarTreeView.SelectedNode = selectedNode;
            _suppressSidebarTreeSelection = false;
        }
    }
}
