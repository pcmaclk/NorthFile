using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                _currentQuery = string.Empty;
                SearchTextBox.Text = string.Empty;
                await ReloadCurrentPresentationAsync();
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            _currentQuery = SearchTextBox.Text?.Trim() ?? string.Empty;
            await ReloadCurrentPresentationAsync();
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            await NavigateToPathAsync(NormalizeAddressInputPath(PathTextBox.Text), pushHistory: true);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadNextPageAsync();
        }

        private async void UpButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusAlreadyAtRoot");
                return;
            }

            if (IsDriveRoot(_currentPath))
            {
                await NavigateToPathAsync(ShellMyComputerPath, pushHistory: true);
                return;
            }

            string? parent = _explorerService.GetParentPath(_currentPath);
            if (string.IsNullOrEmpty(parent))
            {
                await NavigateToPathAsync(ShellMyComputerPath, pushHistory: true);
                return;
            }

            _pendingParentReturnAnchorPath = _currentPath;
            await NavigateToPathAsync(parent, pushHistory: true);
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_directorySessionController.TryGoBack(_backStack, _forwardStack, _currentPath, out string prev))
            {
                UpdateNavButtonsState();
                return;
            }

            UpdateNavButtonsState();
            await NavigateToPathAsync(prev, pushHistory: false);
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_directorySessionController.TryGoForward(_backStack, _forwardStack, _currentPath, out string next))
            {
                UpdateNavButtonsState();
                return;
            }

            UpdateNavButtonsState();
            await NavigateToPathAsync(next, pushHistory: false);
        }
    }
}
