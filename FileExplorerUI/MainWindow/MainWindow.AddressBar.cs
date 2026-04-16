using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Threading.Tasks;
using FileExplorerUI.Workspace;

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
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);

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

        private async void SecondaryPaneAddressTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CancelSecondaryAddressEdit();
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Enter)
            {
                return;
            }

            e.Handled = true;
            await CommitSecondaryAddressEditIfActiveAsync();
        }

        private void AddressBreadcrumbBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source &&
                (source is Button || IsDescendantOf(source, OverflowBreadcrumbButton) || IsDescendantOf(source, BreadcrumbItemsControl)))
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            EnterAddressEditMode(selectAll: true);
        }

        private void SecondaryAddressBreadcrumbBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source &&
                (source is Button || IsDescendantOf(source, SecondaryOverflowBreadcrumbButton) || IsDescendantOf(source, SecondaryBreadcrumbItemsControl)))
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            EnterSecondaryAddressEditMode(selectAll: true);
        }

        private void EnterAddressEditMode(bool selectAll)
        {
            _addressInlineSession ??= new InlineEditSession(
                CommitAddressEditIfActiveAsync,
                CancelAddressEdit,
                source => ReferenceEquals(source, PathTextBox) || IsDescendantOf(source, PathTextBox),
                commitOnExternalClick: false);

            _inlineEditCoordinator.CancelActiveSession();
            _inlineEditCoordinator.BeginSession(_addressInlineSession);
            AddressBreadcrumbBorder.Visibility = Visibility.Collapsed;
            PathTextBox.Visibility = Visibility.Visible;
            PathTextBox.Text = GetDisplayPathText(GetPanelCurrentPath(WorkspacePanelId.Primary));
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

        private void EnterSecondaryAddressEditMode(bool selectAll)
        {
            _secondaryAddressInlineSession ??= new InlineEditSession(
                CommitSecondaryAddressEditIfActiveAsync,
                CancelSecondaryAddressEdit,
                source => ReferenceEquals(source, SecondaryPaneAddressTextBox) || IsDescendantOf(source, SecondaryPaneAddressTextBox),
                commitOnExternalClick: false);

            _inlineEditCoordinator.CancelActiveSession();
            _inlineEditCoordinator.BeginSession(_secondaryAddressInlineSession);
            SecondaryAddressBreadcrumbBorder.Visibility = Visibility.Collapsed;
            SecondaryPaneAddressTextBox.Visibility = Visibility.Visible;
            SecondaryPaneAddressTextBox.Text = GetDisplayPathText(SecondaryPanelState.CurrentPath);
            SecondaryPaneAddressTextBox.Focus(FocusState.Programmatic);
            if (selectAll)
            {
                SecondaryPaneAddressTextBox.SelectAll();
            }
            else
            {
                SecondaryPaneAddressTextBox.SelectionStart = SecondaryPaneAddressTextBox.Text.Length;
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
                PathTextBox.Text = GetDisplayPathText(GetPanelCurrentPath(WorkspacePanelId.Primary));
            }

            PathTextBox.Visibility = Visibility.Collapsed;
            AddressBreadcrumbBorder.Visibility = Visibility.Visible;
        }

        private void ExitSecondaryAddressEditMode(bool commit)
        {
            if (_secondaryAddressInlineSession is not null)
            {
                _inlineEditCoordinator.ClearSession(_secondaryAddressInlineSession);
            }

            if (!commit)
            {
                SecondaryPaneAddressTextBox.Text = GetDisplayPathText(SecondaryPanelState.CurrentPath);
            }

            SecondaryPaneAddressTextBox.Visibility = Visibility.Collapsed;
            SecondaryAddressBreadcrumbBorder.Visibility = Visibility.Visible;
        }

        private void ExitAddressEditModeForPanel(WorkspacePanelId panelId, bool commit)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                ExitSecondaryAddressEditMode(commit);
                return;
            }

            ExitAddressEditMode(commit);
        }

        private async Task CommitAddressEditIfActiveAsync()
        {
            if (PathTextBox.Visibility != Visibility.Visible)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);

            string targetPath = NormalizeAddressInputPath(PathTextBox.Text);
            if (string.Equals(targetPath, GetPanelCurrentPath(WorkspacePanelId.Primary), StringComparison.OrdinalIgnoreCase))
            {
                ExitAddressEditMode(commit: true);
                return;
            }

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

            await NavigatePanelToPathAsync(WorkspacePanelId.Primary, targetPath, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private async Task CommitSecondaryAddressEditIfActiveAsync()
        {
            if (SecondaryPaneAddressTextBox.Visibility != Visibility.Visible)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);

            string targetPath = NormalizeAddressInputPath(SecondaryPaneAddressTextBox.Text);
            if (string.Equals(targetPath, SecondaryPanelState.CurrentPath, StringComparison.OrdinalIgnoreCase))
            {
                ExitSecondaryAddressEditMode(commit: true);
                return;
            }

            if (!string.Equals(targetPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                !_explorerService.DirectoryExists(targetPath))
            {
                SecondaryPaneAddressTextBox.Focus(FocusState.Programmatic);
                SecondaryPaneAddressTextBox.SelectAll();
                return;
            }

            await NavigatePanelToPathAsync(WorkspacePanelId.Secondary, targetPath, pushHistory: true);
            ExitSecondaryAddressEditMode(commit: true);
        }

        private void CancelAddressEdit()
        {
            HideAddressInputTeachingTip();
            ExitAddressEditMode(commit: false);
            FocusEntriesList();
        }

        private void CancelSecondaryAddressEdit()
        {
            ExitSecondaryAddressEditMode(commit: false);
            FocusSecondaryEntriesList();
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

        private bool IsFocusedElementWithinSecondaryAddressEdit()
        {
            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            return ReferenceEquals(focused, SecondaryPaneAddressTextBox) || IsDescendantOf(focused, SecondaryPaneAddressTextBox);
        }
    }
}
