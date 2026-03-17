using System;
using System.Collections.Generic;

namespace FileExplorerUI.Commands;

public interface IFileCommandProvider
{
    bool CanHandle(FileCommandTarget target);
    IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target);
}

public sealed class BaseFileCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return target.Kind != FileCommandTargetKind.None;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        var commands = new List<FileCommandDescriptor>();

        AddIfSupported(commands, target, FileCommandIds.Open, "Open", FileCommandCapabilities.Open);
        AddIfSupported(commands, target, FileCommandIds.Copy, "Copy", FileCommandCapabilities.Copy);
        AddIfSupported(commands, target, FileCommandIds.Cut, "Cut", FileCommandCapabilities.Cut);
        AddIfSupported(commands, target, FileCommandIds.Paste, "Paste", FileCommandCapabilities.PasteInto);
        AddIfSupported(commands, target, FileCommandIds.NewFile, "New File", FileCommandCapabilities.CreateFile);
        AddIfSupported(commands, target, FileCommandIds.NewFolder, "New Folder", FileCommandCapabilities.CreateFolder);
        AddIfSupported(commands, target, FileCommandIds.Rename, "Rename", FileCommandCapabilities.Rename);
        AddIfSupported(commands, target, FileCommandIds.Delete, "Delete", FileCommandCapabilities.Delete);
        AddIfSupported(commands, target, FileCommandIds.Properties, "Properties", FileCommandCapabilities.ShowProperties);

        return commands;
    }

    private static void AddIfSupported(
        ICollection<FileCommandDescriptor> commands,
        FileCommandTarget target,
        string id,
        string text,
        FileCommandCapabilities capability)
    {
        if ((target.Capabilities & capability) == capability)
        {
            commands.Add(new FileCommandDescriptor(id, text, capability));
        }
    }
}

public sealed class FileEntryMenuCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return target.Kind == FileCommandTargetKind.FileEntry;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.OpenWith, "Open with", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.Share, "Share", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.Compress, "Compress", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CreateShortcut, "Create shortcut", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CopyPath, "Copy file path", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.SetTag, "Set tag", FileCommandCapabilities.None)
        };
    }
}

public sealed class DirectoryMenuCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return target.Kind is FileCommandTargetKind.DirectoryEntry or FileCommandTargetKind.DriveRoot;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        string pinCommandId = FileCommandIds.PinToSidebar;
        string pinText = "Pin to sidebar";

        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.OpenInNewWindow, "Open in new window", FileCommandCapabilities.None),
            new FileCommandDescriptor(pinCommandId, pinText, FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.Compress, "Compress", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CopyPath, "Copy folder path", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.SetTag, "Set tag", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.OpenInTerminal, "Open in terminal", FileCommandCapabilities.None)
        };
    }
}

public sealed class BackgroundMenuCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return target.Kind is FileCommandTargetKind.ListBackground or FileCommandTargetKind.CurrentDirectory;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.View, "View", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.SortBy, "Sort by", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.GroupBy, "Group by", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.OpenInTerminal, "Open in terminal", FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.Refresh, "Refresh", FileCommandCapabilities.None)
        };
    }
}

public sealed class ShortcutFileCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return (target.Traits & FileEntryTraits.Shortcut) != 0;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        if ((target.Capabilities & FileCommandCapabilities.OpenTarget) == 0)
        {
            return Array.Empty<FileCommandDescriptor>();
        }

        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.OpenTarget, "Open Target", FileCommandCapabilities.OpenTarget)
        };
    }
}

public sealed class ExecutableFileCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return (target.Traits & FileEntryTraits.Executable) != 0;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        if ((target.Capabilities & FileCommandCapabilities.RunAsAdministrator) == 0)
        {
            return Array.Empty<FileCommandDescriptor>();
        }

        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.RunAsAdministrator, "Run as Administrator", FileCommandCapabilities.RunAsAdministrator)
        };
    }
}

public sealed class ArchiveFileCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return (target.Traits & FileEntryTraits.Archive) != 0;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        var commands = new List<FileCommandDescriptor>();
        if ((target.Capabilities & FileCommandCapabilities.ExtractHere) != 0)
        {
            commands.Add(new FileCommandDescriptor(FileCommandIds.ExtractHere, "Extract Here", FileCommandCapabilities.ExtractHere));
        }

        if ((target.Capabilities & FileCommandCapabilities.ExtractToFolder) != 0)
        {
            commands.Add(new FileCommandDescriptor(FileCommandIds.ExtractToFolder, "Extract to Folder", FileCommandCapabilities.ExtractToFolder));
        }

        return commands;
    }
}

public sealed class PreviewFileCommandProvider : IFileCommandProvider
{
    public bool CanHandle(FileCommandTarget target)
    {
        return (target.Traits & (FileEntryTraits.Image | FileEntryTraits.Video | FileEntryTraits.Audio)) != 0;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        if ((target.Capabilities & FileCommandCapabilities.Preview) == 0)
        {
            return Array.Empty<FileCommandDescriptor>();
        }

        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.Preview, "Preview", FileCommandCapabilities.Preview)
        };
    }
}
