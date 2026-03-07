using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace FileExplorerUI.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RustFileEntry
{
    public uint parent_id;
    public uint name_off;
    public ushort name_len;
    public ushort flags;
    public ulong mft_ref;
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

public sealed record FileRow(ulong MftRef, string Name, bool IsDirectory);
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
    private static readonly object NativeCallGate = new();

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_get_demo_batch")]
    private static partial RustBatchResult GetDemoBatch(uint limit);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_get_engine_version")]
    private static partial IntPtr GetEngineVersionNative();

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_ntfs_probe_volume", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustNtfsVolumeMeta NtfsProbeVolume(string path);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_list_dir_batch", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult ListDirBatch(string path, ulong cursor, uint limit, uint lastFetchMs);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_list_dir_batch_memory", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult ListDirBatchMemory(string path, ulong cursor, uint limit, uint lastFetchMs);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_list_dir_batch_auto", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult ListDirBatchAuto(string path, ulong cursor, uint limit, uint lastFetchMs);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_search_dir_batch_auto", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustBatchResult SearchDirBatchAuto(string path, string query, ulong cursor, uint limit, uint lastFetchMs);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_free_batch_result")]
    private static partial void FreeBatchResult(RustBatchResult result);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_memory_invalidate_dir", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int MemoryInvalidateDir(string path);

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
        return Marshal.PtrToStringUTF8(p) ?? "unknown";
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
            throw new InvalidOperationException($"NTFS probe failed ({meta.error_code})");
        }

        return meta;
    }

    public static unsafe FileBatchPage ReadDirectoryRows(
        string path,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = ListDirBatch(path, cursor, limit, lastFetchMs);
            return DecodeAndFree(batch);
        }
    }

    public static unsafe FileBatchPage ReadDirectoryRowsWithMemoryFallback(
        string path,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult memoryBatch = ListDirBatchMemory(path, cursor, limit, lastFetchMs);
            if (memoryBatch.error_code == 0)
            {
                return DecodeAndFree(memoryBatch);
            }

            // Release potential error message from memory path call before falling back.
            FreeBatchResult(memoryBatch);

            RustBatchResult fallbackBatch = ListDirBatch(path, cursor, limit, lastFetchMs);
            return DecodeAndFree(fallbackBatch);
        }
    }

    public static unsafe FileBatchPage ReadDirectoryRowsAuto(
        string path,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = ListDirBatchAuto(path, cursor, limit, lastFetchMs);
            return DecodeAndFree(batch);
        }
    }

    public static unsafe FileBatchPage SearchDirectoryRowsAuto(
        string path,
        string query,
        ulong cursor = 0,
        uint limit = 500,
        uint lastFetchMs = 0
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = SearchDirBatchAuto(path, query, cursor, limit, lastFetchMs);
            return DecodeAndFree(batch);
        }
    }

    public static unsafe bool TryReadDirectoryRowsAuto(
        string path,
        ulong cursor,
        uint limit,
        uint lastFetchMs,
        out FileBatchPage page,
        out int errorCode,
        out string errorMessage
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = ListDirBatchAuto(path, cursor, limit, lastFetchMs);
            return TryDecodeAndFree(batch, out page, out errorCode, out errorMessage);
        }
    }

    public static unsafe bool TrySearchDirectoryRowsAuto(
        string path,
        string query,
        ulong cursor,
        uint limit,
        uint lastFetchMs,
        out FileBatchPage page,
        out int errorCode,
        out string errorMessage
    )
    {
        lock (NativeCallGate)
        {
            RustBatchResult batch = SearchDirBatchAuto(path, query, cursor, limit, lastFetchMs);
            return TryDecodeAndFree(batch, out page, out errorCode, out errorMessage);
        }
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
            throw new InvalidOperationException($"Memory invalidate dir failed: {code}");
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
            throw new InvalidOperationException($"Memory clear cache failed: {code}");
        }
    }

    public static void DeletePath(string path, bool recursive = true)
    {
        int code;
        lock (NativeCallGate)
        {
            code = DeletePathNative(path, recursive ? (byte)1 : (byte)0);
        }
        if (code != 0)
        {
            throw new InvalidOperationException($"Delete path failed ({code}): {DescribeOperationError(code)}");
        }
    }

    public static void RenamePath(string sourcePath, string targetPath)
    {
        int code;
        lock (NativeCallGate)
        {
            code = RenamePathNative(sourcePath, targetPath);
        }
        if (code != 0)
        {
            throw new InvalidOperationException($"Rename path failed ({code}): {DescribeOperationError(code)}");
        }
    }

    private static unsafe FileBatchPage DecodeAndFree(RustBatchResult batch)
    {
        try
        {
            if (batch.error_code != 0)
            {
                string message = Marshal.PtrToStringUTF8(batch.error_message) ?? "unknown rust ffi error";
                throw new InvalidOperationException($"Rust error {batch.error_code}: {message}");
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
                rows.Add(new FileRow(entry.mft_ref, name, isDirectory));
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
            FreeBatchResult(batch);
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
                errorMessage = Marshal.PtrToStringUTF8(batch.error_message) ?? "unknown rust ffi error";
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
                rows.Add(new FileRow(entry.mft_ref, name, isDirectory));
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
            FreeBatchResult(batch);
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

    public static string DescribeBatchSource(byte sourceKind) =>
        sourceKind switch
        {
            1 => "Traditional",
            2 => "MemoryFallback",
            3 => "NTFS_INDEX_ROOT",
            4 => "Search",
            _ => "Unknown",
        };

    private static string DescribeOperationError(int code) =>
        code switch
        {
            1001 => "invalid parameter",
            2101 => "path not found",
            2102 => "permission denied",
            2103 => "target already exists",
            2104 => "directory is not empty",
            2105 => "resource is busy or timed out",
            2106 => "invalid path or input",
            2107 => "operation is not supported",
            2199 => "generic I/O failure",
            _ => "unknown error",
        };
}
