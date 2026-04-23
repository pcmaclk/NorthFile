using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
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
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is FrameworkElement element)
                {
                    if (element.DataContext is TreeViewNode nodeFromElementDataContext)
                    {
                        return nodeFromElementDataContext;
                    }

                    if (element.DataContext is SidebarTreeEntry entryFromElementDataContext)
                    {
                        TreeViewNode? resolved = FindSidebarTreeNodeByPath(entryFromElementDataContext.FullPath);
                        if (resolved is not null)
                        {
                            return resolved;
                        }
                    }
                }

                current = VisualTreeHelper.GetParent(current);
            }

            TreeViewItem? item = FindAncestor<TreeViewItem>(source);
            if (item?.DataContext is TreeViewNode nodeFromDataContext)
            {
                return nodeFromDataContext;
            }

            if (item?.DataContext is SidebarTreeEntry entryFromDataContext)
            {
                return FindSidebarTreeNodeByPath(entryFromDataContext.FullPath);
            }

            if (item?.Content is TreeViewNode nodeFromContent)
            {
                return nodeFromContent;
            }

            if (item?.Content is SidebarTreeEntry entryFromContent)
            {
                return FindSidebarTreeNodeByPath(entryFromContent.FullPath);
            }

            return null;
        }

        private TreeViewNode? FindSidebarTreeNodeFromPoint(Point point)
        {
            if (_sidebarTreeView is null)
            {
                return null;
            }

            foreach (TreeViewItem item in FindDescendants<TreeViewItem>(_sidebarTreeView))
            {
                TreeViewNode? node = item.DataContext as TreeViewNode ?? item.Content as TreeViewNode;
                if (node is null)
                {
                    SidebarTreeEntry? entry = item.DataContext as SidebarTreeEntry ?? item.Content as SidebarTreeEntry;
                    if (entry is not null)
                    {
                        node = FindSidebarTreeNodeByPath(entry.FullPath);
                    }
                }

                if (node is null || item.ActualWidth <= 0 || item.ActualHeight <= 0)
                {
                    continue;
                }

                try
                {
                    GeneralTransform transform = item.TransformToVisual(_sidebarTreeView);
                    Rect bounds = transform.TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                    if (bounds.Contains(point))
                    {
                        return node;
                    }
                }
                catch
                {
                    // Ignore transient visual-tree failures and keep probing realized items.
                }
            }

            return null;
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
            WorkspacePanelId activePanelId = GetSidebarNavigationTargetPanelId();
            TreeViewNode? selected = _sidebarTreeView.SelectedNode;
            if (selected is not null &&
                !ReferenceEquals(selected, args.Node) &&
                IsTreeNodeDescendant(args.Node, selected) &&
                selected.Content is SidebarTreeEntry selectedEntry)
            {
                _sidebarTreeSelectionMemory[entry.FullPath] = selectedEntry.FullPath;
                shouldSelectCollapsedNode = true;
            }
            else if (IsPathWithin(GetPanelCurrentPath(activePanelId), entry.FullPath))
            {
                _sidebarTreeSelectionMemory[entry.FullPath] = GetPanelCurrentPath(activePanelId);
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
            if (_sidebarTreeView is null)
            {
                return;
            }

            _pendingSidebarTreeContextEntry = null;
            _activeSidebarTreeContextNode = null;

            TreeViewNode? node = FindSidebarTreeNodeFromSource(e.OriginalSource as DependencyObject);
            bool hasPosition = e.TryGetPosition(_sidebarTreeView, out Point point);
            if (node is null && hasPosition)
            {
                node = FindSidebarTreeNodeFromPoint(point);
            }
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
                e.Handled = true;
                return;
            }

            _pendingSidebarTreeContextEntry = entry;
            _activeSidebarTreeContextNode = node;

            FocusSidebarSurface();
            ClearPanelSelection(GetSidebarNavigationTargetPanelId(), clearAnchor: true);

            var contextEntry = new EntryViewModel
            {
                Name = entry.Name,
                DisplayName = entry.Name,
                PendingName = entry.Name,
                FullPath = entry.FullPath,
                Type = GetEntryTypeText(entry.Name, isDirectory: true, isLink: false),
                IconGlyph = entry.IconGlyph,
                IconForeground = GetEntryIconBrush(isDirectory: true, isLink: false, entry.Name),
                SizeText = string.Empty,
                ModifiedText = string.Empty,
                IsDirectory = true,
                IsLink = false,
                IsLoaded = true,
                IsMetadataLoaded = true
            };

            if (hasPosition)
            {
                ShowEntriesContextFlyout(new EntriesContextRequest(
                    _sidebarTreeView,
                    point,
                    contextEntry,
                    IsItemTarget: true,
                    Origin: EntriesContextOrigin.SidebarTree));
            }
            else
            {
                ShowEntriesContextFlyout(new EntriesContextRequest(
                    _sidebarTreeView,
                    new Point(0, 0),
                    contextEntry,
                    IsItemTarget: true,
                    Origin: EntriesContextOrigin.SidebarTree));
            }

            e.Handled = true;
        }

        private async Task NavigateSidebarTreeEntryAsync(SidebarTreeEntry entry)
        {
            WorkspacePanelId targetPanelId = GetSidebarNavigationTargetPanelId();
            FocusSidebarSurface();
            ClearPanelSelection(targetPanelId, clearAnchor: true);

            if (string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                await NavigatePanelToPathAsync(
                    targetPanelId,
                    ShellMyComputerPath,
                    pushHistory: true,
                    focusEntriesAfterNavigation: false);
                return;
            }

            Debug.WriteLine($"[Tree] Selected: {entry.FullPath}");
            if (GetPanelIsLoading(targetPanelId))
            {
                UpdateStatusKey("StatusSidebarTreeNavIgnoredLoading");
                return;
            }

            if (IsExactCurrentPath(entry.FullPath, targetPanelId))
            {
                return;
            }

            try
            {
                Debug.WriteLine($"[Tree] Navigate: {entry.FullPath}");
                await NavigatePanelToPathAsync(
                    targetPanelId,
                    entry.FullPath,
                    pushHistory: true,
                    focusEntriesAfterNavigation: false);
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
