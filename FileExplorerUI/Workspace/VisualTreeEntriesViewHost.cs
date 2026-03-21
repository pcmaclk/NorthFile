using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using Windows.Foundation;

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

    public EntriesViewHitResult ResolveRightTappedHit(RightTappedRoutedEventArgs e)
    {
        FrameworkElement? anchor = FindEntryElement(e.OriginalSource as DependencyObject);
        if (anchor?.DataContext is EntryViewModel entry)
        {
            return new EntriesViewHitResult(anchor, e.GetPosition(anchor), entry, true);
        }

        return new EntriesViewHitResult(_root, e.GetPosition(_root), null, false);
    }

    public EntryViewModel? ResolvePressedEntry(PointerRoutedEventArgs e)
    {
        return FindEntryFromSource(e.OriginalSource as DependencyObject);
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
}
