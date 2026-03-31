using FileExplorerUI.Commands;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using WinRT.Interop;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private Task ExecutePasteForTargetAsync(FileCommandTarget target)
        {
            string targetPath = target.Path ?? string.Empty;
            bool selectPastedEntry = string.Equals(targetPath, _currentPath, StringComparison.OrdinalIgnoreCase);
            return PasteIntoDirectoryAsync(targetPath, selectPastedEntry);
        }

        private void ExecuteCopyPathCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                var package = new DataPackage();
                package.SetText(target.Path);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                UpdateStatusKey("StatusCopyPathReady", target.Path);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCopyPathFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteShareCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                if (!EnsureShareDataTransferManager())
                {
                    UpdateStatusKey("StatusShareFailed", S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                _pendingShareTarget = target;
                IntPtr ownerHandle = _windowHandle != IntPtr.Zero
                    ? _windowHandle
                    : WindowNative.GetWindowHandle(this);
                NativeMethods.ShowShareUIForWindow(ownerHandle);
                UpdateStatusKey("StatusShareOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                _pendingShareTarget = null;
                UpdateStatusKey("StatusShareFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task ExecuteCreateShortcutCommandAsync(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            if (!TryEnsureCurrentDirectoryAvailable(out string errorMessage))
            {
                UpdateStatusKey("StatusCreateShortcutFailed", errorMessage);
                return;
            }

            try
            {
                SuppressNextWatcherRefresh(_currentPath);
                FileOperationResult<CreatedEntryInfo> createResult = await _fileManagementCoordinator.TryCreateShortcutAsync(_currentPath, target.Path);
                if (!createResult.Succeeded)
                {
                    UpdateStatusKey("StatusCreateShortcutFailed", createResult.Failure?.Message ?? S("ErrorFileOperationUnknown"));
                    return;
                }
                CreatedEntryInfo created = createResult.Value!;
                if (!created.ChangeNotified)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, "create-shortcut");
                }

                _selectedEntryPath = created.FullPath;
                await LoadFirstPageAsync();
                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                UpdateFileCommandStates();
                UpdateStatusKey("StatusCreateShortcutSuccess", created.Name);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusCreateShortcutFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task ExecuteCompressZipCommandAsync(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                string sourcePath = target.Path;
                string destinationDirectory = Path.GetDirectoryName(sourcePath.TrimEnd('\\')) ?? _currentPath;
                if (string.IsNullOrWhiteSpace(destinationDirectory) || !_explorerService.DirectoryExists(destinationDirectory))
                {
                    UpdateStatusKey("StatusCompressZipFailed", S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                string archiveName = _explorerService.GenerateUniqueZipArchiveName(destinationDirectory, sourcePath);
                string archivePath = Path.Combine(destinationDirectory, archiveName);

                bool destinationIsCurrentDirectory =
                    string.Equals(destinationDirectory, _currentPath, StringComparison.OrdinalIgnoreCase);
                double detailsVerticalOffset = DetailsEntriesScrollViewer.VerticalOffset;
                double groupedHorizontalOffset = GroupedEntriesScrollViewer.HorizontalOffset;
                if (destinationIsCurrentDirectory)
                {
                    SuppressNextWatcherRefresh(_currentPath);
                }

                FileOperationResult<string> zipResult = await _fileManagementCoordinator.TryCreateZipArchiveAsync(sourcePath, archivePath);
                if (!zipResult.Succeeded)
                {
                    await ShowOperationFailureDialogAsync(
                        "CompressZipFailureDialogTitle",
                        SF("StatusCompressZipFailed", zipResult.Failure?.Message ?? S("ErrorFileOperationUnknown")));
                    return;
                }

                if (destinationIsCurrentDirectory)
                {
                    EnsurePersistentRefreshFallbackInvalidation(_currentPath, "compress-zip");
                    _selectedEntryPath = archivePath;
                    await LoadFirstPageAsync();
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_currentViewMode == EntryViewMode.Details)
                        {
                            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, detailsVerticalOffset), null, disableAnimation: true);
                        }
                        else
                        {
                            double maxOffset = Math.Max(0, GroupedEntriesScrollViewer.ScrollableWidth);
                            GroupedEntriesScrollViewer.ChangeView(Math.Min(maxOffset, groupedHorizontalOffset), null, null, disableAnimation: true);
                        }

                        RestoreListSelectionByPathRespectingViewport();
                        FocusEntriesList();
                    });
                    UpdateFileCommandStates();
                }

                UpdateStatusKey("StatusCompressZipSuccess", archiveName);
            }
            catch (Exception ex)
            {
                await ShowOperationFailureDialogAsync(
                    "CompressZipFailureDialogTitle",
                    SF("StatusCompressZipFailed", FileOperationErrors.ToUserMessage(ex)));
            }
        }

        private Task ExecuteExtractZipSmartCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipCommandCoreAsync(
                target,
                archivePath => _fileManagementCoordinator.TryExtractZipSmartAsync(archivePath),
                "extract-smart");
        }

        private Task ExecuteExtractZipHereCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipCommandCoreAsync(
                target,
                archivePath => _fileManagementCoordinator.TryExtractZipHereAsync(archivePath),
                "extract-here");
        }

        private Task ExecuteExtractZipToFolderCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipCommandCoreAsync(
                target,
                archivePath => _fileManagementCoordinator.TryExtractZipToFolderAsync(archivePath),
                "extract-to-folder");
        }

        private async Task ExecuteExtractZipCommandCoreAsync(
            FileCommandTarget target,
            Func<string, Task<FileOperationResult<ZipExtractionInfo>>> extractAsync,
            string invalidationReason)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                string archivePath = target.Path;
                string destinationDirectory = Path.GetDirectoryName(archivePath.TrimEnd('\\')) ?? _currentPath;
                if (string.IsNullOrWhiteSpace(destinationDirectory) || !_explorerService.DirectoryExists(destinationDirectory))
                {
                    UpdateStatusKey("StatusExtractZipFailed", S("ErrorCurrentFolderUnavailable"));
                    return;
                }

                bool destinationIsCurrentDirectory =
                    string.Equals(destinationDirectory, _currentPath, StringComparison.OrdinalIgnoreCase);
                double detailsVerticalOffset = DetailsEntriesScrollViewer.VerticalOffset;
                double groupedHorizontalOffset = GroupedEntriesScrollViewer.HorizontalOffset;
                if (destinationIsCurrentDirectory)
                {
                    SuppressNextWatcherRefresh(_currentPath);
                }

                FileOperationResult<ZipExtractionInfo> extractResult = await extractAsync(archivePath);
                if (!extractResult.Succeeded)
                {
                    await ShowOperationFailureDialogAsync(
                        "ExtractZipFailureDialogTitle",
                        SF("StatusExtractZipFailed", extractResult.Failure?.Message ?? S("ErrorFileOperationUnknown")));
                    return;
                }

                ZipExtractionInfo extracted = extractResult.Value!;
                string? extractedName = string.IsNullOrWhiteSpace(extracted.PrimarySelectionPath)
                    ? null
                    : Path.GetFileName(extracted.PrimarySelectionPath.TrimEnd('\\'));

                if (destinationIsCurrentDirectory)
                {
                    if (!extracted.ChangeNotified)
                    {
                        EnsurePersistentRefreshFallbackInvalidation(_currentPath, invalidationReason);
                    }

                    _selectedEntryPath = extracted.PrimarySelectionPath;
                    await LoadFirstPageAsync();
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_currentViewMode == EntryViewMode.Details)
                        {
                            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, detailsVerticalOffset), null, disableAnimation: true);
                        }
                        else
                        {
                            double maxOffset = Math.Max(0, GroupedEntriesScrollViewer.ScrollableWidth);
                            GroupedEntriesScrollViewer.ChangeView(Math.Min(maxOffset, groupedHorizontalOffset), null, null, disableAnimation: true);
                        }

                        if (!string.IsNullOrWhiteSpace(_selectedEntryPath))
                        {
                            RestoreListSelectionByPathRespectingViewport();
                        }

                        FocusEntriesList();
                    });
                    UpdateFileCommandStates();
                }

                UpdateStatusKey("StatusExtractZipSuccess", extractedName ?? target.DisplayName);
            }
            catch (Exception ex)
            {
                await ShowOperationFailureDialogAsync(
                    "ExtractZipFailureDialogTitle",
                    SF("StatusExtractZipFailed", FileOperationErrors.ToUserMessage(ex)));
            }
        }

        private void ExecuteOpenInNewWindowCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || !target.IsDirectory)
            {
                return;
            }

            try
            {
                if (Application.Current is App app)
                {
                    app.CreateWindow(target.Path);
                    UpdateStatusKey("StatusOpenedInNewWindow", target.DisplayName);
                }
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenInNewWindowFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task ExecuteOpenTargetCommandAsync(FileCommandTarget target)
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
                    await NavigateToPathAsync(resolvedTargetPath, pushHistory: true);
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

        private void ExecuteRunAsAdministratorCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || target.IsDirectory)
            {
                return;
            }

            try
            {
                _explorerService.RunAsAdministrator(target.Path);
                UpdateStatusKey("StatusRunAsAdministratorStarted", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusRunAsAdministratorFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteOpenWithCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || target.IsDirectory)
            {
                return;
            }

            try
            {
                IntPtr ownerHandle = _windowHandle != IntPtr.Zero
                    ? _windowHandle
                    : WindowNative.GetWindowHandle(this);
                var openAsInfo = new NativeMethods.OpenAsInfo
                {
                    FilePath = target.Path,
                    ClassName = null,
                    Flags = NativeMethods.OAIF_EXEC | NativeMethods.OAIF_HIDE_REGISTRATION
                };
                int hr = NativeMethods.SHOpenWithDialog(ownerHandle, ref openAsInfo);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(hr);
                }

                UpdateStatusKey("StatusOpenWithOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenWithFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteOpenInTerminalCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                _explorerService.OpenPathInTerminal(target.Path);
                UpdateStatusKey("StatusOpenTerminalSuccess", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenTerminalFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void ExecuteShowPropertiesCommand(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return;
            }

            try
            {
                _explorerService.ShowProperties(target.Path);
                UpdateStatusKey("StatusPropertiesOpened", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPropertiesFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private bool EnsureShareDataTransferManager()
        {
            if (_shareDataTransferManager is not null)
            {
                return true;
            }

            IntPtr ownerHandle = _windowHandle != IntPtr.Zero
                ? _windowHandle
                : WindowNative.GetWindowHandle(this);
            if (ownerHandle == IntPtr.Zero)
            {
                return false;
            }

            _shareDataTransferManager = NativeMethods.GetDataTransferManagerForWindow(ownerHandle);
            _shareDataTransferManager.DataRequested += ShareDataTransferManager_DataRequested;
            return true;
        }

        private async void ShareDataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            FileCommandTarget? target = _pendingShareTarget;
            _pendingShareTarget = null;

            if (target is null || string.IsNullOrWhiteSpace(target.Path))
            {
                args.Request.FailWithDisplayText("Share target is unavailable.");
                return;
            }

            DataRequestDeferral deferral = args.Request.GetDeferral();
            try
            {
                DataRequest request = args.Request;
                request.Data.Properties.Title = target.DisplayName;
                request.Data.Properties.Description = target.Path;

                IStorageItem storageItem = target.IsDirectory
                    ? (IStorageItem)await StorageFolder.GetFolderFromPathAsync(target.Path)
                    : await StorageFile.GetFileFromPathAsync(target.Path);

                request.Data.SetStorageItems(new[] { storageItem });
            }
            catch (Exception ex)
            {
                string message = FileOperationErrors.ToUserMessage(ex);
                args.Request.FailWithDisplayText(message);
                UpdateStatusKey("StatusShareFailed", message);
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
