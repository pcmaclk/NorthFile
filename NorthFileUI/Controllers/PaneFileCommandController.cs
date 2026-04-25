using NorthFileUI.Commands;
using NorthFileUI.Workspace;
using System;
using System.Threading.Tasks;

namespace NorthFileUI
{
    internal sealed class PaneFileCommandController
    {
        private readonly Func<WorkspacePanelId> _activePanelProvider;
        private readonly PaneFileCommandHandler _handler;

        public PaneFileCommandController(
            Func<WorkspacePanelId> activePanelProvider,
            PaneFileCommandHandler handler)
        {
            _activePanelProvider = activePanelProvider;
            _handler = handler;
        }

        public WorkspacePanelId ActivePanel => _activePanelProvider();
        public bool CanCreate(WorkspacePanelId panelId) => _handler.CanCreate(panelId);
        public bool CanRename(WorkspacePanelId panelId) => _handler.CanRename(panelId);
        public bool CanDelete(WorkspacePanelId panelId) => _handler.CanDelete(panelId);
        public bool CanCopy(WorkspacePanelId panelId) => _handler.CanCopy(panelId);
        public bool CanCut(WorkspacePanelId panelId) => _handler.CanCut(panelId);
        public bool CanPaste(WorkspacePanelId panelId) => _handler.CanPaste(panelId);
        public bool CanRefresh(WorkspacePanelId panelId) => _handler.CanRefresh(panelId);
        public bool CanPasteTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanPasteTarget(panelId, target);
        public bool CanCreateTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanCreateTarget(panelId, target);
        public bool CanRefreshTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanRefreshTarget(panelId, target);
        public bool CanCopyTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanCopyTarget(panelId, target);
        public bool CanCutTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanCutTarget(panelId, target);
        public bool CanRenameTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanRenameTarget(panelId, target);
        public bool CanDeleteTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanDeleteTarget(panelId, target);
        public bool CanCreateShortcutTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanCreateShortcutTarget(panelId, target);
        public bool CanCompressZipTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanCompressZipTarget(panelId, target);
        public bool CanExtractZipTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanExtractZipTarget(panelId, target);
        public bool CanOpenTarget(WorkspacePanelId panelId, FileCommandTarget target) => _handler.CanOpenTarget(panelId, target);
        public bool CanCreateInActivePane() => CanCreate(ActivePanel);
        public bool CanRenameInActivePane() => CanRename(ActivePanel);
        public bool CanDeleteInActivePane() => CanDelete(ActivePanel);
        public bool CanCopyInActivePane() => CanCopy(ActivePanel);
        public bool CanCutInActivePane() => CanCut(ActivePanel);
        public bool CanPasteInActivePane() => CanPaste(ActivePanel);
        public bool CanRefreshInActivePane() => CanRefresh(ActivePanel);
        public Task ExecuteNewFileAsync(WorkspacePanelId panelId) => _handler.ExecuteNewFileAsync(panelId);
        public Task ExecuteNewFolderAsync(WorkspacePanelId panelId) => _handler.ExecuteNewFolderAsync(panelId);
        public Task ExecuteNewFileInActivePaneAsync() => ExecuteNewFileAsync(ActivePanel);
        public Task ExecuteNewFolderInActivePaneAsync() => ExecuteNewFolderAsync(ActivePanel);
        public Task ExecuteRenameAsync(WorkspacePanelId panelId) => _handler.ExecuteRenameAsync(panelId);
        public Task ExecuteRenameTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteRenameTargetAsync(panelId, target);
        public Task ExecuteRenameInActivePaneAsync() => ExecuteRenameAsync(ActivePanel);
        public Task ExecuteDeleteAsync(WorkspacePanelId panelId) => _handler.ExecuteDeleteAsync(panelId);
        public Task ExecuteDeleteTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteDeleteTargetAsync(panelId, target);
        public Task ExecuteDeleteInActivePaneAsync() => ExecuteDeleteAsync(ActivePanel);
        public void ExecuteCopy(WorkspacePanelId panelId) => _handler.ExecuteCopy(panelId);
        public void ExecuteCut(WorkspacePanelId panelId) => _handler.ExecuteCut(panelId);
        public void ExecuteCopyInActivePane() => ExecuteCopy(ActivePanel);
        public void ExecuteCutInActivePane() => ExecuteCut(ActivePanel);
        public Task ExecutePasteAsync(WorkspacePanelId panelId) => _handler.ExecutePasteAsync(panelId);
        public Task ExecutePasteTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecutePasteTargetAsync(panelId, target);
        public Task ExecutePasteInActivePaneAsync() => ExecutePasteAsync(ActivePanel);
        public Task ExecuteRefreshAsync(WorkspacePanelId panelId) => _handler.ExecuteRefreshAsync(panelId);
        public Task ExecuteRefreshInActivePaneAsync() => ExecuteRefreshAsync(ActivePanel);
        public Task ExecuteCreateShortcutAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteCreateShortcutAsync(panelId, target);
        public Task ExecuteCompressZipAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteCompressZipAsync(panelId, target);
        public Task ExecuteExtractZipSmartAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteExtractZipSmartAsync(panelId, target);
        public Task ExecuteExtractZipHereAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteExtractZipHereAsync(panelId, target);
        public Task ExecuteExtractZipToFolderAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteExtractZipToFolderAsync(panelId, target);
        public Task ExecuteOpenTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _handler.ExecuteOpenTargetAsync(panelId, target);

    }
}
