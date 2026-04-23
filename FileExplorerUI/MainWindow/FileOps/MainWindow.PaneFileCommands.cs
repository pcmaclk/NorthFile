using FileExplorerUI.Commands;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private Task ExecuteNewFileForPaneCoreAsync(WorkspacePanelId panelId)
        {
            return ExecuteCreateEntryForPaneCoreAsync(panelId, isDirectory: false);
        }

        private Task ExecuteNewFolderForPaneCoreAsync(WorkspacePanelId panelId)
        {
            return ExecuteCreateEntryForPaneCoreAsync(panelId, isDirectory: true);
        }

        private async Task ExecuteCreateEntryForPaneCoreAsync(WorkspacePanelId panelId, bool isDirectory)
        {
            string targetDirectoryPath = GetPanelCurrentPath(panelId);
            string createKind = CreateKindLabel(isDirectory);

            if (string.IsNullOrWhiteSpace(targetDirectoryPath) ||
                string.Equals(targetDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusNewFailedOpenFolderFirst", createKind);
                return;
            }

            if (!_explorerService.DirectoryExists(targetDirectoryPath))
            {
                UpdateStatusKey("StatusNewFailedWithReason", createKind, S("ErrorCurrentFolderUnavailable"));
                return;
            }

            while (true)
            {
                try
                {
                    PreparePanelDirectoryMutation(panelId, targetDirectoryPath);

                    FileOperationResult<CreatedEntryInfo> createResult =
                        await _fileManagementCoordinator.TryCreateEntryAsync(targetDirectoryPath, isDirectory);
                    if (!createResult.Succeeded)
                    {
                        if (!await ShowOperationFailureDialogAsync(
                                "CreateFailureDialogGenericTitle",
                                SF("StatusCreateFailed", createResult.Failure?.Message ?? S("ErrorFileOperationUnknown")),
                                allowRetry: true))
                        {
                            return;
                        }

                        continue;
                    }

                    CreatedEntryInfo created = createResult.Value!;
                    await HandleCreatedEntryForPaneAsync(panelId, created, isDirectory);
                    UpdateStatusKey("StatusCreateSuccess", created.Name);
                    return;
                }
                catch (Exception ex)
                {
                    if (!await ShowOperationFailureDialogAsync(
                            "CreateFailureDialogGenericTitle",
                            SF("StatusCreateFailed", FileOperationErrors.ToUserMessage(ex)),
                            allowRetry: true))
                    {
                        return;
                    }
                }
            }
        }

        private async Task HandleCreatedEntryForPaneAsync(WorkspacePanelId panelId, CreatedEntryInfo created, bool isDirectory)
        {
            await HandleCreateMutationForPanelAsync(
                panelId,
                created,
                created.ChangeNotified,
                isDirectory ? "create-folder" : "create-file");
        }

        private Task ExecuteRenameForPaneCoreAsync(WorkspacePanelId panelId)
        {
            if (!TryBuildSelectedRenameTargetForPane(panelId, out FileCommandTarget target))
            {
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return Task.CompletedTask;
            }

            return ExecuteRenameTargetForPaneCoreAsync(panelId, target);
        }

        private Task ExecuteRenameForPaneTargetCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteRenameTargetForPaneCoreAsync(panelId, target);
        }

        private Task ExecuteDeleteForPaneCoreAsync(WorkspacePanelId panelId)
        {
            List<EntryViewModel> selectedEntries = GetSelectedLoadedEntriesForPane(panelId);
            if (selectedEntries.Count > 1)
            {
                return ExecuteDeleteEntriesForPaneCoreAsync(panelId, selectedEntries);
            }

            if (!TryBuildSelectedDeleteTargetForPane(panelId, out FileCommandTarget target))
            {
                UpdateStatusKey("StatusDeleteFailedSelectLoaded");
                return Task.CompletedTask;
            }

            return ExecuteDeleteTargetForPaneCoreAsync(panelId, target);
        }

        private Task ExecuteDeleteForPaneTargetCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteDeleteTargetForPaneCoreAsync(panelId, target);
        }

        private void ExecuteCopyForPaneCore(WorkspacePanelId panelId)
        {
            List<EntryViewModel> entries = GetSelectedLoadedEntriesForPane(panelId);
            if (entries.Count == 0)
            {
                UpdateStatusKey("StatusCopyFailedSelectLoaded");
                return;
            }

            string[] sourcePaths = entries
                .Select(entry => GetPaneEntryPath(panelId, entry))
                .ToArray();
            _fileManagementCoordinator.SetClipboard(sourcePaths, FileTransferMode.Copy);
            UpdateFileCommandStates();
            if (entries.Count == 1)
            {
                UpdateStatusKey("StatusCopyReady", entries[0].Name);
            }
            else
            {
                UpdateStatusKey("StatusCopyMultipleReady", entries.Count);
            }
        }

        private void ExecuteCutForPaneCore(WorkspacePanelId panelId)
        {
            List<EntryViewModel> entries = GetSelectedLoadedEntriesForPane(panelId);
            if (entries.Count == 0)
            {
                UpdateStatusKey("StatusCutFailedSelectLoaded");
                return;
            }

            string[] sourcePaths = entries
                .Select(entry => GetPaneEntryPath(panelId, entry))
                .ToArray();
            if (sourcePaths.Any(IsDriveRoot))
            {
                UpdateStatusKey("StatusCutFailedDriveRootsUnsupported");
                return;
            }

            _fileManagementCoordinator.SetClipboard(sourcePaths, FileTransferMode.Cut);
            UpdateFileCommandStates();
            if (entries.Count == 1)
            {
                UpdateStatusKey("StatusCutReady", entries[0].Name);
            }
            else
            {
                UpdateStatusKey("StatusCutMultipleReady", entries.Count);
            }
        }

        private Task ExecutePasteForPaneCoreAsync(WorkspacePanelId panelId)
        {
            string targetDirectoryPath = GetPanelCurrentPath(panelId);
            return ExecutePasteIntoPaneDirectoryCoreAsync(panelId, targetDirectoryPath, selectPastedEntry: true);
        }

        private Task ExecuteRefreshForPaneCoreAsync(WorkspacePanelId panelId)
        {
            return ForceRefreshPanelDirectoryAsync(panelId, preserveViewport: true);
        }

        private Task ExecutePasteForPaneTargetCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            string targetDirectoryPath = target.Path ?? string.Empty;
            string currentDirectoryPath = GetPanelCurrentPath(panelId);
            bool selectPastedEntry = string.Equals(
                targetDirectoryPath,
                currentDirectoryPath,
                StringComparison.OrdinalIgnoreCase);
            return ExecutePasteIntoPaneDirectoryCoreAsync(panelId, targetDirectoryPath, selectPastedEntry);
        }

        private Task ExecuteCreateShortcutForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteCreateShortcutForPaneCoreInternalAsync(panelId, target);
        }

        private Task ExecuteCompressZipForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteCompressZipForPaneCoreInternalAsync(panelId, target);
        }

        private Task ExecuteExtractZipSmartForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteExtractZipForPaneCoreInternalAsync(
                panelId,
                target,
                (archivePath, progress, cancellationToken) =>
                    _fileManagementCoordinator.TryExtractZipSmartAsync(archivePath, progress, cancellationToken),
                "StatusExtractZipFailed",
                "ExtractZipFailureDialogTitle",
                "StatusExtractZipSuccess",
                "extract-smart");
        }

        private Task ExecuteExtractZipHereForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteExtractZipForPaneCoreInternalAsync(
                panelId,
                target,
                (archivePath, progress, cancellationToken) =>
                    _fileManagementCoordinator.TryExtractZipHereAsync(archivePath, progress, cancellationToken),
                "StatusExtractZipFailed",
                "ExtractZipFailureDialogTitle",
                "StatusExtractZipSuccess",
                "extract-here");
        }

        private Task ExecuteExtractZipToFolderForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteExtractZipForPaneCoreInternalAsync(
                panelId,
                target,
                (archivePath, progress, cancellationToken) =>
                    _fileManagementCoordinator.TryExtractZipToFolderAsync(archivePath, progress, cancellationToken),
                "StatusExtractZipFailed",
                "ExtractZipFailureDialogTitle",
                "StatusExtractZipSuccess",
                "extract-to-folder");
        }

        private Task ExecuteOpenTargetForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return ExecuteOpenTargetForPaneCoreInternalAsync(panelId, target);
        }

        private bool CanCreateForPaneCore(WorkspacePanelId panelId)
        {
            string currentPath = GetPanelCurrentPath(panelId);
            return !string.IsNullOrWhiteSpace(currentPath) &&
                !string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanRenameForPaneCore(WorkspacePanelId panelId)
        {
            return GetSelectedLoadedEntryCountForPane(panelId) == 1;
        }

        private bool CanDeleteForPaneCore(WorkspacePanelId panelId)
        {
            List<EntryViewModel> entries = GetSelectedLoadedEntriesForPane(panelId);
            if (entries.Count == 0)
            {
                return false;
            }

            return entries.All(entry => !IsDriveRoot(GetPaneEntryPath(panelId, entry)));
        }

        private bool CanCopyForPaneCore(WorkspacePanelId panelId)
        {
            return GetSelectedLoadedEntryCountForPane(panelId) > 0;
        }

        private bool CanCutForPaneCore(WorkspacePanelId panelId)
        {
            List<EntryViewModel> entries = GetSelectedLoadedEntriesForPane(panelId);
            if (entries.Count == 0)
            {
                return false;
            }

            return entries.All(entry => !IsDriveRoot(GetPaneEntryPath(panelId, entry)));
        }

        private bool CanPasteForPaneCore(WorkspacePanelId panelId)
        {
            bool canPasteIntoPane = CanCreateForPaneCore(panelId);
            return canPasteIntoPane && _fileManagementCoordinator.HasAvailablePasteItems();
        }

        private bool CanRefreshForPaneCore(WorkspacePanelId panelId)
        {
            return !string.Equals(GetPanelCurrentPath(panelId), ShellMyComputerPath, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanPasteTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            string currentDirectoryPath = GetPanelCurrentPath(panelId);
            return !string.IsNullOrWhiteSpace(target.Path) &&
                !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(target.Path, currentDirectoryPath, StringComparison.OrdinalIgnoreCase) &&
                _fileManagementCoordinator.HasAvailablePasteItems();
        }

        private bool CanCreateTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            string currentDirectoryPath = GetPanelCurrentPath(panelId);
            return !string.IsNullOrWhiteSpace(target.Path) &&
                string.Equals(target.Path, currentDirectoryPath, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currentDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                _explorerService.DirectoryExists(currentDirectoryPath);
        }

        private bool CanRefreshTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            string currentDirectoryPath = GetPanelCurrentPath(panelId);
            return !string.IsNullOrWhiteSpace(target.Path) &&
                string.Equals(target.Path, currentDirectoryPath, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currentDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanCopyTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.Path) && _explorerService.PathExists(target.Path);
        }

        private bool CanCutTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.Path) &&
                !IsDriveRoot(target.Path) &&
                _explorerService.PathExists(target.Path);
        }

        private bool CanRenameTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.Path) &&
                !IsDriveRoot(target.Path) &&
                _explorerService.PathExists(target.Path);
        }

        private bool CanDeleteTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.Path) &&
                !IsDriveRoot(target.Path) &&
                _explorerService.PathExists(target.Path);
        }

        private bool TryBuildSelectedDeleteTargetForPane(WorkspacePanelId panelId, out FileCommandTarget target)
        {
            return TryBuildSelectedTargetForPane(
                panelId,
                FileCommandCapabilities.Delete | FileCommandCapabilities.Copy | FileCommandCapabilities.Cut,
                out target);
        }

        private bool TryBuildSelectedRenameTargetForPane(WorkspacePanelId panelId, out FileCommandTarget target)
        {
            return TryBuildSelectedTargetForPane(panelId, FileCommandCapabilities.Rename, out target);
        }

        private bool TryBuildSelectedTargetForPane(
            WorkspacePanelId panelId,
            FileCommandCapabilities capabilities,
            out FileCommandTarget target)
        {
            if (TryGetSelectedLoadedEntryForPane(panelId, out EntryViewModel? entry))
            {
                string targetPath = GetPaneEntryPath(panelId, entry);
                target = new FileCommandTarget(
                    entry.IsDirectory ? FileCommandTargetKind.DirectoryEntry : FileCommandTargetKind.FileEntry,
                    targetPath,
                    entry.Name,
                    entry.IsDirectory,
                    false,
                    FileEntryTraits.None,
                    capabilities);
                return true;
            }

            target = new FileCommandTarget(
                FileCommandTargetKind.None,
                string.Empty,
                string.Empty,
                false,
                false,
                FileEntryTraits.None,
                FileCommandCapabilities.None);
            return false;
        }

        private async Task ExecuteRenameTargetForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return;
            }

            if (!TryGetPaneLoadedEntryByPath(panelId, target.Path, out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return;
            }

            if (_entriesFlyoutOpen)
            {
                _pendingContextRenameEntry = entry;
                _pendingContextRenamePanelId = panelId;
                return;
            }

            await BeginRenameOverlayAsync(entry!, panelId: panelId);
        }

        private async Task ExecuteDeleteTargetForPaneCoreAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                UpdateStatusKey("StatusDeleteFailedSelectLoaded");
                return;
            }

            if (IsDriveRoot(target.Path))
            {
                UpdateStatusKey("StatusDeleteFailedWithReason", S("StatusCutFailedDriveRootsUnsupported"));
                return;
            }

            bool recursive = target.IsDirectory;
            while (true)
            {
                try
                {
                    if (_appSettings.ConfirmDelete && !await ConfirmDeleteAsync(target.DisplayName, recursive))
                    {
                        return;
                    }

                    FileOperationResult<bool> deleteResult = await RunFileOperationWithProgressAsync(
                        "OperationProgressDeleteTitle",
                        "OperationProgressDeleteMessage",
                        (progress, cancellationToken) =>
                            _fileManagementCoordinator.TryDeleteEntryAsync(target.Path, recursive, progress, cancellationToken));
                    FileOperationsController.DeleteDecision deleteDecision = _fileOperationsController.AnalyzeDeleteResult(
                        deleteResult,
                        S("ErrorFileOperationUnknown"));
                    if (!deleteDecision.Succeeded)
                    {
                        if (deleteDecision.Canceled)
                        {
                            return;
                        }

                        if (!await ShowDeleteFailureDialogAsync(
                                target.DisplayName,
                                deleteResult.Failure?.Error ?? FileOperationError.Unknown,
                                deleteDecision.FailureMessage ?? S("ErrorFileOperationUnknown")))
                        {
                            return;
                        }

                        continue;
                    }

                    await HandleDeletedEntryForPaneAsync(panelId, target, deleteDecision.ChangeNotified);
                    UpdateStatusKey("StatusDeleteSuccess", target.DisplayName, recursive);
                    return;
                }
                catch (Exception ex)
                {
                    if (!await ShowDeleteFailureDialogAsync(
                            target.DisplayName,
                            FileOperationErrors.Classify(ex),
                            FileOperationErrors.ToUserMessage(ex)))
                    {
                        return;
                    }
                }
            }
        }

        private async Task ExecuteDeleteEntriesForPaneCoreAsync(WorkspacePanelId panelId, IReadOnlyList<EntryViewModel> entries)
        {
            var targets = entries
                .Select(entry =>
                {
                    string path = GetPaneEntryPath(panelId, entry);
                    return new FileCommandTarget(
                        entry.IsDirectory ? FileCommandTargetKind.DirectoryEntry : FileCommandTargetKind.FileEntry,
                        path,
                        entry.Name,
                        entry.IsDirectory,
                        false,
                        FileEntryTraits.None,
                        FileCommandCapabilities.Delete | FileCommandCapabilities.Copy | FileCommandCapabilities.Cut);
                })
                .Where(target => !string.IsNullOrWhiteSpace(target.Path) && !IsDriveRoot(target.Path))
                .ToList();

            if (targets.Count == 0)
            {
                UpdateStatusKey("StatusDeleteFailedSelectLoaded");
                return;
            }

            if (_appSettings.ConfirmDelete && !await ConfirmDeleteAsync($"{targets.Count} 个项目", targets.Any(target => target.IsDirectory)))
            {
                return;
            }

            int deletedCount = 0;
            foreach (FileCommandTarget target in targets)
            {
                bool recursive = target.IsDirectory;
                FileOperationResult<bool> deleteResult = await RunFileOperationWithProgressAsync(
                    "OperationProgressDeleteTitle",
                    "OperationProgressDeleteMessage",
                    (progress, cancellationToken) =>
                        _fileManagementCoordinator.TryDeleteEntryAsync(target.Path!, recursive, progress, cancellationToken));
                FileOperationsController.DeleteDecision deleteDecision = _fileOperationsController.AnalyzeDeleteResult(
                    deleteResult,
                    S("ErrorFileOperationUnknown"));
                if (!deleteDecision.Succeeded)
                {
                    if (deleteDecision.Canceled)
                    {
                        return;
                    }

                    await ShowDeleteFailureDialogAsync(
                        target.DisplayName,
                        deleteResult.Failure?.Error ?? FileOperationError.Unknown,
                        deleteDecision.FailureMessage ?? S("ErrorFileOperationUnknown"));
                    continue;
                }

                await HandleDeletedEntryForPaneAsync(panelId, target, deleteDecision.ChangeNotified);
                deletedCount++;
            }

            ClearPanelSelection(panelId, clearAnchor: true);
            UpdateStatusKey("StatusDeleteMultipleSuccess", deletedCount);
        }

        private async Task HandleDeletedEntryForPaneAsync(WorkspacePanelId panelId, FileCommandTarget target, bool changeNotified)
        {
            string targetPath = target.Path!;
            TryRemoveFavoritesForDeletedPath(targetPath);
            HandleDeleteFallbackInvalidationForPanel(panelId, targetPath, changeNotified);

            if (await HandleDeleteMutationForPanelAsync(panelId, targetPath))
            {
                return;
            }
        }

        private async Task ExecutePasteIntoPaneDirectoryCoreAsync(
            WorkspacePanelId panelId,
            string targetDirectoryPath,
            bool selectPastedEntry,
            bool deferContentRefresh = false)
        {
            if (string.IsNullOrWhiteSpace(targetDirectoryPath) ||
                string.Equals(targetDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusPasteFailedOpenFolderFirst");
                return;
            }

            if (!_explorerService.DirectoryExists(targetDirectoryPath))
            {
                UpdateStatusKey("StatusPasteFailedWithReason", S("ErrorCurrentFolderUnavailable"));
                return;
            }

            if (!_fileManagementCoordinator.HasAvailablePasteItems())
            {
                UpdateStatusKey("StatusPasteFailedClipboardEmpty");
                return;
            }

            try
            {
                while (true)
                {
                    PreparePanelDirectoryMutation(panelId, targetDirectoryPath);

                    FilePasteOperationResult pasteOperation = await RunFileOperationWithProgressAsync(
                        "OperationProgressTransferTitle",
                        "OperationProgressTransferMessage",
                        (progress, cancellationToken) =>
                            _fileManagementCoordinator.TryPasteAsync(targetDirectoryPath, progress, cancellationToken));
                    if (!pasteOperation.Succeeded || pasteOperation.PasteResult is null)
                    {
                        if (pasteOperation.Failure?.Error == FileOperationError.Canceled)
                        {
                            UpdateStatus(S("ErrorFileOperationCanceled"));
                            return;
                        }

                        string failureMessage = pasteOperation.Failure?.Message ?? S("ErrorFileOperationUnknown");
                        await ShowOperationFailureDialogAsync(
                            "PasteFailureDialogTitle",
                            SF("StatusPasteFailedWithReason", failureMessage));
                        return;
                    }

                    FilePasteResult result = pasteOperation.PasteResult;
                    (int appliedCount, int conflictCount, int samePathCount, int failureCount, string? firstAppliedPath, bool appliedDirectory) =
                        SummarizePasteResult(result);

                    if (TryGetPasteTargetIsSourceDescendantFailure(result, out FilePasteItemResult descendantFailure))
                    {
                        string itemName = Path.GetFileName(descendantFailure.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        bool skip = await ShowPasteTargetIsSourceDescendantDialogAsync(itemName);
                        if (!skip)
                        {
                            return;
                        }

                        if (appliedCount == 0 && conflictCount == 0)
                        {
                            UpdateStatusKey("StatusPasteSkippedNothingApplied");
                            return;
                        }
                    }

                    while (conflictCount > 0)
                    {
                        FilePasteItemResult? firstConflict = result.Items.FirstOrDefault(item => item.Conflict);
                        string conflictName = Path.GetFileName(firstConflict?.TargetPath ?? string.Empty);
                        PasteConflictDialogDecision decision = await ShowPasteConflictDialogAsync(
                            conflictName,
                            firstConflict?.IsDirectory ?? false,
                            conflictCount);

                        if (decision == PasteConflictDialogDecision.Skip)
                        {
                            if (appliedCount == 0)
                            {
                                return;
                            }

                            break;
                        }

                        FilePasteOperationResult replaceOperation = await RunFileOperationWithProgressAsync(
                            "OperationProgressTransferTitle",
                            "OperationProgressTransferMessage",
                            (progress, cancellationToken) =>
                                _fileManagementCoordinator.TryResolvePasteConflictsAsync(
                                    result,
                                    decision == PasteConflictDialogDecision.ReplaceAll,
                                    progress,
                                    cancellationToken));
                        if (!replaceOperation.Succeeded || replaceOperation.PasteResult is null)
                        {
                            if (replaceOperation.Failure?.Error == FileOperationError.Canceled)
                            {
                                UpdateStatus(S("ErrorFileOperationCanceled"));
                                return;
                            }

                            string failureMessage = replaceOperation.Failure?.Message ?? S("ErrorFileOperationUnknown");
                            await ShowOperationFailureDialogAsync(
                                "PasteFailureDialogTitle",
                                SF("StatusPasteFailedWithReason", failureMessage));
                            return;
                        }

                        result = replaceOperation.PasteResult;
                        (appliedCount, conflictCount, samePathCount, failureCount, firstAppliedPath, appliedDirectory) =
                            SummarizePasteResult(result);
                    }

                    if (appliedCount == 0)
                    {
                        if (samePathCount > 0)
                        {
                            UpdateStatusKey("StatusPasteSkippedSamePath");
                            return;
                        }

                        if (failureCount > 0 && result.Items.Count > 0)
                        {
                            string? message = result.Items[0].ErrorMessage;
                            await ShowOperationFailureDialogAsync(
                                "PasteFailureDialogTitle",
                                SF("StatusPasteFailedWithReason", message ?? S("ErrorFileOperationUnknown")));
                            return;
                        }

                        UpdateStatusKey("StatusPasteSkippedNothingApplied");
                        return;
                    }

                    if (!result.TargetChanged)
                    {
                        EnsurePersistentRefreshFallbackInvalidation(targetDirectoryPath, result.Mode == FileTransferMode.Cut ? "cut-paste" : "copy-paste");
                    }

                    await HandlePastedEntriesForPaneAsync(
                        panelId,
                        targetDirectoryPath,
                        selectPastedEntry,
                        deferContentRefresh,
                        appliedCount,
                        samePathCount,
                        failureCount,
                        appliedDirectory,
                        firstAppliedPath,
                        result.Mode,
                        result.Items);
                    return;
                }
            }
            catch (Exception ex)
            {
                await ShowOperationFailureDialogAsync(
                    "PasteFailureDialogTitle",
                    SF("StatusPasteFailedWithReason", FileOperationErrors.ToUserMessage(ex)));
            }
        }

        private static bool TryGetPasteTargetIsSourceDescendantFailure(
            FilePasteResult result,
            out FilePasteItemResult failure)
        {
            foreach (FilePasteItemResult item in result.Items)
            {
                if (item.Error == FileOperationError.TargetIsSourceDescendant)
                {
                    failure = item;
                    return true;
                }
            }

            failure = default;
            return false;
        }

        private static (int AppliedCount, int ConflictCount, int SamePathCount, int FailureCount, string? FirstAppliedPath, bool AppliedDirectory) SummarizePasteResult(FilePasteResult result)
        {
            int appliedCount = 0;
            int conflictCount = 0;
            int samePathCount = 0;
            int failureCount = 0;
            string? firstAppliedPath = null;
            bool appliedDirectory = false;

            foreach (FilePasteItemResult item in result.Items)
            {
                if (item.Applied)
                {
                    appliedCount++;
                    firstAppliedPath ??= item.TargetPath;
                    appliedDirectory |= item.IsDirectory;
                    continue;
                }

                if (item.Conflict)
                {
                    conflictCount++;
                    continue;
                }

                if (item.SamePath)
                {
                    samePathCount++;
                    continue;
                }

                failureCount++;
            }

            return (appliedCount, conflictCount, samePathCount, failureCount, firstAppliedPath, appliedDirectory);
        }

        private async Task HandlePastedEntriesForPaneAsync(
            WorkspacePanelId panelId,
            string targetDirectoryPath,
            bool selectPastedEntry,
            bool deferContentRefresh,
            int appliedCount,
            int samePathCount,
            int failureCount,
            bool appliedDirectory,
            string? firstAppliedPath,
            FileTransferMode mode,
            System.Collections.Generic.IReadOnlyList<FilePasteItemResult> itemResults)
        {
            await HandlePasteMutationForPanelAsync(
                panelId,
                targetDirectoryPath,
                selectPastedEntry,
                deferContentRefresh,
                appliedCount,
                appliedDirectory,
                firstAppliedPath,
                mode,
                itemResults);

            UpdateFileCommandStates();
            string modeText = mode == FileTransferMode.Cut ? S("OperationMove") : S("OperationPaste");
            UpdateStatusKey("StatusTransferSuccess", modeText, appliedCount, 0, samePathCount, failureCount);
        }

        private async Task ExecuteCreateShortcutForPaneCoreInternalAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            string destinationDirectory = GetPanelCurrentPath(panelId);
            if (string.IsNullOrWhiteSpace(destinationDirectory) ||
                string.Equals(destinationDirectory, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                !_explorerService.DirectoryExists(destinationDirectory))
            {
                UpdateStatusKey("StatusCreateShortcutFailed", S("ErrorCurrentFolderUnavailable"));
                return;
            }

            try
            {
                PreparePanelDirectoryMutation(panelId, destinationDirectory);

                FileOperationResult<CreatedEntryInfo> createResult = await _fileManagementCoordinator.TryCreateShortcutAsync(destinationDirectory, target.Path);
                if (!createResult.Succeeded)
                {
                    UpdateStatusKey("StatusCreateShortcutFailed", createResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }

                CreatedEntryInfo created = createResult.Value!;
                await HandleCreatedTargetEntryForPaneAsync(panelId, destinationDirectory, created, "create-shortcut");
                UpdateStatusKey("StatusCreateShortcutSuccess", created.Name);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCreateShortcutFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task HandleCreatedTargetEntryForPaneAsync(
            WorkspacePanelId panelId,
            string destinationDirectory,
            CreatedEntryInfo created,
            string invalidationReason)
        {
            await HandleCreatedTargetMutationForPanelAsync(
                panelId,
                destinationDirectory,
                created.FullPath,
                created.ChangeNotified,
                invalidationReason);
            UpdateFileCommandStates();
        }

        private async Task ExecuteCompressZipForPaneCoreInternalAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                string sourcePath = target.Path;
                string destinationDirectory = ResolvePaneTargetDirectory(panelId, sourcePath);
                if (string.IsNullOrWhiteSpace(destinationDirectory) || !_explorerService.DirectoryExists(destinationDirectory))
                {
                    UpdateStatusKey("StatusCompressZipFailed", S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                string archiveName = _explorerService.GenerateUniqueZipArchiveName(destinationDirectory, sourcePath);
                string archivePath = Path.Combine(destinationDirectory, archiveName);
                bool destinationIsCurrentPaneDirectory = IsPaneCurrentDirectory(panelId, destinationDirectory);

                if (destinationIsCurrentPaneDirectory)
                {
                    PreparePanelDirectoryMutation(panelId, destinationDirectory);
                }

                FileOperationResult<string> zipResult = await RunFileOperationWithProgressAsync(
                    "OperationProgressCompressTitle",
                    "OperationProgressCompressMessage",
                    (progress, cancellationToken) =>
                        _fileManagementCoordinator.TryCreateZipArchiveAsync(sourcePath, archivePath, progress, cancellationToken));
                if (!zipResult.Succeeded)
                {
                    if (zipResult.Failure?.Error == FileOperationError.Canceled)
                    {
                        UpdateStatus(S("ErrorFileOperationCanceled"));
                        return;
                    }

                    await ShowOperationFailureDialogAsync(
                        "CompressZipFailureDialogTitle",
                        SF("StatusCompressZipFailed", zipResult.Failure?.Message ?? S("ErrorFileOperationUnknown")));
                    return;
                }

                await HandleArchiveCreatedForPaneAsync(
                    panelId,
                    destinationDirectory,
                    archivePath,
                    "compress-zip");
                UpdateStatusKey("StatusCompressZipSuccess", archiveName);
            }
            catch (Exception ex)
            {
                await ShowOperationFailureDialogAsync(
                    "CompressZipFailureDialogTitle",
                    SF("StatusCompressZipFailed", FileOperationErrors.ToUserMessage(ex)));
            }
        }

        private async Task ExecuteExtractZipForPaneCoreInternalAsync(
            WorkspacePanelId panelId,
            FileCommandTarget target,
            Func<string, FileOperationProgressStore, CancellationToken, Task<FileOperationResult<ZipExtractionInfo>>> extractAsync,
            string failureStatusKey,
            string failureDialogTitleKey,
            string successStatusKey,
            string invalidationReason)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                string archivePath = target.Path;
                string destinationDirectory = ResolvePaneTargetDirectory(panelId, archivePath);
                if (string.IsNullOrWhiteSpace(destinationDirectory) || !_explorerService.DirectoryExists(destinationDirectory))
                {
                    UpdateStatusKey(failureStatusKey, S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                bool destinationIsCurrentPaneDirectory = IsPaneCurrentDirectory(panelId, destinationDirectory);
                if (destinationIsCurrentPaneDirectory)
                {
                    PreparePanelDirectoryMutation(panelId, destinationDirectory);
                }

                FileOperationResult<ZipExtractionInfo> extractResult = await RunFileOperationWithProgressAsync(
                    "OperationProgressExtractTitle",
                    "OperationProgressExtractMessage",
                    (progress, cancellationToken) => extractAsync(archivePath, progress, cancellationToken));
                if (!extractResult.Succeeded)
                {
                    if (extractResult.Failure?.Error == FileOperationError.Canceled)
                    {
                        UpdateStatus(S("ErrorFileOperationCanceled"));
                        return;
                    }

                    await ShowOperationFailureDialogAsync(
                        failureDialogTitleKey,
                        SF(failureStatusKey, extractResult.Failure?.Message ?? S("ErrorFileOperationUnknown")));
                    return;
                }

                ZipExtractionInfo extracted = extractResult.Value!;
                await HandleExtractedEntriesForPaneAsync(
                    panelId,
                    destinationDirectory,
                    extracted,
                    invalidationReason);

                string? extractedName = string.IsNullOrWhiteSpace(extracted.PrimarySelectionPath)
                    ? null
                    : Path.GetFileName(extracted.PrimarySelectionPath.TrimEnd('\\'));
                UpdateStatusKey(successStatusKey, extractedName ?? target.DisplayName);
            }
            catch (Exception ex)
            {
                await ShowOperationFailureDialogAsync(
                    failureDialogTitleKey,
                    SF(failureStatusKey, FileOperationErrors.ToUserMessage(ex)));
            }
        }

        private async Task ExecuteOpenTargetForPaneCoreInternalAsync(WorkspacePanelId panelId, FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || target.IsDirectory)
            {
                return;
            }

            try
            {
                string resolvedTargetPath = await Task.Run(() => _explorerService.ResolveShortcutTargetPath(target.Path));
                if (Uri.TryCreate(resolvedTargetPath, UriKind.Absolute, out Uri? uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    _ = Process.Start(new ProcessStartInfo
                    {
                        FileName = resolvedTargetPath,
                        UseShellExecute = true
                    });
                    UpdateStatusKey("StatusOpened", target.DisplayName);
                    return;
                }

                if (_explorerService.DirectoryExists(resolvedTargetPath))
                {
                    await NavigatePanelToPathAsync(panelId, resolvedTargetPath, pushHistory: true);
                    return;
                }

                if (_explorerService.PathExists(resolvedTargetPath))
                {
                    _ = Process.Start(new ProcessStartInfo
                    {
                        FileName = resolvedTargetPath,
                        UseShellExecute = true
                    });
                    UpdateStatusKey("StatusOpened", Path.GetFileName(resolvedTargetPath));
                    return;
                }

                UpdateStatusKey("StatusOpenTargetFailed", resolvedTargetPath);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenTargetFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task HandleArchiveCreatedForPaneAsync(
            WorkspacePanelId panelId,
            string destinationDirectory,
            string archivePath,
            string invalidationReason)
        {
            await HandleCreatedTargetMutationForPanelAsync(
                panelId,
                destinationDirectory,
                archivePath,
                changeNotified: false,
                invalidationReason);
            UpdateFileCommandStates();
        }

        private async Task HandleExtractedEntriesForPaneAsync(
            WorkspacePanelId panelId,
            string destinationDirectory,
            ZipExtractionInfo extracted,
            string invalidationReason)
        {
            await HandleCreatedTargetMutationForPanelAsync(
                panelId,
                destinationDirectory,
                extracted.PrimarySelectionPath,
                extracted.ChangeNotified,
                invalidationReason);
            UpdateFileCommandStates();
        }

        private string ResolvePaneTargetDirectory(WorkspacePanelId panelId, string sourcePath)
        {
            return Path.GetDirectoryName(sourcePath.TrimEnd('\\')) ??
                GetPanelCurrentPath(panelId);
        }

        private bool IsPaneCurrentDirectory(WorkspacePanelId panelId, string directoryPath)
        {
            string currentDirectory = GetPanelCurrentPath(panelId);
            return string.Equals(directoryPath, currentDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanCreateShortcutTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            string currentDirectoryPath = GetPanelCurrentPath(panelId);
            return !string.IsNullOrWhiteSpace(target.Path) &&
                !string.Equals(currentDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                _explorerService.DirectoryExists(currentDirectoryPath);
        }

        private bool CanCompressZipTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.Path) &&
                target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry &&
                _explorerService.PathExists(target.Path);
        }

        private bool CanExtractZipTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.Path) &&
                target.Kind == FileCommandTargetKind.FileEntry &&
                string.Equals(Path.GetExtension(target.Path), ".zip", StringComparison.OrdinalIgnoreCase) &&
                _explorerService.PathExists(target.Path);
        }

        private bool CanOpenTargetForPaneCore(WorkspacePanelId panelId, FileCommandTarget target)
        {
            return !string.IsNullOrWhiteSpace(target.Path) && !target.IsDirectory;
        }

        private WorkspacePanelId ResolvePanelIdForEntriesContextOrigin(EntriesContextOrigin origin)
        {
            return origin == EntriesContextOrigin.SecondaryEntriesList
                ? WorkspacePanelId.Secondary
                : WorkspacePanelId.Primary;
        }
    }
}
