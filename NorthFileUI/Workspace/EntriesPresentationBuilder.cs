using NorthFileUI.Interop;
using System;
using System.Collections.Generic;

namespace NorthFileUI.Workspace;

public sealed class EntriesPresentationBuilder
{
    public int FindInsertIndex(IReadOnlyList<EntryViewModel> entries, EntryViewModel candidate, DirectorySortMode sortMode)
    {
        return FindInsertIndex(entries, candidate, (left, right) => Compare(left, right, sortMode));
    }

    public int FindInsertIndex(
        IReadOnlyList<EntryViewModel> entries,
        EntryViewModel candidate,
        EntrySortField sortField,
        SortDirection sortDirection)
    {
        return FindInsertIndex(entries, candidate, (left, right) => Compare(left, right, sortField, sortDirection));
    }

    private static int FindInsertIndex(
        IReadOnlyList<EntryViewModel> entries,
        EntryViewModel candidate,
        Comparison<EntryViewModel> compare)
    {
        int loadedCount = 0;
        while (loadedCount < entries.Count && entries[loadedCount].IsLoaded)
        {
            loadedCount++;
        }

        int index = 0;
        for (; index < loadedCount; index++)
        {
            if (compare(candidate, entries[index]) < 0)
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

    public int Compare(
        EntryViewModel left,
        EntryViewModel right,
        EntrySortField sortField,
        SortDirection sortDirection)
    {
        if (left.IsDirectory != right.IsDirectory)
        {
            return left.IsDirectory ? -1 : 1;
        }

        int result = sortField switch
        {
            EntrySortField.ModifiedDate => Nullable.Compare(left.ModifiedAt, right.ModifiedAt),
            EntrySortField.Type => StringComparer.CurrentCultureIgnoreCase.Compare(left.Type, right.Type),
            EntrySortField.Size => Nullable.Compare(left.SizeBytes, right.SizeBytes),
            _ => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name)
        };

        if (result == 0 && sortField != EntrySortField.Name)
        {
            result = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        }

        if (sortDirection == SortDirection.Descending)
        {
            result = -result;
        }

        if (result == 0)
        {
            result = StringComparer.CurrentCultureIgnoreCase.Compare(left.FullPath, right.FullPath);
        }

        return result;
    }
}
