using FileExplorerUI.Workspace;
using System;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async Task RefreshCurrentDirectoryInBackgroundAsync(bool preserveViewport = false)
        {
            if (string.Equals(GetPanelCurrentPath(WorkspacePanelId.Primary), ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                PopulateMyComputerEntries();
                return;
            }

            try
            {
                await ReloadPanelDataAsync(
                    WorkspacePanelId.Primary,
                    preserveViewport: preserveViewport,
                    ensureSelectionVisible: false,
                    focusEntries: false);
            }
            catch
            {
                // Keep local state if background refresh fails; next manual load can recover.
            }
        }
    }
}
