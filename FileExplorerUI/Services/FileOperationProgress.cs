using System;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI.Services;

public sealed record FileOperationProgress(
    string OperationName,
    string CurrentPath,
    long CompletedItems,
    long TotalItems,
    long CompletedBytes,
    long TotalBytes,
    bool HasByteProgress,
    bool IsCompleted,
    bool IsCanceled);

public sealed class FileOperationProgressStore
{
    private readonly object _gate = new();
    private FileOperationProgress _snapshot;

    public FileOperationProgressStore(string operationName, long totalItems, long totalBytes = 0)
    {
        _snapshot = new FileOperationProgress(
            operationName,
            string.Empty,
            0,
            Math.Max(1, totalItems),
            0,
            Math.Max(0, totalBytes),
            totalBytes > 0,
            false,
            false);
    }

    public FileOperationProgress Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public void SetCurrent(string currentPath)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { CurrentPath = currentPath };
        }
    }

    public void SetTotals(long totalItems, long totalBytes = 0)
    {
        lock (_gate)
        {
            long normalizedTotalItems = Math.Max(1, totalItems);
            long normalizedTotalBytes = Math.Max(0, totalBytes);
            _snapshot = _snapshot with
            {
                TotalItems = normalizedTotalItems,
                TotalBytes = normalizedTotalBytes,
                HasByteProgress = normalizedTotalBytes > 0
            };
        }
    }

    public void AddCompletedItem(string currentPath)
    {
        lock (_gate)
        {
            long completedItems = Math.Min(_snapshot.TotalItems, _snapshot.CompletedItems + 1);
            _snapshot = _snapshot with
            {
                CurrentPath = currentPath,
                CompletedItems = completedItems,
                IsCompleted = completedItems >= _snapshot.TotalItems
            };
        }
    }

    public void AddCompletedBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }

        lock (_gate)
        {
            long completedBytes = _snapshot.TotalBytes > 0
                ? Math.Min(_snapshot.TotalBytes, _snapshot.CompletedBytes + bytes)
                : _snapshot.CompletedBytes + bytes;
            _snapshot = _snapshot with
            {
                CompletedBytes = completedBytes,
                HasByteProgress = _snapshot.TotalBytes > 0
            };
        }
    }

    public void MarkCompleted()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                CompletedItems = _snapshot.TotalItems,
                CompletedBytes = _snapshot.TotalBytes > 0 ? _snapshot.TotalBytes : _snapshot.CompletedBytes,
                IsCompleted = true
            };
        }
    }

    public void MarkCanceled()
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { IsCanceled = true };
        }
    }
}

public sealed class FileOperationProgressTracker
{
    private readonly FileOperationProgressStore _store;

    public FileOperationProgressTracker(FileOperationProgressStore store)
    {
        _store = store;
    }

    public int CompletedItems => checked((int)Math.Min(int.MaxValue, _store.Snapshot.CompletedItems));

    public void ReportCurrent(string currentPath)
    {
        _store.SetCurrent(currentPath);
    }

    public void ReportCompleted(string currentPath)
    {
        _store.AddCompletedItem(currentPath);
    }

    public void ReportBytes(long bytes)
    {
        _store.AddCompletedBytes(bytes);
    }
}

public sealed class FileOperationJob<T>
{
    private readonly CancellationTokenSource _cancellation;

    public FileOperationJob(FileOperationProgressStore progressStore, Task<T> completion, CancellationTokenSource cancellation)
    {
        ProgressStore = progressStore;
        Completion = completion;
        _cancellation = cancellation;
    }

    public FileOperationProgressStore ProgressStore { get; }

    public Task<T> Completion { get; }

    public bool IsCompleted => Completion.IsCompleted;

    public void Cancel()
    {
        ProgressStore.MarkCanceled();
        _cancellation.Cancel();
    }
}
