using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using FileExplorerUI.Workspace;

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
            StyledSidebarView.Focus(FocusState.Pointer);
        }

        private void SetActiveSelectionSurface(SelectionSurfaceId surface)
        {
            SyncActivePanelPresentationState();

            if (surface == SelectionSurfaceId.PrimaryPane)
            {
                _workspaceLayoutHost.ActivatePanel(WorkspacePanelId.Primary);
            }
            else if (surface == SelectionSurfaceId.SecondaryPane && _isDualPaneEnabled)
            {
                _workspaceLayoutHost.ActivatePanel(WorkspacePanelId.Secondary);
            }

            _selectionSurfaceCoordinator.SetActiveSurface(surface);
            UpdateSelectionActivityState();
            NotifyWorkspacePanelVisualStateChanged();
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

        private void NotifyWorkspacePanelVisualStateChanged()
        {
            RaisePropertyChanged(
                nameof(IsPrimaryWorkspacePanelActive),
                nameof(IsSecondaryWorkspacePanelActive),
                nameof(PrimaryPaneToolbarBackground),
                nameof(PrimaryPaneBodyBackground),
                nameof(PrimaryPaneInputBackground),
                nameof(PrimaryPaneBorderBrush),
                nameof(PrimaryPaneBodyTranslation),
                nameof(SecondaryPaneToolbarBackground),
                nameof(SecondaryPaneBodyBackground),
                nameof(SecondaryPaneInputBackground),
                nameof(SecondaryPaneBorderBrush),
                nameof(SecondaryPaneBodyTranslation));
        }

        private void PrimaryPaneSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
        }

        private void SecondaryPaneSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
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
