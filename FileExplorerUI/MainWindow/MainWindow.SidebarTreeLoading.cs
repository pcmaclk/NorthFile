using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
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

            _sidebarTreeContextFlyout = new MenuFlyout();
            var renameItem = new MenuFlyoutItem { Text = S("CommonRename") };
            renameItem.Click += SidebarTreeContextRename_Click;
            _sidebarTreeContextFlyout.Items.Add(renameItem);
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

            await SelectSidebarTreePathAsync(_currentPath);
        }

        private async Task SelectSidebarTreePathAsync(string path)
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
                        _suppressSidebarTreeSelection = true;
                        _sidebarTreeView.SelectedNode = node;
                        _suppressSidebarTreeSelection = false;
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
                _suppressSidebarTreeSelection = true;
                _sidebarTreeView.SelectedNode = current;
                _suppressSidebarTreeSelection = false;
                return;
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
                        break;
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
                        break;
                    }

                    if (current.HasUnrealizedChildren && current.IsExpanded)
                    {
                        await PopulateSidebarTreeChildrenAsync(current, walk, CancellationToken.None);
                    }
                }
            }

            _suppressSidebarTreeSelection = true;
            _sidebarTreeView.SelectedNode = current;
            _suppressSidebarTreeSelection = false;
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
