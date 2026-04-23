using System;
using System.IO;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void StyledSidebarView_NavigateRequested(object? sender, SidebarNavigateRequestedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Path))
            {
                return;
            }

            WorkspacePanelId targetPanelId = GetSidebarNavigationTargetPanelId();
            if (GetPanelIsLoading(targetPanelId))
            {
                UpdateStatusKey("StatusSidebarNavIgnoredLoading");
                return;
            }

            FocusSidebarSurface();
            ClearPanelSelection(targetPanelId, clearAnchor: true);

            if (IsCurrentPath(e.Path, targetPanelId))
            {
                StyledSidebarView.SetSelectedPath(GetPanelCurrentPath(targetPanelId));
                return;
            }

            try
            {
                await NavigatePanelToPathAsync(
                    targetPanelId,
                    e.Path,
                    pushHistory: true,
                    focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", FileOperationErrors.ToUserMessage(ex));
                StyledSidebarView.SetSelectedPath(GetPanelCurrentPath(targetPanelId));
            }
        }

        private void StyledSidebarView_SettingsRequested(object? sender, EventArgs e)
        {
            EnterSettingsShell();
        }

        private void TitleBarSettingsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            EnterSettingsShell();
        }

        private void SidebarCollapseButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            _sidebarPinnedCompact = !_sidebarPinnedCompact;
            if (!_sidebarPinnedCompact)
            {
                _sidebarPreferredExpandedWidth = Math.Max(_sidebarPreferredExpandedWidth, SidebarExpandedDefaultWidth);
            }

            ApplySidebarWidthLayout();
        }

        private void UpdateSidebarSelectionOnly()
        {
            UpdateSidebarSelectionForPanelPathChange(GetSidebarNavigationTargetPanelId(), force: true);
        }

        private void UpdateSidebarSelectionForPanelPathChange(WorkspacePanelId panelId, bool force = false)
        {
            if (!force && GetSidebarNavigationTargetPanelId() != panelId)
            {
                return;
            }

            string currentPath = GetPanelCurrentPath(panelId);
            StyledSidebarView.SetSelectedPath(currentPath);
            _ = SelectSidebarTreePathAsync(currentPath);
        }

        private WorkspacePanelId GetSidebarNavigationTargetPanelId()
        {
            return _isDualPaneEnabled ? _workspaceLayoutHost.ActivePanel : WorkspacePanelId.Primary;
        }

        private bool IsCurrentPath(string candidate, WorkspacePanelId panelId)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            string currentPath = GetPanelCurrentPath(panelId);
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return false;
            }

            if (string.Equals(candidate, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(candidate, currentPath, StringComparison.OrdinalIgnoreCase);
            }

            string curr = Path.GetFullPath(currentPath).TrimEnd('\\');
            string cand = Path.GetFullPath(candidate).TrimEnd('\\');
            if (string.Equals(curr, cand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return curr.StartsWith(cand + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsExactCurrentPath(string candidate, WorkspacePanelId panelId)
        {
            string currentPath = GetPanelCurrentPath(panelId);
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(currentPath))
            {
                return false;
            }

            try
            {
                string curr = Path.GetFullPath(currentPath).TrimEnd('\\');
                string cand = Path.GetFullPath(candidate).TrimEnd('\\');
                return string.Equals(curr, cand, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }
}
