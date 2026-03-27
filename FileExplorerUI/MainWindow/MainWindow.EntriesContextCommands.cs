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
            if (flyoutActive)
            {
                _pendingEntriesContextCommand = new PendingEntriesContextCommand(commandId, target);
                HideActiveEntriesContextFlyout();
                return;
            }

            _ = ExecuteEntriesContextCommandAsync(commandId, target);
        }

        private async Task ExecuteEntriesContextCommandAsync(string commandId, FileCommandTarget target)
        {
            switch (commandId)
            {
                case FileCommandIds.Open:
                    await ExecuteOpenEntriesContextTargetAsync(target);
                    break;
                case FileCommandIds.Copy:
                    ExecuteCopy();
                    break;
                case FileCommandIds.Cut:
                    ExecuteCut();
                    break;
                case FileCommandIds.Paste:
                    await ExecutePasteForTargetAsync(target);
                    break;
                case FileCommandIds.Rename:
                    await ExecuteRenameSelectedAsync();
                    break;
                case FileCommandIds.Delete:
                    await ExecuteDeleteSelectedAsync();
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

            return commandId switch
            {
                FileCommandIds.Open => !string.IsNullOrWhiteSpace(target.Path),
                FileCommandIds.Copy => CanCopySelectedEntry(),
                FileCommandIds.Cut => CanCutSelectedEntry(),
                FileCommandIds.Paste => !string.IsNullOrWhiteSpace(target.Path) &&
                    !string.Equals(target.Path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) &&
                    _fileManagementCoordinator.HasAvailablePasteItems(),
                FileCommandIds.Rename => CanRenameSelectedEntry(),
                FileCommandIds.Delete => CanDeleteSelectedEntry(),
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
