using NorthFileUI.Commands;
using NorthFileUI.Workspace;
using System;
using System.Threading.Tasks;

namespace NorthFileUI
{
    internal sealed class PaneFileCommandHandler
    {
        private readonly Func<WorkspacePanelId, Task> _newFileAsync;
        private readonly Func<WorkspacePanelId, Task> _newFolderAsync;
        private readonly Func<WorkspacePanelId, bool> _canCreate;
        private readonly Func<WorkspacePanelId, bool> _canRename;
        private readonly Func<WorkspacePanelId, bool> _canDelete;
        private readonly Func<WorkspacePanelId, bool> _canCopy;
        private readonly Func<WorkspacePanelId, bool> _canCut;
        private readonly Func<WorkspacePanelId, bool> _canPaste;
        private readonly Func<WorkspacePanelId, bool> _canRefresh;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canPasteTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canCreateTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canRefreshTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canCopyTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canCutTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canRenameTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canDeleteTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canCreateShortcutTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canCompressZipTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canExtractZipTarget;
        private readonly Func<WorkspacePanelId, FileCommandTarget, bool> _canOpenTarget;
        private readonly Func<WorkspacePanelId, Task> _renameAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _renameTargetAsync;
        private readonly Func<WorkspacePanelId, Task> _deleteAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _deleteTargetAsync;
        private readonly Action<WorkspacePanelId> _copy;
        private readonly Action<WorkspacePanelId> _cut;
        private readonly Func<WorkspacePanelId, Task> _pasteAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _pasteTargetAsync;
        private readonly Func<WorkspacePanelId, Task> _refreshAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _createShortcutAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _compressZipAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _extractZipSmartAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _extractZipHereAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _extractZipToFolderAsync;
        private readonly Func<WorkspacePanelId, FileCommandTarget, Task> _openTargetAsync;

        public PaneFileCommandHandler(
            Func<WorkspacePanelId, Task> newFileAsync,
            Func<WorkspacePanelId, Task> newFolderAsync,
            Func<WorkspacePanelId, bool> canCreate,
            Func<WorkspacePanelId, bool> canRename,
            Func<WorkspacePanelId, bool> canDelete,
            Func<WorkspacePanelId, bool> canCopy,
            Func<WorkspacePanelId, bool> canCut,
            Func<WorkspacePanelId, bool> canPaste,
            Func<WorkspacePanelId, bool> canRefresh,
            Func<WorkspacePanelId, FileCommandTarget, bool> canPasteTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canCreateTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canRefreshTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canCopyTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canCutTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canRenameTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canDeleteTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canCreateShortcutTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canCompressZipTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canExtractZipTarget,
            Func<WorkspacePanelId, FileCommandTarget, bool> canOpenTarget,
            Func<WorkspacePanelId, Task> renameAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> renameTargetAsync,
            Func<WorkspacePanelId, Task> deleteAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> deleteTargetAsync,
            Action<WorkspacePanelId> copy,
            Action<WorkspacePanelId> cut,
            Func<WorkspacePanelId, Task> pasteAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> pasteTargetAsync,
            Func<WorkspacePanelId, Task> refreshAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> createShortcutAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> compressZipAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> extractZipSmartAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> extractZipHereAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> extractZipToFolderAsync,
            Func<WorkspacePanelId, FileCommandTarget, Task> openTargetAsync)
        {
            _newFileAsync = newFileAsync;
            _newFolderAsync = newFolderAsync;
            _canCreate = canCreate;
            _canRename = canRename;
            _canDelete = canDelete;
            _canCopy = canCopy;
            _canCut = canCut;
            _canPaste = canPaste;
            _canRefresh = canRefresh;
            _canPasteTarget = canPasteTarget;
            _canCreateTarget = canCreateTarget;
            _canRefreshTarget = canRefreshTarget;
            _canCopyTarget = canCopyTarget;
            _canCutTarget = canCutTarget;
            _canRenameTarget = canRenameTarget;
            _canDeleteTarget = canDeleteTarget;
            _canCreateShortcutTarget = canCreateShortcutTarget;
            _canCompressZipTarget = canCompressZipTarget;
            _canExtractZipTarget = canExtractZipTarget;
            _canOpenTarget = canOpenTarget;
            _renameAsync = renameAsync;
            _renameTargetAsync = renameTargetAsync;
            _deleteAsync = deleteAsync;
            _deleteTargetAsync = deleteTargetAsync;
            _copy = copy;
            _cut = cut;
            _pasteAsync = pasteAsync;
            _pasteTargetAsync = pasteTargetAsync;
            _refreshAsync = refreshAsync;
            _createShortcutAsync = createShortcutAsync;
            _compressZipAsync = compressZipAsync;
            _extractZipSmartAsync = extractZipSmartAsync;
            _extractZipHereAsync = extractZipHereAsync;
            _extractZipToFolderAsync = extractZipToFolderAsync;
            _openTargetAsync = openTargetAsync;
        }

