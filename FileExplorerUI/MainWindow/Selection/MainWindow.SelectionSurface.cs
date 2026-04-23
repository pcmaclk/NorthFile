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
            ClearPanelSelection(WorkspacePanelId.Primary, clearAnchor: false);
        }

        private void ClearListSelectionAndAnchor()
        {
            ClearPanelSelection(WorkspacePanelId.Primary, clearAnchor: true);
        }

        private void ClearExplicitSelectionKeepAnchor()
        {
            ClearPanelSelection(WorkspacePanelId.Primary, clearAnchor: false);
        }

        private bool TryClearActivePanelSelection(bool clearAnchor)
        {
            WorkspacePanelId panelId = _isDualPaneEnabled
                ? _workspaceLayoutHost.ActivePanel
                : WorkspacePanelId.Primary;
            PanelViewState panelState = GetPanelState(panelId);
            if (panelState.SelectedEntryPaths.Count == 0 &&
                string.IsNullOrWhiteSpace(panelState.SelectedEntryPath))
            {
                return false;
            }

            ClearPanelSelection(panelId, clearAnchor);
            FocusPanelEntries(panelId);
            return true;
        }

        private void FocusEntriesList()
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            GetVisibleEntriesRoot().Focus(FocusState.Programmatic);
        }

        private void FocusSecondaryEntriesList()
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            SecondaryEntriesScrollViewer.Focus(FocusState.Programmatic);
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
            WorkspaceTabPerf.Mark("selection.active.enter", $"surface={surface}");
            WorkspacePanelId previousActivePanel = _workspaceLayoutHost.ActivePanel;
            bool previousDualPaneEnabled = _isDualPaneEnabled;
            WorkspacePanelId? requestedPanel = surface switch
            {
                SelectionSurfaceId.PrimaryPane => WorkspacePanelId.Primary,
                SelectionSurfaceId.SecondaryPane when _isDualPaneEnabled => WorkspacePanelId.Secondary,
                _ => null
            };
            SyncActivePanelPresentationState();
            WorkspaceTabPerf.Mark("selection.active.presentation");

            if (requestedPanel == WorkspacePanelId.Primary)
            {
                _workspaceLayoutHost.ActivatePanel(WorkspacePanelId.Primary);
            }
            else if (requestedPanel == WorkspacePanelId.Secondary)
            {
                _workspaceLayoutHost.ActivatePanel(WorkspacePanelId.Secondary);
            }

            if (requestedPanel is WorkspacePanelId activePanel &&
                previousDualPaneEnabled &&
                previousActivePanel != activePanel)
            {
                ClearPanelSelection(previousActivePanel, clearAnchor: true);
            }

            _selectionSurfaceCoordinator.SetActiveSurface(surface);
            UpdateSelectionActivityState();
            WorkspaceTabPerf.Mark("selection.active.state");
            NotifyWorkspacePanelVisualStateChanged(previousActivePanel, previousDualPaneEnabled);
            if (requestedPanel is not null &&
                (previousActivePanel != _workspaceLayoutHost.ActivePanel || previousDualPaneEnabled != _isDualPaneEnabled))
            {
                UpdateSidebarSelectionOnly();
            }

            WorkspaceTabPerf.Mark("selection.active.visual");
            UpdateFileCommandStates();
            WorkspaceTabPerf.Mark("selection.active.commands");
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
                UpdateSecondaryEntrySelectionVisuals();
                return;
            }

            _isEntriesSelectionActive = entriesSelectionActive;
            UpdateEntrySelectionVisuals();
            UpdateSecondaryEntrySelectionVisuals();
        }

        private void NotifyWorkspacePanelVisualStateChanged()
        {
            RaisePropertyChanged(
                nameof(IsPrimaryWorkspacePanelActive),
                nameof(IsSecondaryWorkspacePanelActive));
            RaiseWorkspacePanelShellPropertiesChanged();
        }

        private void NotifyWorkspacePanelVisualStateChanged(
            WorkspacePanelId previousActivePanel,
            bool previousDualPaneEnabled)
        {
            if (previousDualPaneEnabled == _isDualPaneEnabled &&
                previousActivePanel == _workspaceLayoutHost.ActivePanel)
            {
                return;
            }

            bool previousPrimaryActive = !previousDualPaneEnabled || previousActivePanel == WorkspacePanelId.Primary;
            bool previousSecondaryActive = previousDualPaneEnabled && previousActivePanel == WorkspacePanelId.Secondary;
            bool currentPrimaryActive = IsWorkspacePanelActive(WorkspacePanelId.Primary);
            bool currentSecondaryActive = IsWorkspacePanelActive(WorkspacePanelId.Secondary);

            if (previousPrimaryActive != currentPrimaryActive)
            {
                RaisePropertyChanged(nameof(IsPrimaryWorkspacePanelActive));
                RaiseWorkspacePanelShellPropertiesChanged(WorkspacePanelId.Primary);
            }

            if (previousSecondaryActive != currentSecondaryActive)
            {
                RaisePropertyChanged(nameof(IsSecondaryWorkspacePanelActive));
                RaiseWorkspacePanelShellPropertiesChanged(WorkspacePanelId.Secondary);
            }
        }

        private void PrimaryPaneSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            {
                return;
            }

            CancelRenameOverlayForPanelSwitch(WorkspacePanelId.Primary);
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
        }

        private void PrimaryPaneInput_GotFocus(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
        }

        private void PrimaryPaneInput_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            {
                return;
            }

            CancelRenameOverlayForPanelSwitch(WorkspacePanelId.Primary);
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
        }

        private void SecondaryPaneSurface_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            if (!e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            {
                return;
            }

            CancelRenameOverlayForPanelSwitch(WorkspacePanelId.Secondary);
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            if (e.OriginalSource is DependencyObject source &&
                IsDescendantOf(source, SecondaryEntriesScrollViewer) &&
                FindAncestorWithDataContext<EntryViewModel>(source) is null &&
                (SecondaryPanelState.SelectedEntryPaths.Count > 0 ||
                    !string.IsNullOrWhiteSpace(SecondaryPanelState.SelectedEntryPath)))
            {
                ClearPanelSelection(WorkspacePanelId.Secondary, clearAnchor: false);
                FocusSecondaryEntriesList();
            }
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
