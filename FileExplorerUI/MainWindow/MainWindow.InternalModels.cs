using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace FileExplorerUI
{
    internal sealed record MetadataWorkItem(int Index, string Name, ulong MftRef, bool IsDirectory, bool IsLink);

    internal sealed record MetadataPayload(string SizeText, string ModifiedText, string IconGlyph, Brush IconForeground);

    internal sealed record MetadataHydrationResult(MetadataWorkItem Item, MetadataPayload Payload);

    internal sealed record ViewportMetadataResult(
        int Index,
        long? SizeBytes,
        string SizeText,
        DateTime? ModifiedAt,
        string ModifiedText);

    internal sealed class DirectoryViewState
    {
        public double DetailsVerticalOffset { get; init; }
        public string? SelectedEntryPath { get; init; }
    }

    internal sealed record EntryGroupDescriptor(string BucketKey, string StateKey, string Label, string OrderKey);

    internal sealed class EntryGroupBucket
    {
        public required EntryGroupDescriptor Descriptor { get; init; }

        public List<EntryViewModel> Items { get; } = new();
    }

    internal enum EntryIconKind
    {
        Folder,
        FolderLink,
        File,
        FileLink,
        Text,
        Archive,
        Image,
        Video,
        Audio,
        Pdf,
        Word,
        Excel,
        PowerPoint,
        Code,
        Executable,
        Shortcut,
        DiskImage
    }
}
