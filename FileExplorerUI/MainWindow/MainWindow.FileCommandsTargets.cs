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
            bool selectPastedEntry = string.Equals(
                targetPath,
                GetPanelCurrentPath(WorkspacePanelId.Primary),
                StringComparison.OrdinalIgnoreCase);
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
            await ExecuteCreateShortcutForPaneCoreInternalAsync(WorkspacePanelId.Primary, target);
        }

        private async Task ExecuteCompressZipCommandAsync(FileCommandTarget target)
        {
            await ExecuteCompressZipForPaneCoreInternalAsync(WorkspacePanelId.Primary, target);
        }

        private Task ExecuteExtractZipSmartCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipForPaneCoreInternalAsync(
                WorkspacePanelId.Primary,
                target,
                (archivePath, progress, cancellationToken) =>
                    _fileManagementCoordinator.TryExtractZipSmartAsync(archivePath, progress, cancellationToken),
                "StatusExtractZipFailed",
                "ExtractZipFailureDialogTitle",
                "StatusExtractZipSuccess",
                "extract-smart");
        }

        private Task ExecuteExtractZipHereCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipForPaneCoreInternalAsync(
                WorkspacePanelId.Primary,
                target,
                (archivePath, progress, cancellationToken) =>
                    _fileManagementCoordinator.TryExtractZipHereAsync(archivePath, progress, cancellationToken),
                "StatusExtractZipFailed",
                "ExtractZipFailureDialogTitle",
                "StatusExtractZipSuccess",
                "extract-here");
        }

        private Task ExecuteExtractZipToFolderCommandAsync(FileCommandTarget target)
        {
            return ExecuteExtractZipForPaneCoreInternalAsync(
                WorkspacePanelId.Primary,
                target,
                (archivePath, progress, cancellationToken) =>
                    _fileManagementCoordinator.TryExtractZipToFolderAsync(archivePath, progress, cancellationToken),
                "StatusExtractZipFailed",
                "ExtractZipFailureDialogTitle",
                "StatusExtractZipSuccess",
                "extract-to-folder");
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

        private async Task ExecuteOpenInNewTabCommandAsync(FileCommandTarget target)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || !target.IsDirectory)
            {
                return;
            }

            try
            {
                await _workspaceTabController.OpenPathInNewTabAsync(target.Path);
                UpdateStatusKey("StatusOpenedInNewTab", target.DisplayName);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenInNewTabFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }

        private async Task ExecuteOpenTargetCommandAsync(FileCommandTarget target)
        {
            await ExecuteOpenTargetForPaneCoreInternalAsync(WorkspacePanelId.Primary, target);
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
