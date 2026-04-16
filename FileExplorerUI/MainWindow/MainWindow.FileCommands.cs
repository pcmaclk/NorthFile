using FileExplorerUI.Commands;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void RenameButton_Click(object sender, RoutedEventArgs e)
        {
            _ = _paneFileCommandController.ExecuteRenameInActivePaneAsync();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            await _paneFileCommandController.ExecuteDeleteInActivePaneAsync();
        }

        private async void NewFileButton_Click(object sender, RoutedEventArgs e)
        {
            await _paneFileCommandController.ExecuteNewFileInActivePaneAsync();
        }

        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            await _paneFileCommandController.ExecuteNewFolderInActivePaneAsync();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            _paneFileCommandController.ExecuteCopyInActivePane();
        }

        private void CutButton_Click(object sender, RoutedEventArgs e)
        {
            _paneFileCommandController.ExecuteCutInActivePane();
        }

        private async void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            await _paneFileCommandController.ExecutePasteInActivePaneAsync();
        }

        private bool TryGetSelectedLoadedEntry([NotNullWhen(true)] out EntryViewModel? entry)
        {
            entry = GetSelectedLoadedEntry()!;
            return entry is not null;
        }

        private EntryViewModel? GetSelectedLoadedEntry()
        {
            if (string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                return null;
            }

            return PrimaryEntries.FirstOrDefault(entry =>
                entry.IsLoaded &&
                !entry.IsGroupHeader &&
                string.Equals(entry.FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase));
        }

        private int GetSelectedEntryIndex()
        {
            EntryViewModel? selected = GetSelectedLoadedEntry();
            return selected is null ? -1 : PrimaryEntries.IndexOf(selected);
        }

        private bool CanPasteIntoCurrentDirectory()
        {
            return !string.Equals(GetPanelCurrentPath(WorkspacePanelId.Primary), ShellMyComputerPath, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanCreateInCurrentDirectory()
        {
            return CanPasteIntoCurrentDirectory();
        }

        private bool TryEnsureCurrentDirectoryAvailable(out string errorMessage)
        {
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            if (string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = S("ErrorOpenFolderFirst");
                return false;
            }

            if (!_explorerService.DirectoryExists(currentPath))
            {
                errorMessage = S("ErrorCurrentFolderUnavailable");
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private bool CanCopySelectedEntry()
        {
            return TryGetSelectedLoadedEntry(out _);
        }

        private bool CanCutSelectedEntry()
        {
            return TryGetSelectedLoadedEntry(out _);
        }

        private bool CanRenameSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out _))
            {
                return false;
            }

            int selectedIndex = GetSelectedEntryIndex();
            return selectedIndex >= 0 && selectedIndex < PrimaryEntries.Count;
        }

        private bool CanDeleteSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out _))
            {
                return false;
            }

            int selectedIndex = GetSelectedEntryIndex();
            return selectedIndex >= 0 && selectedIndex < PrimaryEntries.Count;
        }

        private void UpdateFileCommandStates()
        {
            bool canCreate = _paneFileCommandController.CanCreateInActivePane();
            bool canRename = _paneFileCommandController.CanRenameInActivePane();
            bool canDelete = _paneFileCommandController.CanDeleteInActivePane();
            bool canCopy = _paneFileCommandController.CanCopyInActivePane();
            bool canCut = _paneFileCommandController.CanCutInActivePane();
            bool canPaste = _paneFileCommandController.CanPasteInActivePane();

            if (NewFileButton is not null)
            {
                NewFileButton.IsEnabled = canCreate;
            }

            if (NewFolderButton is not null)
            {
                NewFolderButton.IsEnabled = canCreate;
            }

            if (RenameButton is not null)
            {
                RenameButton.IsEnabled = canRename;
            }

            if (DeleteButton is not null)
            {
                DeleteButton.IsEnabled = canDelete;
            }

            if (CopyButton is not null)
            {
                CopyButton.IsEnabled = canCopy;
            }

            if (CutButton is not null)
            {
                CutButton.IsEnabled = canCut;
            }

            if (PasteButton is not null)
            {
                PasteButton.IsEnabled = canPaste;
            }
        }

        private Task ExecuteNewFileAsync()
        {
            return ExecuteCreateEntryForPaneCoreAsync(WorkspacePanelId.Primary, isDirectory: false);
        }

        private Task ExecuteNewFolderAsync()
        {
            return ExecuteCreateEntryForPaneCoreAsync(WorkspacePanelId.Primary, isDirectory: true);
        }

        private Task ExecuteRenameSelectedAsync()
        {
            if (!TryBuildSelectedRenameTargetForPane(WorkspacePanelId.Primary, out FileCommandTarget target))
            {
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return Task.CompletedTask;
            }

            return ExecuteRenameTargetForPaneCoreAsync(WorkspacePanelId.Primary, target);
        }

        private async Task ExecuteDeleteSelectedAsync()
        {
            if (!TryBuildSelectedDeleteTargetForPane(WorkspacePanelId.Primary, out FileCommandTarget target))
            {
                UpdateStatusKey("StatusDeleteFailedSelectLoaded");
                return;
            }

            await ExecuteDeleteTargetForPaneCoreAsync(WorkspacePanelId.Primary, target);
        }

        private void ExecuteCopy()
        {
            if (!CanCopySelectedEntry())
            {
                UpdateStatusKey("StatusCopyFailedSelectLoaded");
                return;
            }

            CopySelectedEntry();
        }

        private void ExecuteCut()
        {
            if (!CanCutSelectedEntry())
            {
                UpdateStatusKey("StatusCutFailedSelectLoaded");
                return;
            }

            CutSelectedEntry();
        }

        private Task ExecutePasteAsync()
        {
            return ExecutePasteIntoPaneDirectoryCoreAsync(
                WorkspacePanelId.Primary,
                GetPanelCurrentPath(WorkspacePanelId.Primary),
                selectPastedEntry: true);
        }

        private void CopySelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusCopyFailedSelectLoaded");
                return;
            }

            if (string.Equals(GetPanelCurrentPath(WorkspacePanelId.Primary), ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusCopyFailedDriveRootsUnsupported");
                return;
            }

            _fileManagementCoordinator.SetClipboard(new[] { entry.FullPath }, FileTransferMode.Copy);
            UpdateFileCommandStates();
            UpdateStatusKey("StatusCopyReady", entry.Name);
        }

        private void CutSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusCutFailedSelectLoaded");
                return;
            }

            if (string.Equals(GetPanelCurrentPath(WorkspacePanelId.Primary), ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusCutFailedDriveRootsUnsupported");
                return;
            }

            _fileManagementCoordinator.SetClipboard(new[] { entry.FullPath }, FileTransferMode.Cut);
            UpdateFileCommandStates();
            UpdateStatusKey("StatusCutReady", entry.Name);
        }

        private Task PasteIntoCurrentDirectoryAsync()
        {
            return ExecutePasteIntoPaneDirectoryCoreAsync(
                WorkspacePanelId.Primary,
                GetPanelCurrentPath(WorkspacePanelId.Primary),
                selectPastedEntry: true);
        }

        private async Task PasteIntoDirectoryAsync(string targetDirectoryPath, bool selectPastedEntry)
        {
            await ExecutePasteIntoPaneDirectoryCoreAsync(WorkspacePanelId.Primary, targetDirectoryPath, selectPastedEntry);
        }

    }
}
