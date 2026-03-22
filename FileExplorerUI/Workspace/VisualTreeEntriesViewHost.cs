using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using Windows.Foundation;
using FileExplorerUI.Controls;

namespace FileExplorerUI.Workspace;

public sealed class VisualTreeEntriesViewHost : IEntriesViewHost
{
    private readonly FrameworkElement _root;

    public VisualTreeEntriesViewHost(FrameworkElement root)
    {
        _root = root;
    }

    public void SetItems(IReadOnlyList<EntryViewModel> items)
    {
    }

    public void SetSelectedEntry(string? fullPath)
    {
    }

    public EntryViewModel? ResolveDoubleTappedEntry(DoubleTappedRoutedEventArgs e)
    {
        return FindEntryFromSource(e.OriginalSource as DependencyObject);
    }

    public EntriesViewHitResult? ResolveRightTappedHit(RightTappedRoutedEventArgs e)
    {
        if (FindAncestor<EntryGroupHeader>(e.OriginalSource as DependencyObject) is not null)
        {
            return null;
        }

        FrameworkElement? anchor = FindEntryElement(e.OriginalSource as DependencyObject);
        if (anchor?.DataContext is EntryViewModel entry && !entry.IsGroupHeader)
        {
            return new EntriesViewHitResult(anchor, e.GetPosition(anchor), entry, true);
        }

        return new EntriesViewHitResult(_root, e.GetPosition(_root), null, false);
    }

    public EntryViewModel? ResolvePressedEntry(PointerRoutedEventArgs e)
    {
        return FindEntryFromSource(e.OriginalSource as DependencyObject);
    }

    public FrameworkElement? FindEntryContainer(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        foreach (FrameworkElement element in FindDescendants<FrameworkElement>(_root))
        {
            if (element.DataContext is EntryViewModel entry &&
                string.Equals(entry.FullPath, fullPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return element;
            }
        }

        return null;
    }

    public EntryNameCell? FindEntryNameCell(string? fullPath)
    {
        FrameworkElement? container = FindEntryContainer(fullPath);
        return container is null ? null : FindDescendant<EntryNameCell>(container);
    }

    public bool ScrollEntryIntoView(string? fullPath)
    {
        if (_root is not ScrollViewer viewer ||
            string.IsNullOrWhiteSpace(fullPath) ||
            FindEntryContainer(fullPath) is not FrameworkElement element ||
            viewer.Content is not UIElement content)
        {
            return false;
        }

        GeneralTransform transform = element.TransformToVisual(content);
        Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return false;
        }

        bool prefersVertical = viewer.VerticalScrollMode == ScrollMode.Enabled &&
                               viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Hidden;
        if (prefersVertical)
        {
            double top = bounds.Y;
            double bottom = bounds.Y + bounds.Height;
            double viewportTop = viewer.VerticalOffset;
            double viewportBottom = viewportTop + viewer.ViewportHeight;
            if (top >= viewportTop && bottom <= viewportBottom)
            {
                return true;
            }

            double targetOffset = top < viewportTop
                ? System.Math.Max(0, top)
                : System.Math.Max(0, bottom - viewer.ViewportHeight);
            viewer.ChangeView(null, targetOffset, null, disableAnimation: false);
        }
        else
        {
            double left = bounds.X;
            double right = bounds.X + bounds.Width;
            double viewportLeft = viewer.HorizontalOffset;
            double viewportRight = viewportLeft + viewer.ViewportWidth;
            if (left >= viewportLeft && right <= viewportRight)
            {
                return true;
            }

            double targetOffset = left < viewportLeft
                ? System.Math.Max(0, left)
                : System.Math.Max(0, right - viewer.ViewportWidth);
            viewer.ChangeView(targetOffset, null, null, disableAnimation: false);
        }
        return true;
    }

    private static EntryViewModel? FindEntryFromSource(DependencyObject? source)
    {
        FrameworkElement? element = FindEntryElement(source);
        return element?.DataContext as EntryViewModel;
    }

    private static FrameworkElement? FindEntryElement(DependencyObject? start)
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is FrameworkElement element && element.DataContext is EntryViewModel)
            {
                return element;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
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
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (T nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
