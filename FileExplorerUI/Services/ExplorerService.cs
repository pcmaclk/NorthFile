using FileExplorerUI.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI.Services;

public sealed class ExplorerService
{
    public string GenerateUniqueNewFileName(string directoryPath)
    {
        const string baseName = "New File";
        const string extension = ".txt";
        string candidate = baseName + extension;
        int suffix = 2;

        while (PathExists(Path.Combine(directoryPath, candidate)))
        {
            candidate = $"{baseName} ({suffix}){extension}";
            suffix++;
        }

        return candidate;
    }

    public string GenerateUniqueNewFolderName(string directoryPath)
    {
        const string baseName = "New Folder";
        string candidate = baseName;
        int suffix = 2;

        while (PathExists(Path.Combine(directoryPath, candidate)))
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

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

    public Task CopyPathAsync(string sourcePath, string targetPath)
    {
        return Task.Run(() =>
        {
            if (Directory.Exists(sourcePath))
            {
                CopyDirectory(sourcePath, targetPath);
                return;
            }

            string? targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(sourcePath, targetPath, overwrite: false);
        });
    }

    public Task MovePathAsync(string sourcePath, string targetPath)
    {
        return Task.Run(() =>
        {
            if (Directory.Exists(sourcePath))
            {
                MoveDirectory(sourcePath, targetPath);
                return;
            }

            string? targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Move(sourcePath, targetPath);
        });
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

    public Task CreatePathAsync(string path, bool isDirectory)
    {
        return isDirectory ? CreateDirectoryAsync(path) : CreateEmptyFileAsync(path);
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);

        foreach (string directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relativePath));
        }

        foreach (string file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourcePath, file);
            string destinationPath = Path.Combine(targetPath, relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            File.Copy(file, destinationPath, overwrite: false);
        }
    }

    private static void MoveDirectory(string sourcePath, string targetPath)
    {
        try
        {
            Directory.Move(sourcePath, targetPath);
        }
        catch (IOException)
        {
            CopyDirectory(sourcePath, targetPath);
            Directory.Delete(sourcePath, recursive: true);
        }
    }
}
