using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void SidebarNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_suppressSidebarNavSelection || args.IsSettingsSelected)
            {
                return;
            }

            if (args.SelectedItemContainer is not NavigationViewItem item || item.Tag is not string target || string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            if (_isLoading)
            {
                UpdateStatusKey("StatusSidebarNavIgnoredLoading");
                return;
            }

            FocusSidebarSurface();
            ClearListSelectionAndAnchor();

            if (IsCurrentPath(target))
            {
                return;
            }

            try
            {
                await NavigateToPathAsync(target, pushHistory: true, focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async void StyledSidebarView_NavigateRequested(object? sender, SidebarNavigateRequestedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Path))
            {
                return;
            }

            if (_isLoading)
            {
                UpdateStatusKey("StatusSidebarNavIgnoredLoading");
                return;
            }

            FocusSidebarSurface();
            ClearListSelectionAndAnchor();

            if (IsCurrentPath(e.Path))
            {
                StyledSidebarView.SetSelectedPath(_currentPath);
                return;
            }

            try
            {
                await NavigateToPathAsync(e.Path, pushHistory: true, focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarNavFailed", FileOperationErrors.ToUserMessage(ex));
                StyledSidebarView.SetSelectedPath(_currentPath);
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

        private void ApplySidebarSelectionImmediate(string target)
        {
            if (SidebarNavView is null)
            {
                return;
            }

            NavigationViewItem? selected = null;
            int bestLen = -1;
            string selectedPath = Path.GetFullPath(target).TrimEnd('\\');
            foreach ((string path, NavigationViewItem item) in _sidebarPathButtons)
            {
                string currentPath = Path.GetFullPath(path).TrimEnd('\\');
                bool matched = string.Equals(currentPath, selectedPath, StringComparison.OrdinalIgnoreCase)
                    || selectedPath.StartsWith(currentPath + "\\", StringComparison.OrdinalIgnoreCase);
                if (!matched)
                {
                    continue;
                }

                if (path.Length > bestLen)
                {
                    bestLen = path.Length;
                    selected = item;
                }
            }

            _suppressSidebarNavSelection = true;
            SidebarNavView.SelectedItem = selected;
            _suppressSidebarNavSelection = false;

            StyledSidebarView.SetSelectedPath(target);
            _ = SelectSidebarTreePathAsync(target);
        }

        private void UpdateSidebarSelectionOnly()
        {
            NavigationViewItem? selected = null;
            int bestLen = -1;
            foreach ((string path, NavigationViewItem item) in _sidebarPathButtons)
            {
                if (!IsCurrentPath(path))
                {
                    continue;
                }

                if (path.Length > bestLen)
                {
                    bestLen = path.Length;
                    selected = item;
                }
            }

            if (SidebarNavView is not null)
            {
                _suppressSidebarNavSelection = true;
                SidebarNavView.SelectedItem = selected;
                _suppressSidebarNavSelection = false;
            }

            StyledSidebarView.SetSelectedPath(_currentPath);
            _ = SelectSidebarTreePathAsync(_currentPath);
        }

        private bool IsCurrentPath(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return false;
            }

            if (string.Equals(candidate, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(candidate, _currentPath, StringComparison.OrdinalIgnoreCase);
            }

            string curr = Path.GetFullPath(_currentPath).TrimEnd('\\');
            string cand = Path.GetFullPath(candidate).TrimEnd('\\');
            if (string.Equals(curr, cand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return curr.StartsWith(cand + "\\", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsExactCurrentPath(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(_currentPath))
            {
                return false;
            }

            try
            {
                string curr = Path.GetFullPath(_currentPath).TrimEnd('\\');
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
