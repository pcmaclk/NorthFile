using FileExplorerUI.Interop;
using System;
using System.Collections.Generic;

namespace FileExplorerUI.Workspace;

public sealed class EntriesPresentationBuilder
{
    public int FindInsertIndex(IReadOnlyList<EntryViewModel> entries, EntryViewModel candidate, DirectorySortMode sortMode)
    {
        int loadedCount = 0;
        while (loadedCount < entries.Count && entries[loadedCount].IsLoaded)
        {
            loadedCount++;
        }

        int index = 0;
        for (; index < loadedCount; index++)
        {
            if (Compare(candidate, entries[index], sortMode) < 0)
            {
                break;
            }
        }

        return index;
    }

    public int Compare(EntryViewModel left, EntryViewModel right, DirectorySortMode sortMode)
    {
        if (sortMode == DirectorySortMode.FolderFirstNameAsc)
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        }

        return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    }
}
