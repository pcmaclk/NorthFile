using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NorthFileUI.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RustFileEntry
{
    public uint parent_id;
    public uint name_off;
    public ushort name_len;
    public ushort flags;
    public ulong mft_ref;
    public ulong size_bytes;
    public long modified_unix_ms;
}

[StructLayout(LayoutKind.Sequential)]
public struct RustBatchResult
{
    public IntPtr entries;
    public uint entries_len;
    public IntPtr names_utf16;
    public uint names_len;
    public uint total_entries;
    public uint scanned_entries;
    public uint matched_entries;
    public ulong next_cursor;
    public uint suggested_next_limit;
    public byte source_kind;
    public int error_code;
    public byte has_more;
    public IntPtr error_message;
}

[StructLayout(LayoutKind.Sequential)]
public struct RustNtfsVolumeMeta
{
    public uint bytes_per_sector;
    public uint bytes_per_cluster;
    public uint bytes_per_record;
    public ulong mft_lcn;
    public int error_code;
}

public sealed record FileRow(
    ulong MftRef,
    string Name,
    bool IsDirectory,
    bool IsLink,
    long? SizeBytes,
    DateTime? ModifiedAt);
public enum DirectorySortMode : byte
{
    FolderFirstNameAsc = 1
}

public sealed record FileBatchPage(
    IReadOnlyList<FileRow> Rows,
    uint TotalEntries,
    uint ScannedEntries,
    uint MatchedEntries,
    ulong NextCursor,
    bool HasMore,
    uint SuggestedNextLimit,
    byte SourceKind
);

public static partial class RustBatchInterop
{
    private const ulong UnknownSizeBytes = ulong.MaxValue;
    private const long UnknownModifiedUnixMs = long.MinValue;
    private static readonly object NativeCallGate = new();
    private static string S(string key) => LocalizedStrings.Instance.Get(key);
    private static string SF(string key, params object[] args) => string.Format(S(key), args);

