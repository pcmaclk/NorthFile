using FileExplorerUI.Interop;
using FileSystem = Microsoft.VisualBasic.FileIO.FileSystem;
using UIOption = Microsoft.VisualBasic.FileIO.UIOption;
using RecycleOption = Microsoft.VisualBasic.FileIO.RecycleOption;
using UICancelOption = Microsoft.VisualBasic.FileIO.UICancelOption;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI.Services;

public readonly record struct ZipExtractionPlan(string DestinationDirectory, string? PrimarySelectionPath);

public sealed class ExplorerService
{
    private const uint SeeMaskInvokeIdList = 0x0000000C;
    private const int ShowNormal = 1;

    public string GenerateUniqueNewFileName(string directoryPath)
    {
        string baseName = LocalizedStrings.Instance.Get("DefaultNewFileBaseName");
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
        string baseName = LocalizedStrings.Instance.Get("DefaultNewFolderBaseName");
        string candidate = baseName;
        int suffix = 2;

        while (PathExists(Path.Combine(directoryPath, candidate)))
        {
            candidate = $"{baseName} ({suffix})";
            suffix++;
        }

        return candidate;
    }

    public string GenerateUniqueShortcutName(string directoryPath, string targetPath)
    {
        string normalizedTargetPath = targetPath.TrimEnd('\\');
        string baseName = Path.GetFileName(normalizedTargetPath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = normalizedTargetPath;
        }

        string suffix = LocalizedStrings.Instance.Get("ShortcutNameSuffix");
        string candidateBaseName = baseName + suffix;
        string candidate = candidateBaseName + ".lnk";
        int disambiguator = 2;

        while (PathExists(Path.Combine(directoryPath, candidate)))
        {
            candidate = $"{candidateBaseName} ({disambiguator}).lnk";
            disambiguator++;
        }

        return candidate;
    }

    public string GenerateUniqueZipArchiveName(string directoryPath, string sourcePath)
    {
        string normalizedSourcePath = sourcePath.TrimEnd('\\');
        string baseName = Directory.Exists(sourcePath)
            ? Path.GetFileName(normalizedSourcePath)
            : Path.GetFileNameWithoutExtension(normalizedSourcePath);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "Archive";
        }

        string candidate = baseName + ".zip";
        int suffix = 2;

        while (PathExists(Path.Combine(directoryPath, candidate)))
        {
            candidate = $"{baseName} ({suffix}).zip";
            suffix++;
        }

