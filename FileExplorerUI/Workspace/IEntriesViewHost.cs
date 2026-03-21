using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace FileExplorerUI.Workspace;

public interface IEntriesViewHost
{
    void SetItems(IReadOnlyList<EntryViewModel> items);

    void SetSelectedEntry(string? fullPath);

    EntryViewModel? ResolveDoubleTappedEntry(DoubleTappedRoutedEventArgs e);

    EntriesViewHitResult ResolveRightTappedHit(RightTappedRoutedEventArgs e);

    EntryViewModel? ResolvePressedEntry(PointerRoutedEventArgs e);
}
