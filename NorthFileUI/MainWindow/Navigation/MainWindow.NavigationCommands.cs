using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.IO;
using System.Threading.Tasks;
using NorthFileUI.Workspace;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                await ClearPanelSearchAsync(WorkspacePanelId.Primary);
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await CommitPanelSearchAsync(WorkspacePanelId.Primary, SearchTextBox.Text ?? string.Empty);
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            SetPanelQueryText(WorkspacePanelId.Primary, SearchTextBox.Text);
        }

        private async void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            string targetPath = NormalizeAddressInputPath(PathTextBox.Text);
            if (string.Equals(targetPath, GetPanelCurrentPath(WorkspacePanelId.Primary), StringComparison.OrdinalIgnoreCase))
            {
                await ForceRefreshPanelDirectoryAsync(WorkspacePanelId.Primary, preserveViewport: true);
                return;
            }

            await NavigatePanelToPathAsync(WorkspacePanelId.Primary, targetPath, pushHistory: true);
        }

        private async void NextButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadNextPageAsync();
        }

        private async void UpButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            await TryNavigatePanelUpAsync(WorkspacePanelId.Primary);
        }

        private async void BackButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            await TryNavigatePanelBackAsync(WorkspacePanelId.Primary);
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            await TryNavigatePanelForwardAsync(WorkspacePanelId.Primary);
        }
    }
}
