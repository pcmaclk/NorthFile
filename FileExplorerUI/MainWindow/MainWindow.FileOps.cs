using FileExplorerUI.Services;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void ApplyLocalRename(int index, string newName)
        {
            if (!_fileOperationsController.ApplyLocalRename(_entries, index, newName, _currentPath, GetEntryDisplayName))
            {
                return;
            }
            InvalidatePresentationSourceCache();
            RequestMetadataForCurrentViewport();
        }

        private void ApplyLocalDelete(int index)
        {
            if (!_fileOperationsController.ApplyLocalDelete(
                _entries,
                index,
                ref _selectedEntryPath,
                ref _focusedEntryPath,
                ref _totalEntries,
                _nextCursor,
                out bool hasMore))
            {
                return;
            }
            _hasMore = hasMore;
            InvalidatePresentationSourceCache();
            UpdateFileCommandStates();
        }

        private void UpdateListEntryNameForCurrentDirectory(string sourcePath, string newName)
        {
            if (!_fileOperationsController.UpdateListEntryNameForCurrentDirectory(
                _entries,
                _currentPath,
                sourcePath,
                newName,
                _selectedEntryPath,
                GetEntryDisplayName,
                out EntryViewModel? current,
                out bool wasSelected) || current is null)
            {
                return;
            }

            InvalidatePresentationSourceCache();

            if (wasSelected)
            {
                _ = DispatcherQueue.TryEnqueue(() => SelectEntryInList(current, ensureVisible: true));
            }
        }

        private EntryViewModel CreateLocalCreatedEntryModel(string name, bool isDirectory)
        {
            return _fileOperationsController.CreateLocalCreatedEntryModel(
                name,
                isDirectory,
                _currentPath,
                GetEntryDisplayName,
                GetEntryTypeText,
                GetEntryIconGlyph,
                GetEntryIconBrush);
        }

        private EntryViewModel InsertLocalCreatedEntry(EntryViewModel entry, int insertIndex)
        {
            _fileOperationsController.InsertLocalCreatedEntry(
                _entries,
                entry,
                insertIndex,
                ref _totalEntries,
                _nextCursor,
                out bool hasMore);
            _hasMore = hasMore;
            _lastTitleWasReadFailed = false;
            UpdateWindowTitle();
            return entry;
        }

    }
}
