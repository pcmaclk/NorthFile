using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace NorthFileUI.Services;

public enum FileTransferMode
{
    Copy,
    Cut
}

public readonly record struct CreatedEntryInfo(string Name, string FullPath, bool IsDirectory, bool ChangeNotified);

public readonly record struct RenamedEntryInfo(string SourcePath, string TargetPath, bool ChangeNotified);

public readonly record struct FileClipboardItem(string SourcePath, string Name, bool IsDirectory);

public sealed record FileClipboardState(FileTransferMode Mode, IReadOnlyList<FileClipboardItem> Items);

public readonly record struct FilePasteItemResult(
    string SourcePath,
    string TargetPath,
    bool Applied,
    bool Conflict,
    bool SamePath,
    bool IsDirectory,
    string? ErrorMessage,
    FileOperationError Error = FileOperationError.Unknown);

public sealed record FilePasteResult(
    FileTransferMode Mode,
    IReadOnlyList<FilePasteItemResult> Items,
    bool TargetChanged,
    bool SourceChanged);

public sealed record FilePasteOperationResult(
    FilePasteResult? PasteResult,
    FileOperationFailure? Failure)
{
    public bool Succeeded => Failure is null;
}

public readonly record struct ZipExtractionInfo(
    string DestinationDirectory,
    string? PrimarySelectionPath,
    bool ChangeNotified);

public sealed class FileManagementCoordinator
{
    private readonly ExplorerService _explorerService;
    private FileClipboardState? _clipboardState;
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

    public FileManagementCoordinator(ExplorerService explorerService)
    {
        _explorerService = explorerService;
    }

    public FileClipboardState? ClipboardState => _clipboardState;

    public bool HasClipboardItems => _clipboardState is { Items.Count: > 0 };

    public bool HasAvailablePasteItems()
    {
        if (HasClipboardItems)
        {
            return true;
        }

        try
        {
            DataPackageView content = Clipboard.GetContent();
            return content.Contains(StandardDataFormats.StorageItems);
        }
        catch
        {
            return false;
        }
    }

    public async Task<CreatedEntryInfo> CreateEntryAsync(string directoryPath, bool isDirectory)
    {
        string name = isDirectory
            ? _explorerService.GenerateUniqueNewFolderName(directoryPath)
            : _explorerService.GenerateUniqueNewFileName(directoryPath);
        string fullPath = Path.Combine(directoryPath, name);

        await _explorerService.CreatePathAsync(fullPath, isDirectory);
        bool changeNotified = TryMarkPathChanged(directoryPath);
        return new CreatedEntryInfo(name, fullPath, isDirectory, changeNotified);
    }

    public async Task<FileOperationResult<CreatedEntryInfo>> TryCreateEntryAsync(string directoryPath, bool isDirectory)
    {
        try
        {
            return FileOperationResult<CreatedEntryInfo>.Success(await CreateEntryAsync(directoryPath, isDirectory));
        }
        catch (Exception ex)
        {
            return FileOperationResult<CreatedEntryInfo>.Fail(ex);
        }
    }

    public async Task<CreatedEntryInfo> CreateShortcutAsync(string directoryPath, string targetPath)
    {
        string name = _explorerService.GenerateUniqueShortcutName(directoryPath, targetPath);
        string fullPath = Path.Combine(directoryPath, name);

        await _explorerService.CreateShortcutAsync(targetPath, fullPath);
        bool changeNotified = TryMarkPathChanged(directoryPath);
        return new CreatedEntryInfo(name, fullPath, IsDirectory: false, changeNotified);
    }

    public async Task<FileOperationResult<CreatedEntryInfo>> TryCreateShortcutAsync(string directoryPath, string targetPath)
    {
        try
        {
            return FileOperationResult<CreatedEntryInfo>.Success(await CreateShortcutAsync(directoryPath, targetPath));
        }
        catch (Exception ex)
        {
            return FileOperationResult<CreatedEntryInfo>.Fail(ex);
        }
    }

    public async Task<RenamedEntryInfo> RenameEntryAsync(string directoryPath, string currentName, string newName)
    {
        string sourcePath = Path.Combine(directoryPath, currentName);
        string targetPath = Path.Combine(directoryPath, newName);
        await _explorerService.RenamePathAsync(sourcePath, targetPath);
        bool changeNotified = TryMarkPathChanged(directoryPath);
        return new RenamedEntryInfo(sourcePath, targetPath, changeNotified);
    }

