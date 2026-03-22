using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace FileExplorerUI.Workspace;

public sealed record EntriesViewHitResult(
    UIElement Anchor,
    Point Position,
    EntryViewModel? Entry,
    bool IsItemTarget);
