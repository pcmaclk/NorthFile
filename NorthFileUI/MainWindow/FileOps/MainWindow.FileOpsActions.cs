using NorthFileUI.Services;
using NorthFileUI.Workspace;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private enum PasteConflictDialogDecision
        {
            Replace,
            ReplaceAll,
            Skip
        }

        private async Task RenameEntryAsync(EntryViewModel entry, int selectedIndex, string newName)
        {
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            string src = Path.Combine(currentPath, entry.Name);
            string oldName = entry.Name;
            TreeViewNode? renamedTreeNode = entry.IsDirectory ? FindSidebarTreeNodeByPath(src) : null;
            while (true)
            {
                try
                {
                    FileOperationResult<RenamedEntryInfo> renameResult = await _fileManagementCoordinator.TryRenameEntryAsync(currentPath, entry.Name, newName);
                    FileOperationsController.RenameDecision renameDecision = _fileOperationsController.AnalyzeRenameResult(
                        renameResult,
                        S("ErrorFileOperationUnknown"));
                    if (!renameDecision.Succeeded || renameDecision.RenamedInfo is null)
                    {
                        if (!await ShowRenameFailureDialogAsync(
                            oldName,
                            renameResult.Failure?.Error ?? FileOperationError.Unknown,
                            renameDecision.FailureMessage ?? S("ErrorFileOperationUnknown")))
                        {
                            RenameOverlayTextBox.Focus(FocusState.Programmatic);
                            SelectRenameTargetText(RenameOverlayTextBox, entry);
                            return;
                        }

                        continue;
                    }

                    RenamedEntryInfo renamed = renameDecision.RenamedInfo.Value;
                    RenameTextBox.Text = string.Empty;
                    HideRenameOverlay();
                    _selectedEntryPath = renamed.TargetPath;
                    if (!renamed.ChangeNotified)
                    {
                        EnsurePersistentRefreshFallbackInvalidation(currentPath, "rename");
                    }
                    if (entry.IsDirectory)
                    {
                        if (renamedTreeNode is not null)
                        {
                            UpdateSidebarTreeNodePath(renamedTreeNode, renamed.SourcePath, renamed.TargetPath, newName);
                        }
                        else if (FindSidebarTreeNodeByPath(currentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                        {
                            await PopulateSidebarTreeChildrenAsync(parentNode, currentPath, CancellationToken.None, expandAfterLoad: true);
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
                    return;
                }
                catch (Exception ex)
                {
                    if (!await ShowRenameFailureDialogAsync(oldName, FileOperationErrors.Classify(ex), FileOperationErrors.ToUserMessage(ex)))
                    {
                        RenameOverlayTextBox.Focus(FocusState.Programmatic);
                        SelectRenameTargetText(RenameOverlayTextBox, entry);
                        return;
                    }
                }
            }
        }

        private async Task<bool> ShowRenameFailureDialogAsync(string itemName, FileOperationError error, string detailMessage)
        {
            string titleKey;
            string bodyKey;

            switch (error)
            {
                case FileOperationError.InUse:
                    titleKey = "RenameFailureDialogInUseTitle";
                    bodyKey = "RenameFailureDialogInUseBody";
                    break;
                case FileOperationError.AccessDenied:
                    titleKey = "RenameFailureDialogAccessDeniedTitle";
                    bodyKey = "RenameFailureDialogAccessDeniedBody";
                    break;
                default:
                    titleKey = "RenameFailureDialogGenericTitle";
                    bodyKey = "RenameFailureDialogGenericBody";
                    break;
            }

            var body = new StackPanel
            {
                Spacing = 12
            };
            body.Children.Add(new TextBlock
            {
                Text = string.Format(S(bodyKey), itemName),
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(detailMessage) &&
                !string.Equals(detailMessage, S("ErrorFileOperationUnknown"), StringComparison.Ordinal))
            {
                body.Children.Add(new TextBlock
                {
                    Text = detailMessage,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8
                });
            }

            var dialog = new ContentDialog
            {
                Title = S(titleKey),
                Content = body,
                PrimaryButtonText = S("DialogRetryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };

            PrepareWindowDialog(dialog);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task DeleteEntryAsync(EntryViewModel entry, int selectedIndex, string targetPath, bool recursive)
        {
            while (true)
            {
                try
                {
                    if (_appSettings.ConfirmDelete && !await ConfirmDeleteAsync(entry.Name, recursive))
                    {
                        return;
                    }

                    FileOperationResult<bool> deleteResult = await RunFileOperationWithProgressAsync(
                        "OperationProgressDeleteTitle",
                        "OperationProgressDeleteMessage",
                        (progress, cancellationToken) =>
                            _fileManagementCoordinator.TryDeleteEntryAsync(targetPath, recursive, progress, cancellationToken));
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
                            entry.Name,
                            deleteResult.Failure?.Error ?? FileOperationError.Unknown,
                            deleteDecision.FailureMessage ?? S("ErrorFileOperationUnknown")))
                        {
                            return;
                        }

                        continue;
                    }

                    bool changeNotified = deleteDecision.ChangeNotified;
                    if (!changeNotified)
                    {
                        string parentPath = _fileOperationsController.ResolveDeleteFallbackParentPath(
                            targetPath,
                            GetPanelCurrentPath(WorkspacePanelId.Primary));
                        EnsurePersistentRefreshFallbackInvalidation(parentPath, "delete");
                    }

                    TryRemoveFavoritesForDeletedPath(targetPath);
                    ApplyLocalDelete(selectedIndex);
                    UpdateStatusKey("StatusDeleteSuccess", entry.Name, recursive);
                    return;
                }
                catch (Exception ex)
                {
                    if (!await ShowDeleteFailureDialogAsync(entry.Name, FileOperationErrors.Classify(ex), FileOperationErrors.ToUserMessage(ex)))
                    {
                        return;
                    }
                }
            }
        }

        private async Task<bool> ShowDeleteFailureDialogAsync(string itemName, FileOperationError error, string detailMessage)
        {
            string titleKey;
            string bodyKey;

            switch (error)
            {
                case FileOperationError.InUse:
                    titleKey = "DeleteFailureDialogInUseTitle";
                    bodyKey = "DeleteFailureDialogInUseBody";
                    break;
                case FileOperationError.AccessDenied:
                    titleKey = "DeleteFailureDialogAccessDeniedTitle";
                    bodyKey = "DeleteFailureDialogAccessDeniedBody";
                    break;
                default:
                    titleKey = "DeleteFailureDialogGenericTitle";
                    bodyKey = "DeleteFailureDialogGenericBody";
                    break;
            }

            var body = new StackPanel
            {
                Spacing = 12
            };
            body.Children.Add(new TextBlock
            {
                Text = string.Format(S(bodyKey), itemName),
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(detailMessage) &&
                !string.Equals(detailMessage, S("ErrorFileOperationUnknown"), StringComparison.Ordinal))
            {
                body.Children.Add(new TextBlock
                {
                    Text = detailMessage,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8
                });
            }

            var dialog = new ContentDialog
            {
                Title = S(titleKey),
                Content = body,
                PrimaryButtonText = S("DialogRetryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };

            PrepareWindowDialog(dialog);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task<bool> ShowCreateFailureDialogAsync(string itemName, FileOperationError error, string detailMessage)
        {
            string titleKey;
            string bodyKey;

            switch (error)
            {
                case FileOperationError.InUse:
                    titleKey = "CreateFailureDialogInUseTitle";
                    bodyKey = "CreateFailureDialogInUseBody";
                    break;
                case FileOperationError.AccessDenied:
                    titleKey = "CreateFailureDialogAccessDeniedTitle";
                    bodyKey = "CreateFailureDialogAccessDeniedBody";
                    break;
                default:
                    titleKey = "CreateFailureDialogGenericTitle";
                    bodyKey = "CreateFailureDialogGenericBody";
                    break;
            }

            var body = new StackPanel
            {
                Spacing = 12
            };
            body.Children.Add(new TextBlock
            {
                Text = string.Format(S(bodyKey), itemName),
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(detailMessage) &&
                !string.Equals(detailMessage, S("ErrorFileOperationUnknown"), StringComparison.Ordinal))
            {
                body.Children.Add(new TextBlock
                {
                    Text = detailMessage,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8
                });
            }

            var dialog = new ContentDialog
            {
                Title = S(titleKey),
                Content = body,
                PrimaryButtonText = S("DialogRetryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };

            PrepareWindowDialog(dialog);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task<bool> ShowOperationFailureDialogAsync(string titleKey, string message, bool allowRetry = false)
        {
            EnsureOperationFeedbackOverlay();
            if (_operationFeedbackDialog is null)
            {
                return false;
            }

            Controls.ModalActionDialogResult result = await _operationFeedbackDialog.ShowAsync(
                S(titleKey),
                message,
                allowRetry ? S("DialogRetryButton") : S("DialogCloseButton"),
                allowRetry ? S("DialogCloseButton") : string.Empty);
            return allowRetry && result == Controls.ModalActionDialogResult.Primary;
        }

        private async Task<bool> ShowPasteTargetIsSourceDescendantDialogAsync(string? itemName)
        {
            EnsureOperationFeedbackOverlay();
            if (_operationFeedbackDialog is null)
            {
                return false;
            }

            string message = string.IsNullOrWhiteSpace(itemName)
                ? S("PasteTargetIsSourceDescendantDialogMessage")
                : SF("PasteTargetIsSourceDescendantDialogMessageWithItem", itemName);

            Controls.ModalActionDialogResult result = await _operationFeedbackDialog.ShowAsync(
                S("PasteInterruptedDialogTitle"),
                message,
                S("DialogSkipButton"),
                S("DialogCancelButton"));
            return result == Controls.ModalActionDialogResult.Primary;
        }

        private async Task<PasteConflictDialogDecision> ShowPasteConflictDialogAsync(string? itemName, bool isDirectory, int conflictCount)
        {
            string primaryMessage = conflictCount switch
            {
                1 when !string.IsNullOrWhiteSpace(itemName) => SF(isDirectory ? "PasteConflictDialogPrimarySingleFolder" : "PasteConflictDialogPrimarySingleFile", itemName),
                > 1 when !string.IsNullOrWhiteSpace(itemName) => SF("PasteConflictDialogPrimaryMultipleWithCurrent", conflictCount, itemName),
                _ => SF("PasteConflictDialogPrimaryMultiple", conflictCount)
            };

            EnsurePasteConflictOverlay();
            if (_pasteConflictDialog is null)
            {
                return PasteConflictDialogDecision.Skip;
            }

            Controls.ModalActionDialogResult result = await _pasteConflictDialog.ShowAsync(
                S("PasteConflictDialogTitle"),
                primaryMessage,
                conflictCount == 1
                    ? S("PasteConflictDialogReplaceSingleButton")
                    : S("PasteConflictDialogReplaceMultipleButton"),
                conflictCount == 1
                    ? S("PasteConflictDialogSkipSingleButton")
                    : S("PasteConflictDialogSkipMultipleButton"),
                conflictCount == 1
                    ? string.Empty
                    : S("PasteConflictDialogReplaceAllButton"));

            return result switch
            {
                Controls.ModalActionDialogResult.Primary => PasteConflictDialogDecision.Replace,
                Controls.ModalActionDialogResult.Tertiary => PasteConflictDialogDecision.ReplaceAll,
                _ => PasteConflictDialogDecision.Skip
            };
        }

        private void EnsurePasteConflictOverlay()
        {
            if (_pasteConflictDialog is not null)
            {
                return;
            }

            _pasteConflictDialog = new Controls.ModalActionDialog();
            AttachWindowOverlay(_pasteConflictDialog);
        }

        private void EnsureOperationFeedbackOverlay()
        {
            if (_operationFeedbackDialog is not null)
            {
                return;
            }

            _operationFeedbackDialog = new Controls.ModalActionDialog();
            AttachWindowOverlay(_operationFeedbackDialog);
        }

        private async Task<T> RunFileOperationWithProgressAsync<T>(
            string titleKey,
            string messageKey,
            Func<FileOperationProgressStore, CancellationToken, Task<T>> operation,
            string operationName = "file-operation",
            long totalItems = 1,
            long totalBytes = 0)
        {
            EnsureFileOperationProgressOverlay();
            using var cancellation = new CancellationTokenSource();
            var progressStore = new FileOperationProgressStore(operationName, totalItems, totalBytes);

            _fileOperationProgressOverlay?.Show(
                S(titleKey),
                S(messageKey),
                S("DialogCancelButton"),
                () =>
                {
                    _fileOperationProgressOverlay?.MarkCanceling(S("OperationProgressCanceling"));
                    progressStore.MarkCanceled();
                    cancellation.Cancel();
                });

            Task<T> completion = Task.Run(async () => await operation(progressStore, cancellation.Token));
            var job = new FileOperationJob<T>(progressStore, completion, cancellation);

            try
            {
                T result = await PollFileOperationJobAsync(job);
                progressStore.MarkCompleted();
                _fileOperationProgressOverlay?.Update(progressStore.Snapshot, S("OperationProgressCountFormat"));
                return result;
            }
            finally
            {
                _fileOperationProgressOverlay?.Close();
            }
        }

        private async Task<T> PollFileOperationJobAsync<T>(FileOperationJob<T> job)
        {
            FileOperationProgress lastSnapshot = job.ProgressStore.Snapshot;
            _fileOperationProgressOverlay?.Update(lastSnapshot, S("OperationProgressCountFormat"));

            while (!job.Completion.IsCompleted)
            {
                await Task.WhenAny(job.Completion, Task.Delay(16));

                FileOperationProgress snapshot = job.ProgressStore.Snapshot;
                if (!snapshot.Equals(lastSnapshot))
                {
                    _fileOperationProgressOverlay?.Update(snapshot, S("OperationProgressCountFormat"));
                    lastSnapshot = snapshot;
                }
            }

            return await job.Completion;
        }

        private void EnsureFileOperationProgressOverlay()
        {
            if (_fileOperationProgressOverlay is not null)
            {
                return;
            }

            _fileOperationProgressOverlay = new Controls.FileOperationProgressOverlay();
            AttachWindowOverlay(_fileOperationProgressOverlay, zIndex: 210);
        }
    }
}