    private static void AppendBatchInteropPerfLog(string message)
    {
        try
        {
            string baseDirectory = AppContext.BaseDirectory;
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return;
            }

            string logPath = Path.Combine(baseDirectory, "batch-interop-perf.log");
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line);
        }
        catch
        {
        }
    }

    private static RustBatchResult InvokeBatchNative(
        string operation,
        string path,
        ulong cursor,
        uint limit,
        Func<RustBatchResult> nativeCall,
        out long waitMs,
        out long nativeMs)
    {
        Stopwatch waitSw = Stopwatch.StartNew();
        lock (NativeCallGate)
        {
            waitSw.Stop();
            waitMs = waitSw.ElapsedMilliseconds;

            Stopwatch nativeSw = Stopwatch.StartNew();
            RustBatchResult batch = nativeCall();
            nativeSw.Stop();
            nativeMs = nativeSw.ElapsedMilliseconds;

            AppendBatchInteropPerfLog(
                $"[BATCH-INTEROP] op={operation} stage=native path=\"{path}\" cursor={cursor} limit={limit} wait={waitMs}ms native={nativeMs}ms error={batch.error_code} rows={batch.entries_len} total={batch.total_entries} source={batch.source_kind}");
            return batch;
        }
    }

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_get_demo_batch")]
    private static partial RustBatchResult GetDemoBatch(uint limit);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_get_engine_version")]
    private static partial IntPtr GetEngineVersionNative();

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_ntfs_probe_volume", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustNtfsVolumeMeta NtfsProbeVolume(string path);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_list_dir_batch", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult ListDirBatch(string path, ulong cursor, uint limit, uint lastFetchMs, byte sortMode);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_list_dir_batch_memory", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult ListDirBatchMemory(string path, ulong cursor, uint limit, uint lastFetchMs, byte sortMode);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_list_dir_batch_auto", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult ListDirBatchAuto(string path, ulong cursor, uint limit, uint lastFetchMs, byte sortMode);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_search_dir_batch_auto", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult SearchDirBatchAuto(string path, string query, ulong cursor, uint limit, uint lastFetchMs, byte sortMode);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_free_batch_result")]
    private static partial void FreeBatchResult(RustBatchResult result);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_memory_invalidate_dir", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MemoryInvalidateDir(string path);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_memory_invalidate_session_dir", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MemoryInvalidateSessionDir(string path);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_memory_clear_cache")]
    private static partial int MemoryClearCache();

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_delete_path", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int DeletePathNative(string path, byte recursive);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_rename_path", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int RenamePathNative(string sourcePath, string targetPath);

    public static unsafe IReadOnlyList<FileRow> ReadDemoRows(uint limit = 128)
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = GetDemoBatch(limit);
            return DecodeAndFree(batch).Rows;
        }
    }

    public static string GetEngineVersion()
    {
        IntPtr p = GetEngineVersionNative();
        return Marshal.PtrToStringUTF8(p) ?? S("InteropUnknown");
    }

    public static RustNtfsVolumeMeta ProbeNtfsVolume(string path)
    {
        RustNtfsVolumeMeta meta;
        lock (NativeCallGate)
        {
            meta = NtfsProbeVolume(path);
        }
        if (meta.error_code != 0)
        {
            throw new InvalidOperationException(SF("InteropNtfsProbeFailed", meta.error_code));
        }

        return meta;
    }

    public static unsafe FileBatchPage ReadDirectoryRows(
        string path,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0,
        DirectorySortMode sortMode = DirectorySortMode.FolderFirstNameAsc
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = ListDirBatch(path, cursor, limit, lastFetchMs, (byte)sortMode);
            return DecodeAndFree(batch);
        }
    }

    public static unsafe FileBatchPage ReadDirectoryRowsWithMemoryFallback(
        string path,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0,
        DirectorySortMode sortMode = DirectorySortMode.FolderFirstNameAsc
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult memoryBatch = ListDirBatchMemory(path, cursor, limit, lastFetchMs, (byte)sortMode);
            if (memoryBatch.error_code == 0)
            {
                return DecodeAndFree(memoryBatch);
            }

            // Release potential error message from memory path call before falling back.
            FreeBatchResult(memoryBatch);

            RustBatchResult fallbackBatch = ListDirBatch(path, cursor, limit, lastFetchMs, (byte)sortMode);
            return DecodeAndFree(fallbackBatch);
        }
    }

    public static unsafe FileBatchPage ReadDirectoryRowsAuto(
        string path,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0,
        DirectorySortMode sortMode = DirectorySortMode.FolderFirstNameAsc
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = ListDirBatchAuto(path, cursor, limit, lastFetchMs, (byte)sortMode);
            return DecodeAndFree(batch);
        }
    }

    public static unsafe FileBatchPage SearchDirectoryRowsAuto(
        string path,
        string query,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0,
        DirectorySortMode sortMode = DirectorySortMode.FolderFirstNameAsc
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = SearchDirBatchAuto(path, query, cursor, limit, lastFetchMs, (byte)sortMode);
            return DecodeAndFree(batch);
        }
    }

    public static unsafe bool TryReadDirectoryRowsAuto(
        string path,
        ulong cursor,
        uint limit,
        uint lastFetchMs,
        DirectorySortMode sortMode,
        out FileBatchPage page,
        out int errorCode,
        out string errorMessage
    )
    {
        RustBatchResult batch = InvokeBatchNative(
            "dir-auto.try",
            path,
            cursor,
            limit,
            () => ListDirBatchAuto(path, cursor, limit, lastFetchMs, (byte)sortMode),
            out long waitMs,
            out long nativeMs);

        Stopwatch decodeSw = Stopwatch.StartNew();
        bool ok = TryDecodeAndFree(batch, out page, out errorCode, out errorMessage);
        decodeSw.Stop();
        AppendBatchInteropPerfLog(
            $"[BATCH-INTEROP] op=dir-auto.try stage=decode path=\"{path}\" cursor={cursor} limit={limit} wait={waitMs}ms native={nativeMs}ms decode={decodeSw.ElapsedMilliseconds}ms ok={ok} rows={page.Rows.Count} total={page.TotalEntries} error={errorCode} source={page.SourceKind}");
        return ok;
    }

    public static unsafe bool TrySearchDirectoryRowsAuto(
        string path,
        string query,
        ulong cursor,
        uint limit,
        uint lastFetchMs,
        DirectorySortMode sortMode,
        out FileBatchPage page,
        out int errorCode,
        out string errorMessage
    )
    {
        RustBatchResult batch = InvokeBatchNative(
            "search-auto.try",
            path,
            cursor,
            limit,
            () => SearchDirBatchAuto(path, query, cursor, limit, lastFetchMs, (byte)sortMode),
            out long waitMs,
            out long nativeMs);

        Stopwatch decodeSw = Stopwatch.StartNew();
        bool ok = TryDecodeAndFree(batch, out page, out errorCode, out errorMessage);
        decodeSw.Stop();
        AppendBatchInteropPerfLog(
            $"[BATCH-INTEROP] op=search-auto.try stage=decode path=\"{path}\" cursor={cursor} limit={limit} wait={waitMs}ms native={nativeMs}ms decode={decodeSw.ElapsedMilliseconds}ms ok={ok} rows={page.Rows.Count} total={page.TotalEntries} error={errorCode} source={page.SourceKind}");
        return ok;
    }

    public static void InvalidateMemoryDirectory(string path)
    {
        int code;
        lock (NativeCallGate)
        {
            code = MemoryInvalidateDir(path);
        }
        if (code != 0)
        {
            throw new InvalidOperationException(SF("InteropMemoryInvalidateDirFailed", code));
        }
    }

    public static void InvalidateMemorySessionDirectory(string path)
    {
        int code;
        lock (NativeCallGate)
        {
            code = MemoryInvalidateSessionDir(path);
        }
        if (code != 0)
        {
            throw new InvalidOperationException(SF("InteropMemoryInvalidateDirFailed", code));
        }
    }

    public static void ClearMemoryCache()
    {
        int code;
        lock (NativeCallGate)
        {
            code = MemoryClearCache();
        }
        if (code != 0)
        {
            throw new InvalidOperationException(SF("InteropMemoryClearCacheFailed", code));
        }
    }

    public static void DeletePath(string path, bool recursive = true)
    {
        if (!TryDeletePath(path, recursive, out Exception? error))
        {
            throw error!;
        }
    }

    public static bool TryDeletePath(string path, bool recursive, out Exception? error)
    {
        int code;
        lock (NativeCallGate)
        {
            code = DeletePathNative(path, recursive ? (byte)1 : (byte)0);
        }

        error = code == 0
            ? null
            : CreateOperationException(code, "InteropDeletePathFailed");
        return error is null;
    }

    public static void RenamePath(string sourcePath, string targetPath)
    {
        if (!TryRenamePath(sourcePath, targetPath, out Exception? error))
        {
            throw error!;
        }
    }

    public static bool TryRenamePath(string sourcePath, string targetPath, out Exception? error)
    {
        int code;
        lock (NativeCallGate)
        {
            code = RenamePathNative(sourcePath, targetPath);
        }

        error = code == 0
            ? null
            : CreateOperationException(code, "InteropRenamePathFailed");
        return error is null;
    }

    private static Exception CreateOperationException(int code, string messageKey)
    {
        string message = SF(messageKey, code, DescribeOperationError(code));
        return code switch
        {
            2101 => new DirectoryNotFoundException(message),
            2102 => new UnauthorizedAccessException(message),
            2103 => new IOException(message, unchecked((int)0x80070050)),
            2104 => new IOException(message),
            2105 => new IOException(message, unchecked((int)0x80070020)),
            2106 => new IOException(message, unchecked((int)0x8007007B)),
            2107 => new NotSupportedException(message),
            _ => new IOException(message)
        };
    }

    private static unsafe FileBatchPage DecodeAndFree(RustBatchResult batch)
    {
        try
        {
            if (batch.error_code != 0)
            {
                string message = Marshal.PtrToStringUTF8(batch.error_message) ?? S("InteropUnknownRustFfiError");
                throw new InvalidOperationException(SF("InteropRustErrorWithCode", batch.error_code, message));
            }

            ReadOnlySpan<RustFileEntry> entries = new(batch.entries.ToPointer(), checked((int)batch.entries_len));
            ReadOnlySpan<char> names = new(batch.names_utf16.ToPointer(), checked((int)batch.names_len));

            List<FileRow> rows = new(entries.Length);
            foreach (ref readonly RustFileEntry entry in entries)
            {
                int off = checked((int)entry.name_off);
                int len = checked((int)entry.name_len);
                string name = new(names.Slice(off, len));
                bool isDirectory = (entry.flags & 0x0001) != 0;
                bool isLink = (entry.flags & 0x0002) != 0;
                rows.Add(new FileRow(
                    entry.mft_ref,
                    name,
                    isDirectory,
                    isLink,
                    DecodeSizeBytes(entry.size_bytes),
                    DecodeModifiedAt(entry.modified_unix_ms)));
            }

            return new FileBatchPage(
                rows,
                batch.total_entries,
                batch.scanned_entries,
                batch.matched_entries,
                batch.next_cursor,
                batch.has_more != 0,
                batch.suggested_next_limit,
                batch.source_kind
            );
        }
        finally
        {
            lock (NativeCallGate)
            {
                FreeBatchResult(batch);
            }
        }
    }

    private static unsafe bool TryDecodeAndFree(
        RustBatchResult batch,
        out FileBatchPage page,
        out int errorCode,
        out string errorMessage
    )
    {
        try
        {
            if (batch.error_code != 0)
            {
                errorCode = batch.error_code;
                errorMessage = Marshal.PtrToStringUTF8(batch.error_message) ?? S("InteropUnknownRustFfiError");
                page = EmptyPage;
                return false;
            }

            ReadOnlySpan<RustFileEntry> entries = new(batch.entries.ToPointer(), checked((int)batch.entries_len));
            ReadOnlySpan<char> names = new(batch.names_utf16.ToPointer(), checked((int)batch.names_len));

            List<FileRow> rows = new(entries.Length);
            foreach (ref readonly RustFileEntry entry in entries)
            {
                int off = checked((int)entry.name_off);
                int len = checked((int)entry.name_len);
                string name = new(names.Slice(off, len));
                bool isDirectory = (entry.flags & 0x0001) != 0;
                bool isLink = (entry.flags & 0x0002) != 0;
                rows.Add(new FileRow(
                    entry.mft_ref,
                    name,
                    isDirectory,
                    isLink,
                    DecodeSizeBytes(entry.size_bytes),
                    DecodeModifiedAt(entry.modified_unix_ms)));
            }

            page = new FileBatchPage(
                rows,
                batch.total_entries,
                batch.scanned_entries,
                batch.matched_entries,
                batch.next_cursor,
                batch.has_more != 0,
                batch.suggested_next_limit,
                batch.source_kind
            );
            errorCode = 0;
            errorMessage = string.Empty;
            return true;
        }
        finally
        {
            lock (NativeCallGate)
            {
                FreeBatchResult(batch);
            }
        }
    }

    private static readonly FileBatchPage EmptyPage = new(
        Array.Empty<FileRow>(),
        0,
        0,
        0,
        0,
        false,
        0,
        0
    );

    private static long? DecodeSizeBytes(ulong value) =>
        value == UnknownSizeBytes
            ? null
            : checked((long)value);

    private static DateTime? DecodeModifiedAt(long value)
    {
        if (value == UnknownModifiedUnixMs)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime;
        }
        catch
        {
            return null;
        }
    }

    public static string DescribeBatchSource(byte sourceKind) =>
        sourceKind switch
        {
            1 => "Traditional",
            2 => "MemoryFallback",
            3 => "NTFS_INDEX_ROOT",
            4 => "Search",
            5 => "PersistentDirectoryCache",
            _ => "Unknown",
        };

    private static string DescribeOperationError(int code) =>
        code switch
        {
            1001 => S("InteropOperationInvalidParameter"),
            2101 => S("InteropOperationPathNotFound"),
            2102 => S("InteropOperationPermissionDenied"),
            2103 => S("InteropOperationTargetAlreadyExists"),
            2104 => S("InteropOperationDirectoryNotEmpty"),
            2105 => S("InteropOperationResourceBusyOrTimedOut"),
            2106 => S("InteropOperationInvalidPathOrInput"),
            2107 => S("InteropOperationNotSupported"),
            2199 => S("InteropOperationGenericIoFailure"),
            _ => S("InteropOperationUnknownError"),
        };
}
