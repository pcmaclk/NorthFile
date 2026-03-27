using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async Task<bool> ConfirmDeleteAsync(string name, bool recursive)
        {
            string contentKey = recursive ? "DialogDeleteFolderContent" : "DialogDeleteFileContent";
            var dialog = new ContentDialog
            {
                Title = S("DialogDeleteTitle"),
                Content = SF(contentKey, name),
                PrimaryButtonText = S("DialogDeletePrimaryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Close
            };

            if (this.Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}
