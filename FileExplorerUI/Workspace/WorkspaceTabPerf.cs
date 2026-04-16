using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace FileExplorerUI.Workspace;

public sealed class WorkspaceTabPerfScope : IDisposable
{
    private readonly WorkspaceTabPerfScope? _previous;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastElapsedMs;
    private bool _disposed;

    internal WorkspaceTabPerfScope(int id, string operation, string detail)
    {
        Id = id;
        Operation = operation;
        _previous = WorkspaceTabPerf.Current;
        WorkspaceTabPerf.Current = this;
        Mark("start", detail);
    }

    public int Id { get; }

    public string Operation { get; }

    public void Mark(string stage, string detail = "")
    {
        long totalMs = _stopwatch.ElapsedMilliseconds;
        long deltaMs = totalMs - _lastElapsedMs;
        _lastElapsedMs = totalMs;
        WorkspaceTabPerf.Write(Id, Operation, totalMs, deltaMs, stage, detail);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Mark("end");
        WorkspaceTabPerf.Current = _previous;
        _disposed = true;
    }
}

public static class WorkspaceTabPerf
{
    private static readonly AsyncLocal<WorkspaceTabPerfScope?> s_current = new();
    private static readonly object s_logLock = new();
    private static readonly string s_logPath = Path.Combine(AppContext.BaseDirectory, "tab-perf.log");
    private static int s_sequence;

    internal static WorkspaceTabPerfScope? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    public static WorkspaceTabPerfScope Begin(string operation, string detail = "")
    {
        return new WorkspaceTabPerfScope(
            Interlocked.Increment(ref s_sequence),
            operation,
            detail);
    }

    public static void Mark(string stage, string detail = "")
    {
        WorkspaceTabPerfScope? current = Current;
        if (current is null)
        {
            Write(0, "global", 0, 0, stage, detail);
            return;
        }

        current.Mark(stage, detail);
    }

    internal static void Write(int id, string operation, long totalMs, long deltaMs, string stage, string detail)
    {
        string message = string.IsNullOrWhiteSpace(detail)
            ? $"[TAB-PERF #{id}] total={totalMs}ms delta={deltaMs}ms op={operation} stage={stage}"
            : $"[TAB-PERF #{id}] total={totalMs}ms delta={deltaMs}ms op={operation} stage={stage} detail=\"{detail}\"";

        Debug.WriteLine(message);
        try
        {
            lock (s_logLock)
            {
                File.AppendAllText(
                    s_logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
