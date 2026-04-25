using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NorthFileUI.Workspace;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private async void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            await HandleBreadcrumbButtonClickAsync(sender, WorkspacePanelId.Primary);
        }

        private async void SecondaryBreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            await HandleBreadcrumbButtonClickAsync(sender, WorkspacePanelId.Secondary);
        }

        private async Task HandleBreadcrumbButtonClickAsync(object sender, WorkspacePanelId panelId)
        {
            if (sender is not Button btn || btn.Tag is not string target)
            {
                return;
            }

            // The current directory crumb is intentionally inert.
            // Only the reserved blank area in the address bar enters edit mode.
            if (btn.DataContext is BreadcrumbItemViewModel item && item.IsLast)
            {
                return;
            }

            await NavigatePanelToPathAsync(panelId, target, pushHistory: true);
            ExitAddressEditModeForPanel(panelId, commit: true);
        }

        private void BreadcrumbChevronButton_Click(object sender, RoutedEventArgs e)
        {
            ShowBreadcrumbChevronFlyout(sender, WorkspacePanelId.Primary);
        }

        private void SecondaryBreadcrumbChevronButton_Click(object sender, RoutedEventArgs e)
        {
            ShowBreadcrumbChevronFlyout(sender, WorkspacePanelId.Secondary);
        }

        private void ShowBreadcrumbChevronFlyout(object sender, WorkspacePanelId panelId)
        {
            if (sender is not Button btn || btn.Tag is not string basePath)
            {
                return;
            }

            var flyout = new MenuFlyout();
            if (Application.Current.Resources.TryGetValue("BreadcrumbMenuFlyoutPresenterStyle", out object styleObj)
                && styleObj is Style presenterStyle)
            {
                flyout.MenuFlyoutPresenterStyle = presenterStyle;
            }
            _activeBreadcrumbFlyout = flyout;
            flyout.Closed -= BreadcrumbSplitFlyout_Closed;
            flyout.Closed += BreadcrumbSplitFlyout_Closed;

            try
            {
                if (string.Equals(basePath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (DriveInfo drive in _explorerService.GetReadyDrives())
                    {
                        flyout.Items.Add(new MenuFlyoutItem
                        {
                            Text = drive.Name,
                            Tag = new BreadcrumbNavigationTarget(panelId, drive.Name)
                        });
                        if (flyout.Items[^1] is MenuFlyoutItem driveItem)
                        {
                            driveItem.Click += BreadcrumbSubdirMenuItem_Click;
                        }
                    }
                }
                else if (_explorerService.DirectoryExists(basePath))
                {
                    string[] dirs = _explorerService.GetDirectories(basePath);
                    Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                    int shown = 0;
                    foreach (string dir in dirs)
                    {
                        string name = Path.GetFileName(dir.TrimEnd('\\'));
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = dir;
                        }

                        flyout.Items.Add(new MenuFlyoutItem
                        {
                            Text = name,
                            Tag = new BreadcrumbNavigationTarget(panelId, dir)
                        });
                        if (flyout.Items[^1] is MenuFlyoutItem item)
                        {
                            item.Click += BreadcrumbSubdirMenuItem_Click;
                        }
                        shown++;
                        if (shown >= 80)
                        {
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Fallback item below.
            }

            if (flyout.Items.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = S("CommonEmpty"),
                    IsEnabled = false
                });
            }

            ShowBreadcrumbFlyout(flyout, btn);
        }

        private void OverflowBreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            ShowOverflowBreadcrumbFlyout(sender, WorkspacePanelId.Primary);
        }

        private void SecondaryOverflowBreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            ShowOverflowBreadcrumbFlyout(sender, WorkspacePanelId.Secondary);
        }

        private void ShowOverflowBreadcrumbFlyout(object sender, WorkspacePanelId panelId)
        {
            if (sender is not Button btn)
            {
                return;
            }

            var flyout = new MenuFlyout();
            if (Application.Current.Resources.TryGetValue("BreadcrumbMenuFlyoutPresenterStyle", out object styleObj)
                && styleObj is Style presenterStyle)
            {
                flyout.MenuFlyoutPresenterStyle = presenterStyle;
            }

            _activeBreadcrumbFlyout = flyout;
            flyout.Closed -= BreadcrumbSplitFlyout_Closed;
            flyout.Closed += BreadcrumbSplitFlyout_Closed;

            foreach (BreadcrumbItemViewModel item in GetBreadcrumbState(panelId).HiddenBreadcrumbItems)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = item.Label,
                    Tag = new BreadcrumbNavigationTarget(panelId, item.FullPath)
                };
                menuItem.Click += OverflowBreadcrumbMenuItem_Click;
                flyout.Items.Add(menuItem);
            }

            if (flyout.Items.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = S("CommonEmpty"),
                    IsEnabled = false
                });
            }

            ShowBreadcrumbFlyout(flyout, btn);
        }

        private void ShowBreadcrumbFlyout(MenuFlyout flyout, FrameworkElement target)
        {
            var options = new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft
            };

            flyout.ShowAt(target, options);
        }

        private void BreadcrumbSplitFlyout_Closed(object? sender, object e)
        {
            if (ReferenceEquals(sender, _activeBreadcrumbFlyout))
            {
                _activeBreadcrumbFlyout = null;
            }
        }

        private bool HasChildDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !_explorerService.DirectoryExists(path))
            {
                return false;
            }

            return _explorerService.DirectoryHasChildDirectories(path);
        }

        private bool HasBreadcrumbChildren(string path)
        {
            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return _explorerService.GetReadyDrives().Count > 0;
            }

            return HasChildDirectory(path);
        }

        private async void BreadcrumbSubdirMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item ||
                item.Tag is not BreadcrumbNavigationTarget navigationTarget)
            {
                return;
            }

            await NavigatePanelToPathAsync(navigationTarget.PanelId, navigationTarget.Path, pushHistory: true);
            ExitAddressEditModeForPanel(navigationTarget.PanelId, commit: true);
        }

        private async void OverflowBreadcrumbMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item ||
                item.Tag is not BreadcrumbNavigationTarget navigationTarget)
            {
                return;
            }

            await NavigatePanelToPathAsync(navigationTarget.PanelId, navigationTarget.Path, pushHistory: true);
            ExitAddressEditModeForPanel(navigationTarget.PanelId, commit: true);
        }

        private void UpdateBreadcrumbs(string path)
        {
            UpdateBreadcrumbs(WorkspacePanelId.Primary, path);
        }

        private void UpdateSecondaryPaneBreadcrumbs(string path)
        {
            UpdateBreadcrumbs(WorkspacePanelId.Secondary, path);
        }

        private void UpdateBreadcrumbs(WorkspacePanelId panelId, string path)
        {
            PanelNavigationState breadcrumbState = GetBreadcrumbState(panelId);
            RaiseBreadcrumbPresentationChanged(panelId);
            breadcrumbState.Breadcrumbs.Clear();
            breadcrumbState.BreadcrumbWidthsReady = false;
            breadcrumbState.BreadcrumbVisibleStartIndex = -1;
            breadcrumbState.HiddenBreadcrumbItems.Clear();
            breadcrumbState.VisibleBreadcrumbs.Clear();
            if (GetOverflowBreadcrumbButton(panelId) is Button overflowButton)
            {
                overflowButton.Visibility = Visibility.Collapsed;
            }

            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                breadcrumbState.Breadcrumbs.Add(new BreadcrumbItemViewModel
                {
                    Label = S("SidebarMyComputer"),
                    FullPath = ShellMyComputerPath,
                    HasChildren = false,
                    IconGlyph = BreadcrumbMyComputerGlyph,
                    ChevronVisibility = Visibility.Collapsed,
                    IsLast = true,
                    MeasuredWidth = 0
                });
                ResetVisibleBreadcrumbs(panelId, breadcrumbState.Breadcrumbs);
                return;
            }

            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;

            breadcrumbState.Breadcrumbs.Add(new BreadcrumbItemViewModel
            {
                Label = S("SidebarMyComputer"),
                FullPath = ShellMyComputerPath,
                HasChildren = HasBreadcrumbChildren(ShellMyComputerPath),
                IconGlyph = BreadcrumbMyComputerGlyph,
                ChevronVisibility = Visibility.Collapsed
            });

            if (!string.IsNullOrEmpty(root))
            {
                breadcrumbState.Breadcrumbs.Add(new BreadcrumbItemViewModel
                {
                    Label = root,
                    FullPath = root,
                    HasChildren = HasBreadcrumbChildren(root),
                    ChevronVisibility = Visibility.Collapsed
                });
            }

            string remaining = fullPath.Substring(root.Length).Trim('\\');
            if (!string.IsNullOrEmpty(remaining))
            {
                string current = root;
                string[] parts = remaining.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                foreach (string part in parts)
                {
                    current = string.IsNullOrEmpty(current) ? part : Path.Combine(current, part);
                    breadcrumbState.Breadcrumbs.Add(new BreadcrumbItemViewModel
                    {
                        Label = part,
                        FullPath = current,
                        HasChildren = HasBreadcrumbChildren(current),
                        ChevronVisibility = Visibility.Collapsed
                    });
                }
            }

            for (int i = 0; i < breadcrumbState.Breadcrumbs.Count; i++)
            {
                BreadcrumbItemViewModel item = breadcrumbState.Breadcrumbs[i];
                item.IsLast = i == breadcrumbState.Breadcrumbs.Count - 1;
                item.ChevronVisibility = item.HasChildren && !item.IsLast
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                item.MeasuredWidth = 0;
            }

            ResetVisibleBreadcrumbs(panelId, breadcrumbState.Breadcrumbs);
        }

        private void AddressBreadcrumbBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVisibleBreadcrumbs(WorkspacePanelId.Primary);
        }

        private void SecondaryAddressBreadcrumbBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVisibleBreadcrumbs(WorkspacePanelId.Secondary);
        }

        private void UpdateVisibleBreadcrumbs(WorkspacePanelId panelId)
        {
            PanelNavigationState breadcrumbState = GetBreadcrumbState(panelId);
            Button? overflowButton = GetOverflowBreadcrumbButton(panelId);

            if (breadcrumbState.Breadcrumbs.Count == 0)
            {
                breadcrumbState.HiddenBreadcrumbItems.Clear();
                breadcrumbState.VisibleBreadcrumbs.Clear();
                RaiseBreadcrumbPresentationChanged(panelId);
                if (overflowButton is not null)
                {
                    overflowButton.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (!breadcrumbState.BreadcrumbWidthsReady)
            {
                return;
            }

            int visibleStartIndex = CalculateVisibleBreadcrumbStartIndex(panelId);
            if (visibleStartIndex == breadcrumbState.BreadcrumbVisibleStartIndex)
            {
                RaiseBreadcrumbPresentationChanged(panelId);
                if (overflowButton is not null)
                {
                    overflowButton.Visibility = visibleStartIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
                }
                return;
            }

            breadcrumbState.BreadcrumbVisibleStartIndex = visibleStartIndex;
            breadcrumbState.HiddenBreadcrumbItems.Clear();
            for (int i = 0; i < visibleStartIndex; i++)
            {
                breadcrumbState.HiddenBreadcrumbItems.Add(breadcrumbState.Breadcrumbs[i]);
            }

            var visibleItems = new List<BreadcrumbItemViewModel>();
            for (int i = visibleStartIndex; i < breadcrumbState.Breadcrumbs.Count; i++)
            {
                visibleItems.Add(breadcrumbState.Breadcrumbs[i]);
            }

            ResetVisibleBreadcrumbs(panelId, visibleItems);

            if (overflowButton is not null)
            {
                overflowButton.Visibility = visibleStartIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            RaiseBreadcrumbPresentationChanged(panelId);
        }

        private int CalculateVisibleBreadcrumbStartIndex(WorkspacePanelId panelId)
        {
            PanelNavigationState breadcrumbState = GetBreadcrumbState(panelId);
            Border? border = GetBreadcrumbBorder(panelId);
            double availableWidth = border?.ActualWidth ?? 0;
            if (border is null || availableWidth <= 0)
            {
                return 0;
            }

            Thickness padding = border.Padding;
            Thickness borderThickness = border.BorderThickness;
            availableWidth -= padding.Left + padding.Right + borderThickness.Left + borderThickness.Right + BreadcrumbWidthReserve;

            double usedWidth = 0;

            for (int i = breadcrumbState.Breadcrumbs.Count - 1; i >= 0; i--)
            {
                double itemWidth = breadcrumbState.Breadcrumbs[i].MeasuredWidth;
                double requiredWidth = usedWidth + itemWidth;
                if (i < breadcrumbState.Breadcrumbs.Count - 1)
                {
                    requiredWidth += BreadcrumbItemSpacing;
                }

                if (i > 0)
                {
                    requiredWidth += BreadcrumbOverflowButtonWidth;
                }

                if (i == breadcrumbState.Breadcrumbs.Count - 1 || requiredWidth <= availableWidth)
                {
                    usedWidth += itemWidth;
                    if (i < breadcrumbState.Breadcrumbs.Count - 1)
                    {
                        usedWidth += BreadcrumbItemSpacing;
                    }
                    continue;
                }

                return i + 1;
            }

            return 0;
        }

        private void ResetVisibleBreadcrumbs(WorkspacePanelId panelId, IEnumerable<BreadcrumbItemViewModel> items)
        {
            ObservableCollection<BreadcrumbItemViewModel> visibleBreadcrumbs = GetBreadcrumbState(panelId).VisibleBreadcrumbs;
            visibleBreadcrumbs.Clear();
            foreach (BreadcrumbItemViewModel item in items)
            {
                visibleBreadcrumbs.Add(item);
            }
            RaiseBreadcrumbPresentationChanged(panelId);
        }

        private void MeasureBreadcrumbItem_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement, WorkspacePanelId.Primary);
        }

        private void MeasureBreadcrumbItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement, WorkspacePanelId.Primary);
        }

        private void SecondaryMeasureBreadcrumbItem_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement, WorkspacePanelId.Secondary);
        }

        private void SecondaryMeasureBreadcrumbItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement, WorkspacePanelId.Secondary);
        }

        private void UpdateMeasuredBreadcrumbWidth(FrameworkElement? element, WorkspacePanelId panelId)
        {
            if (element?.DataContext is not BreadcrumbItemViewModel item || element.ActualWidth <= 0)
            {
                return;
            }

            item.MeasuredWidth = element.ActualWidth;
            PanelNavigationState breadcrumbState = GetBreadcrumbState(panelId);
            if (!breadcrumbState.BreadcrumbWidthsReady && AreAllBreadcrumbWidthsMeasured(panelId))
            {
                breadcrumbState.BreadcrumbWidthsReady = true;
                UpdateVisibleBreadcrumbs(panelId);
            }
        }

        private bool AreAllBreadcrumbWidthsMeasured(WorkspacePanelId panelId)
        {
            ObservableCollection<BreadcrumbItemViewModel> breadcrumbs = GetBreadcrumbState(panelId).Breadcrumbs;
            for (int i = 0; i < breadcrumbs.Count; i++)
            {
                if (breadcrumbs[i].MeasuredWidth <= 0)
                {
                    return false;
                }
            }

            return breadcrumbs.Count > 0;
        }

        private PanelNavigationState GetBreadcrumbState(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary
                ? SecondaryPanelState.Navigation
                : PrimaryPanelNavigation;
        }

        private Border? GetBreadcrumbBorder(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary
                ? SecondaryAddressBreadcrumbBorder
                : AddressBreadcrumbBorder;
        }

        private Button? GetOverflowBreadcrumbButton(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary
                ? SecondaryOverflowBreadcrumbButton
                : OverflowBreadcrumbButton;
        }

        private void RaiseBreadcrumbPresentationChanged(WorkspacePanelId panelId)
        {
            RaisePaneBreadcrumbPresentationChanged(panelId);
        }

        private readonly record struct BreadcrumbNavigationTarget(WorkspacePanelId PanelId, string Path);
    }
}
