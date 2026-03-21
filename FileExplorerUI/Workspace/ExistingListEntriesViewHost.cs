using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace FileExplorerUI.Workspace;

public sealed class ExistingListEntriesViewHost : IEntriesViewHost
{
    private readonly ListView _listView;

    public ExistingListEntriesViewHost(ListView listView)
    {
        _listView = listView;
    }

    public void SetItems(IReadOnlyList<EntryViewModel> items)
    {
        _listView.ItemsSource = items;
    }

    public void SetSelectedEntry(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            _listView.SelectedItem = null;
            return;
        }

        foreach (object? item in _listView.Items)
        {
            if (item is EntryViewModel entry &&
                string.Equals(entry.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                _listView.SelectedItem = entry;
                return;
            }
        }
    }

    public EntryViewModel? ResolveDoubleTappedEntry(DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source)
        {
            ListViewItem? item = FindAncestor<ListViewItem>(source);
            if (item?.DataContext is EntryViewModel tappedEntry)
            {
                return tappedEntry;
            }
        }

        Point position = e.GetPosition(_listView);
        if (FindListViewItemAt(position) is ListViewItem hitItem &&
            hitItem.DataContext is EntryViewModel hitEntry)
        {
            return hitEntry;
        }

        return _listView.SelectedItem as EntryViewModel;
    }

    public EntriesViewHitResult ResolveRightTappedHit(RightTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            FindAncestor<ListViewItem>(source) is ListViewItem item &&
            item.DataContext is EntryViewModel entry)
        {
            return new EntriesViewHitResult(item, e.GetPosition(item), entry, true);
        }

        Point position = e.GetPosition(_listView);
        if (TryFindListViewItemBoundsAt(position, out ListViewItem? hitItem, out _) &&
            hitItem?.DataContext is EntryViewModel hitEntry)
        {
            return new EntriesViewHitResult(hitItem, e.GetPosition(hitItem), hitEntry, true);
        }

        return new EntriesViewHitResult(_listView, position, null, false);
    }

    public EntryViewModel? ResolvePressedEntry(PointerRoutedEventArgs e)
    {
        Point position = e.GetCurrentPoint(_listView).Position;
        ListViewItem? item = FindListViewItemAt(position)
                             ?? FindAncestor<ListViewItem>(e.OriginalSource as DependencyObject);
        return item?.DataContext as EntryViewModel;
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

    private ListViewItem? FindListViewItemAt(Point position)
    {
        foreach (UIElement hit in VisualTreeHelper.FindElementsInHostCoordinates(position, _listView, includeAllElements: true))
        {
            if (hit is ListViewItem direct)
            {
                return direct;
            }

            if (hit is DependencyObject dep)
            {
                ListViewItem? ancestor = FindAncestor<ListViewItem>(dep);
                if (ancestor is not null)
                {
                    return ancestor;
                }
            }
        }

        if (position.X > 0)
        {
            Point leftAlignedPosition = new(1, position.Y);
            foreach (UIElement hit in VisualTreeHelper.FindElementsInHostCoordinates(leftAlignedPosition, _listView, includeAllElements: true))
            {
                if (hit is ListViewItem direct)
                {
                    return direct;
                }

                if (hit is DependencyObject dep)
                {
                    ListViewItem? ancestor = FindAncestor<ListViewItem>(dep);
                    if (ancestor is not null)
                    {
                        return ancestor;
                    }
                }
            }
        }

        return FindListViewItemByVerticalPosition(position.Y);
    }

    private bool TryFindListViewItemBoundsAt(Point position, out ListViewItem? item, out Rect bounds)
    {
        item = null;
        bounds = default;

        foreach (UIElement hit in VisualTreeHelper.FindElementsInHostCoordinates(position, _listView, includeAllElements: true))
        {
            if (hit is ListViewItem direct)
            {
                Rect directBounds = GetListViewItemBounds(direct);
                if (directBounds.Contains(position))
                {
                    item = direct;
                    bounds = directBounds;
                    return true;
                }
            }

            if (hit is DependencyObject dep)
            {
                ListViewItem? ancestor = FindAncestor<ListViewItem>(dep);
                if (ancestor is not null)
                {
                    Rect ancestorBounds = GetListViewItemBounds(ancestor);
                    if (ancestorBounds.Contains(position))
                    {
                        item = ancestor;
                        bounds = ancestorBounds;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private Rect GetListViewItemBounds(ListViewItem item)
    {
        GeneralTransform transform = item.TransformToVisual(_listView);
        return transform.TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
    }

    private ListViewItem? FindListViewItemByVerticalPosition(double y)
    {
        for (int i = 0; i < _listView.Items.Count; i++)
        {
            if (_listView.ContainerFromIndex(i) is not ListViewItem item)
            {
                continue;
            }

            GeneralTransform? transform = item.TransformToVisual(_listView);
            Rect bounds = transform.TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            if (y >= bounds.Top && y <= bounds.Bottom)
            {
                return item;
            }
        }

        return null;
    }
}
