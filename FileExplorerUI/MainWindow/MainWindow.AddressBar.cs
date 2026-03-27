using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void CloseActiveBreadcrumbFlyout()
        {
            if (_activeBreadcrumbFlyout?.IsOpen == true)
            {
                _activeBreadcrumbFlyout.Hide();
            }
        }

        private async void PathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CancelAddressEdit();
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await CommitAddressEditIfActiveAsync();
        }

        private async void PathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (IsFocusedElementWithinAddressEdit())
            {
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private void AddressBreadcrumbBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            EnterAddressEditMode(selectAll: true);
        }

        private void EnterAddressEditMode(bool selectAll)
        {
            _addressInlineSession ??= new InlineEditSession(
                CommitAddressEditIfActiveAsync,
                CancelAddressEdit,
                source => ReferenceEquals(source, PathTextBox) || IsDescendantOf(source, PathTextBox));

            _inlineEditCoordinator.CancelActiveSession();
            _inlineEditCoordinator.BeginSession(_addressInlineSession);
            AddressBreadcrumbBorder.Visibility = Visibility.Collapsed;
            PathTextBox.Visibility = Visibility.Visible;
            PathTextBox.Text = GetDisplayPathText(_currentPath);
            PathTextBox.Focus(FocusState.Programmatic);
            if (selectAll)
            {
                PathTextBox.SelectAll();
            }
            else
            {
                PathTextBox.SelectionStart = PathTextBox.Text.Length;
            }
        }

        private void ExitAddressEditMode(bool commit)
        {
            if (_addressInlineSession is not null)
            {
                _inlineEditCoordinator.ClearSession(_addressInlineSession);
            }

            if (!commit)
            {
                PathTextBox.Text = GetDisplayPathText(_currentPath);
            }

            PathTextBox.Visibility = Visibility.Collapsed;
            AddressBreadcrumbBorder.Visibility = Visibility.Visible;
        }

        private async Task CommitAddressEditIfActiveAsync()
        {
            if (PathTextBox.Visibility != Visibility.Visible)
            {
                return;
            }

            string targetPath = NormalizeAddressInputPath(PathTextBox.Text);
            await NavigateToPathAsync(targetPath, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private void CancelAddressEdit()
        {
            ExitAddressEditMode(commit: false);
        }

        private bool IsFocusedElementWithinAddressEdit()
        {
            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            return ReferenceEquals(focused, PathTextBox) || IsDescendantOf(focused, PathTextBox);
        }
    }
}
