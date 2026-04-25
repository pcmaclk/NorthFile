using NorthFileUI.Interop;

namespace NorthFileUI.Services;

public sealed class SearchEntryResultSet : IEntryResultSet
{
    private readonly ExplorerService _explorerService;

    public SearchEntryResultSet(ExplorerService explorerService, string path, string query, DirectorySortMode sortMode)
    {
        _explorerService = explorerService;
        Path = path;
        Query = query;
        SortMode = sortMode;
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
        return _explorerService.TrySearchDirectoryRowsAuto(
            Path,
            Query,
            startIndex,
            count,
            lastFetchMs,
            SortMode,
            out page,
            out errorCode,
            out errorMessage);
    }
}