        return candidate;
    }

    public string GetDefaultZipExtractionFolderName(string archivePath)
    {
        string baseName = Path.GetFileNameWithoutExtension(archivePath.TrimEnd('\\'));
        return string.IsNullOrWhiteSpace(baseName) ? "Archive" : baseName;
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

    public List<FileExplorerUI.SidebarTreeEntry> EnumerateSidebarDirectories(string path, int maxChildren)
    {
        var list = new List<FileExplorerUI.SidebarTreeEntry>();
        try
        {
            foreach (string dir in Directory.EnumerateDirectories(path))
            {
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

    public IEntryResultSet CreateDirectoryResultSet(string path, DirectorySortMode sortMode)
    {
        return new DirectoryEntryResultSet(this, path, sortMode);
    }

    public IEntryResultSet CreateSearchResultSet(string path, string query, DirectorySortMode sortMode)
    {
        return new SearchEntryResultSet(this, path, query, sortMode);
    }

    public string DescribeBatchSource(byte sourceKind)
    {
        return RustBatchInterop.DescribeBatchSource(sourceKind);
    }

    public void InvalidateMemoryDirectory(string path)
    {
        RustBatchInterop.InvalidateMemoryDirectory(path);
    }

    public void InvalidateMemorySessionDirectory(string path)
    {
        RustBatchInterop.InvalidateMemorySessionDirectory(path);
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

    public Task<Exception?> TryRenamePathAsync(string sourcePath, string targetPath)
    {
        return Task.Run(() =>
        {
            RustBatchInterop.TryRenamePath(sourcePath, targetPath, out Exception? error);
            return error;
        });
    }

    public Task DeletePathAsync(string path, bool recursive)
    {
        return Task.Run(() =>
        {
            if (Directory.Exists(path))
            {
                FileSystem.DeleteDirectory(
                    path,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
                return;
            }

            FileSystem.DeleteFile(
                path,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
        });
    }

    public Task<Exception?> TryDeletePathAsync(string path, bool recursive)
    {
        return Task.Run(() =>
        {
            if (Directory.Exists(path))
            {
                try
                {
                    FileSystem.DeleteDirectory(
                        path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin,
                        UICancelOption.ThrowException);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            }

            try
            {
                FileSystem.DeleteFile(
                    path,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);
                return (Exception?)null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });
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

    public Task DeleteExistingPathForReplaceAsync(string path)
    {
        return Task.Run(() =>
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
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

    public void OpenPathInTerminal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = "wt.exe",
                Arguments = $"-d \"{path}\"",
                UseShellExecute = true
            });
            return;
        }
        catch
        {
        }

        _ = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            WorkingDirectory = path,
            UseShellExecute = true
        });
    }

    public void ShowProperties(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            fMask = SeeMaskInvokeIdList,
            lpVerb = "properties",
            lpFile = path,
            nShow = ShowNormal
        };

        if (!ShellExecuteEx(ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void RunAsAdministrator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is required.", nameof(path));
        }

        var info = new ShellExecuteInfo
        {
            cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
            lpVerb = "runas",
            lpFile = path,
            nShow = ShowNormal
        };

        if (!ShellExecuteEx(ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public string ResolveShortcutTargetPath(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath))
        {
            throw new ArgumentException("Path is required.", nameof(shortcutPath));
        }

        string extension = Path.GetExtension(shortcutPath);
        if (string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string line in File.ReadLines(shortcutPath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = line["URL=".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            throw new InvalidOperationException("Shortcut target is unavailable.");
        }

        Type shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
            ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: new object[] { shortcutPath });

            if (shortcut is null)
            {
                throw new InvalidOperationException("Shortcut target is unavailable.");
            }

            object? targetPath = shortcut.GetType().InvokeMember(
                "TargetPath",
                BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null);
            object? arguments = shortcut.GetType().InvokeMember(
                "Arguments",
                BindingFlags.GetProperty,
                binder: null,
                target: shortcut,
                args: null);

            string? value = targetPath as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException("Shortcut target is unavailable.");
            }

            if (string.Equals(Path.GetFileName(value), "explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                TryExtractExplorerShortcutTarget(arguments as string, out string? extractedTarget))
            {
                return extractedTarget;
            }

            return value;
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
            {
                Marshal.ReleaseComObject(shortcut);
            }

            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }

    private static bool TryExtractExplorerShortcutTarget(string? arguments, out string extractedTarget)
    {
        extractedTarget = string.Empty;
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return false;
        }

        foreach (string rawPart in arguments.Split(','))
        {
            string part = rawPart.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (part.StartsWith("/select", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("/e", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("/root", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("/n", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Directory.Exists(part) || File.Exists(part))
            {
                extractedTarget = part;
                return true;
            }
        }

        string trimmed = arguments.Trim();
        int quoteStart = trimmed.IndexOf('"');
        if (quoteStart >= 0)
        {
            int quoteEnd = trimmed.IndexOf('"', quoteStart + 1);
            if (quoteEnd > quoteStart)
            {
                string quoted = trimmed[(quoteStart + 1)..quoteEnd];
                if (Directory.Exists(quoted) || File.Exists(quoted))
                {
                    extractedTarget = quoted;
                    return true;
                }
            }
        }

        return false;
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

    public Task CreateShortcutAsync(string targetPath, string shortcutPath)
    {
        return Task.Run(
            () =>
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)
                    ?? throw new InvalidOperationException("WScript.Shell is unavailable.");
                object? shell = null;
                object? shortcut = null;

                try
                {
                    shell = Activator.CreateInstance(shellType);
                    shortcut = shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: shell,
                        args: new object[] { shortcutPath });

                    if (shortcut is null)
                    {
                        throw new InvalidOperationException("Failed to create shortcut object.");
                    }

                    Type shortcutType = shortcut.GetType();
                    shortcutType.InvokeMember(
                        "TargetPath",
                        BindingFlags.SetProperty,
                        binder: null,
                        target: shortcut,
                        args: new object[] { targetPath });

                    string workingDirectory = Directory.Exists(targetPath)
                        ? targetPath
                        : Path.GetDirectoryName(targetPath) ?? string.Empty;
                    shortcutType.InvokeMember(
                        "WorkingDirectory",
                        BindingFlags.SetProperty,
                        binder: null,
                        target: shortcut,
                        args: new object[] { workingDirectory });

                    shortcutType.InvokeMember(
                        "Save",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: shortcut,
                        args: Array.Empty<object>());
                }
                finally
                {
                    if (shortcut is not null && Marshal.IsComObject(shortcut))
                    {
                        Marshal.ReleaseComObject(shortcut);
                    }

                    if (shell is not null && Marshal.IsComObject(shell))
                    {
                        Marshal.ReleaseComObject(shell);
                    }
                }
            });
    }

    public Task CreateZipArchiveAsync(string sourcePath, string archivePath)
    {
        return Task.Run(
            () =>
            {
                string? archiveDirectory = Path.GetDirectoryName(archivePath);
                if (!string.IsNullOrWhiteSpace(archiveDirectory))
                {
                    Directory.CreateDirectory(archiveDirectory);
                }

                if (Directory.Exists(sourcePath))
                {
                    CreateZipArchiveFromDirectory(sourcePath, archivePath);
                    return;
                }

                if (File.Exists(sourcePath))
                {
                    CreateZipArchiveFromFile(sourcePath, archivePath);
                    return;
                }

                throw new FileNotFoundException("Source path does not exist.", sourcePath);
            });
    }

    public Task<ZipExtractionPlan> ExtractZipHereAsync(string archivePath, string destinationDirectory)
    {
        return Task.Run(() => ExtractZipCore(archivePath, destinationDirectory, ZipExtractionMode.Here));
    }

    public Task<ZipExtractionPlan> ExtractZipToFolderAsync(string archivePath, string destinationDirectory)
    {
        return Task.Run(() => ExtractZipCore(archivePath, destinationDirectory, ZipExtractionMode.ToFolder));
    }

    public Task<ZipExtractionPlan> ExtractZipSmartAsync(string archivePath, string destinationDirectory)
    {
        return Task.Run(() => ExtractZipCore(archivePath, destinationDirectory, ZipExtractionMode.Smart));
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

    private static void CreateZipArchiveFromDirectory(string sourcePath, string archivePath)
    {
        string rootName = Path.GetFileName(sourcePath.TrimEnd('\\'));
        if (string.IsNullOrWhiteSpace(rootName))
        {
            rootName = "Archive";
        }

        using FileStream stream = new(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        bool hasEntries = false;
        foreach (string directory in Directory.EnumerateDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourcePath, directory).Replace('\\', '/');
            archive.CreateEntry($"{rootName}/{relativePath}/");
            hasEntries = true;
        }

        foreach (string file in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourcePath, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{rootName}/{relativePath}", CompressionLevel.Optimal);
            hasEntries = true;
        }

        if (!hasEntries)
        {
            archive.CreateEntry($"{rootName}/");
        }
    }

    private static void CreateZipArchiveFromFile(string sourcePath, string archivePath)
    {
        using FileStream stream = new(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Optimal);
    }

    private ZipExtractionPlan ExtractZipCore(string archivePath, string destinationDirectory, ZipExtractionMode mode)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Archive path does not exist.", archivePath);
        }

        string destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(destinationRoot))
        {
            throw new DirectoryNotFoundException(destinationRoot);
        }

        using FileStream stream = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        ZipArchiveInspection inspection = InspectZipArchive(archive);
        bool extractToNamedFolder = mode switch
        {
            ZipExtractionMode.Here => false,
            ZipExtractionMode.ToFolder => true,
            _ => !inspection.CanExtractSingleRootDirectoryHere
        };

        string extractionRoot = extractToNamedFolder
            ? Path.Combine(destinationRoot, GetDefaultZipExtractionFolderName(archivePath))
            : destinationRoot;
        string normalizedExtractionRoot = Path.GetFullPath(extractionRoot).TrimEnd('\\');

        if (extractToNamedFolder && PathExists(normalizedExtractionRoot))
        {
            throw CreateAlreadyExistsException(normalizedExtractionRoot);
        }

        var impliedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filesToExtract = new List<(ZipArchiveEntry Entry, string DestinationPath)>();

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relativePath = NormalizeArchiveEntryPath(entry.FullName);
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            string destinationPath = Path.GetFullPath(Path.Combine(
                normalizedExtractionRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            EnsurePathWithinRoot(destinationPath, normalizedExtractionRoot);

            if (IsDirectoryEntry(entry))
            {
                impliedDirectories.Add(destinationPath.TrimEnd('\\'));
                continue;
            }

            string? parentDirectory = Path.GetDirectoryName(destinationPath);
            while (!string.IsNullOrWhiteSpace(parentDirectory) &&
                !string.Equals(parentDirectory.TrimEnd('\\'), normalizedExtractionRoot, StringComparison.OrdinalIgnoreCase))
            {
                impliedDirectories.Add(parentDirectory.TrimEnd('\\'));
                parentDirectory = Path.GetDirectoryName(parentDirectory);
            }

            filesToExtract.Add((entry, destinationPath));
        }

        foreach (string directoryPath in impliedDirectories)
        {
            if (PathExists(directoryPath))
            {
                throw CreateAlreadyExistsException(directoryPath);
            }
        }

        foreach ((_, string destinationPath) in filesToExtract)
        {
            if (PathExists(destinationPath))
            {
                throw CreateAlreadyExistsException(destinationPath);
            }
        }

        if (!Directory.Exists(normalizedExtractionRoot))
        {
            Directory.CreateDirectory(normalizedExtractionRoot);
        }

        foreach (string directoryPath in SortPathsByLength(impliedDirectories))
        {
            Directory.CreateDirectory(directoryPath);
        }

        foreach ((ZipArchiveEntry entry, string destinationPath) in filesToExtract)
        {
            string? parentDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            using Stream entryStream = entry.Open();
            using FileStream fileStream = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            entryStream.CopyTo(fileStream);
            File.SetLastWriteTime(destinationPath, entry.LastWriteTime.LocalDateTime);
        }

        string? primarySelectionPath = extractToNamedFolder
            ? normalizedExtractionRoot
            : inspection.GetPrimarySelectionPath(normalizedExtractionRoot);
        return new ZipExtractionPlan(normalizedExtractionRoot, primarySelectionPath);
    }

    private static IOException CreateAlreadyExistsException(string path)
    {
        return new IOException($"Destination already exists: {path}", unchecked((int)0x800700B7));
    }

    private static IEnumerable<string> SortPathsByLength(IEnumerable<string> paths)
    {
        var sorted = new List<string>(paths);
        sorted.Sort((left, right) => left.Length.CompareTo(right.Length));
        return sorted;
    }

    private static ZipArchiveInspection InspectZipArchive(ZipArchive archive)
    {
        var topLevelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool canExtractSingleRootDirectoryHere = true;
        bool hasEntries = false;
        bool singleTopLevelIsDirectory = false;
        string? singleTopLevelName = null;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relativePath = NormalizeArchiveEntryPath(entry.FullName);
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            hasEntries = true;
            string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            topLevelNames.Add(segments[0]);
            if (segments.Length == 1)
            {
                singleTopLevelIsDirectory = IsDirectoryEntry(entry);
                if (!singleTopLevelIsDirectory)
                {
                    canExtractSingleRootDirectoryHere = false;
                }
            }
            else
            {
                singleTopLevelIsDirectory = true;
            }
        }

        if (topLevelNames.Count != 1 || !singleTopLevelIsDirectory)
        {
            canExtractSingleRootDirectoryHere = false;
        }

        foreach (string name in topLevelNames)
        {
            singleTopLevelName = name;
            break;
        }

        return new ZipArchiveInspection(hasEntries, canExtractSingleRootDirectoryHere, singleTopLevelName, topLevelNames.Count);
    }

    private static string NormalizeArchiveEntryPath(string entryPath)
    {
        string normalized = entryPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string segment in segments)
        {
            if (segment is "." or "..")
            {
                throw new InvalidDataException("Archive entry contains an unsafe path.");
            }
        }

        return string.Join('/', segments);
    }

    private static void EnsurePathWithinRoot(string path, string rootPath)
    {
        string normalizedPath = Path.GetFullPath(path).TrimEnd('\\');
        if (string.Equals(normalizedPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!normalizedPath.StartsWith(rootPath + "\\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Archive entry escapes extraction root.");
        }
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        return entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
            entry.FullName.EndsWith("\\", StringComparison.Ordinal);
    }

    private enum ZipExtractionMode
    {
        Smart,
        Here,
        ToFolder
    }

    private readonly record struct ZipArchiveInspection(
        bool HasEntries,
        bool CanExtractSingleRootDirectoryHere,
        string? SingleTopLevelName,
        int TopLevelCount)
    {
        public string? GetPrimarySelectionPath(string extractionRoot)
        {
            if (!HasEntries || string.IsNullOrWhiteSpace(SingleTopLevelName) || TopLevelCount != 1)
            {
                return null;
            }

            return Path.Combine(extractionRoot, SingleTopLevelName);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteEx(ref ShellExecuteInfo lpExecInfo);
}
