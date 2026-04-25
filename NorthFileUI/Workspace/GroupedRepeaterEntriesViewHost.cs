using System;
using System.Collections.Generic;
using NorthFileUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace NorthFileUI.Workspace;

public sealed class GroupedRepeaterEntriesViewHost : IEntriesViewHost
{
    private readonly ScrollViewer _viewer;
    private readonly ItemsRepeater _repeater;
    private readonly GroupedListVirtualizingLayout _layout;
    private IReadOnlyList<EntryViewModel> _items = Array.Empty<EntryViewModel>();

    public GroupedRepeaterEntriesViewHost(
        ScrollViewer viewer,
        ItemsRepeater repeater,
        GroupedListVirtualizingLayout layout)
    {
        _viewer = viewer;
        _repeater = repeater;
        _layout = layout;
    }

    public void SetItems(IReadOnlyList<EntryViewModel> items)
    {
        _items = items ?? Array.Empty<EntryViewModel>();
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

        return new EntriesViewHitResult(_viewer, e.GetPosition(_viewer), null, false);
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

        foreach (FrameworkElement element in FindEntryElements(_repeater))
        {
            if (element.DataContext is EntryViewModel entry &&
                string.Equals(entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
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
        int index = FindIndex(fullPath);
        if (index < 0)
        {
            return false;
        }

        if (FindEntryContainer(fullPath) is FrameworkElement element &&
            _viewer.Content is UIElement content)
        {
            GeneralTransform transform = element.TransformToVisual(content);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            if (bounds.Width > 0 && bounds.Height > 0)
            {
                return ScrollBoundsIntoView(bounds);
            }
        }

        if (_layout.TryGetBoundsForIndex(index, out Rect layoutBounds))
        {
            return ScrollBoundsIntoView(layoutBounds);
        }

        return false;
    }

    private bool ScrollBoundsIntoView(Rect bounds)
    {
        double left = bounds.X;
        double right = bounds.X + bounds.Width;
        double viewportLeft = _viewer.HorizontalOffset;
        double viewportRight = viewportLeft + _viewer.ViewportWidth;
        if (left >= viewportLeft && right <= viewportRight)
        {
            return true;
        }

        double horizontalOffset = left < viewportLeft
            ? Math.Max(0, left)
            : Math.Max(0, right - _viewer.ViewportWidth);
        _viewer.ChangeView(horizontalOffset, null, null, disableAnimation: false);
        return true;
    }

    private int FindIndex(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return -1;
        }

        for (int i = 0; i < _items.Count; i++)
        {
            EntryViewModel item = _items[i];
            if (!item.IsGroupHeader &&
                string.Equals(item.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static IEnumerable<FrameworkElement> FindEntryElements(DependencyObject root)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.DataContext is EntryViewModel)
            {
                yield return element;
            }

            foreach (FrameworkElement nested in FindEntryElements(child))
            {
                yield return nested;
            }
        }
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
}
