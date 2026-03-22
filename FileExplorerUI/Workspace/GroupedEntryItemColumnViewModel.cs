using System.Collections.Generic;

namespace FileExplorerUI.Workspace;

public sealed class GroupedEntryItemColumnViewModel
{
    public IReadOnlyList<EntryViewModel> Items { get; set; } = [];
}