        public bool CanCreate(WorkspacePanelId panelId) => _canCreate(panelId);
        public bool CanRename(WorkspacePanelId panelId) => _canRename(panelId);
        public bool CanDelete(WorkspacePanelId panelId) => _canDelete(panelId);
        public bool CanCopy(WorkspacePanelId panelId) => _canCopy(panelId);
        public bool CanCut(WorkspacePanelId panelId) => _canCut(panelId);
        public bool CanPaste(WorkspacePanelId panelId) => _canPaste(panelId);
        public bool CanRefresh(WorkspacePanelId panelId) => _canRefresh(panelId);
        public bool CanPasteTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canPasteTarget(panelId, target);
        public bool CanCreateTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canCreateTarget(panelId, target);
        public bool CanRefreshTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canRefreshTarget(panelId, target);
        public bool CanCopyTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canCopyTarget(panelId, target);
        public bool CanCutTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canCutTarget(panelId, target);
        public bool CanRenameTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canRenameTarget(panelId, target);
        public bool CanDeleteTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canDeleteTarget(panelId, target);
        public bool CanCreateShortcutTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canCreateShortcutTarget(panelId, target);
        public bool CanCompressZipTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canCompressZipTarget(panelId, target);
        public bool CanExtractZipTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canExtractZipTarget(panelId, target);
        public bool CanOpenTarget(WorkspacePanelId panelId, FileCommandTarget target) => _canOpenTarget(panelId, target);

        public Task ExecuteNewFileAsync(WorkspacePanelId panelId) => _newFileAsync(panelId);
        public Task ExecuteNewFolderAsync(WorkspacePanelId panelId) => _newFolderAsync(panelId);
        public Task ExecuteRenameAsync(WorkspacePanelId panelId) => _renameAsync(panelId);
        public Task ExecuteRenameTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _renameTargetAsync(panelId, target);
        public Task ExecuteDeleteAsync(WorkspacePanelId panelId) => _deleteAsync(panelId);
        public Task ExecuteDeleteTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _deleteTargetAsync(panelId, target);
        public void ExecuteCopy(WorkspacePanelId panelId) => _copy(panelId);
        public void ExecuteCut(WorkspacePanelId panelId) => _cut(panelId);
        public Task ExecutePasteAsync(WorkspacePanelId panelId) => _pasteAsync(panelId);
        public Task ExecutePasteTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _pasteTargetAsync(panelId, target);
        public Task ExecuteRefreshAsync(WorkspacePanelId panelId) => _refreshAsync(panelId);
        public Task ExecuteCreateShortcutAsync(WorkspacePanelId panelId, FileCommandTarget target) => _createShortcutAsync(panelId, target);
        public Task ExecuteCompressZipAsync(WorkspacePanelId panelId, FileCommandTarget target) => _compressZipAsync(panelId, target);
        public Task ExecuteExtractZipSmartAsync(WorkspacePanelId panelId, FileCommandTarget target) => _extractZipSmartAsync(panelId, target);
        public Task ExecuteExtractZipHereAsync(WorkspacePanelId panelId, FileCommandTarget target) => _extractZipHereAsync(panelId, target);
        public Task ExecuteExtractZipToFolderAsync(WorkspacePanelId panelId, FileCommandTarget target) => _extractZipToFolderAsync(panelId, target);
        public Task ExecuteOpenTargetAsync(WorkspacePanelId panelId, FileCommandTarget target) => _openTargetAsync(panelId, target);
    }
}
