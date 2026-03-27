using FileExplorerUI.Collections;
using FileExplorerUI.Services;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using System.IO;

namespace FileExplorerUI
{
    internal sealed class FileOperationsController
    {
        internal sealed record RenameDecision(
            bool Succeeded,
            string? FailureMessage,
            RenamedEntryInfo? RenamedInfo);

        internal sealed record DeleteDecision(
            bool Succeeded,
            bool Canceled,
            bool ChangeNotified,
            string? FailureMessage);

        public bool ApplyLocalRename(
            BatchObservableCollection<EntryViewModel> entries,
            int index,
            string newName,
            string currentPath,
            Func<string, bool, string> displayNameResolver)
        {
            if (index < 0 || index >= entries.Count)
            {
                return false;
            }

            EntryViewModel current = entries[index];
            current.Name = newName;
            current.PendingName = newName;
            current.DisplayName = displayNameResolver(newName, current.IsDirectory);
            current.FullPath = Path.Combine(currentPath, newName);
            current.SizeText = string.Empty;
            current.ModifiedText = string.Empty;
            current.IsLoaded = true;
            current.IsMetadataLoaded = false;
            return true;
        }

        public bool ApplyLocalDelete(
            BatchObservableCollection<EntryViewModel> entries,
            int index,
            ref string? selectedEntryPath,
            ref string? focusedEntryPath,
            ref uint totalEntries,
            ulong nextCursor,
            out bool hasMore)
        {
            hasMore = false;
            if (index < 0 || index >= entries.Count)
            {
                return false;
            }

            if (string.Equals(selectedEntryPath, entries[index].FullPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedEntryPath = null;
            }
            if (string.Equals(focusedEntryPath, entries[index].FullPath, StringComparison.OrdinalIgnoreCase))
            {
                focusedEntryPath = null;
            }

            entries.RemoveAt(index);
            if (totalEntries > 0)
            {
                totalEntries--;
            }
            if (entries.Count > totalEntries)
            {
                entries.RemoveAt(entries.Count - 1);
            }

            hasMore = nextCursor < totalEntries;
            return true;
        }

        public bool UpdateListEntryNameForCurrentDirectory(
            BatchObservableCollection<EntryViewModel> entries,
            string currentPath,
            string sourcePath,
            string newName,
            string? selectedEntryPath,
            Func<string, bool, string> displayNameResolver,
            out EntryViewModel? updatedEntry,
            out bool wasSelected)
        {
            updatedEntry = null;
            wasSelected = false;
            string parentPath = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            if (!string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int index = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].FullPath, sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return false;
            }

            EntryViewModel current = entries[index];
            wasSelected = string.Equals(selectedEntryPath, current.FullPath, StringComparison.OrdinalIgnoreCase);
            current.Name = newName;
            current.PendingName = newName;
            current.DisplayName = displayNameResolver(newName, current.IsDirectory);
            current.FullPath = Path.Combine(currentPath, newName);
            updatedEntry = current;
            return true;
        }

        public EntryViewModel CreateLocalCreatedEntryModel(
            string name,
            bool isDirectory,
            string currentPath,
            Func<string, bool, string> displayNameResolver,
            Func<string, bool, bool, string> typeResolver,
            Func<bool, bool, string, string> glyphResolver,
            Func<bool, bool, string, Brush> brushResolver)
        {
            return new EntryViewModel
            {
                Name = name,
                DisplayName = displayNameResolver(name, isDirectory),
                PendingName = name,
                FullPath = Path.Combine(currentPath, name),
                Type = typeResolver(name, isDirectory, false),
                IconGlyph = glyphResolver(isDirectory, false, name),
                IconForeground = brushResolver(isDirectory, false, name),
                MftRef = 0,
                SizeText = isDirectory ? string.Empty : "0 B",
                ModifiedText = DateTime.Now.ToString("g", CultureInfo.CurrentCulture),
                IsDirectory = isDirectory,
                IsLink = false,
                IsLoaded = true,
                IsMetadataLoaded = true
            };
        }

        public void InsertLocalCreatedEntry(
            BatchObservableCollection<EntryViewModel> entries,
            EntryViewModel entry,
            int insertIndex,
            ref uint totalEntries,
            ulong nextCursor,
            out bool hasMore)
        {
            entries.Insert(insertIndex, entry);
            totalEntries++;
            hasMore = nextCursor < totalEntries;
        }

        public RenameDecision AnalyzeRenameResult(FileOperationResult<RenamedEntryInfo> result, string unknownFailureMessage)
        {
            if (!result.Succeeded)
            {
                return new RenameDecision(
                    Succeeded: false,
                    FailureMessage: result.Failure?.Message ?? unknownFailureMessage,
                    RenamedInfo: null);
            }

            return new RenameDecision(
                Succeeded: true,
                FailureMessage: null,
                RenamedInfo: result.Value!);
        }

        public DeleteDecision AnalyzeDeleteResult(FileOperationResult<bool> result, string unknownFailureMessage)
        {
            if (!result.Succeeded)
            {
                bool canceled = result.Failure?.Error == FileOperationError.Canceled;
                return new DeleteDecision(
                    Succeeded: false,
                    Canceled: canceled,
                    ChangeNotified: false,
                    FailureMessage: canceled ? null : result.Failure?.Message ?? unknownFailureMessage);
            }

            return new DeleteDecision(
                Succeeded: true,
                Canceled: false,
                ChangeNotified: result.Value,
                FailureMessage: null);
        }

        public string ResolveDeleteFallbackParentPath(string targetPath, string currentPath)
        {
            return Path.GetDirectoryName(targetPath) ?? currentPath;
        }
    }
}
