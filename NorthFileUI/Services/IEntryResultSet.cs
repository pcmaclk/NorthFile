using NorthFileUI.Interop;

namespace NorthFileUI.Services;

public interface IEntryResultSet
{
    string Path { get; }

    string Query { get; }

    DirectorySortMode SortMode { get; }

    bool TryReadRange(
        ulong startIndex,
        uint count,
        uint lastFetchMs,
        out FileBatchPage page,
        out int errorCode,
        out string errorMessage);
}
