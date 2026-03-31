using FileExplorerUI.Commands;
using FileExplorerUI.Services;
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
            bool isSidebarTreeContext = origin == EntriesContextOrigin.SidebarTree;
            switch (commandId)
            {
                case FileCommandIds.Open:
                    await ExecuteOpenEntriesContextTargetAsync(target);
                    break;
                case FileCommandIds.Copy:
                    if (isSidebarTreeContext)
                    {
                        ExecuteCopyOrCutContextTarget(target, FileTransferMode.Copy);
                    }
                    else
                    {
                        ExecuteCopy();
                    }
                    break;
                case FileCommandIds.Cut:
                    if (isSidebarTreeContext)
                    {
                        ExecuteCopyOrCutContextTarget(target, FileTransferMode.Cut);
                    }
                    else
                    {
                        ExecuteCut();
                    }
                    break;
                case FileCommandIds.Paste:
                    await ExecutePasteForTargetAsync(target);
                    break;
                case FileCommandIds.Rename:
                    if (isSidebarTreeContext)
                    {
                        await ExecuteRenameSidebarTreeContextTargetAsync(target);
                    }
                    else
                    {
                        await ExecuteRenameSelectedAsync();
                    }
                    break;
                case FileCommandIds.Delete:
                    if (isSidebarTreeContext)
                    {
                        await ExecuteDeleteSidebarTreeContextTargetAsync(target);
                    }
                    else
                    {
                        await ExecuteDeleteSelectedAsync();
                    }
                    break;
                case FileCommandIds.NewFile:
                    await ExecuteNewFileAsync();
                    break;
                case FileCommandIds.NewFolder:
                    await ExecuteNewFolderAsync();
                    break;
                case FileCommandIds.Refresh:
                    await RefreshCurrentDirectoryInBackgroundAsync();
                    break;
                case FileCommandIds.CopyPath:
                    ExecuteCopyPathCommand(target);
                    break;
                case FileCommandIds.Share:
                    ExecuteShareCommand(target);
                    break;
                case FileCommandIds.CreateShortcut:
                    await ExecuteCreateShortcutCommandAsync(target);
                    break;
                case FileCommandIds.CompressZip:
                    await ExecuteCompressZipCommandAsync(target);
                    break;
                case FileCommandIds.ExtractSmart:
                    await ExecuteExtractZipSmartCommandAsync(target);
                    break;
                case FileCommandIds.ExtractHere:
                    await ExecuteExtractZipHereCommandAsync(target);
                    break;
                case FileCommandIds.ExtractToFolder:
                    await ExecuteExtractZipToFolderCommandAsync(target);
                    break;
                case FileCommandIds.OpenWith:
                    ExecuteOpenWithCommand(target);
                    break;
                case FileCommandIds.OpenTarget:
                    await ExecuteOpenTargetCommandAsync(target);
                    break;
                case FileCommandIds.RunAsAdministrator:
                    ExecuteRunAsAdministratorCommand(target);
                    break;
                case FileCommandIds.OpenInNewWindow:
                    ExecuteOpenInNewWindowCommand(target);
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
            return commandId switch
            {
                FileCommandIds.Open => !string.IsNullOrWhiteSpace(target.Path),
                FileCommandIds.Copy => isSidebarContext
                    ? !string.IsNullOrWhiteSpace(target.Path) && _explorerService.PathExists(target.Path)
                    : CanCopySelectedEntry() && IsEntriesContextSelectionAligned(target),
                FileCommandIds.Cut => isSidebarContext
                    ? !string.IsNullOrWhiteSpace(target.Path) &&
                        !IsDriveRoot(target.Path) &&
                        _explorerService.PathExists(target.Path)
                    : CanCutSelectedEntry() && IsEntriesContextSelectionAligned(target),
                FileCommandIds.Paste => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                    _fileManagementCoordinator.HasAvailablePasteItems(),
                FileCommandIds.Rename => isSidebarContext
                    ? !string.IsNullOrWhiteSpace(target.Path) &&
                        !IsDriveRoot(target.Path) &&
                        _explorerService.PathExists(target.Path)
                    : CanRenameSelectedEntry() && IsEntriesContextSelectionAligned(target),
                FileCommandIds.Delete => isSidebarContext
                    ? !string.IsNullOrWhiteSpace(target.Path) &&
                        !IsDriveRoot(target.Path) &&
                        _explorerService.PathExists(target.Path)
                    : CanDeleteSelectedEntry() && IsEntriesContextSelectionAligned(target),
                FileCommandIds.NewFile or FileCommandIds.NewFolder =>
                    !string.IsNullOrWhiteSpace(target.Path) &&
                    string.Equals(target.Path, _currentPath, StringComparison.OrdinalIgnoreCase) &&
                    CanCreateInCurrentDirectory(),
                FileCommandIds.Refresh =>
                    string.Equals(target.Path, _currentPath, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase),
                FileCommandIds.CopyPath => !string.IsNullOrWhiteSpace(target.Path),
                FileCommandIds.Share => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry,
                FileCommandIds.CreateShortcut => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                    _explorerService.DirectoryExists(_currentPath),
                FileCommandIds.CompressZip => !string.IsNullOrWhiteSpace(target.Path) &&
                    target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry &&
                    _explorerService.PathExists(target.Path),
                FileCommandIds.ExtractSmart or FileCommandIds.ExtractHere or FileCommandIds.ExtractToFolder =>
                    !string.IsNullOrWhiteSpace(target.Path) &&
                    target.Kind == FileCommandTargetKind.FileEntry &&
                    string.Equals(Path.GetExtension(target.Path), ".zip", StringComparison.OrdinalIgnoreCase) &&
                    _explorerService.PathExists(target.Path),
                FileCommandIds.OpenWith => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.OpenTarget => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.RunAsAdministrator => !string.IsNullOrWhiteSpace(target.Path) &&
                    !target.IsDirectory,
                FileCommandIds.OpenInNewWindow => !string.IsNullOrWhiteSpace(target.Path) &&
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

                if (IsPathWithin(_currentPath, target.Path))
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

                    await NavigateToPathAsync(parentPath, pushHistory: true, focusEntriesAfterNavigation: false);
                }

                UpdateStatusKey("StatusDeleteSuccess", target.DisplayName, recursive);
                return;
            }
        }

        private async Task ExecuteOpenEntriesContextTargetAsync(FileCommandTarget target)
        {
            if (target.IsDirectory)
            {
                await NavigateToPathAsync(target.Path ?? ShellMyComputerPath, pushHistory: true);
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
