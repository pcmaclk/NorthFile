using FileExplorerUI.Commands;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void EntriesContextMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem item || item.Tag is not string commandId)
            {
                return;
            }

            ExecuteEntriesContextCommand(commandId);
        }

        private void ExecuteEntriesContextCommand(object? parameter)
        {
            if (parameter is not string commandId || !TryBuildActiveEntriesContextTarget(out FileCommandTarget target))
            {
                return;
            }

            if (!CanExecuteEntriesContextCommand(commandId, target))
            {
                return;
            }

            bool flyoutActive = _entriesFlyoutOpen || (_activeEntriesContextFlyout?.IsOpen ?? false);
            EntriesContextOrigin origin = _entriesContextRequest?.Origin ?? EntriesContextOrigin.EntriesList;
            if (flyoutActive)
            {
                _pendingEntriesContextCommand = new PendingEntriesContextCommand(commandId, target, origin);
                HideActiveEntriesContextFlyout();
                return;
            }

            _ = ExecuteEntriesContextCommandAsync(commandId, target, origin);
        }

        private async Task ExecuteEntriesContextCommandAsync(string commandId, FileCommandTarget target, EntriesContextOrigin origin)
        {
            SyncSelectionSurfaceForEntriesContextOrigin(origin);
            bool isSidebarTreeContext = origin == EntriesContextOrigin.SidebarTree;
            bool isSecondaryEntriesContext = origin == EntriesContextOrigin.SecondaryEntriesList;
            WorkspacePanelId contextPanelId = ResolvePanelIdForEntriesContextOrigin(origin);
            switch (commandId)
            {
                case FileCommandIds.Open:
                    await ExecuteOpenEntriesContextTargetAsync(target, origin);
                    break;
                case FileCommandIds.Copy:
                    if (isSidebarTreeContext)
                    {
                        ExecuteCopyOrCutContextTarget(target, FileTransferMode.Copy);
                    }
                    else
                    {
                        _paneFileCommandController.ExecuteCopy(contextPanelId);
                    }
                    break;
                case FileCommandIds.Cut:
                    if (isSidebarTreeContext)
                    {
                        ExecuteCopyOrCutContextTarget(target, FileTransferMode.Cut);
                    }
                    else
                    {
                        _paneFileCommandController.ExecuteCut(contextPanelId);
                    }
                    break;
                case FileCommandIds.Paste:
                    await _paneFileCommandController.ExecutePasteTargetAsync(contextPanelId, target);
                    break;
                case FileCommandIds.Rename:
                    if (isSidebarTreeContext)
                    {
                        await ExecuteRenameSidebarTreeContextTargetAsync(target);
                    }
                    else
                    {
                        await _paneFileCommandController.ExecuteRenameTargetAsync(contextPanelId, target);
                    }
                    break;
                case FileCommandIds.Delete:
                    if (isSidebarTreeContext)
                    {
                        await ExecuteDeleteSidebarTreeContextTargetAsync(target);
                    }
                    else
                    {
                        await _paneFileCommandController.ExecuteDeleteTargetAsync(contextPanelId, target);
                    }
                    break;
                case FileCommandIds.NewFile:
                    await _paneFileCommandController.ExecuteNewFileAsync(contextPanelId);
                    break;
                case FileCommandIds.NewFolder:
                    await _paneFileCommandController.ExecuteNewFolderAsync(contextPanelId);
                    break;
                case FileCommandIds.Refresh:
                    await _paneFileCommandController.ExecuteRefreshAsync(contextPanelId);
                    break;
                case FileCommandIds.CopyPath:
                    ExecuteCopyPathCommand(target);
                    break;
                case FileCommandIds.Share:
                    ExecuteShareCommand(target);
                    break;
                case FileCommandIds.CreateShortcut:
                    await _paneFileCommandController.ExecuteCreateShortcutAsync(contextPanelId, target);
                    break;
                case FileCommandIds.CompressZip:
                    await _paneFileCommandController.ExecuteCompressZipAsync(contextPanelId, target);
                    break;
                case FileCommandIds.ExtractSmart:
                    await _paneFileCommandController.ExecuteExtractZipSmartAsync(contextPanelId, target);
                    break;
                case FileCommandIds.ExtractHere:
                    await _paneFileCommandController.ExecuteExtractZipHereAsync(contextPanelId, target);
                    break;
                case FileCommandIds.ExtractToFolder:
                    await _paneFileCommandController.ExecuteExtractZipToFolderAsync(contextPanelId, target);
                    break;
                case FileCommandIds.OpenWith:
                    ExecuteOpenWithCommand(target);
                    break;
                case FileCommandIds.OpenTarget:
                    await _paneFileCommandController.ExecuteOpenTargetAsync(contextPanelId, target);
                    break;
                case FileCommandIds.RunAsAdministrator:
                    ExecuteRunAsAdministratorCommand(target);
                    break;
                case FileCommandIds.OpenInNewWindow:
                    ExecuteOpenInNewWindowCommand(target);
                    break;
                case FileCommandIds.OpenInNewTab:
                    await ExecuteOpenInNewTabCommandAsync(target);
                    break;
                case FileCommandIds.PinToSidebar:
                case FileCommandIds.UnpinFromSidebar:
                    ToggleFavoriteForTarget(target);
                    break;
                case FileCommandIds.OpenInTerminal:
                    ExecuteOpenInTerminalCommand(target);
                    break;
                case FileCommandIds.Properties:
                    ExecuteShowPropertiesCommand(target);
                    break;
            }
        }

        private void SyncSelectionSurfaceForEntriesContextOrigin(EntriesContextOrigin origin)
        {
            switch (origin)
            {
                case EntriesContextOrigin.SecondaryEntriesList:
                    SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
                    break;
                case EntriesContextOrigin.SidebarPinned:
                case EntriesContextOrigin.SidebarTree:
                    SetActiveSelectionSurface(SelectionSurfaceId.Sidebar);
                    break;
                default:
                    SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
                    break;
            }
        }

        private bool TryBuildActiveEntriesContextTarget(out FileCommandTarget target)
        {
            EntryViewModel? contextEntry = _entriesContextRequest?.Entry ?? _lastEntriesContextItem;
            target = ResolveEntriesContextTarget(contextEntry);
            return target.Kind != FileCommandTargetKind.None;
        }

        private bool CanExecuteEntriesContextCommand(string commandId, FileCommandTarget target)
        {
            IReadOnlyList<FileCommandDescriptor> descriptors = _fileCommandCatalog.BuildCommands(target);
            bool supported = descriptors.Any(descriptor => string.Equals(descriptor.Id, commandId, StringComparison.Ordinal));
            if (!supported)
            {
                return false;
            }

            bool isSidebarContext = _entriesContextRequest?.Origin is EntriesContextOrigin.SidebarPinned or EntriesContextOrigin.SidebarTree;
            WorkspacePanelId contextPanelId = ResolvePanelIdForEntriesContextOrigin(_entriesContextRequest?.Origin ?? EntriesContextOrigin.EntriesList);
            return commandId switch
            {
                FileCommandIds.Open => !string.IsNullOrWhiteSpace(target.Path),
                FileCommandIds.Copy => isSidebarContext
                    ? _paneFileCommandController.CanCopyTarget(contextPanelId, target)
                    : IsPrimaryEntriesContextOrigin(_entriesContextRequest?.Origin)
                        ? CanCopySelectedEntry() && IsEntriesContextSelectionAligned(target)
                        : _paneFileCommandController.CanCopyTarget(contextPanelId, target),
                FileCommandIds.Cut => isSidebarContext
                    ? _paneFileCommandController.CanCutTarget(contextPanelId, target)
                    : IsPrimaryEntriesContextOrigin(_entriesContextRequest?.Origin)
                        ? CanCutSelectedEntry() && IsEntriesContextSelectionAligned(target)
                        : _paneFileCommandController.CanCutTarget(contextPanelId, target),
                FileCommandIds.Paste => _paneFileCommandController.CanPasteTarget(contextPanelId, target),
                FileCommandIds.Rename => isSidebarContext
                    ? !string.IsNullOrWhiteSpace(target.Path) &&
                        !IsDriveRoot(target.Path) &&
                        _explorerService.PathExists(target.Path)
                    : _paneFileCommandController.CanRenameTarget(contextPanelId, target) &&
                        (!IsPrimaryEntriesContextOrigin(_entriesContextRequest?.Origin) || IsEntriesContextSelectionAligned(target)),
                FileCommandIds.Delete => isSidebarContext
                    ? !string.IsNullOrWhiteSpace(target.Path) &&
                        !IsDriveRoot(target.Path) &&
                        _explorerService.PathExists(target.Path)
                    : _paneFileCommandController.CanDeleteTarget(contextPanelId, target) &&
                        (!IsPrimaryEntriesContextOrigin(_entriesContextRequest?.Origin) || IsEntriesContextSelectionAligned(target)),
                FileCommandIds.NewFile or FileCommandIds.NewFolder =>
                    _paneFileCommandController.CanCreateTarget(contextPanelId, target),
                FileCommandIds.Refresh => _paneFileCommandController.CanRefreshTarget(contextPanelId, target),
                FileCommandIds.CopyPath => !string.IsNullOrWhiteSpace(target.Path),
                FileCommandIds.Share => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry,
                FileCommandIds.CreateShortcut => _paneFileCommandController.CanCreateShortcutTarget(contextPanelId, target),
                FileCommandIds.CompressZip => _paneFileCommandController.CanCompressZipTarget(contextPanelId, target),
                FileCommandIds.ExtractSmart or FileCommandIds.ExtractHere or FileCommandIds.ExtractToFolder =>
                    _paneFileCommandController.CanExtractZipTarget(contextPanelId, target),
                FileCommandIds.OpenWith => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.OpenTarget => _paneFileCommandController.CanOpenTarget(contextPanelId, target),
                FileCommandIds.RunAsAdministrator => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.OpenInNewWindow => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.IsDirectory,
                FileCommandIds.OpenInNewTab => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.IsDirectory,
                FileCommandIds.PinToSidebar or FileCommandIds.UnpinFromSidebar => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.IsDirectory,
                FileCommandIds.OpenInTerminal => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase),
                FileCommandIds.Properties => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static bool IsPrimaryEntriesContextOrigin(EntriesContextOrigin? origin)
        {
            return origin is null or EntriesContextOrigin.EntriesList;
        }

        private bool IsEntriesContextSelectionAligned(FileCommandTarget target)
        {
            if (_entriesContextRequest?.Origin == EntriesContextOrigin.SidebarTree)
            {
                return true;
            }

            if (_entriesContextRequest is not { IsItemTarget: true, Entry: not null })
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(target.Path) || string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return false;
            }

            return string.Equals(_selectedEntryPath, target.Path, StringComparison.OrdinalIgnoreCase);
        }

        private void ExecuteCopyOrCutContextTarget(FileCommandTarget target, FileTransferMode mode)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                UpdateStatusKey(mode == FileTransferMode.Copy ? "StatusCopyFailedSelectLoaded" : "StatusCutFailedSelectLoaded");
                return;
            }

            if (mode == FileTransferMode.Cut && IsDriveRoot(target.Path))
            {
                UpdateStatusKey("StatusCutFailedDriveRootsUnsupported");
                return;
            }

            _fileManagementCoordinator.SetClipboard(new[] { target.Path }, mode);
            UpdateFileCommandStates();
            UpdateStatusKey(mode == FileTransferMode.Copy ? "StatusCopyReady" : "StatusCutReady", target.DisplayName);
        }

        private Task ExecuteRenameSidebarTreeContextTargetAsync(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) ||
                FindSidebarTreeNodeByPath(target.Path)?.Content is not SidebarTreeEntry treeEntry)
            {
                UpdateStatusKey("StatusRenameFailedSelectTreeNode");
                return Task.CompletedTask;
            }

            _pendingSidebarTreeContextEntry = treeEntry;
            return BeginSidebarTreeRenameAsync(treeEntry);
        }

        private async Task ExecuteDeleteSidebarTreeContextTargetAsync(FileCommandTarget target)
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

            bool recursive = true;
            if (_appSettings.ConfirmDelete && !await ConfirmDeleteAsync(target.DisplayName, recursive))
            {
                return;
            }

            while (true)
            {
                FileOperationResult<bool> deleteResult = await _fileManagementCoordinator.TryDeleteEntryAsync(target.Path, recursive);
                FileOperationsController.DeleteDecision deleteDecision = _fileOperationsController.AnalyzeDeleteResult(deleteResult, target.Path);
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

                TryRemoveFavoritesForDeletedPath(target.Path);
                if (FindSidebarTreeNodeByPath(target.Path) is TreeViewNode node && node.Parent is TreeViewNode parentNode)
                {
                    parentNode.Children.Remove(node);
                }

                if (IsPathWithin(GetPanelCurrentPath(WorkspacePanelId.Primary), target.Path))
                {
                    string? parentPath = Path.GetDirectoryName(target.Path.TrimEnd('\\'));
                    if (string.IsNullOrWhiteSpace(parentPath))
                    {
                        parentPath = Path.GetPathRoot(target.Path);
                    }

                    if (string.IsNullOrWhiteSpace(parentPath))
                    {
                        parentPath = ShellMyComputerPath;
                    }

                await NavigatePanelToPathAsync(
                    WorkspacePanelId.Primary,
                    parentPath,
                    pushHistory: true,
                    focusEntriesAfterNavigation: false);
                }

                UpdateStatusKey("StatusDeleteSuccess", target.DisplayName, recursive);
                return;
            }
        }

        private async Task ExecuteOpenEntriesContextTargetAsync(FileCommandTarget target, EntriesContextOrigin origin)
        {
            if (target.IsDirectory)
            {
                if (origin == EntriesContextOrigin.SecondaryEntriesList)
                {
                    await NavigatePanelToPathAsync(WorkspacePanelId.Secondary, target.Path ?? ShellMyComputerPath, pushHistory: true);
                }
                else
                {
                await NavigatePanelToPathAsync(
                    WorkspacePanelId.Primary,
                    target.Path ?? ShellMyComputerPath,
                    pushHistory: true);
                }
                return;
            }

            if ((target.Traits & FileEntryTraits.Shortcut) != 0)
            {
                await ExecuteOpenTargetCommandAsync(target);
                return;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = target.Path,
                    UseShellExecute = true
                });
                UpdateStatusKey("StatusOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }
    }
}
