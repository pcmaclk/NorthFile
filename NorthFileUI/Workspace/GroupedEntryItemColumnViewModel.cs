using System.Collections.Generic;

namespace NorthFileUI.Workspace;

public sealed class GroupedEntryItemColumnViewModel
{
    public IReadOnlyList<EntryViewModel> Items { get; set; } = [];
}
