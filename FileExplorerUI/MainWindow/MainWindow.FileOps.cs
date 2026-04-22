using FileExplorerUI.Collections;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using System;
using System.IO;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void ApplyLocalRename(int index, string newName)
        {
            if (!_fileOperationsController.ApplyLocalRename(
                    PrimaryEntries,
                    index,
                    newName,
                    GetPanelCurrentPath(WorkspacePanelId.Primary),
                    GetEntryDisplayName))
            {
                return;
            }
            InvalidatePresentationSourceCache();
            RequestMetadataForCurrentViewport();
        }

        private void ApplyLocalDelete(int index)
        {
            uint totalEntries = GetPanelTotalEntries(WorkspacePanelId.Primary);
            if (!_fileOperationsController.ApplyLocalDelete(
                PrimaryEntries,
                index,
                _selectedEntryPath,
                _focusedEntryPath,
                ref totalEntries,
                GetPanelNextCursor(WorkspacePanelId.Primary),
                out string? updatedSelectedEntryPath,
                out string? updatedFocusedEntryPath,
                out bool hasMore))
            {
                return;
            }
            SetPanelTotalEntries(WorkspacePanelId.Primary, totalEntries);
            _selectedEntryPath = updatedSelectedEntryPath;
            _focusedEntryPath = updatedFocusedEntryPath;
            SetPanelHasMore(WorkspacePanelId.Primary, hasMore);
            InvalidatePresentationSourceCache();
            UpdateFileCommandStates();
        }

        private bool ApplyLocalDeleteToSecondaryPane(string targetPath)
        {
            BatchObservableCollection<EntryViewModel> entries = SecondaryPanelState.DataSession.Entries;
            int index = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                EntryViewModel candidate = entries[i];
                if (!candidate.IsLoaded || candidate.IsGroupHeader)
                {
                    continue;
                }

                if (string.Equals(candidate.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return false;
            }

            uint totalEntries = SecondaryPanelState.DataSession.TotalEntries;
            if (!_fileOperationsController.ApplyLocalDelete(
                entries,
                index,
                SecondaryPanelState.SelectedEntryPath,
                SecondaryPanelState.FocusedEntryPath,
                ref totalEntries,
                SecondaryPanelState.DataSession.NextCursor,
                out string? updatedSelectedEntryPath,
                out string? updatedFocusedEntryPath,
                out bool hasMore))
            {
                return false;
            }

            SecondaryPanelState.SelectedEntryPath = updatedSelectedEntryPath;
            SecondaryPanelState.FocusedEntryPath = updatedFocusedEntryPath;
            SecondaryPanelState.DataSession.TotalEntries = totalEntries;
            SecondaryPanelState.DataSession.HasMore = hasMore;
            SecondaryPanelState.DataSession.PresentationSourceEntries.Clear();
            SecondaryPanelState.DataSession.PresentationSourceEntries.AddRange(entries);
            UpdateSecondaryEntrySelectionVisuals();
            RaiseSecondaryPaneDataStateChanged();
            UpdateFileCommandStates();
            return true;
        }

        private bool ApplyLocalDeleteToPane(WorkspacePanelId panelId, string targetPath)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                return ApplyLocalDeleteToSecondaryPane(targetPath);
            }

            int index = -1;
            for (int i = 0; i < PrimaryEntries.Count; i++)
            {
                EntryViewModel candidate = PrimaryEntries[i];
                if (!candidate.IsLoaded || candidate.IsGroupHeader)
                {
                    continue;
                }

                string candidatePath = Path.Combine(GetPanelCurrentPath(WorkspacePanelId.Primary), candidate.Name);
                if (string.Equals(candidatePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return false;
            }

            ApplyLocalDelete(index);
            return true;
        }

        private void UpdateListEntryNameForCurrentDirectory(string sourcePath, string newName)
        {
            if (!_fileOperationsController.UpdateListEntryNameForCurrentDirectory(
                PrimaryEntries,
                GetPanelCurrentPath(WorkspacePanelId.Primary),
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
                GetPanelCurrentPath(WorkspacePanelId.Primary),
                GetEntryDisplayName,
                GetEntryTypeText,
                GetEntryIconGlyph,
                GetEntryIconBrush);
        }

        private EntryViewModel CreateLocalCreatedEntryModelForPane(WorkspacePanelId panelId, string name, bool isDirectory)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                return _fileOperationsController.CreateLocalCreatedEntryModel(
                    name,
                    isDirectory,
                    GetPanelCurrentPath(panelId),
                    GetEntryDisplayName,
                    GetEntryTypeText,
                    GetEntryIconGlyph,
                    GetEntryIconBrush);
            }

            return CreateLocalCreatedEntryModel(name, isDirectory);
        }

        private EntryViewModel CreateLocalEntryModelForPanePath(WorkspacePanelId panelId, string targetPath, bool isDirectory)
        {
            string name = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            EntryViewModel entry = CreateLocalCreatedEntryModelForPane(panelId, name, isDirectory);
            entry.FullPath = targetPath;
            entry.IsPendingCreate = false;
            entry.PendingCreateIsDirectory = false;
            entry.IsMetadataLoaded = false;
            PopulateEntryMetadata(entry);
            return entry;
        }

        private EntryViewModel InsertLocalCreatedEntry(EntryViewModel entry, int insertIndex)
        {
            uint totalEntries = GetPanelTotalEntries(WorkspacePanelId.Primary);
            _fileOperationsController.InsertLocalCreatedEntry(
                PrimaryEntries,
                entry,
                insertIndex,
                ref totalEntries,
                GetPanelNextCursor(WorkspacePanelId.Primary),
                out bool hasMore);
            SetPanelTotalEntries(WorkspacePanelId.Primary, totalEntries);
            SetPanelHasMore(WorkspacePanelId.Primary, hasMore);
            _lastTitleWasReadFailed = false;
            UpdateWindowTitle();
            return entry;
        }

        private EntryViewModel InsertLocalCreatedEntryToSecondaryPane(EntryViewModel entry, int insertIndex)
        {
            PanelDataSession session = SecondaryPanelState.DataSession;
            uint totalEntries = session.TotalEntries;
            _fileOperationsController.InsertLocalCreatedEntry(
                session.Entries,
                entry,
                insertIndex,
                ref totalEntries,
                session.NextCursor,
                out bool hasMore);
            session.TotalEntries = totalEntries;
            session.HasMore = hasMore;
            session.PresentationSourceEntries.Clear();
            session.PresentationSourceEntries.AddRange(session.Entries);
            UpdateSecondaryEntrySelectionVisuals();
            RaiseSecondaryPaneDataStateChanged();
            return entry;
        }

        private EntryViewModel InsertLocalCreatedEntryToPane(WorkspacePanelId panelId, EntryViewModel entry, int insertIndex)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                return InsertLocalCreatedEntryToSecondaryPane(entry, insertIndex);
            }

            return InsertLocalCreatedEntry(entry, insertIndex);
        }

        private bool TryApplyLocalUpsertToPane(WorkspacePanelId panelId, string targetPath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            string name = Path.GetFileName(targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name) || !ShouldIncludeEntry(targetPath, name))
            {
                return false;
            }

            ApplyLocalDeleteToPane(panelId, targetPath);

            EntryViewModel entry = CreateLocalEntryModelForPanePath(panelId, targetPath, isDirectory);
            int insertIndex = _entriesPresentationBuilder.FindInsertIndex(
                GetPanelEntries(panelId),
                entry,
                GetPanelSortField(panelId),
                GetPanelSortDirection(panelId));
            InsertLocalCreatedEntryToPane(panelId, entry, insertIndex);

            if (panelId == WorkspacePanelId.Primary)
            {
                InvalidatePresentationSourceCache();
                RequestMetadataForCurrentViewport();
            }

            return true;
        }

    }
}
