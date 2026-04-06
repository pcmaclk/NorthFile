using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void SecondaryPaneAddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            _workspaceLayoutHost.ShellState.Secondary.AddressText = textBox.Text ?? string.Empty;
            RaisePropertyChanged(nameof(SecondaryPaneAddressText), nameof(SecondaryPaneAddressEditorText));
        }

        private void SecondaryPaneAddressTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                _workspaceLayoutHost.ShellState.Secondary.AddressText = GetDisplayPathText(_workspaceLayoutHost.ShellState.Secondary.CurrentPath);
                RaisePropertyChanged(
                    nameof(SecondaryPaneAddressText),
                    nameof(SecondaryPaneAddressEditorText));
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            string targetPath = NormalizeAddressInputPath(_workspaceLayoutHost.ShellState.Secondary.AddressText);
            if (!string.Equals(targetPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                !_explorerService.DirectoryExists(targetPath))
            {
                return;
            }

            _workspaceLayoutHost.ShellState.Secondary.CurrentPath = targetPath;
            _workspaceLayoutHost.ShellState.Secondary.AddressText = GetDisplayPathText(targetPath);
            RaisePropertyChanged(
                nameof(SecondaryPaneAddressText),
                nameof(SecondaryPaneAddressEditorText),
                nameof(SecondaryPanePlaceholderText));
        }

        private void SecondaryPaneSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            _workspaceLayoutHost.ShellState.Secondary.QueryText = textBox.Text ?? string.Empty;
            RaisePropertyChanged(nameof(SecondaryPaneSearchText));
        }

        private void SecondaryPaneSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != Windows.System.VirtualKey.Escape)
            {
                return;
            }

            e.Handled = true;
            _workspaceLayoutHost.ShellState.Secondary.QueryText = string.Empty;
            RaisePropertyChanged(nameof(SecondaryPaneSearchText));
        }
    }
}
