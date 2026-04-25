using System;
using System.Collections.Generic;
using System.IO;

namespace NorthFileUI.Commands;

public interface IFileCommandProvider
{
    bool CanHandle(FileCommandTarget target);
    IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target);
}

public sealed class BaseFileCommandProvider : IFileCommandProvider
{
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

    public bool CanHandle(FileCommandTarget target)
    {
        return target.Kind != FileCommandTargetKind.None;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        var commands = new List<FileCommandDescriptor>();

        AddIfSupported(commands, target, FileCommandIds.Open, S("CommonOpen"), FileCommandCapabilities.Open);
        AddIfSupported(commands, target, FileCommandIds.Copy, S("CommonCopy"), FileCommandCapabilities.Copy);
        AddIfSupported(commands, target, FileCommandIds.Cut, S("CommonCut"), FileCommandCapabilities.Cut);
        AddIfSupported(commands, target, FileCommandIds.Paste, S("CommonPaste"), FileCommandCapabilities.PasteInto);
        AddIfSupported(commands, target, FileCommandIds.NewFile, S("CommonNewFile"), FileCommandCapabilities.CreateFile);
        AddIfSupported(commands, target, FileCommandIds.NewFolder, S("CommonNewFolder"), FileCommandCapabilities.CreateFolder);
        AddIfSupported(commands, target, FileCommandIds.Rename, S("CommonRename"), FileCommandCapabilities.Rename);
        AddIfSupported(commands, target, FileCommandIds.Delete, S("CommonDelete"), FileCommandCapabilities.Delete);
        AddIfSupported(commands, target, FileCommandIds.Properties, S("CommonProperties"), FileCommandCapabilities.ShowProperties);

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
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

    public bool CanHandle(FileCommandTarget target)
    {
        return target.Kind == FileCommandTargetKind.FileEntry;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.OpenWith, S("CommonOpenWith"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.Share, S("CommonShare"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CompressZip, S("CommonCompressZip"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CreateShortcut, S("CommonCreateShortcut"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CopyPath, S("CommonCopyFilePath"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.SetTag, S("CommonSetTag"), FileCommandCapabilities.None)
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
        string pinText = LocalizedStrings.Instance.Get("CommonPinToSidebar");

        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.OpenInNewTab, LocalizedStrings.Instance.Get("CommonOpenInNewTab"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.OpenInNewWindow, LocalizedStrings.Instance.Get("CommonOpenInNewWindow"), FileCommandCapabilities.None),
            new FileCommandDescriptor(pinCommandId, pinText, FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.Share, LocalizedStrings.Instance.Get("CommonShare"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CreateShortcut, LocalizedStrings.Instance.Get("CommonCreateShortcut"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CompressZip, LocalizedStrings.Instance.Get("CommonCompressZip"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.CopyPath, LocalizedStrings.Instance.Get("CommonCopyFolderPath"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.SetTag, LocalizedStrings.Instance.Get("CommonSetTag"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.OpenInTerminal, LocalizedStrings.Instance.Get("CommonOpenInTerminal"), FileCommandCapabilities.None)
        };
    }
}

public sealed class BackgroundMenuCommandProvider : IFileCommandProvider
{
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

    public bool CanHandle(FileCommandTarget target)
    {
        return target.Kind is FileCommandTargetKind.ListBackground or FileCommandTargetKind.CurrentDirectory;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        return new[]
        {
            new FileCommandDescriptor(FileCommandIds.View, S("CommonView"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.SortBy, S("CommonSortBy"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.GroupBy, S("CommonGroupBy"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.OpenInTerminal, S("CommonOpenInTerminal"), FileCommandCapabilities.None),
            new FileCommandDescriptor(FileCommandIds.Refresh, S("CommonRefresh"), FileCommandCapabilities.None)
        };
    }
}

public sealed class ShortcutFileCommandProvider : IFileCommandProvider
{
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

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
            new FileCommandDescriptor(FileCommandIds.OpenTarget, S("CommonOpenTarget"), FileCommandCapabilities.OpenTarget)
        };
    }
}

public sealed class ExecutableFileCommandProvider : IFileCommandProvider
{
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

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
            new FileCommandDescriptor(FileCommandIds.RunAsAdministrator, S("CommonRunAsAdministrator"), FileCommandCapabilities.RunAsAdministrator)
        };
    }
}

public sealed class ArchiveFileCommandProvider : IFileCommandProvider
{
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

    public bool CanHandle(FileCommandTarget target)
    {
        return (target.Traits & FileEntryTraits.Archive) != 0;
    }

    public IReadOnlyList<FileCommandDescriptor> GetCommands(FileCommandTarget target)
    {
        if (!string.Equals(Path.GetExtension(target.Path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<FileCommandDescriptor>();
        }

        var commands = new List<FileCommandDescriptor>();
        if ((target.Capabilities & FileCommandCapabilities.ExtractHere) != 0)
        {
            commands.Add(new FileCommandDescriptor(FileCommandIds.ExtractSmart, S("CommonExtractSmart"), FileCommandCapabilities.ExtractHere));
        }

        if ((target.Capabilities & FileCommandCapabilities.ExtractHere) != 0)
        {
            commands.Add(new FileCommandDescriptor(FileCommandIds.ExtractHere, S("CommonExtractHere"), FileCommandCapabilities.ExtractHere));
        }

        if ((target.Capabilities & FileCommandCapabilities.ExtractToFolder) != 0)
        {
            commands.Add(new FileCommandDescriptor(FileCommandIds.ExtractToFolder, S("CommonExtractToFolder"), FileCommandCapabilities.ExtractToFolder));
        }

        return commands;
    }
}

public sealed class PreviewFileCommandProvider : IFileCommandProvider
{
    private static string S(string key) => LocalizedStrings.Instance.Get(key);

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
            new FileCommandDescriptor(FileCommandIds.Preview, S("CommonPreview"), FileCommandCapabilities.Preview)
        };
    }
}
