using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void BuildSidebarItems()
        {
            if (SidebarNavView is null)
            {
                return;
            }

            SidebarNavView.MenuItems.Clear();
            _sidebarPathButtons.Clear();
            _sidebarQuickAccessPaths.Clear();
            _sidebarDrivePaths.Clear();

            NavigationViewItem CreateLeaf(string label, string path, Symbol icon, bool inQuickAccess, bool inDrives)
            {
                var item = new NavigationViewItem
                {
                    Content = label,
                    Tag = path,
                    Icon = new SymbolIcon(icon)
                };
                _sidebarPathButtons[path] = item;
                if (inQuickAccess)
                {
                    _sidebarQuickAccessPaths.Add(path);
                }
                if (inDrives)
                {
                    _sidebarDrivePaths.Add(path);
                }
                return item;
            }

            var quickAccess = new NavigationViewItem
            {
                Content = S("SidebarPinned"),
                Icon = new SymbolIcon(Symbol.Favorite),
                SelectsOnInvoked = false
            };
            foreach (SidebarNavItemModel favorite in BuildSidebarFavoriteModels())
            {
                if (string.IsNullOrWhiteSpace(favorite.Path))
                {
                    continue;
                }

                quickAccess.MenuItems.Add(CreateLeaf(favorite.Label, favorite.Path, Symbol.Folder, true, false));
            }
            SidebarNavView.MenuItems.Add(quickAccess);

            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = S("SidebarCloud"), Icon = new SymbolIcon(Symbol.World), SelectsOnInvoked = false });
            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = S("SidebarNetwork"), Icon = new SymbolIcon(Symbol.Globe), SelectsOnInvoked = false });
            SidebarNavView.MenuItems.Add(new NavigationViewItem { Content = S("SidebarTags"), Icon = new SymbolIcon(Symbol.Tag), SelectsOnInvoked = false });

            StyledSidebarView.SetPinnedItems(BuildSidebarFavoriteModels());
            StyledSidebarView.SetExtraItems(new[]
            {
                new SidebarNavItemModel("cloud", S("SidebarCloud"), null, "\uE753", selectable: false),
                new SidebarNavItemModel("network", S("SidebarNetwork"), null, "\uE774", selectable: false),
                new SidebarNavItemModel("tags", S("SidebarTags"), null, "\uE8EC", selectable: false)
            });

            EnsureSidebarTreeView();
            if (_sidebarTreeView is not null)
            {
                StyledSidebarView.AttachTreeView(_sidebarTreeView);
                _ = PopulateSidebarTreeRootsAsync();
            }

            StyledSidebarView.SetSectionVisibility(
                _appSettings.ShowFavorites,
                _appSettings.ShowCloud,
                _appSettings.ShowNetwork,
                _appSettings.ShowTags);
            ApplySidebarCompactState(_isSidebarCompact);
            UpdateSidebarSelectionOnly();
        }


        private static void UpdateSidebarTreeNodePath(TreeViewNode node, string oldPath, string newPath, string? replacementName = null)
        {
            if (node.Content is SidebarTreeEntry entry)
            {
                string updatedPath = entry.FullPath;
                if (string.Equals(entry.FullPath, oldPath, StringComparison.OrdinalIgnoreCase))
                {
                    updatedPath = newPath;
                }
                else if (entry.FullPath.StartsWith(oldPath + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    updatedPath = newPath + entry.FullPath[oldPath.Length..];
                }

                if (!string.Equals(updatedPath, entry.FullPath, StringComparison.OrdinalIgnoreCase) || replacementName is not null)
                {
                    node.Content = new SidebarTreeEntry(replacementName ?? entry.Name, updatedPath, entry.IconGlyph);
                }
            }

            foreach (TreeViewNode child in node.Children)
            {
                UpdateSidebarTreeNodePath(child, oldPath, newPath);
            }
        }

        private TreeViewNode? FindSidebarTreeNodeFromSource(DependencyObject? source)
        {
            TreeViewItem? item = FindAncestor<TreeViewItem>(source);
            if (item?.DataContext is TreeViewNode nodeFromDataContext)
            {
                return nodeFromDataContext;
            }

            if (item?.Content is TreeViewNode nodeFromContent)
            {
                return nodeFromContent;
            }

            return _sidebarTreeView?.SelectedNode;
        }

        private void SidebarTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
        {
            if (_suppressSidebarTreeSelection)
            {
                _suppressSidebarTreeSelection = false;
                return;
            }
        }

        private async void SidebarTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (args.Node.Content is not SidebarTreeEntry entry)
            {
                return;
            }

            try
            {
                if (args.Node.HasUnrealizedChildren)
                {
                    await PopulateSidebarTreeChildrenAsync(args.Node, entry.FullPath, CancellationToken.None, expandAfterLoad: true);
                }
                await RestoreSidebarTreeSelectionAsync(args.Node);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarTreeExpandFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void SidebarTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
        {
            if (_sidebarTreeView is null || args.Node.Content is not SidebarTreeEntry entry)
            {
                return;
            }

            bool shouldSelectCollapsedNode = false;
            TreeViewNode? selected = _sidebarTreeView.SelectedNode;
            if (selected is not null &&
                !ReferenceEquals(selected, args.Node) &&
                IsTreeNodeDescendant(args.Node, selected) &&
                selected.Content is SidebarTreeEntry selectedEntry)
            {
                _sidebarTreeSelectionMemory[entry.FullPath] = selectedEntry.FullPath;
                shouldSelectCollapsedNode = true;
            }
            else if (IsPathWithin(_currentPath, entry.FullPath))
            {
                _sidebarTreeSelectionMemory[entry.FullPath] = _currentPath;
                shouldSelectCollapsedNode = true;
            }

            if (shouldSelectCollapsedNode)
            {
                _suppressSidebarTreeSelection = true;
                _sidebarTreeView.SelectedNode = args.Node;
                _suppressSidebarTreeSelection = false;
            }
        }

        private async void SidebarTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            SidebarTreeEntry? entry = args.InvokedItem switch
            {
                SidebarTreeEntry direct => direct,
                TreeViewNode node when node.Content is SidebarTreeEntry nested => nested,
                _ => sender.SelectedNode?.Content as SidebarTreeEntry
            };

            if (entry is null)
            {
                return;
            }

            await NavigateSidebarTreeEntryAsync(entry);
        }

        private void SidebarTree_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
        {
            if (_sidebarTreeView is null || _sidebarTreeContextFlyout is null)
            {
                return;
            }

            TreeViewNode? node = FindSidebarTreeNodeFromSource(e.OriginalSource as DependencyObject);
            bool isRootPath = false;
            if (node?.Content is SidebarTreeEntry candidate)
            {
                string root = Path.GetPathRoot(candidate.FullPath) ?? string.Empty;
                isRootPath = !string.IsNullOrEmpty(root) &&
                    string.Equals(root.TrimEnd('\\'), candidate.FullPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }

            if (node?.Content is not SidebarTreeEntry entry ||
                string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                isRootPath)
            {
                return;
            }

            _pendingSidebarTreeContextEntry = entry;
            if (!ReferenceEquals(_sidebarTreeView.SelectedNode, node))
            {
                _suppressSidebarTreeSelection = true;
                _sidebarTreeView.SelectedNode = node;
                _suppressSidebarTreeSelection = false;
            }

            if (e.TryGetPosition(_sidebarTreeView, out Point point))
            {
                _sidebarTreeContextFlyout.ShowAt(_sidebarTreeView, new FlyoutShowOptions
                {
                    Position = point
                });
            }
            else
            {
                _sidebarTreeContextFlyout.ShowAt(_sidebarTreeView);
            }

            e.Handled = true;
        }

        private async Task NavigateSidebarTreeEntryAsync(SidebarTreeEntry entry)
        {
            FocusSidebarSurface();
            ClearListSelectionAndAnchor();

            if (string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                _currentPath = ShellMyComputerPath;
                PathTextBox.Text = GetDisplayPathText(ShellMyComputerPath);
                _currentQuery = string.Empty;
                SearchTextBox.Text = string.Empty;
                UpdateBreadcrumbs(_currentPath);
                UpdateNavButtonsState();
                _ = SelectSidebarTreePathAsync(_currentPath);
                await LoadFirstPageAsync();
                return;
            }

            Debug.WriteLine($"[Tree] Selected: {entry.FullPath}");
            if (_isLoading)
            {
                UpdateStatusKey("StatusSidebarTreeNavIgnoredLoading");
                return;
            }

            if (IsExactCurrentPath(entry.FullPath))
            {
                return;
            }

            try
            {
                Debug.WriteLine($"[Tree] Navigate: {entry.FullPath}");
                await NavigateToPathAsync(entry.FullPath, pushHistory: true, focusEntriesAfterNavigation: false);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSidebarTreeNavFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private static bool IsPathWithin(string candidate, string ancestor)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(ancestor))
            {
                return false;
            }

            if (ancestor.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(ancestor, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate, ancestor, StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                string normalizedCandidate = Path.GetFullPath(candidate).TrimEnd('\\');
                string normalizedAncestor = Path.GetFullPath(ancestor).TrimEnd('\\');
                return string.Equals(normalizedCandidate, normalizedAncestor, StringComparison.OrdinalIgnoreCase)
                    || normalizedCandidate.StartsWith(normalizedAncestor + "\\", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

    }
}
