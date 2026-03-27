using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit)
                {
                    return hit;
                }

                T? nested = FindDescendant<T>(child);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                {
                    yield return typed;
                }

                foreach (T nested in FindDescendants<T>(child))
                {
                    yield return nested;
                }
            }
        }

        private static T? FindDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed && string.Equals(typed.Name, name, StringComparison.Ordinal))
                {
                    return typed;
                }

                T? nested = FindDescendantByName<T>(child, name);
                if (nested is not null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            DependencyObject? current = start;
            while (current is not null)
            {
                if (current is T hit)
                {
                    return hit;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private TreeViewItem? FindTreeViewItemForNode(TreeViewNode targetNode)
        {
            return _sidebarTreeView is null ? null : FindTreeViewItemForNode(_sidebarTreeView, targetNode);
        }

        private static TreeViewItem? FindTreeViewItemForNode(DependencyObject root, TreeViewNode targetNode)
        {
            if (root is TreeViewItem item &&
                (ReferenceEquals(item.DataContext, targetNode) || ReferenceEquals(item.Content, targetNode)))
            {
                return item;
            }

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                TreeViewItem? hit = FindTreeViewItemForNode(child, targetNode);
                if (hit is not null)
                {
                    return hit;
                }
            }

            return null;
        }

        private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
