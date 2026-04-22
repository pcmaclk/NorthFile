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

            if (GetPanelIsLoading(WorkspacePanelId.Primary))
            {
                UpdateStatusKey("StatusSidebarNavIgnoredLoading");
                return;
            }

            FocusSidebarSurface();
            ClearListSelectionAndAnchor();

            if (IsCurrentPath(e.Path))
            {
                StyledSidebarView.SetSelectedPath(GetPanelCurrentPath(WorkspacePanelId.Primary));
                return;
            }

            try
            {
            await NavigatePanelToPathAsync(
                WorkspacePanelId.Primary,
                e.Path,
                pushHistory: true,
                focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", FileOperationErrors.ToUserMessage(ex));
                StyledSidebarView.SetSelectedPath(GetPanelCurrentPath(WorkspacePanelId.Primary));
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
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            StyledSidebarView.SetSelectedPath(currentPath);
            _ = SelectSidebarTreePathAsync(currentPath);
        }

        private bool IsCurrentPath(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
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

        private bool IsExactCurrentPath(string candidate)
        {
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
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
