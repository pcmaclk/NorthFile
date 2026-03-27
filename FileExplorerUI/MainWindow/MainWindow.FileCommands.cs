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
            _ = ExecuteRenameSelectedAsync();
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteDeleteSelectedAsync();
        }

        private async void NewFileButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteNewFileAsync();
        }

        private async void NewFolderButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecuteNewFolderAsync();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCopy();
        }

        private void CutButton_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCut();
        }

        private async void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            await ExecutePasteAsync();
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

            return _entries.FirstOrDefault(entry =>
                entry.IsLoaded &&
                !entry.IsGroupHeader &&
                string.Equals(entry.FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase));
        }

        private int GetSelectedEntryIndex()
        {
            EntryViewModel? selected = GetSelectedLoadedEntry();
            return selected is null ? -1 : _entries.IndexOf(selected);
        }

        private bool CanPasteIntoCurrentDirectory()
        {
            return !string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase);
        }

        private bool CanCreateInCurrentDirectory()
        {
            return CanPasteIntoCurrentDirectory();
        }

        private bool TryEnsureCurrentDirectoryAvailable(out string errorMessage)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = S("ErrorOpenFolderFirst");
                return false;
            }

            if (!_explorerService.DirectoryExists(_currentPath))
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
            return selectedIndex >= 0 && selectedIndex < _entries.Count;
        }

        private bool CanDeleteSelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out _))
            {
                return false;
            }

            int selectedIndex = GetSelectedEntryIndex();
            return selectedIndex >= 0 && selectedIndex < _entries.Count;
        }

        private void UpdateFileCommandStates()
        {
            bool canCreate = CanCreateInCurrentDirectory();
            bool canRename = CanRenameSelectedEntry();
            bool canDelete = CanDeleteSelectedEntry();
            bool canCopy = CanCopySelectedEntry();
            bool canCut = CanCutSelectedEntry();
            bool canPaste = CanPasteIntoCurrentDirectory() && _fileManagementCoordinator.HasAvailablePasteItems();

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
            return ExecuteNewEntryAsync(isDirectory: false);
        }

        private Task ExecuteNewFolderAsync()
        {
            return ExecuteNewEntryAsync(isDirectory: true);
        }

        private async Task ExecuteNewEntryAsync(bool isDirectory)
        {
            if (!CanCreateInCurrentDirectory())
            {
                UpdateStatusKey("StatusNewFailedOpenFolderFirst", CreateKindLabel(isDirectory));
                return;
            }

            if (!TryEnsureCurrentDirectoryAvailable(out string createError))
            {
                UpdateStatusKey("StatusNewFailedWithReason", CreateKindLabel(isDirectory), createError);
                return;
            }

            await CreateNewEntryAsync(isDirectory);
        }

        private Task ExecuteRenameSelectedAsync()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return Task.CompletedTask;
            }

            int selectedIndex = GetSelectedEntryIndex();
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatusKey("StatusRenameFailedInvalidIndex");
                return Task.CompletedTask;
            }

            if (_entriesFlyoutOpen)
            {
                _pendingContextRenameEntry = entry;
                return Task.CompletedTask;
            }

            return BeginRenameOverlayAsync(entry);
        }

        private async Task ExecuteDeleteSelectedAsync()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusDeleteFailedSelectLoaded");
                return;
            }

            int selectedIndex = GetSelectedEntryIndex();
            if (selectedIndex < 0 || selectedIndex >= _entries.Count)
            {
                UpdateStatusKey("StatusDeleteFailedInvalidIndex");
                return;
            }

            bool recursive = entry.IsDirectory;
            string target = Path.Combine(_currentPath, entry.Name);
            await DeleteEntryAsync(entry, selectedIndex, target, recursive);
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
            return PasteIntoDirectoryAsync(_currentPath, selectPastedEntry: true);
        }

        private void CopySelectedEntry()
        {
            if (!TryGetSelectedLoadedEntry(out EntryViewModel? entry))
            {
                UpdateStatusKey("StatusCopyFailedSelectLoaded");
                return;
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
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

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
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
            return PasteIntoDirectoryAsync(_currentPath, selectPastedEntry: true);
        }

        private async Task PasteIntoDirectoryAsync(string targetDirectoryPath, bool selectPastedEntry)
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
                FilePasteOperationResult pasteOperation = await _fileManagementCoordinator.TryPasteAsync(targetDirectoryPath);
                if (!pasteOperation.Succeeded || pasteOperation.PasteResult is null)
                {
                    UpdateStatusKey("StatusPasteFailedWithReason", pasteOperation.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }

                FilePasteResult result = pasteOperation.PasteResult;
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

                if (appliedCount == 0)
                {
                    if (conflictCount > 0)
                    {
                        UpdateStatusKey("StatusPasteSkippedConflicts", conflictCount);
                    }
                    else if (samePathCount > 0)
                    {
                        UpdateStatusKey("StatusPasteSkippedSamePath");
                    }
                    else if (failureCount > 0 && result.Items.Count > 0)
                    {
                        string? message = result.Items[0].ErrorMessage;
                        UpdateStatusKey("StatusPasteFailedWithReason", message ?? string.Empty);
                    }
                    else
                    {
                        UpdateStatusKey("StatusPasteSkippedNothingApplied");
                    }
                    return;
                }

                if (!result.TargetChanged)
                {
                    EnsurePersistentRefreshFallbackInvalidation(targetDirectoryPath, result.Mode == FileTransferMode.Cut ? "cut-paste" : "copy-paste");
                }

                if (selectPastedEntry && appliedCount == 1 && !string.IsNullOrWhiteSpace(firstAppliedPath))
                {
                    _selectedEntryPath = firstAppliedPath;
                }

                await LoadFirstPageAsync();

                if (appliedDirectory &&
                    FindSidebarTreeNodeByPath(targetDirectoryPath) is TreeViewNode parentNode &&
                    parentNode.IsExpanded)
                {
                    await PopulateSidebarTreeChildrenAsync(parentNode, targetDirectoryPath, CancellationToken.None, expandAfterLoad: true);
                }

                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);

                UpdateFileCommandStates();
                string modeText = result.Mode == FileTransferMode.Cut ? S("OperationMove") : S("OperationPaste");
                UpdateStatusKey("StatusTransferSuccess", modeText, appliedCount, conflictCount, samePathCount, failureCount);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPasteFailedWithReason", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task CreateNewEntryAsync(bool isDirectory)
        {
            string createKind = CreateKindLabel(isDirectory);
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusNewFailedOpenFolderFirst", createKind);
                return;
            }

            if (!_explorerService.DirectoryExists(_currentPath))
            {
                UpdateStatusKey("StatusNewFailedWithReason", createKind, S("ErrorCurrentFolderUnavailable"));
                return;
            }

            try
            {
                SuppressNextWatcherRefresh(_currentPath);
                FileOperationResult<CreatedEntryInfo> createResult = await _fileManagementCoordinator.TryCreateEntryAsync(_currentPath, isDirectory);
                if (!createResult.Succeeded)
                {
                    UpdateStatusKey("StatusCreateFailed", createResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                CreatedEntryInfo created = createResult.Value!;
                if (!created.ChangeNotified)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, isDirectory ? "create-folder" : "create-file");
                }

                EntryViewModel entry = CreateLocalCreatedEntryModel(created.Name, created.IsDirectory);
                int insertIndex = FindInsertIndexForEntry(entry);
                if (!IsIndexInCurrentViewport(insertIndex))
                {
                    await EnsureCreateInsertVisibleAsync(insertIndex);
                }

                InsertLocalCreatedEntry(entry, insertIndex);
                _pendingCreatedEntrySelection = entry;
                UpdateStatusKey("StatusCreateSuccess", created.Name);
                await StartRenameForCreatedEntryAsync(entry, insertIndex);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCreateFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

    }
}
