using System;
using System.Collections.Generic;
using System.IO;

namespace NorthFileUI.Commands;

public static class FileCommandTargetResolver
{
    public static FileCommandTarget ResolveEntry(string path, bool isDirectory)
    {
        string displayName = GetDisplayName(path);
        FileEntryTraits traits = isDirectory ? FileEntryTraits.None : ResolveTraits(path);
        FileCommandCapabilities capabilities = isDirectory
            ? GetDirectoryCapabilities(FileCommandTargetKind.DirectoryEntry)
            : GetFileCapabilities(traits);

        return new FileCommandTarget(
            isDirectory ? FileCommandTargetKind.DirectoryEntry : FileCommandTargetKind.FileEntry,
            path,
            displayName,
            isDirectory,
            IsVirtual: false,
            traits,
            capabilities);
    }

    public static FileCommandTarget ResolveDriveRoot(string rootPath)
    {
        string displayName = GetDisplayName(rootPath);
        return new FileCommandTarget(
            FileCommandTargetKind.DriveRoot,
            rootPath,
            displayName,
            IsDirectory: true,
            IsVirtual: false,
            FileEntryTraits.None,
            GetDirectoryCapabilities(FileCommandTargetKind.DriveRoot));
    }

    public static FileCommandTarget ResolveCurrentDirectory(string directoryPath)
    {
        string displayName = GetDisplayName(directoryPath);
        return new FileCommandTarget(
            FileCommandTargetKind.CurrentDirectory,
            directoryPath,
            displayName,
            IsDirectory: true,
            IsVirtual: false,
            FileEntryTraits.None,
            GetDirectoryCapabilities(FileCommandTargetKind.CurrentDirectory));
    }

    public static FileCommandTarget ResolveListBackground(string directoryPath)
    {
        string displayName = GetDisplayName(directoryPath);
        return new FileCommandTarget(
            FileCommandTargetKind.ListBackground,
            directoryPath,
            displayName,
            IsDirectory: true,
            IsVirtual: false,
            FileEntryTraits.None,
            GetDirectoryCapabilities(FileCommandTargetKind.ListBackground));
    }

    public static FileCommandTarget ResolveTreeDirectoryNode(string directoryPath)
    {
        string displayName = GetDisplayName(directoryPath);
        return new FileCommandTarget(
            FileCommandTargetKind.TreeDirectoryNode,
            directoryPath,
            displayName,
            IsDirectory: true,
            IsVirtual: false,
            FileEntryTraits.None,
            GetDirectoryCapabilities(FileCommandTargetKind.TreeDirectoryNode));
    }

    public static FileCommandTarget ResolveVirtualNode(string id, string displayName)
    {
        return new FileCommandTarget(
            FileCommandTargetKind.VirtualNode,
            id,
            displayName,
            IsDirectory: true,
            IsVirtual: true,
            FileEntryTraits.None,
            FileCommandCapabilities.Open);
    }

    private static FileCommandCapabilities GetDirectoryCapabilities(FileCommandTargetKind kind)
    {
        return kind switch
        {
            FileCommandTargetKind.CurrentDirectory or FileCommandTargetKind.ListBackground =>
                FileCommandCapabilities.PasteInto |
                FileCommandCapabilities.CreateFile |
                FileCommandCapabilities.CreateFolder |
                FileCommandCapabilities.ShowProperties,

            FileCommandTargetKind.DriveRoot =>
                FileCommandCapabilities.Open |
                FileCommandCapabilities.Copy |
                FileCommandCapabilities.PasteInto |
                FileCommandCapabilities.CreateFile |
                FileCommandCapabilities.CreateFolder |
                FileCommandCapabilities.ShowProperties,

            FileCommandTargetKind.DirectoryEntry or FileCommandTargetKind.TreeDirectoryNode =>
                FileCommandCapabilities.Open |
                FileCommandCapabilities.Rename |
                FileCommandCapabilities.Delete |
                FileCommandCapabilities.Copy |
                FileCommandCapabilities.Cut |
                FileCommandCapabilities.PasteInto |
                FileCommandCapabilities.CreateFile |
                FileCommandCapabilities.CreateFolder |
                FileCommandCapabilities.ShowProperties,

            _ => FileCommandCapabilities.None
        };
    }

    private static FileCommandCapabilities GetFileCapabilities(FileEntryTraits traits)
    {
        FileCommandCapabilities capabilities =
            FileCommandCapabilities.Open |
            FileCommandCapabilities.Rename |
            FileCommandCapabilities.Delete |
            FileCommandCapabilities.Copy |
            FileCommandCapabilities.Cut |
            FileCommandCapabilities.ShowProperties;

        if ((traits & FileEntryTraits.Shortcut) != 0)
        {
            capabilities |= FileCommandCapabilities.OpenTarget;
        }

        if ((traits & (FileEntryTraits.Executable | FileEntryTraits.Script)) != 0)
        {
            capabilities |= FileCommandCapabilities.RunAsAdministrator;
        }

        if ((traits & FileEntryTraits.Archive) != 0)
        {
            capabilities |= FileCommandCapabilities.ExtractHere | FileCommandCapabilities.ExtractToFolder;
        }

        if ((traits & (FileEntryTraits.Image | FileEntryTraits.Video | FileEntryTraits.Audio)) != 0)
        {
            capabilities |= FileCommandCapabilities.Preview;
        }

        return capabilities;
    }

    private static FileEntryTraits ResolveTraits(string path)
    {
        string extension = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
        FileEntryTraits traits = FileEntryTraits.None;

        if (extension is ".lnk" or ".url")
        {
            traits |= FileEntryTraits.Shortcut;
        }

        if (extension is ".exe" or ".msi")
        {
            traits |= FileEntryTraits.Executable;
        }

        if (extension is ".bat" or ".cmd" or ".ps1" or ".psm1" or ".vbs" or ".js" or ".py" or ".sh")
        {
            traits |= FileEntryTraits.Script;
        }

        if (extension is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".cab")
        {
            traits |= FileEntryTraits.Archive;
        }

        if (extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".heic")
        {
            traits |= FileEntryTraits.Image;
        }

        if (extension is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v")
        {
            traits |= FileEntryTraits.Video;
        }

        if (extension is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma")
        {
            traits |= FileEntryTraits.Audio;
        }

        if (extension is ".txt" or ".log" or ".md" or ".ini" or ".cfg" or ".conf" or ".json" or ".xml" or ".csv" or ".yml" or ".yaml")
        {
            traits |= FileEntryTraits.Text;
        }

        return traits;
    }

    private static string GetDisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string normalizedPath = path.TrimEnd('\\');
        string name = Path.GetFileName(normalizedPath);
        return string.IsNullOrWhiteSpace(name) ? normalizedPath : name;
    }
}
