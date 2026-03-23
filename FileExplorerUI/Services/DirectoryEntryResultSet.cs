using FileExplorerUI.Interop;

namespace FileExplorerUI.Services;

public sealed class DirectoryEntryResultSet : IEntryResultSet
{
    private readonly ExplorerService _explorerService;

    public DirectoryEntryResultSet(ExplorerService explorerService, string path, DirectorySortMode sortMode)
    {
        _explorerService = explorerService;
        Path = path;
        SortMode = sortMode;
        Query = string.Empty;
    }

    public string Path { get; }

    public string Query { get; }

    public DirectorySortMode SortMode { get; }

    public bool TryReadRange(
        ulong startIndex,
        uint count,
        uint lastFetchMs,
        out FileBatchPage page,
        out int errorCode,
        out string errorMessage)
    {
        return _explorerService.TryReadDirectoryRowsAuto(
            Path,
            startIndex,
            count,
            lastFetchMs,
            SortMode,
            out page,
            out errorCode,
            out errorMessage);
    }
}
