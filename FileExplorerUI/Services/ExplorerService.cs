using FileExplorerUI.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI.Services;

public sealed class ExplorerService
{
    public string GetEngineVersion()
    {
        return RustBatchInterop.GetEngineVersion();
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public bool PathExists(string path)
    {
        return File.Exists(path) || Directory.Exists(path);
    }

    public string? GetParentPath(string path)
    {
        return Directory.GetParent(path)?.FullName;
    }

    public string[] GetDirectories(string path)
    {
        return Directory.GetDirectories(path);
    }

    public IReadOnlyList<DriveInfo> GetReadyDrives()
    {
        var drives = new List<DriveInfo>();
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady)
            {
                drives.Add(drive);
            }
        }

        return drives;
    }

    public async Task<List<FileExplorerUI.SidebarTreeEntry>> EnumerateSidebarDirectoriesAsync(string path, CancellationToken cancellationToken, int maxChildren)
    {
        return await Task.Run(
            () =>
            {
                var list = new List<FileExplorerUI.SidebarTreeEntry>();
                try
                {
                    foreach (string dir in Directory.EnumerateDirectories(path))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string name = Path.GetFileName(dir.TrimEnd('\\'));
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            name = dir;
                        }

                        list.Add(new FileExplorerUI.SidebarTreeEntry(name, dir));
                        if (list.Count >= maxChildren)
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    return list;
                }

                return list;
            },
            cancellationToken
        );
    }

    public bool DirectoryHasChildDirectories(string path)
    {
        try
        {
            using IEnumerator<string> enumerator = Directory.EnumerateDirectories(path).GetEnumerator();
            return enumerator.MoveNext();
        }
        catch
        {
            return false;
        }
    }

    public bool TryReadDirectoryRowsAuto(
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
        return RustBatchInterop.TryReadDirectoryRowsAuto(path, cursor, limit, lastFetchMs, sortMode, out page, out errorCode, out errorMessage);
    }

    public bool TrySearchDirectoryRowsAuto(
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
        return RustBatchInterop.TrySearchDirectoryRowsAuto(path, query, cursor, limit, lastFetchMs, sortMode, out page, out errorCode, out errorMessage);
    }

    public string DescribeBatchSource(byte sourceKind)
    {
        return RustBatchInterop.DescribeBatchSource(sourceKind);
    }

    public void InvalidateMemoryDirectory(string path)
    {
        RustBatchInterop.InvalidateMemoryDirectory(path);
    }

    public RustUsnCapability ProbeUsnCapability(string path)
    {
        return RustBatchInterop.ProbeUsnCapability(path);
    }

    public void MarkPathChanged(string path)
    {
        RustBatchInterop.MarkPathChanged(path);
    }

    public Task RenamePathAsync(string sourcePath, string targetPath)
    {
        return Task.Run(() => RustBatchInterop.RenamePath(sourcePath, targetPath));
    }

    public Task DeletePathAsync(string path, bool recursive)
    {
        return Task.Run(() => RustBatchInterop.DeletePath(path, recursive));
    }

    public Task CreateEmptyFileAsync(string path)
    {
        return Task.Run(
            () =>
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
        );
    }

    public Task CreateDirectoryAsync(string path)
    {
        return Task.Run(() => Directory.CreateDirectory(path));
    }
}
