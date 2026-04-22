using FileExplorerUI.Workspace;
using System;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async Task RefreshCurrentDirectoryInBackgroundAsync(bool preserveViewport = false)
        {
            await RefreshPanelDirectoryInBackgroundAsync(WorkspacePanelId.Primary, preserveViewport);
        }

        private async Task RefreshPanelDirectoryInBackgroundAsync(WorkspacePanelId panelId, bool preserveViewport = false)
        {
            string currentPath = GetPanelCurrentPath(panelId);
            if (string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                if (panelId == WorkspacePanelId.Primary)
                {
                    PopulateMyComputerEntries();
                }

                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(currentPath) ||
                    string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                InvalidateDirectoryDataForRefresh(panelId, currentPath, "background-refresh");
                await ReloadPanelDataAsync(
                    panelId,
                    preserveViewport: preserveViewport,
                    ensureSelectionVisible: false,
                    focusEntries: false);
            }
            catch
            {
                // Keep local state if background refresh fails; next manual load can recover.
            }
        }

        private async Task ForceRefreshPanelDirectoryAsync(WorkspacePanelId panelId, bool preserveViewport)
        {
            string currentPath = GetPanelCurrentPath(panelId);
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return;
            }

            if (string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                if (panelId == WorkspacePanelId.Primary)
                {
                    PopulateMyComputerEntries();
                    ApplyCurrentPresentation();
                }
                else
                {
                    InvalidatePanelDataLoadedForCurrentNavigation(panelId);
                    await ReloadPanelDataAsync(
                        panelId,
                        preserveViewport: preserveViewport,
                        ensureSelectionVisible: false,
                        focusEntries: false);
                }

                return;
            }

            InvalidateDirectoryDataForRefresh(panelId, currentPath, "manual-refresh");
            await ReloadPanelDataAsync(
                panelId,
                preserveViewport: preserveViewport,
                ensureSelectionVisible: false,
                focusEntries: false);
        }

        private async Task RefreshPanelsForDirectoryChangeAsync(string directoryPath, string reason)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            var refreshTasks = new System.Collections.Generic.List<Task>(2);
            if (string.Equals(GetPanelCurrentPath(WorkspacePanelId.Primary), directoryPath, StringComparison.OrdinalIgnoreCase))
            {
                InvalidateDirectoryDataForRefresh(WorkspacePanelId.Primary, directoryPath, reason);
                refreshTasks.Add(ReloadPanelDataAsync(
                    WorkspacePanelId.Primary,
                    preserveViewport: true,
                    ensureSelectionVisible: false,
                    focusEntries: false));
            }

            if (_isDualPaneEnabled &&
                string.Equals(GetPanelCurrentPath(WorkspacePanelId.Secondary), directoryPath, StringComparison.OrdinalIgnoreCase))
            {
                InvalidateDirectoryDataForRefresh(WorkspacePanelId.Secondary, directoryPath, reason);
                refreshTasks.Add(ReloadPanelDataAsync(
                    WorkspacePanelId.Secondary,
                    preserveViewport: true,
                    ensureSelectionVisible: false,
                    focusEntries: false));
            }

            if (refreshTasks.Count > 0)
            {
                await Task.WhenAll(refreshTasks);
            }
        }

        private void InvalidateDirectoryDataForRefresh(WorkspacePanelId panelId, string directoryPath, string reason)
        {
            try
            {
                _explorerService.MarkPathChanged(directoryPath);
            }
            catch
            {
                // Ignore mark failures; forced result-set recreation below still refreshes UI state.
            }

            EnsurePersistentRefreshFallbackInvalidation(directoryPath, reason);
            InvalidatePanelDataLoadedForCurrentNavigation(panelId);
            SetPanelActiveEntryResultSet(panelId, null);
            SetPanelLastFetchMs(panelId, 0);
        }
    }
}
