using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void BreadcrumbButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string target)
            {
                return;
            }

            await NavigateToPathAsync(target, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private void BreadcrumbChevronButton_Click(object sender, RoutedEventArgs e)
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
                            Tag = drive.Name
                        });
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
                            Tag = dir
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

            foreach (BreadcrumbItemViewModel item in _hiddenBreadcrumbItems)
            {
                var menuItem = new MenuFlyoutItem
                {
                    Text = item.Label,
                    Tag = item.FullPath
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
            if (sender is not MenuFlyoutItem item || item.Tag is not string target)
            {
                return;
            }

            await NavigateToPathAsync(target, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private async void OverflowBreadcrumbMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not string target)
            {
                return;
            }

            await NavigateToPathAsync(target, pushHistory: true);
            ExitAddressEditMode(commit: true);
        }

        private void UpdateBreadcrumbs(string path)
        {
            Breadcrumbs.Clear();
            _breadcrumbWidthsReady = false;
            _breadcrumbVisibleStartIndex = -1;
            _hiddenBreadcrumbItems.Clear();
            VisibleBreadcrumbs.Clear();
            if (BreadcrumbItemsControl is not null)
            {
                BreadcrumbItemsControl.Opacity = 0;
            }
            if (OverflowBreadcrumbButton is not null)
            {
                OverflowBreadcrumbButton.Visibility = Visibility.Collapsed;
            }

            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                Breadcrumbs.Add(new BreadcrumbItemViewModel
                {
                    Label = S("SidebarMyComputer"),
                    FullPath = ShellMyComputerPath,
                    HasChildren = false,
                    IconGlyph = BreadcrumbMyComputerGlyph,
                    ChevronVisibility = Visibility.Collapsed,
                    IsLast = true,
                    MeasuredWidth = 0
                });
                return;
            }

            string fullPath = Path.GetFullPath(path);
            string root = Path.GetPathRoot(fullPath) ?? string.Empty;

            Breadcrumbs.Add(new BreadcrumbItemViewModel
            {
                Label = S("SidebarMyComputer"),
                FullPath = ShellMyComputerPath,
                HasChildren = HasBreadcrumbChildren(ShellMyComputerPath),
                IconGlyph = BreadcrumbMyComputerGlyph,
                ChevronVisibility = Visibility.Collapsed
            });

            if (!string.IsNullOrEmpty(root))
            {
                Breadcrumbs.Add(new BreadcrumbItemViewModel
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
                    Breadcrumbs.Add(new BreadcrumbItemViewModel
                    {
                        Label = part,
                        FullPath = current,
                        HasChildren = HasBreadcrumbChildren(current),
                        ChevronVisibility = Visibility.Collapsed
                    });
                }
            }

            for (int i = 0; i < Breadcrumbs.Count; i++)
            {
                BreadcrumbItemViewModel item = Breadcrumbs[i];
                item.IsLast = i == Breadcrumbs.Count - 1;
                item.ChevronVisibility = item.HasChildren && !item.IsLast
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                item.MeasuredWidth = 0;
            }
        }

        private void AddressBreadcrumbBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateVisibleBreadcrumbs();
        }

        private void UpdateVisibleBreadcrumbs()
        {
            if (Breadcrumbs.Count == 0)
            {
                _hiddenBreadcrumbItems.Clear();
                VisibleBreadcrumbs.Clear();
                if (OverflowBreadcrumbButton is not null)
                {
                    OverflowBreadcrumbButton.Visibility = Visibility.Collapsed;
                }
                return;
            }

            if (!_breadcrumbWidthsReady)
            {
                return;
            }

            int visibleStartIndex = CalculateVisibleBreadcrumbStartIndex();
            if (visibleStartIndex == _breadcrumbVisibleStartIndex)
            {
                return;
            }

            _breadcrumbVisibleStartIndex = visibleStartIndex;
            _hiddenBreadcrumbItems.Clear();
            for (int i = 0; i < visibleStartIndex; i++)
            {
                _hiddenBreadcrumbItems.Add(Breadcrumbs[i]);
            }

            var visibleItems = new List<BreadcrumbItemViewModel>();
            for (int i = visibleStartIndex; i < Breadcrumbs.Count; i++)
            {
                visibleItems.Add(Breadcrumbs[i]);
            }

            ResetVisibleBreadcrumbs(visibleItems);

            if (BreadcrumbItemsControl is not null)
            {
                BreadcrumbItemsControl.Opacity = 1;
            }
            if (OverflowBreadcrumbButton is not null)
            {
                OverflowBreadcrumbButton.Visibility = visibleStartIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private int CalculateVisibleBreadcrumbStartIndex()
        {
            Border? border = AddressBreadcrumbBorder;
            double availableWidth = border?.ActualWidth ?? 0;
            if (border is null || availableWidth <= 0)
            {
                return 0;
            }

            Thickness padding = border.Padding;
            Thickness borderThickness = border.BorderThickness;
            availableWidth -= padding.Left + padding.Right + borderThickness.Left + borderThickness.Right + BreadcrumbWidthReserve;

            double usedWidth = 0;

            for (int i = Breadcrumbs.Count - 1; i >= 0; i--)
            {
                double itemWidth = Breadcrumbs[i].MeasuredWidth;
                double requiredWidth = usedWidth + itemWidth;
                if (i < Breadcrumbs.Count - 1)
                {
                    requiredWidth += BreadcrumbItemSpacing;
                }

                if (i > 0)
                {
                    requiredWidth += BreadcrumbOverflowButtonWidth;
                }

                if (i == Breadcrumbs.Count - 1 || requiredWidth <= availableWidth)
                {
                    usedWidth += itemWidth;
                    if (i < Breadcrumbs.Count - 1)
                    {
                        usedWidth += BreadcrumbItemSpacing;
                    }
                    continue;
                }

                return i + 1;
            }

            return 0;
        }

        private void ResetVisibleBreadcrumbs(IEnumerable<BreadcrumbItemViewModel> items)
        {
            VisibleBreadcrumbs.Clear();
            foreach (BreadcrumbItemViewModel item in items)
            {
                VisibleBreadcrumbs.Add(item);
            }
        }

        private void MeasureBreadcrumbItem_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement);
        }

        private void MeasureBreadcrumbItem_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateMeasuredBreadcrumbWidth(sender as FrameworkElement);
        }

        private void UpdateMeasuredBreadcrumbWidth(FrameworkElement? element)
        {
            if (element?.DataContext is not BreadcrumbItemViewModel item || element.ActualWidth <= 0)
            {
                return;
            }

            item.MeasuredWidth = element.ActualWidth;
            if (!_breadcrumbWidthsReady && AreAllBreadcrumbWidthsMeasured())
            {
                _breadcrumbWidthsReady = true;
                UpdateVisibleBreadcrumbs();
            }
        }

        private bool AreAllBreadcrumbWidthsMeasured()
        {
            for (int i = 0; i < Breadcrumbs.Count; i++)
            {
                if (Breadcrumbs[i].MeasuredWidth <= 0)
                {
                    return false;
                }
            }

            return Breadcrumbs.Count > 0;
        }
    }
}
