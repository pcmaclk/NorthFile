using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
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

            string targetPath = NormalizeAddressInputPath(PathTextBox.Text);
            if (!string.Equals(targetPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                !_explorerService.DirectoryExists(targetPath))
            {
                CancelAddressEdit();
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private void AddressBreadcrumbBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source &&
                (source is Button || IsDescendantOf(source, OverflowBreadcrumbButton) || IsDescendantOf(source, BreadcrumbItemsControl)))
            {
                return;
            }

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
            if (!string.Equals(targetPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                !_explorerService.DirectoryExists(targetPath))
            {
                ShowAddressInputTeachingTip(
                    S("AddressInputNotFoundTeachingTipTitle"),
                    SF("AddressInputNotFoundTeachingTipMessage", targetPath));
                PathTextBox.Focus(FocusState.Programmatic);
                PathTextBox.SelectAll();
                return;
            }

            await NavigateToPathAsync(targetPath, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private void CancelAddressEdit()
        {
            HideAddressInputTeachingTip();
            ExitAddressEditMode(commit: false);
        }

        private void PathTextBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            HideAddressInputTeachingTip();
        }

        private void ShowAddressInputTeachingTip(string title, string message)
        {
            if (AddressInputTeachingTip is null)
            {
                return;
            }

            EnsureAddressInputTeachingTipTimer();
            _addressInputTeachingTipTimer!.Stop();
            AddressInputTeachingTip.IsOpen = false;
            AddressInputTeachingTip.Target = PathTextBox;
            AddressInputTeachingTip.Title = title;
            AddressInputTeachingTip.Subtitle = message;
            AddressInputTeachingTip.IsOpen = true;
            _addressInputTeachingTipTimer.Interval = TimeSpan.FromSeconds(5);
            _addressInputTeachingTipTimer.Start();
        }

        private void HideAddressInputTeachingTip()
        {
            _addressInputTeachingTipTimer?.Stop();
            if (AddressInputTeachingTip is not null)
            {
                AddressInputTeachingTip.IsOpen = false;
            }
        }

        private void EnsureAddressInputTeachingTipTimer()
        {
            if (_addressInputTeachingTipTimer is not null)
            {
                return;
            }

            _addressInputTeachingTipTimer = new DispatcherTimer();
            _addressInputTeachingTipTimer.Tick += AddressInputTeachingTipTimer_Tick;
        }

        private void AddressInputTeachingTipTimer_Tick(object? sender, object e)
        {
            HideAddressInputTeachingTip();
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