    public async Task<FileOperationResult<RenamedEntryInfo>> TryRenameEntryAsync(string directoryPath, string currentName, string newName)
    {
        string sourcePath = Path.Combine(directoryPath, currentName);
        string targetPath = Path.Combine(directoryPath, newName);
        Exception? error = await _explorerService.TryRenamePathAsync(sourcePath, targetPath);
        if (error is not null)
        {
            return FileOperationResult<RenamedEntryInfo>.Fail(error);
        }

        bool changeNotified = TryMarkPathChanged(directoryPath);
        return FileOperationResult<RenamedEntryInfo>.Success(new RenamedEntryInfo(sourcePath, targetPath, changeNotified));
    }

    public async Task<bool> DeleteEntryAsync(string targetPath, bool recursive)
    {
        await _explorerService.DeletePathAsync(targetPath, recursive);
        string? parentPath = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentPath))
        {
            return TryMarkPathChanged(parentPath);
        }

        return false;
    }

    public async Task<FileOperationResult<bool>> TryDeleteEntryAsync(string targetPath, bool recursive)
    {
        return await TryDeleteEntryAsync(targetPath, recursive, new FileOperationProgressStore("delete", 1), CancellationToken.None);
    }

    public async Task<FileOperationResult<bool>> TryDeleteEntryAsync(
        string targetPath,
        bool recursive,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        progressStore.SetTotals(_explorerService.CountFileOperationItems(targetPath));
        var tracker = new FileOperationProgressTracker(progressStore);
        Exception? error = await _explorerService.TryDeletePathAsync(targetPath, recursive, tracker, cancellationToken);
        if (error is not null)
        {
            return FileOperationResult<bool>.Fail(error);
        }

        string? parentPath = Path.GetDirectoryName(targetPath);
        bool changeNotified = !string.IsNullOrWhiteSpace(parentPath) && TryMarkPathChanged(parentPath);
        return FileOperationResult<bool>.Success(changeNotified);
    }

    public async Task<FileOperationResult<string>> TryCreateZipArchiveAsync(string sourcePath, string archivePath)
    {
        return await TryCreateZipArchiveAsync(sourcePath, archivePath, new FileOperationProgressStore("compress", 1), CancellationToken.None);
    }

    public async Task<FileOperationResult<string>> TryCreateZipArchiveAsync(
        string sourcePath,
        string archivePath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        try
        {
            progressStore.SetTotals(_explorerService.CountFileOperationItems(sourcePath), _explorerService.CountFileOperationBytes(sourcePath));
            var tracker = new FileOperationProgressTracker(progressStore);
            await _explorerService.CreateZipArchiveAsync(sourcePath, archivePath, tracker, cancellationToken);
            return FileOperationResult<string>.Success(archivePath);
        }
        catch (Exception ex)
        {
            return FileOperationResult<string>.Fail(ex);
        }
    }

    public async Task<FileOperationResult<ZipExtractionInfo>> TryExtractZipHereAsync(string archivePath)
    {
        return await TryExtractZipHereAsync(archivePath, new FileOperationProgressStore("extract", 1), CancellationToken.None);
    }

    public async Task<FileOperationResult<ZipExtractionInfo>> TryExtractZipHereAsync(
        string archivePath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        try
        {
            string destinationDirectory = RequireArchiveParentDirectory(archivePath);
            progressStore.SetTotals(_explorerService.CountZipExtractionItems(archivePath), _explorerService.CountZipExtractionBytes(archivePath));
            var tracker = new FileOperationProgressTracker(progressStore);
            ZipExtractionPlan plan = await _explorerService.ExtractZipHereAsync(archivePath, destinationDirectory, tracker, cancellationToken);
            bool changeNotified = TryMarkPathChanged(destinationDirectory);
            return FileOperationResult<ZipExtractionInfo>.Success(new ZipExtractionInfo(destinationDirectory, plan.PrimarySelectionPath, changeNotified));
        }
        catch (Exception ex)
        {
            return FileOperationResult<ZipExtractionInfo>.Fail(ex);
        }
    }

    public async Task<FileOperationResult<ZipExtractionInfo>> TryExtractZipToFolderAsync(string archivePath)
    {
        return await TryExtractZipToFolderAsync(archivePath, new FileOperationProgressStore("extract", 1), CancellationToken.None);
    }

    public async Task<FileOperationResult<ZipExtractionInfo>> TryExtractZipToFolderAsync(
        string archivePath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        try
        {
            string destinationDirectory = RequireArchiveParentDirectory(archivePath);
            progressStore.SetTotals(_explorerService.CountZipExtractionItems(archivePath), _explorerService.CountZipExtractionBytes(archivePath));
            var tracker = new FileOperationProgressTracker(progressStore);
            ZipExtractionPlan plan = await _explorerService.ExtractZipToFolderAsync(archivePath, destinationDirectory, tracker, cancellationToken);
            bool changeNotified = TryMarkPathChanged(destinationDirectory);
            return FileOperationResult<ZipExtractionInfo>.Success(new ZipExtractionInfo(destinationDirectory, plan.PrimarySelectionPath, changeNotified));
        }
        catch (Exception ex)
        {
            return FileOperationResult<ZipExtractionInfo>.Fail(ex);
        }
    }

    public async Task<FileOperationResult<ZipExtractionInfo>> TryExtractZipSmartAsync(string archivePath)
    {
        return await TryExtractZipSmartAsync(archivePath, new FileOperationProgressStore("extract", 1), CancellationToken.None);
    }

    public async Task<FileOperationResult<ZipExtractionInfo>> TryExtractZipSmartAsync(
        string archivePath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        try
        {
            string destinationDirectory = RequireArchiveParentDirectory(archivePath);
            progressStore.SetTotals(_explorerService.CountZipExtractionItems(archivePath), _explorerService.CountZipExtractionBytes(archivePath));
            var tracker = new FileOperationProgressTracker(progressStore);
            ZipExtractionPlan plan = await _explorerService.ExtractZipSmartAsync(archivePath, destinationDirectory, tracker, cancellationToken);
            bool changeNotified = TryMarkPathChanged(destinationDirectory);
            return FileOperationResult<ZipExtractionInfo>.Success(new ZipExtractionInfo(destinationDirectory, plan.PrimarySelectionPath, changeNotified));
        }
        catch (Exception ex)
        {
            return FileOperationResult<ZipExtractionInfo>.Fail(ex);
        }
    }

    public bool TryValidateName(string directoryPath, string currentName, string proposedName, out string error)
    {
        if (string.IsNullOrWhiteSpace(proposedName))
        {
            error = LocalizedStrings.Instance.Get("ErrorNameEmpty");
            return false;
        }

        if (proposedName is "." or "..")
        {
            error = string.Format(LocalizedStrings.Instance.Get("ErrorNameReserved"), proposedName);
            return false;
        }

        if (proposedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = string.Format(LocalizedStrings.Instance.Get("ErrorNameInvalidChars"), proposedName);
            return false;
        }

        string targetPath = Path.Combine(directoryPath, proposedName);
        if (!string.Equals(proposedName, currentName, StringComparison.OrdinalIgnoreCase) &&
            _explorerService.PathExists(targetPath))
        {
            error = string.Format(LocalizedStrings.Instance.Get("ErrorNameAlreadyExists"), proposedName);
            return false;
        }

        error = string.Empty;
        return true;
    }

    public void SetClipboard(IEnumerable<string> sourcePaths, FileTransferMode mode)
    {
        var items = new List<FileClipboardItem>();
        foreach (string sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                continue;
            }

            bool isDirectory = _explorerService.DirectoryExists(sourcePath);
            if (!isDirectory && !_explorerService.PathExists(sourcePath))
            {
                continue;
            }

            string normalizedPath = sourcePath.TrimEnd('\\');
            string name = Path.GetFileName(normalizedPath);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = normalizedPath;
            }

            items.Add(new FileClipboardItem(sourcePath, name, isDirectory));
        }

        _clipboardState = items.Count > 0
            ? new FileClipboardState(mode, items)
            : null;
    }

    public void ClearClipboard()
    {
        _clipboardState = null;
    }

    public async Task<FilePasteResult> PasteAsync(string targetDirectoryPath)
    {
        return await PasteAsync(targetDirectoryPath, new FileOperationProgressStore("transfer", 1), CancellationToken.None);
    }

    public async Task<FilePasteResult> PasteAsync(
        string targetDirectoryPath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        if (_clipboardState is null || _clipboardState.Items.Count == 0)
        {
            return new FilePasteResult(FileTransferMode.Copy, Array.Empty<FilePasteItemResult>(), TargetChanged: false, SourceChanged: false);
        }

        if (_clipboardState.Mode == FileTransferMode.Cut)
        {
            return await MoveClipboardItemsAsync(_clipboardState, targetDirectoryPath, progressStore, cancellationToken);
        }

        return await CopyClipboardItemsAsync(_clipboardState, targetDirectoryPath, progressStore, cancellationToken);
    }

    private async Task<FilePasteResult> CopyClipboardItemsAsync(
        FileClipboardState clipboardState,
        string targetDirectoryPath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var results = new List<FilePasteItemResult>(clipboardState.Items.Count);
            bool allApplied = true;
            FileTransferMode mode = clipboardState.Mode;
            int totalItems = CountTransferItems(clipboardState.Items, mode);
            long totalBytes = CountTransferBytes(clipboardState.Items, mode);
            progressStore.SetTotals(totalItems, totalBytes);
            var tracker = new FileOperationProgressTracker(progressStore);
            bool hasAppliedTarget = false;
            var changedSourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FileClipboardItem item in clipboardState.Items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    allApplied = false;
                    break;
                }

                string targetPath = Path.Combine(targetDirectoryPath, item.Name);
                bool samePath = string.Equals(item.SourcePath.TrimEnd('\\'), targetPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (samePath && mode == FileTransferMode.Copy)
                {
                    targetPath = _explorerService.GenerateUniqueCopyTargetPath(targetDirectoryPath, item.SourcePath);
                    samePath = false;
                }

                if (samePath)
                {
                    results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: false, Conflict: false, SamePath: true, IsDirectory: item.IsDirectory, ErrorMessage: S("ErrorPasteSameSourceAndTarget")));
                    allApplied = false;
                    continue;
                }

                if (item.IsDirectory && _explorerService.IsDirectoryTargetSelfOrDescendant(item.SourcePath, targetDirectoryPath))
                {
                    results.Add(CreateTargetIsSourceDescendantResult(item, targetPath));
                    allApplied = false;
                    continue;
                }

                bool conflict = _explorerService.PathExists(targetPath);
                if (conflict)
                {
                    results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: false, Conflict: true, SamePath: false, IsDirectory: item.IsDirectory, ErrorMessage: S("ErrorPasteTargetAlreadyExists")));
                    allApplied = false;
                    continue;
                }

                try
                {
                    if (mode == FileTransferMode.Copy)
                    {
                        _explorerService.CopyPath(item.SourcePath, targetPath, tracker, cancellationToken);
                    }
                    else
                    {
                        _explorerService.MovePath(item.SourcePath, targetPath, tracker, cancellationToken);

                        string? sourceParentPath = _explorerService.GetParentPath(item.SourcePath);
                        if (!string.IsNullOrWhiteSpace(sourceParentPath))
                        {
                            changedSourceParents.Add(sourceParentPath);
                        }
                    }

                    results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: true, Conflict: false, SamePath: false, IsDirectory: item.IsDirectory, ErrorMessage: null));
                    hasAppliedTarget = true;
                }
                catch (Exception ex)
                {
                    results.Add(new FilePasteItemResult(
                        item.SourcePath,
                        targetPath,
                        Applied: false,
                        Conflict: false,
                        SamePath: false,
                        IsDirectory: item.IsDirectory,
                        ErrorMessage: FileOperationErrors.ToUserMessage(ex),
                        Error: FileOperationErrors.Classify(ex)));
                    allApplied = false;
                }
            }

            if (mode == FileTransferMode.Cut && allApplied)
            {
                _clipboardState = null;
            }

            bool targetChanged = hasAppliedTarget && TryMarkPathChanged(targetDirectoryPath);
            bool sourceChanged = false;
            foreach (string sourceParentPath in changedSourceParents)
            {
                sourceChanged |= TryMarkPathChanged(sourceParentPath);
            }

            return new FilePasteResult(
                mode,
                results,
                TargetChanged: targetChanged,
                SourceChanged: sourceChanged);
        });
    }

    private async Task<FilePasteResult> MoveClipboardItemsAsync(
        FileClipboardState clipboardState,
        string targetDirectoryPath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var results = new List<FilePasteItemResult>(clipboardState.Items.Count);
            bool allApplied = true;
            progressStore.SetTotals(CountTransferItems(clipboardState.Items, FileTransferMode.Cut));
            var tracker = new FileOperationProgressTracker(progressStore);
            bool hasAppliedTarget = false;
            var changedSourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FileClipboardItem item in clipboardState.Items)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    allApplied = false;
                    break;
                }

                string targetPath = Path.Combine(targetDirectoryPath, item.Name);
                bool samePath = string.Equals(item.SourcePath.TrimEnd('\\'), targetPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                if (samePath)
                {
                    results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: false, Conflict: false, SamePath: true, IsDirectory: item.IsDirectory, ErrorMessage: S("ErrorPasteSameSourceAndTarget")));
                    allApplied = false;
                    continue;
                }

                if (item.IsDirectory && _explorerService.IsDirectoryTargetSelfOrDescendant(item.SourcePath, targetDirectoryPath))
                {
                    results.Add(CreateTargetIsSourceDescendantResult(item, targetPath));
                    allApplied = false;
                    continue;
                }

                if (_explorerService.PathExists(targetPath))
                {
                    results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: false, Conflict: true, SamePath: false, IsDirectory: item.IsDirectory, ErrorMessage: S("ErrorPasteTargetAlreadyExists")));
                    allApplied = false;
                    continue;
                }

                try
                {
                    _explorerService.MovePath(item.SourcePath, targetPath, tracker, cancellationToken);

                    string? sourceParentPath = _explorerService.GetParentPath(item.SourcePath);
                    if (!string.IsNullOrWhiteSpace(sourceParentPath))
                    {
                        changedSourceParents.Add(sourceParentPath);
                    }

                    hasAppliedTarget = true;
                    results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: true, Conflict: false, SamePath: false, IsDirectory: item.IsDirectory, ErrorMessage: null));
                }
                catch (Exception ex)
                {
                    results.Add(new FilePasteItemResult(
                        item.SourcePath,
                        targetPath,
                        Applied: false,
                        Conflict: false,
                        SamePath: false,
                        IsDirectory: item.IsDirectory,
                        ErrorMessage: FileOperationErrors.ToUserMessage(ex),
                        Error: FileOperationErrors.Classify(ex)));
                    allApplied = false;
                }
            }

            if (allApplied && ReferenceEquals(_clipboardState, clipboardState))
            {
                _clipboardState = null;
            }

            bool targetChanged = hasAppliedTarget && TryMarkPathChanged(targetDirectoryPath);
            bool sourceChanged = false;
            foreach (string sourceParentPath in changedSourceParents)
            {
                sourceChanged |= TryMarkPathChanged(sourceParentPath);
            }

            return new FilePasteResult(
                FileTransferMode.Cut,
                results,
                TargetChanged: targetChanged,
                SourceChanged: sourceChanged);
        });
    }

    public async Task<FilePasteOperationResult> TryPasteAsync(string targetDirectoryPath)
    {
        return await TryPasteAsync(targetDirectoryPath, new FileOperationProgressStore("transfer", 1), CancellationToken.None);
    }

    public async Task<FilePasteOperationResult> TryPasteAsync(
        string targetDirectoryPath,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        try
        {
            return new FilePasteOperationResult(await PasteAsync(targetDirectoryPath, progressStore, cancellationToken), Failure: null);
        }
        catch (Exception ex)
        {
            return new FilePasteOperationResult(PasteResult: null, new FileOperationFailure(FileOperationErrors.Classify(ex), FileOperationErrors.ToUserMessage(ex)));
        }
    }

    public async Task<FilePasteOperationResult> TryResolvePasteConflictsAsync(FilePasteResult priorResult, bool replaceAll = true)
    {
        return await TryResolvePasteConflictsAsync(priorResult, replaceAll, new FileOperationProgressStore("transfer", 1), CancellationToken.None);
    }

    public async Task<FilePasteOperationResult> TryResolvePasteConflictsAsync(
        FilePasteResult priorResult,
        bool replaceAll,
        FileOperationProgressStore progressStore,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolvedItems = new List<FilePasteItemResult>(priorResult.Items.Count);
            bool targetChanged = priorResult.TargetChanged;
            bool sourceChanged = priorResult.SourceChanged;
            bool resolvedConflict = false;
            var changedTargetParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changedSourceParents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            progressStore.SetTotals(CountTransferItems(priorResult.Items, priorResult.Mode), CountTransferBytes(priorResult.Items, priorResult.Mode));
            var tracker = new FileOperationProgressTracker(progressStore);

            foreach (FilePasteItemResult item in priorResult.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.Conflict || (!replaceAll && resolvedConflict))
                {
                    resolvedItems.Add(item);
                    continue;
                }

                string itemTargetDirectory = Path.GetDirectoryName(item.TargetPath) ?? item.TargetPath;
                if (item.IsDirectory && _explorerService.IsDirectoryTargetSelfOrDescendant(item.SourcePath, itemTargetDirectory))
                {
                    resolvedItems.Add(CreateTargetIsSourceDescendantResult(item, item.TargetPath));
                    continue;
                }

                await _explorerService.DeleteExistingPathForReplaceAsync(item.TargetPath);
                if (priorResult.Mode == FileTransferMode.Copy)
                {
                    await _explorerService.CopyPathAsync(item.SourcePath, item.TargetPath, tracker, cancellationToken);
                }
                else
                {
                    await _explorerService.MovePathAsync(item.SourcePath, item.TargetPath, tracker, cancellationToken);
                    string? sourceParentPath = _explorerService.GetParentPath(item.SourcePath);
                    if (!string.IsNullOrWhiteSpace(sourceParentPath))
                    {
                        changedSourceParents.Add(sourceParentPath);
                    }
                }

                changedTargetParents.Add(Path.GetDirectoryName(item.TargetPath) ?? item.TargetPath);
                resolvedItems.Add(item with
                {
                    Applied = true,
                    Conflict = false,
                    ErrorMessage = null
                });
                resolvedConflict = true;
            }

            foreach (string targetParentPath in changedTargetParents)
            {
                targetChanged |= TryMarkPathChanged(targetParentPath);
            }

            foreach (string sourceParentPath in changedSourceParents)
            {
                sourceChanged |= TryMarkPathChanged(sourceParentPath);
            }

            return new FilePasteOperationResult(
                new FilePasteResult(priorResult.Mode, resolvedItems, targetChanged, sourceChanged),
                Failure: null);
        }
        catch (Exception ex)
        {
            return new FilePasteOperationResult(
                PasteResult: null,
                new FileOperationFailure(FileOperationErrors.Classify(ex), FileOperationErrors.ToUserMessage(ex)));
        }
    }

    private static FilePasteItemResult CreateTargetIsSourceDescendantResult(FileClipboardItem item, string targetPath)
    {
        return new FilePasteItemResult(
            item.SourcePath,
            targetPath,
            Applied: false,
            Conflict: false,
            SamePath: false,
            IsDirectory: item.IsDirectory,
            ErrorMessage: FileOperationErrors.ToUserMessage(FileOperationError.TargetIsSourceDescendant),
            Error: FileOperationError.TargetIsSourceDescendant);
    }

    private static FilePasteItemResult CreateTargetIsSourceDescendantResult(FilePasteItemResult item, string targetPath)
    {
        return item with
        {
            TargetPath = targetPath,
            Applied = false,
            Conflict = false,
            SamePath = false,
            ErrorMessage = FileOperationErrors.ToUserMessage(FileOperationError.TargetIsSourceDescendant),
            Error = FileOperationError.TargetIsSourceDescendant
        };
    }

    private int CountTransferItems(IEnumerable<FileClipboardItem> items, FileTransferMode mode)
    {
        int count = 0;
        foreach (FileClipboardItem item in items)
        {
            count += mode == FileTransferMode.Cut
                ? 1
                : _explorerService.CountFileOperationItems(item.SourcePath);
        }

        return Math.Max(1, count);
    }

    private int CountTransferItems(IEnumerable<FilePasteItemResult> items, FileTransferMode mode)
    {
        int count = 0;
        foreach (FilePasteItemResult item in items)
        {
            if (item.Conflict)
            {
                count += mode == FileTransferMode.Cut
                    ? 1
                    : _explorerService.CountFileOperationItems(item.SourcePath);
            }
        }

        return Math.Max(1, count);
    }

    private long CountTransferBytes(IEnumerable<FileClipboardItem> items, FileTransferMode mode)
    {
        if (mode != FileTransferMode.Copy)
        {
            return 0;
        }

        long count = 0;
        foreach (FileClipboardItem item in items)
        {
            count += _explorerService.CountFileOperationBytes(item.SourcePath);
        }

        return count;
    }

    private long CountTransferBytes(IEnumerable<FilePasteItemResult> items, FileTransferMode mode)
    {
        if (mode != FileTransferMode.Copy)
        {
            return 0;
        }

        long count = 0;
        foreach (FilePasteItemResult item in items)
        {
            if (item.Conflict)
            {
                count += _explorerService.CountFileOperationBytes(item.SourcePath);
            }
        }

        return count;
    }

    private bool TryMarkPathChanged(string path)
    {
        try
        {
            _explorerService.MarkPathChanged(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string RequireArchiveParentDirectory(string archivePath)
    {
        string? parentPath = Path.GetDirectoryName(archivePath.TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(parentPath))
        {
            throw new DirectoryNotFoundException(archivePath);
        }

        return parentPath;
    }
}
