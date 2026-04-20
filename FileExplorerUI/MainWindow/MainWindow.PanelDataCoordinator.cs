using FileExplorerUI.Interop;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private sealed record PanelEntriesLoadResult(
            List<EntryViewModel> Entries,
            IEntryResultSet? ActiveEntryResultSet,
            ulong NextCursor,
            bool HasMore,
            uint TotalEntries);

        private async Task<PanelEntriesLoadResult> LoadPanelEntriesSnapshotAsync(
            string path,
            string queryText,
            uint lastFetchMs,
            CancellationToken cancellationToken,
            NavigationPerfSession? perf = null,
            string perfPrefix = "panel-load",
            bool swallowAccessDeniedAsEmpty = false)
        {
            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                List<EntryViewModel> drives = CreateMyComputerDriveEntries();
                return new PanelEntriesLoadResult(
                    drives,
                    null,
                    0,
                    false,
                    (uint)drives.Count);
            }

            IEntryResultSet resultSet = string.IsNullOrWhiteSpace(queryText)
                ? _explorerService.CreateDirectoryResultSet(path, GetPanelDirectorySortMode(WorkspacePanelId.Primary))
                : _explorerService.CreateSearchResultSet(path, queryText, GetPanelDirectorySortMode(WorkspacePanelId.Primary));

            var loadedEntries = new List<EntryViewModel>();
            ulong cursor = 0;
            const uint limit = 512;
            uint totalEntries = 0;
            bool hasMore;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileBatchPage page;
                bool ok;
                int rustErrorCode;
                string rustErrorMessage;

                (ok, page, rustErrorCode, rustErrorMessage) = await Task.Run(() =>
                {
                    bool success = resultSet.TryReadRange(
                        cursor,
                        limit,
                        lastFetchMs,
                        out FileBatchPage p,
                        out int code,
                        out string message);
                    return (success, p, code, message);
                }, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (!ok)
                {
                    if (swallowAccessDeniedAsEmpty && IsRustAccessDenied(rustErrorCode, rustErrorMessage))
                    {
                        perf?.Mark($"{perfPrefix}.access-denied");
                        break;
                    }

                    throw new InvalidOperationException($"Rust error {rustErrorCode}: {rustErrorMessage}");
                }

                totalEntries = page.TotalEntries;
                foreach (FileRow row in page.Rows)
                {
                    string fullPath = Path.Combine(path, row.Name);
                    if (!ShouldIncludeEntry(fullPath, row.Name))
                    {
                        continue;
                    }

                    EntryViewModel entry = CreateLoadedEntryModel(path, row);
                    PopulateEntryMetadata(entry);
                    loadedEntries.Add(entry);
                }

                cursor = page.NextCursor;
                hasMore = page.HasMore;
                perf?.Mark($"{perfPrefix}.batch", $"loaded={loadedEntries.Count} total={totalEntries} hasMore={hasMore}");
            }
            while (hasMore);

            return new PanelEntriesLoadResult(
                loadedEntries,
                resultSet,
                cursor,
                false,
                (uint)loadedEntries.Count);
        }

        private Task ReloadPanelDataAsync(
            WorkspacePanelId panelId,
            bool preserveViewport,
            bool ensureSelectionVisible = false,
            bool focusEntries = false)
        {
            return RestorePanelStateAsync(panelId, preserveViewport, ensureSelectionVisible, focusEntries);
        }

        private Task ReloadPanelWithSelectionAsync(
            WorkspacePanelId panelId,
            string? selectedPath,
            bool preserveViewport = true,
            bool ensureSelectionVisible = true,
            bool focusEntries = true)
        {
            SetPanelSelectedEntryPath(panelId, selectedPath);
            SetPanelFocusedEntryPath(panelId, selectedPath);
            return ReloadPanelDataAsync(panelId, preserveViewport, ensureSelectionVisible, focusEntries);
        }

        private Task ReloadPanelAtPathAsync(
            WorkspacePanelId panelId,
            string path,
            bool preserveViewport,
            bool ensureSelectionVisible = false,
            bool focusEntries = false)
        {
            SetPanelCurrentPath(panelId, path);
            RaisePaneAddressPropertiesChanged(panelId);
            return ReloadPanelDataAsync(panelId, preserveViewport, ensureSelectionVisible, focusEntries);
        }

        private async Task<bool> HandleDeleteMutationForPanelAsync(WorkspacePanelId panelId, string targetPath)
        {
            if (panelId == WorkspacePanelId.Primary)
            {
                if (!ApplyLocalDeleteToPane(panelId, targetPath))
                {
                    await ReloadPanelDataAsync(
                        panelId,
                        preserveViewport: false,
                        ensureSelectionVisible: false,
                        focusEntries: false);
                }

                return true;
            }

            if (string.Equals(GetPanelSelectedEntryPath(panelId), targetPath, StringComparison.OrdinalIgnoreCase))
            {
                SetPanelSelectedEntryPath(panelId, null);
            }

            if (string.Equals(GetPanelFocusedEntryPath(panelId), targetPath, StringComparison.OrdinalIgnoreCase))
            {
                SetPanelFocusedEntryPath(panelId, null);
            }

            string currentPath = GetPanelCurrentPath(panelId);
            if (IsPathWithin(currentPath, targetPath))
            {
                string? parentPath = Path.GetDirectoryName(targetPath.TrimEnd('\\'));
                if (string.IsNullOrWhiteSpace(parentPath))
                {
                    parentPath = Path.GetPathRoot(targetPath);
                }

                await ReloadPanelAtPathAsync(
                    panelId,
                    string.IsNullOrWhiteSpace(parentPath) ? ShellMyComputerPath : parentPath,
                    preserveViewport: false,
                    ensureSelectionVisible: false,
                    focusEntries: false);
                return true;
            }

            if (!ApplyLocalDeleteToPane(panelId, targetPath))
            {
                await ReloadPanelDataAsync(
                    panelId,
                    preserveViewport: false,
                    ensureSelectionVisible: false,
                    focusEntries: false);
            }

            return true;
        }

        private void HandleDeleteFallbackInvalidationForPanel(
            WorkspacePanelId panelId,
            string targetPath,
            bool changeNotified)
        {
            if (changeNotified || panelId != WorkspacePanelId.Primary)
            {
                return;
            }

            string parentPath = _fileOperationsController.ResolveDeleteFallbackParentPath(
                targetPath,
                GetPanelCurrentPath(WorkspacePanelId.Primary));
            EnsurePersistentRefreshFallbackInvalidation(parentPath, "delete");
        }

        private async Task HandlePasteMutationForPanelAsync(
            WorkspacePanelId panelId,
            string targetDirectoryPath,
            bool selectPastedEntry,
            int appliedCount,
            bool appliedDirectory,
            string? firstAppliedPath,
            FileTransferMode mode,
            IReadOnlyList<FilePasteItemResult> itemResults)
        {
            bool shouldEnsureSelectionVisible = selectPastedEntry &&
                appliedCount == 1 &&
                !string.IsNullOrWhiteSpace(firstAppliedPath);
            if (shouldEnsureSelectionVisible)
            {
                SetPanelSelectedEntryPath(panelId, firstAppliedPath);
                SetPanelFocusedEntryPath(panelId, firstAppliedPath);
            }

            PreparePanelDirectoryMutation(panelId, targetDirectoryPath);

            if (string.Equals(targetDirectoryPath, GetPanelCurrentPath(panelId), StringComparison.OrdinalIgnoreCase))
            {
                if (!TryApplyLocalPasteTargetMutationForPanel(
                        panelId,
                        targetDirectoryPath,
                        selectPastedEntry,
                        appliedCount,
                        firstAppliedPath,
                        itemResults))
                {
                    InvalidatePanelDataLoadedForCurrentNavigation(panelId);
                    await ReloadPanelDataAsync(
                        panelId,
                        preserveViewport: true,
                        ensureSelectionVisible: shouldEnsureSelectionVisible,
                        focusEntries: true);
                }
            }

            if (panelId == WorkspacePanelId.Primary &&
                appliedDirectory &&
                FindSidebarTreeNodeByPath(targetDirectoryPath) is TreeViewNode parentNode &&
                parentNode.IsExpanded)
            {
                await PopulateSidebarTreeChildrenAsync(parentNode, targetDirectoryPath, CancellationToken.None, expandAfterLoad: true);
            }

            if (mode == FileTransferMode.Cut)
            {
                await RefreshPanelsForMoveSourcesAsync(targetDirectoryPath, itemResults);
            }
        }

        private bool TryApplyLocalPasteTargetMutationForPanel(
            WorkspacePanelId panelId,
            string targetDirectoryPath,
            bool selectPastedEntry,
            int appliedCount,
            string? firstAppliedPath,
            IReadOnlyList<FilePasteItemResult> itemResults)
        {
            if (appliedCount <= 0 ||
                string.Equals(targetDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(GetPanelQueryText(panelId)))
            {
                return false;
            }

            var appliedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FilePasteItemResult item in itemResults)
            {
                if (!item.Applied || string.IsNullOrWhiteSpace(item.TargetPath))
                {
                    continue;
                }

                string? targetParent = _explorerService.GetParentPath(item.TargetPath);
                if (string.IsNullOrWhiteSpace(targetParent) ||
                    !string.Equals(targetParent, targetDirectoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!appliedTargets.Add(item.TargetPath))
                {
                    continue;
                }

                if (!TryApplyLocalUpsertToPane(panelId, item.TargetPath, item.IsDirectory))
                {
                    return false;
                }
            }

            if (appliedTargets.Count == 0)
            {
                return false;
            }

            if (selectPastedEntry &&
                appliedCount == 1 &&
                !string.IsNullOrWhiteSpace(firstAppliedPath))
            {
                SetPanelSelectedEntryPath(panelId, firstAppliedPath);
                SetPanelFocusedEntryPath(panelId, firstAppliedPath);
                if (panelId == WorkspacePanelId.Primary)
                {
                    UpdateEntrySelectionVisuals();
                }
                else
                {
                    UpdateSecondaryEntrySelectionVisuals();
                }
            }

            RefreshPanelStatus(panelId);
            return true;
        }

        private async Task RefreshPanelsForMoveSourcesAsync(
            string targetDirectoryPath,
            IReadOnlyList<FilePasteItemResult> itemResults)
        {
            var sourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FilePasteItemResult item in itemResults)
            {
                if (!item.Applied || string.IsNullOrWhiteSpace(item.SourcePath))
                {
                    continue;
                }

                string? sourceParentPath = _explorerService.GetParentPath(item.SourcePath);
                if (string.IsNullOrWhiteSpace(sourceParentPath) ||
                    string.Equals(sourceParentPath, targetDirectoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sourceParents.Add(sourceParentPath);
                SuppressNextWatcherRefresh(sourceParentPath);
            }

            if (sourceParents.Count == 0)
            {
                return;
            }

            foreach (WorkspacePanelId candidatePanelId in new[] { WorkspacePanelId.Primary, WorkspacePanelId.Secondary })
            {
                if (!sourceParents.Contains(GetPanelCurrentPath(candidatePanelId)))
                {
                    continue;
                }

                bool appliedLocalDelete = true;
                foreach (FilePasteItemResult item in itemResults)
                {
                    if (!item.Applied || string.IsNullOrWhiteSpace(item.SourcePath))
                    {
                        continue;
                    }

                    string? sourceParentPath = _explorerService.GetParentPath(item.SourcePath);
                    if (!string.Equals(sourceParentPath, GetPanelCurrentPath(candidatePanelId), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    appliedLocalDelete &= ApplyLocalDeleteToPane(candidatePanelId, item.SourcePath);
                }

                if (!appliedLocalDelete)
                {
                    InvalidatePanelDataLoadedForCurrentNavigation(candidatePanelId);
                    await ReloadPanelDataAsync(
                        candidatePanelId,
                        preserveViewport: true,
                        ensureSelectionVisible: false,
                        focusEntries: false);
                    continue;
                }

                RefreshPanelStatus(candidatePanelId);
            }
        }

        private async Task HandleCreatedTargetMutationForPanelAsync(
            WorkspacePanelId panelId,
            string destinationDirectory,
            string? selectionPath,
            bool changeNotified,
            string invalidationReason)
        {
            if (string.IsNullOrWhiteSpace(selectionPath) ||
                !string.Equals(destinationDirectory, GetPanelCurrentPath(panelId), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (panelId == WorkspacePanelId.Primary && !changeNotified)
            {
                EnsurePersistentRefreshFallbackInvalidation(
                    GetPanelCurrentPath(WorkspacePanelId.Primary),
                    invalidationReason);
            }

            await ReloadPanelWithSelectionAsync(panelId, selectionPath);
        }

        private async Task<bool> HandleCreateMutationForPanelAsync(
            WorkspacePanelId panelId,
            CreatedEntryInfo created,
            bool changeNotified,
            string invalidationReason)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                EntryViewModel createdEntry = CreateLocalCreatedEntryModelForPane(panelId, created.Name, created.IsDirectory);
                int insertIndex = _entriesPresentationBuilder.FindInsertIndex(
                    GetPanelEntries(panelId),
                    createdEntry,
                    GetPanelSortField(panelId),
                    GetPanelSortDirection(panelId));
                InsertLocalCreatedEntryToPane(panelId, createdEntry, insertIndex);
                SetPanelSelectedEntryPath(panelId, createdEntry.FullPath);
                SetPanelFocusedEntryPath(panelId, createdEntry.FullPath);
                await BeginRenameOverlayAsync(
                    createdEntry,
                    ensureVisible: true,
                    updateSelection: false,
                    panelId: panelId);
                return true;
            }

            if (!changeNotified)
            {
                EnsurePersistentRefreshFallbackInvalidation(
                    GetPanelCurrentPath(WorkspacePanelId.Primary),
                    invalidationReason);
            }

            EntryViewModel entry = CreateLocalCreatedEntryModel(created.Name, created.IsDirectory);
            int primaryInsertIndex = FindInsertIndexForEntry(entry);
            if (!IsIndexInCurrentViewport(primaryInsertIndex))
            {
                await EnsureCreateInsertVisibleAsync(primaryInsertIndex);
            }

            InsertLocalCreatedEntryToPane(panelId, entry, primaryInsertIndex);
            _pendingCreatedEntrySelection = entry;
            await StartRenameForCreatedEntryAsync(entry, primaryInsertIndex);
            return true;
        }

        private void PreparePanelDirectoryMutation(WorkspacePanelId panelId, string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return;
            }

            SuppressNextWatcherRefresh(directoryPath);
        }
    }
}
