using NorthFileUI.Workspace;
using System;
using System.Threading.Tasks;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private Task RestorePanelStateAsync(
            WorkspacePanelId panelId,
            bool preserveViewport,
            bool ensureSelectionVisible,
            bool focusEntries)
        {
            PanelViewState panel = _workspaceLayoutHost.GetPanelState(panelId);
            return panelId == WorkspacePanelId.Secondary
                ? RestoreSimplePanelStateAsync(
                    panelId,
                    panel,
                    preserveViewport,
                    ensureSelectionVisible,
                    focusEntries)
                : RestorePrimaryPanelStateAsync(
                    panel,
                    preserveViewport,
                    ensureSelectionVisible,
                    focusEntries);
        }

        private async Task NavigatePanelToPathAsync(WorkspacePanelId panelId, string path, bool pushHistory, bool focusEntriesAfterNavigation = true)
        {
            if (panelId == WorkspacePanelId.Primary)
            {
                await NavigateToPathAsync(path, pushHistory, focusEntriesAfterNavigation);
                return;
            }

            string targetPath = NormalizeAddressInputPath(path);
            if (!string.Equals(targetPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                !_explorerService.DirectoryExists(targetPath))
            {
                return;
            }

            PanelViewState panel = _workspaceLayoutHost.GetPanelState(panelId);
            _directorySessionController.ApplyPushHistoryIfNeeded(
                panel.Navigation.BackStack,
                panel.Navigation.ForwardStack,
                panel.CurrentPath,
                targetPath,
                pushHistory);

            SetPanelCurrentPath(panelId, targetPath);
            SetPanelQueryText(panelId, string.Empty, syncEditor: panelId == WorkspacePanelId.Primary);
            RaisePaneAddressPropertiesChanged(panelId);
            RaisePanelNavigationStateChanged(panelId);
            UpdateSecondaryPaneBreadcrumbs(targetPath);
            await ReloadPanelDataAsync(
                panelId,
                preserveViewport: false,
                ensureSelectionVisible: false,
                focusEntries: focusEntriesAfterNavigation);
        }

        private async Task<bool> TryNavigatePanelBackAsync(WorkspacePanelId panelId)
        {
            PanelViewState panel = _workspaceLayoutHost.GetPanelState(panelId);
            if (!_directorySessionController.TryGoBack(
                    panel.Navigation.BackStack,
                    panel.Navigation.ForwardStack,
                    panel.CurrentPath,
                    out string previousPath))
            {
                RaisePanelNavigationStateChanged(panelId);
                return false;
            }

            await NavigatePanelToPathAsync(panelId, previousPath, pushHistory: false);
            return true;
        }

        private async Task<bool> TryNavigatePanelForwardAsync(WorkspacePanelId panelId)
        {
            PanelViewState panel = _workspaceLayoutHost.GetPanelState(panelId);
            if (!_directorySessionController.TryGoForward(
                    panel.Navigation.BackStack,
                    panel.Navigation.ForwardStack,
                    panel.CurrentPath,
                    out string nextPath))
            {
                RaisePanelNavigationStateChanged(panelId);
                return false;
            }

            await NavigatePanelToPathAsync(panelId, nextPath, pushHistory: false);
            return true;
        }

        private async Task<bool> TryNavigatePanelUpAsync(WorkspacePanelId panelId)
        {
            PanelViewState panel = _workspaceLayoutHost.GetPanelState(panelId);
            string currentPath = panel.CurrentPath;
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                RaisePanelNavigationStateChanged(panelId);
                return false;
            }

            if (string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                if (panelId == WorkspacePanelId.Primary)
                {
                    UpdateStatusKey("StatusAlreadyAtRoot");
                }

                RaisePanelNavigationStateChanged(panelId);
                return false;
            }

            if (IsDriveRoot(currentPath))
            {
                await NavigatePanelToPathAsync(panelId, ShellMyComputerPath, pushHistory: true);
                return true;
            }

            string? parent = _explorerService.GetParentPath(currentPath);
            if (panelId == WorkspacePanelId.Primary)
            {
                _pendingParentReturnAnchorPath = currentPath;
            }

            await NavigatePanelToPathAsync(
                panelId,
                string.IsNullOrWhiteSpace(parent) ? ShellMyComputerPath : parent,
                pushHistory: true);
            return true;
        }

        private void RaisePanelNavigationStateChanged(WorkspacePanelId panelId)
        {
            UpdateSidebarSelectionForPanelPathChange(panelId);

            if (panelId == WorkspacePanelId.Primary)
            {
                UpdateNavButtonsState();
                return;
            }

            RaiseSecondaryPaneNavigationStateChanged();
        }

        private async Task CommitPanelSearchAsync(WorkspacePanelId panelId, string queryText)
        {
            SetPanelQueryText(panelId, queryText, syncEditor: panelId == WorkspacePanelId.Primary);
            await ReloadPanelDataAsync(
                panelId,
                preserveViewport: false,
                ensureSelectionVisible: false,
                focusEntries: false);
        }

        private async Task ClearPanelSearchAsync(WorkspacePanelId panelId)
        {
            SetPanelQueryText(panelId, string.Empty, syncEditor: panelId == WorkspacePanelId.Primary);
            await ReloadPanelDataAsync(
                panelId,
                preserveViewport: false,
                ensureSelectionVisible: false,
                focusEntries: false);
        }
    }
}
