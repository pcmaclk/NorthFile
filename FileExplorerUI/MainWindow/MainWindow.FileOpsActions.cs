using FileExplorerUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private enum PasteConflictDialogDecision
        {
            Replace,
            Skip
        }

        private async Task RenameEntryAsync(EntryViewModel entry, int selectedIndex, string newName)
        {
            string src = Path.Combine(_currentPath, entry.Name);
            string oldName = entry.Name;
            TreeViewNode? renamedTreeNode = entry.IsDirectory ? FindSidebarTreeNodeByPath(src) : null;
            while (true)
            {
                try
                {
                    FileOperationResult<RenamedEntryInfo> renameResult = await _fileManagementCoordinator.TryRenameEntryAsync(_currentPath, entry.Name, newName);
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

            if (Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

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

                    FileOperationResult<bool> deleteResult = await _fileManagementCoordinator.TryDeleteEntryAsync(targetPath, recursive);
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
                        string parentPath = _fileOperationsController.ResolveDeleteFallbackParentPath(targetPath, _currentPath);
                        EnsurePersistentRefreshFallbackInvalidation(parentPath, "delete");
                    }

                    TryRemoveFavoritesForDeletedPath(targetPath);
                    ApplyLocalDelete(selectedIndex);
                    _ = RefreshCurrentDirectoryInBackgroundAsync(preserveViewport: true);
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

            if (Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

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

            if (Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task<bool> ShowOperationFailureDialogAsync(string titleKey, string message, bool allowRetry = false)
        {
            var body = new StackPanel
            {
                Spacing = 12
            };
            body.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                Title = S(titleKey),
                Content = body,
                CloseButtonText = S("DialogCloseButton"),
                DefaultButton = ContentDialogButton.Close
            };

            if (allowRetry)
            {
                dialog.PrimaryButtonText = S("DialogRetryButton");
                dialog.DefaultButton = ContentDialogButton.Primary;
            }

            if (Content is FrameworkElement root)
            {
                dialog.XamlRoot = root.XamlRoot;
            }

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private async Task<PasteConflictDialogDecision> ShowPasteConflictDialogAsync(string? itemName, bool isDirectory, int conflictCount)
        {
            string primaryMessage = conflictCount == 1 && !string.IsNullOrWhiteSpace(itemName)
                ? SF(isDirectory ? "PasteConflictDialogPrimarySingleFolder" : "PasteConflictDialogPrimarySingleFile", itemName)
                : SF("PasteConflictDialogPrimaryMultiple", conflictCount);

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
                    : S("PasteConflictDialogSkipMultipleButton"));
            return result == Controls.ModalActionDialogResult.Primary
                ? PasteConflictDialogDecision.Replace
                : PasteConflictDialogDecision.Skip;
        }

        private void EnsurePasteConflictOverlay()
        {
            if (_pasteConflictDialog is not null)
            {
                return;
            }

            if (Content is not Grid rootGrid)
            {
                return;
            }

            _pasteConflictDialog = new Controls.ModalActionDialog();
            Grid.SetRowSpan(_pasteConflictDialog, 2);
            Canvas.SetZIndex(_pasteConflictDialog, 200);
            rootGrid.Children.Add(_pasteConflictDialog);
        }
    }
}
