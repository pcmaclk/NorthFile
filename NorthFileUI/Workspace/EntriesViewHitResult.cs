using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace NorthFileUI.Workspace;

public sealed record EntriesViewHitResult(
    UIElement Anchor,
    Point Position,
    EntryViewModel? Entry,
    bool IsItemTarget);
