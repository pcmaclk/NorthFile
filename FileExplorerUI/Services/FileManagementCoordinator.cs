using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace FileExplorerUI.Services;

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
    string? ErrorMessage);

public sealed record FilePasteResult(
    FileTransferMode Mode,
    IReadOnlyList<FilePasteItemResult> Items,
    bool TargetChanged,
    bool SourceChanged);

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

    public async Task<RenamedEntryInfo> RenameEntryAsync(string directoryPath, string currentName, string newName)
    {
        string sourcePath = Path.Combine(directoryPath, currentName);
        string targetPath = Path.Combine(directoryPath, newName);
        await _explorerService.RenamePathAsync(sourcePath, targetPath);
        bool changeNotified = TryMarkPathChanged(directoryPath);
        return new RenamedEntryInfo(sourcePath, targetPath, changeNotified);
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
        if (_clipboardState is null || _clipboardState.Items.Count == 0)
        {
            return new FilePasteResult(FileTransferMode.Copy, Array.Empty<FilePasteItemResult>(), TargetChanged: false, SourceChanged: false);
        }

        var results = new List<FilePasteItemResult>(_clipboardState.Items.Count);
        bool targetChanged = false;
        bool sourceChanged = false;
        bool allApplied = true;
        FileTransferMode mode = _clipboardState.Mode;

        foreach (FileClipboardItem item in _clipboardState.Items)
        {
            string targetPath = Path.Combine(targetDirectoryPath, item.Name);
            bool samePath = string.Equals(item.SourcePath.TrimEnd('\\'), targetPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            bool conflict = !samePath && _explorerService.PathExists(targetPath);

            if (samePath)
            {
                results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: false, Conflict: false, SamePath: true, IsDirectory: item.IsDirectory, ErrorMessage: S("ErrorPasteSameSourceAndTarget")));
                allApplied = false;
                continue;
            }

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
                    await _explorerService.CopyPathAsync(item.SourcePath, targetPath);
                }
                else
                {
                    await _explorerService.MovePathAsync(item.SourcePath, targetPath);

                    string? sourceParentPath = _explorerService.GetParentPath(item.SourcePath);
                    if (!string.IsNullOrWhiteSpace(sourceParentPath))
                    {
                        sourceChanged |= TryMarkPathChanged(sourceParentPath);
                    }
                }

                results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: true, Conflict: false, SamePath: false, IsDirectory: item.IsDirectory, ErrorMessage: null));
                targetChanged |= TryMarkPathChanged(targetDirectoryPath);
            }
            catch (Exception ex)
            {
                results.Add(new FilePasteItemResult(item.SourcePath, targetPath, Applied: false, Conflict: false, SamePath: false, IsDirectory: item.IsDirectory, ErrorMessage: ex.Message));
                allApplied = false;
            }
        }

        if (mode == FileTransferMode.Cut && allApplied)
        {
            _clipboardState = null;
        }

        return new FilePasteResult(
            mode,
            results,
            TargetChanged: targetChanged,
            SourceChanged: sourceChanged);
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
}
