using System;
using System.Collections.Generic;

namespace FileExplorerUI.Workspace;

public interface IEntriesViewHost
{
    void SetItems(IReadOnlyList<EntryViewModel> items);

    void SetSelectedEntry(string? fullPath);

    event EventHandler<EntryViewModel>? EntryInvoked;

    event EventHandler<EntryViewModel>? EntryContextRequested;

    event EventHandler? BackgroundContextRequested;
}
