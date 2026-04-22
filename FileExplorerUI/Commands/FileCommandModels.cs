using System;

namespace FileExplorerUI.Commands;

public enum FileCommandTargetKind
{
    None,
    ListBackground,
    CurrentDirectory,
    FileEntry,
    DirectoryEntry,
    DriveRoot,
    TreeDirectoryNode,
    VirtualNode
}

[Flags]
public enum FileEntryTraits
{
    None = 0,
    Shortcut = 1 << 0,
    Executable = 1 << 1,
    Archive = 1 << 2,
    Image = 1 << 3,
    Video = 1 << 4,
    Audio = 1 << 5,
    Text = 1 << 6,
    Script = 1 << 7
}

[Flags]
public enum FileCommandCapabilities
{
    None = 0,
    Open = 1 << 0,
    Rename = 1 << 1,
    Delete = 1 << 2,
    Copy = 1 << 3,
    Cut = 1 << 4,
    PasteInto = 1 << 5,
    CreateFile = 1 << 6,
    CreateFolder = 1 << 7,
    ShowProperties = 1 << 8,
    OpenTarget = 1 << 9,
    RunAsAdministrator = 1 << 10,
    ExtractHere = 1 << 11,
    ExtractToFolder = 1 << 12,
    Preview = 1 << 13
}

public sealed record FileCommandTarget(
    FileCommandTargetKind Kind,
    string? Path,
    string DisplayName,
    bool IsDirectory,
    bool IsVirtual,
    FileEntryTraits Traits,
    FileCommandCapabilities Capabilities);

public sealed record FileCommandDescriptor(
    string Id,
    string Text,
    FileCommandCapabilities RequiredCapabilities);

public static class FileCommandIds
{
    public const string Open = "open";
    public const string OpenWith = "open-with";
    public const string Share = "share";
    public const string Compress = "compress";
    public const string CompressZip = "compress-zip";
    public const string Compress7z = "compress-7z";
    public const string CreateShortcut = "create-shortcut";
    public const string CopyPath = "copy-path";
    public const string SetTag = "set-tag";
    public const string OpenInNewTab = "open-in-new-tab";
    public const string OpenInNewWindow = "open-in-new-window";
    public const string PinToSidebar = "pin-to-sidebar";
    public const string UnpinFromSidebar = "unpin-from-sidebar";
    public const string OpenInTerminal = "open-in-terminal";
    public const string View = "view";
    public const string SortBy = "sort-by";
    public const string GroupBy = "group-by";
    public const string Refresh = "refresh";
    public const string Rename = "rename";
    public const string Delete = "delete";
    public const string Copy = "copy";
    public const string Cut = "cut";
    public const string Paste = "paste";
    public const string NewFile = "new-file";
    public const string NewFolder = "new-folder";
    public const string Properties = "properties";
    public const string OpenTarget = "open-target";
    public const string RunAsAdministrator = "run-as-administrator";
    public const string ExtractHere = "extract-here";
    public const string ExtractToFolder = "extract-to-folder";
    public const string ExtractSmart = "extract-smart";
    public const string Preview = "preview";
}
