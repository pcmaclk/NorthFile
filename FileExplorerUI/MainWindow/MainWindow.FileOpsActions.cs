using FileExplorerUI.Services;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async Task RenameEntryAsync(EntryViewModel entry, int selectedIndex, string newName)
        {
            string src = Path.Combine(_currentPath, entry.Name);
            string oldName = entry.Name;
            TreeViewNode? renamedTreeNode = entry.IsDirectory ? FindSidebarTreeNodeByPath(src) : null;
            try
            {
                FileOperationResult<RenamedEntryInfo> renameResult = await _fileManagementCoordinator.TryRenameEntryAsync(_currentPath, entry.Name, newName);
                FileOperationsController.RenameDecision renameDecision = _fileOperationsController.AnalyzeRenameResult(
                    renameResult,
                    S("ErrorFileOperationUnknown"));
                if (!renameDecision.Succeeded || renameDecision.RenamedInfo is null)
                {
                    if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                    {
                        CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                    }
                    UpdateStatusKey("StatusRenameFailedWithReason", renameDecision.FailureMessage ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                RenamedEntryInfo renamed = renameDecision.RenamedInfo.Value;
                RenameTextBox.Text = string.Empty;
                HideRenameOverlay();
                _selectedEntryPath = renamed.TargetPath;
                if (!renamed.ChangeNotified)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, "rename");
                }
                if (entry.IsDirectory)
                {
                    if (renamedTreeNode is not null)
                    {
                        UpdateSidebarTreeNodePath(renamedTreeNode, renamed.SourcePath, renamed.TargetPath, newName);
                    }
                    else if (FindSidebarTreeNodeByPath(_currentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                    {
                        await PopulateSidebarTreeChildrenAsync(parentNode, _currentPath, CancellationToken.None, expandAfterLoad: true);
                    }
                }
                ApplyLocalRename(selectedIndex, newName);
                if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                {
                    CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                }
                else
                {
                    _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                }

                TryUpdateFavoritePathsForRename(renamed.SourcePath, renamed.TargetPath);
                UpdateStatusKey("StatusRenameSuccess", oldName, newName);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                {
                    CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                }
                UpdateStatusKey("StatusRenameFailedWithReason", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task DeleteEntryAsync(EntryViewModel entry, int selectedIndex, string targetPath, bool recursive)
        {
            try
            {
                if (_appSettings.ConfirmDelete && !await ConfirmDeleteAsync(entry.Name, recursive))
                {
                    UpdateStatusKey("StatusDeleteCanceled");
                    return;
                }

                FileOperationResult<bool> deleteResult = await _fileManagementCoordinator.TryDeleteEntryAsync(targetPath, recursive);
                FileOperationsController.DeleteDecision deleteDecision = _fileOperationsController.AnalyzeDeleteResult(
                    deleteResult,
                    S("ErrorFileOperationUnknown"));
                if (!deleteDecision.Succeeded)
                {
                    if (deleteDecision.Canceled)
                    {
                        UpdateStatusKey("StatusDeleteCanceled");
                        return;
                    }

                    UpdateStatusKey("StatusDeleteFailedWithReason", deleteDecision.FailureMessage ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                bool changeNotified = deleteDecision.ChangeNotified;
                if (!changeNotified)
                {
                    string parentPath = _fileOperationsController.ResolveDeleteFallbackParentPath(targetPath, _currentPath);
                    EnsurePersistentRefreshFallbackInvalidation(parentPath, "delete");
                }

                TryRemoveFavoritesForDeletedPath(targetPath);
                ApplyLocalDelete(selectedIndex);
                _ = RefreshCurrentDirectoryInBackgroundAsync(preserveViewport: true);
                UpdateStatusKey("StatusDeleteSuccess", entry.Name, recursive);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusDeleteFailedWithReason", FileOperationErrors.ToUserMessage(ex));
            }
        }
    }
}
