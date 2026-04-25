using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using NorthFileUI.Workspace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private void EnsureSidebarTreeView()
        {
            if (_sidebarTreeView is not null)
            {
                return;
            }

            _sidebarTreeView = new TreeView
            {
                ItemTemplate = SidebarTreeItemTemplate,
                SelectionMode = TreeViewSelectionMode.Single,
                CanDragItems = false,
                CanReorderItems = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                UseSystemFocusVisuals = false
            };
            _sidebarTreeView.SelectionChanged += SidebarTree_SelectionChanged;
            _sidebarTreeView.Expanding += SidebarTree_Expanding;
            _sidebarTreeView.Collapsed += SidebarTree_Collapsed;
            _sidebarTreeView.ItemInvoked += SidebarTree_ItemInvoked;
            _sidebarTreeView.ContextRequested += SidebarTree_ContextRequested;
            StyledSidebarView.AttachTreeView(_sidebarTreeView);
        }

        private async Task PopulateSidebarTreeRootsAsync()
        {
            EnsureSidebarTreeView();
            if (_sidebarTreeView is null)
            {
                return;
            }

            _sidebarTreeCts?.Cancel();
            var cts = new CancellationTokenSource();
            _sidebarTreeCts = cts;

            _sidebarTreeView.RootNodes.Clear();
            var computerEntry = new SidebarTreeEntry(S("SidebarMyComputer"), "shell:mycomputer", "\uE7F4");
            var computerNode = CreateSidebarTreeNode(computerEntry, hasUnrealizedChildren: false);
            computerNode.IsExpanded = true;
            _sidebarTreeView.RootNodes.Add(computerNode);

            foreach (DriveInfo drive in _explorerService.GetReadyDrives())
            {
                string root = drive.RootDirectory.FullName;
                string label = drive.Name.TrimEnd('\\');
                var rootEntry = new SidebarTreeEntry(label, root);
                computerNode.Children.Add(CreateSidebarTreeNode(rootEntry, hasUnrealizedChildren: _explorerService.DirectoryHasChildDirectories(root)));
            }

            await SelectSidebarTreePathAsync(GetPanelCurrentPath(GetSidebarNavigationTargetPanelId()));
        }

        private async Task SelectSidebarTreePathAsync(string path)
        {
            await SelectSidebarTreePathAsync(path, _appSettings.ExpandSidebarTreeToCurrentPath);
        }

        private async Task SelectSidebarTreePathAsync(string path, bool expandToTarget)
        {
            if (_sidebarTreeView is null)
            {
                return;
            }

            string target = string.IsNullOrWhiteSpace(path) ? @"C:\" : path.Trim();
            if (string.Equals(target, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                foreach (TreeViewNode node in _sidebarTreeView.RootNodes)
                {
                    if (node.Content is SidebarTreeEntry entry &&
                        string.Equals(entry.FullPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                    {
                        SelectSidebarTreeNode(node);
                        if (expandToTarget)
                        {
                            int requestVersion = ++_sidebarTreeScrollRequestVersion;
                            await BringSidebarTreeNodeIntoViewAsync(node, requestVersion);
                        }
                        return;
                    }
                }

                return;
            }

            string root = Path.GetPathRoot(target) ?? string.Empty;
            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            TreeViewNode? current = null;
            TreeViewNode? computer = null;
            foreach (TreeViewNode node in _sidebarTreeView.RootNodes)
            {
                if (node.Content is SidebarTreeEntry entry &&
                    string.Equals(entry.FullPath, "shell:mycomputer", StringComparison.OrdinalIgnoreCase))
                {
                    computer = node;
                    break;
                }
            }

            if (computer is null)
            {
                return;
            }

            current = computer;
            if (!computer.IsExpanded)
            {
                if (expandToTarget)
                {
                    computer.IsExpanded = true;
                }
                else
                {
                    SelectSidebarTreeNode(current);
                    return;
                }
            }

            foreach (TreeViewNode node in computer.Children)
            {
                if (node.Content is SidebarTreeEntry entry &&
                    string.Equals(entry.FullPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    current = node;
                    break;
                }
            }

            if (current is null)
            {
                return;
            }

            string relative = target.Substring(root.Length).Trim('\\');
            if (!string.IsNullOrEmpty(relative))
            {
                string[] parts = relative.Split('\\', StringSplitOptions.RemoveEmptyEntries);
                string walk = root;
                foreach (string part in parts)
                {
                    if (!ReferenceEquals(current, computer) && !current.IsExpanded)
                    {
                        if (!expandToTarget)
                        {
                            break;
                        }

                        current.IsExpanded = true;
                    }

                    walk = Path.Combine(walk, part);
                    TreeViewNode? next = null;
                    foreach (TreeViewNode child in current.Children)
                    {
                        if (child.Content is SidebarTreeEntry childEntry &&
                            string.Equals(childEntry.FullPath, walk, StringComparison.OrdinalIgnoreCase))
                        {
                            next = child;
                            break;
                        }
                    }

                    if (next is null)
                    {
                        if (current.HasUnrealizedChildren && current.IsExpanded)
                        {
                            if (current.Content is SidebarTreeEntry currentEntry)
                            {
                                await PopulateSidebarTreeChildrenAsync(current, currentEntry.FullPath, CancellationToken.None);
                            }
                        }
                        foreach (TreeViewNode child in current.Children)
                        {
                            if (child.Content is SidebarTreeEntry childEntry &&
                                string.Equals(childEntry.FullPath, walk, StringComparison.OrdinalIgnoreCase))
                            {
                                next = child;
                                break;
                            }
                        }
                    }

                    if (next is null)
                    {
                        break;
                    }

                    current = next;

                    if (!string.Equals(walk, target, StringComparison.OrdinalIgnoreCase) && !current.IsExpanded)
                    {
                        if (!expandToTarget)
                        {
                            break;
                        }

                        current.IsExpanded = true;
                    }

                    if (current.HasUnrealizedChildren && current.IsExpanded)
                    {
                        await PopulateSidebarTreeChildrenAsync(current, walk, CancellationToken.None);
                    }
                }
            }

            SelectSidebarTreeNode(current);
            if (expandToTarget)
            {
                int finalRequestVersion = ++_sidebarTreeScrollRequestVersion;
                await BringSidebarTreeNodeIntoViewAsync(current, finalRequestVersion);
            }
        }

        private void SelectSidebarTreeNode(TreeViewNode node)
        {
            if (_sidebarTreeView is null)
            {
                return;
            }

            _suppressSidebarTreeSelection = true;
            _sidebarTreeView.SelectedNode = node;
            _suppressSidebarTreeSelection = false;
        }

        private async Task BringSidebarTreeNodeIntoViewAsync(TreeViewNode node, int requestVersion)
        {
            if (_sidebarTreeView is null)
            {
                return;
            }

            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (requestVersion != _sidebarTreeScrollRequestVersion)
                {
                    return;
                }

                _sidebarTreeView.UpdateLayout();
                TreeViewItem? item = FindTreeViewItemForNode(node);
                if (item is not null)
                {
                    ScrollViewer? treeScrollViewer = ResolveSidebarTreeScrollViewer(item);
                    const double edgeTolerance = 0.5;
                    bool wasAboveViewport = false;
                    bool wasBelowViewport = false;
                    double rowHeight = item.ActualHeight;
                    string nodePath = (node.Content as SidebarTreeEntry)?.FullPath ?? "<unknown>";

                    if (treeScrollViewer is not null &&
                        item.ActualHeight > 0 &&
                        item.ActualWidth > 0)
                    {
                        Rect boundsInViewport = item.TransformToVisual(treeScrollViewer)
                            .TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                        rowHeight = boundsInViewport.Height > 0 ? boundsInViewport.Height : item.ActualHeight;
                        wasAboveViewport = boundsInViewport.Top < -edgeTolerance;
                        wasBelowViewport = boundsInViewport.Bottom > treeScrollViewer.ViewportHeight + edgeTolerance;
                    }

                    if (wasAboveViewport)
                    {
                        item.StartBringIntoView(new BringIntoViewOptions
                        {
                            VerticalAlignmentRatio = 0,
                            VerticalOffset = rowHeight,
                            AnimationDesired = false
                        });
                        TraceTreeScroll($"phase=bring path=\"{nodePath}\" mode=top offset={rowHeight:F2}");
                    }
                    else if (wasBelowViewport)
                    {
                        item.StartBringIntoView(new BringIntoViewOptions
                        {
                            VerticalAlignmentRatio = 1,
                            VerticalOffset = -rowHeight,
                            AnimationDesired = false
                        });
                        TraceTreeScroll($"phase=bring path=\"{nodePath}\" mode=bottom offset={-rowHeight:F2}");
                    }
                    else
                    {
                        return;
                    }

                    await Task.Delay(16);
                    _sidebarTreeView.UpdateLayout();
                    if (requestVersion != _sidebarTreeScrollRequestVersion)
                    {
                        return;
                    }

                    treeScrollViewer = ResolveSidebarTreeScrollViewer(item);
                    if (treeScrollViewer is not null &&
                        item.ActualHeight > 0 &&
                        item.ActualWidth > 0)
                    {
                        // no-op: keep post-bring validation silent in normal flow.
                    }
                    else
                    {
                        TraceTreeScroll($"phase=post-bring path=\"{nodePath}\" skipped reason=viewer-null-or-item-size");
                    }

                    return;
                }

                await Task.Delay(16);
            }
        }

        private ScrollViewer? ResolveSidebarTreeScrollViewer(DependencyObject item)
        {
            DependencyObject? current = _sidebarTreeView;
            ScrollViewer? fallback = null;
            while (current is not null)
            {
                if (current is ScrollViewer viewer)
                {
                    fallback ??= viewer;
                    if (viewer.ScrollableHeight > 0 ||
                        viewer.VerticalScrollMode != ScrollMode.Disabled)
                    {
                        return viewer;
                    }
                }

                current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
            }

            if (fallback is not null)
            {
                return fallback;
            }

            ScrollViewer? directAncestor = FindAncestor<ScrollViewer>(item);
            if (directAncestor is not null)
            {
                return directAncestor;
            }

            return FindDescendant<ScrollViewer>(_sidebarTreeView!)
                ?? FindDescendant<ScrollViewer>(StyledSidebarView);
        }

        private static void TraceTreeScroll(string message)
        {
            AppendNavigationPerfLog($"[TREE-SCROLL] {message}");
        }

        private static TreeViewNode CreateSidebarTreeNode(SidebarTreeEntry entry, bool hasUnrealizedChildren)
        {
            var node = new TreeViewNode
            {
                Content = entry,
                HasUnrealizedChildren = hasUnrealizedChildren
            };
            return node;
        }

        private static DataTemplate CreateSidebarTreeItemTemplate()
        {
            const string xaml =
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Grid>" +
                "<StackPanel VerticalAlignment=\"Center\" Orientation=\"Horizontal\" Spacing=\"6\">" +
                "<FontIcon VerticalAlignment=\"Center\" FontFamily=\"Segoe Fluent Icons\" FontSize=\"12\" Glyph=\"{Binding Content.IconGlyph}\" />" +
                "<TextBlock x:Name=\"SidebarTreeNameTextBlock\" VerticalAlignment=\"Center\" Text=\"{Binding Content.Name}\" TextTrimming=\"CharacterEllipsis\" />" +
                "</StackPanel>" +
                "</Grid>" +
                "</DataTemplate>";

            return (DataTemplate)XamlReader.Load(xaml);
        }

        private async Task PopulateSidebarTreeChildrenAsync(TreeViewNode node, string path, CancellationToken ct, bool expandAfterLoad = false)
        {
            List<SidebarTreeEntry> children = await _explorerService.EnumerateSidebarDirectoriesAsync(path, ct, SidebarTreeMaxChildren);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            node.Children.Clear();
            foreach (SidebarTreeEntry child in children)
            {
                node.Children.Add(CreateSidebarTreeNode(child, hasUnrealizedChildren: _explorerService.DirectoryHasChildDirectories(child.FullPath)));
            }
            node.HasUnrealizedChildren = false;
            if (expandAfterLoad)
            {
                node.IsExpanded = true;
            }
        }

        private async Task RefreshSidebarTreeNodeChildrenIfExpandedAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || _sidebarTreeView is null)
            {
                return;
            }

            TreeViewNode? node = FindSidebarTreeNodeByPath(path);
            if (node is null || !node.IsExpanded)
            {
                return;
            }

            await PopulateSidebarTreeChildrenAsync(node, path, CancellationToken.None, expandAfterLoad: true);
        }

        private TreeViewNode? FindSidebarTreeNodeByPath(string path)
        {
            if (_sidebarTreeView is null)
            {
                return null;
            }

            foreach (TreeViewNode root in _sidebarTreeView.RootNodes)
            {
                TreeViewNode? match = FindSidebarTreeNodeByPath(root, path);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private static TreeViewNode? FindSidebarTreeNodeByPath(TreeViewNode node, string path)
        {
            if (node.Content is SidebarTreeEntry entry &&
                string.Equals(entry.FullPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            foreach (TreeViewNode child in node.Children)
            {
                TreeViewNode? match = FindSidebarTreeNodeByPath(child, path);
                if (match is not null)
                {
                    return match;
                }
            }

            return null;
        }

        private async Task RestoreSidebarTreeSelectionAsync(TreeViewNode node)
        {
            if (node.Content is not SidebarTreeEntry entry)
            {
                return;
            }

            if (!_sidebarTreeSelectionMemory.TryGetValue(entry.FullPath, out string? target))
            {
                return;
            }

            _sidebarTreeSelectionMemory.Remove(entry.FullPath);
            await SelectSidebarTreePathAsync(target);
        }

        private static bool IsTreeNodeDescendant(TreeViewNode ancestor, TreeViewNode node)
        {
            TreeViewNode? current = node;
            while (current is not null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }
                current = current.Parent;
            }
            return false;
        }
    }
}
