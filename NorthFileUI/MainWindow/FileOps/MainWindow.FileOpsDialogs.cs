using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using Windows.Foundation;

namespace NorthFileUI
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

            PrepareWindowDialog(dialog);

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
    }
}
