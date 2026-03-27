using FileExplorerUI.Interop;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void RequestMetadataForLoadedRange(int startIndex, int endIndex)
        {
            if (IsDetailsViewportInteractionHot() || IsSparseViewportLoadQueuedOrActive())
            {
                return;
            }

            List<MetadataWorkItem> items = CollectMetadataWorkItems(startIndex, endIndex);
            if (items.Count == 0)
            {
                return;
            }

            CancellationTokenSource? baseCts = _directoryLoadCts;
            if (baseCts is null)
            {
                return;
            }

            long snapshotVersion = _directorySnapshotVersion;
            string path = _currentPath;
            CancellationToken token = baseCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await HydrateMetadataBatchAsync(path, snapshotVersion, items, token);
                }
                catch (OperationCanceledException)
                {
                }
            }, token);
        }

        private List<MetadataWorkItem> CollectMetadataWorkItems(int startIndex, int endIndex)
        {
            var items = new List<MetadataWorkItem>();
            if (startIndex < 0 || startIndex >= _entries.Count || endIndex < startIndex)
            {
                return items;
            }

            int cappedEnd = Math.Min(endIndex, _entries.Count - 1);
            for (int i = startIndex; i <= cappedEnd; i++)
            {
                EntryViewModel entry = _entries[i];
                if (!entry.IsLoaded || entry.IsMetadataLoaded)
                {
                    continue;
                }

                items.Add(new MetadataWorkItem(i, entry.Name, entry.MftRef, entry.IsDirectory, entry.IsLink));
            }

            return items;
        }

        private async Task HydrateMetadataBatchAsync(
            string path,
            long snapshotVersion,
            IReadOnlyList<MetadataWorkItem> items,
            CancellationToken token
        )
        {
            if (items.Count == 0)
            {
                return;
            }

            var results = new List<MetadataHydrationResult>(items.Count);
            foreach (MetadataWorkItem item in items)
            {
                token.ThrowIfCancellationRequested();

                MetadataPayload payload = BuildMetadataPayload(path, item.Name, item.IsDirectory, item.IsLink, token);
                results.Add(new MetadataHydrationResult(item, payload));
            }

            if (token.IsCancellationRequested || results.Count == 0)
            {
                return;
            }

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (snapshotVersion != _directorySnapshotVersion)
                {
                    return;
                }

                foreach (MetadataHydrationResult result in results)
                {
                    ApplyMetadataPayload(snapshotVersion, result.Item, result.Payload);
                }
            });
        }

        private MetadataPayload BuildMetadataPayload(
            string path,
            string name,
            bool isDirectory,
            bool isLink,
            CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();
            string fullPath = Path.Combine(path, name);
            string sizeText = isDirectory ? string.Empty : GetFileSizeText(fullPath);
            token.ThrowIfCancellationRequested();
            string modifiedText = GetModifiedTimeText(fullPath, isDirectory);
            string iconGlyph = GetEntryIconGlyph(isDirectory, isLink, name);
            Brush iconForeground = GetEntryIconBrush(isDirectory, isLink, name);
            return new MetadataPayload(sizeText, modifiedText, iconGlyph, iconForeground);
        }

        private static List<ViewportMetadataResult> BuildViewportMetadataResults(
            string path,
            int pageStartIndex,
            IReadOnlyList<FileRow> rows)
        {
            if (rows.Count == 0)
            {
                return [];
            }

            var results = new List<ViewportMetadataResult>(rows.Count);

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                int logicalIndex = pageStartIndex + rowIndex;
                FileRow row = rows[rowIndex];
                string fullPath = Path.Combine(path, row.Name);
                if (row.IsDirectory)
                {
                    DateTime? modifiedAt = TryGetModifiedTime(fullPath, isDirectory: true);
                    results.Add(new ViewportMetadataResult(
                        logicalIndex,
                        null,
                        string.Empty,
                        modifiedAt,
                        FormatModifiedTime(modifiedAt)));
                    continue;
                }

                (long? sizeBytes, string sizeText) = TryGetFileSize(fullPath);
                DateTime? modifiedFileAt = TryGetModifiedTime(fullPath, isDirectory: false);
                results.Add(new ViewportMetadataResult(
                    logicalIndex,
                    sizeBytes,
                    sizeText,
                    modifiedFileAt,
                    FormatModifiedTime(modifiedFileAt)));
            }

            return results;
        }

        private void ApplyViewportMetadataResults(long snapshotVersion, IReadOnlyList<ViewportMetadataResult> results)
        {
            if (snapshotVersion != _directorySnapshotVersion || results.Count == 0)
            {
                return;
            }

            foreach (ViewportMetadataResult result in results)
            {
                if (result.Index < 0 || result.Index >= _entries.Count)
                {
                    continue;
                }

                EntryViewModel entry = _entries[result.Index];
                if (!entry.IsLoaded)
                {
                    continue;
                }

                entry.SizeBytes = result.SizeBytes;
                entry.SizeText = result.SizeText;
                entry.ModifiedAt = result.ModifiedAt;
                entry.ModifiedText = result.ModifiedText;
                entry.IsMetadataLoaded = true;
            }
        }

        private void ApplyMetadataPayload(long snapshotVersion, MetadataWorkItem item, MetadataPayload payload)
        {
            if (snapshotVersion != _directorySnapshotVersion)
            {
                return;
            }

            if (item.Index < 0 || item.Index >= _entries.Count)
            {
                return;
            }

            EntryViewModel current = _entries[item.Index];
            if (!current.IsLoaded)
            {
                return;
            }

            if (current.MftRef != item.MftRef || !string.Equals(current.Name, item.Name, StringComparison.Ordinal))
            {
                return;
            }

            current.SizeText = payload.SizeText;
            current.ModifiedText = payload.ModifiedText;
            current.IconGlyph = payload.IconGlyph;
            current.IconForeground = payload.IconForeground;
            current.IsMetadataLoaded = true;
        }
    }
}
