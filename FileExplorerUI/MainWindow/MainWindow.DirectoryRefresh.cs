using FileExplorerUI.Workspace;
using System;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async Task RefreshCurrentDirectoryInBackgroundAsync(bool preserveViewport = false)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                PopulateMyComputerEntries();
                return;
            }

            double detailsVerticalOffset = DetailsEntriesScrollViewer.VerticalOffset;
            double groupedHorizontalOffset = GroupedEntriesScrollViewer.HorizontalOffset;

            try
            {
                UpdateUsnCapability(_currentPath);
                ConfigureDirectoryWatcher(_currentPath);
                EnsureRefreshFallbackInvalidation(_currentPath, "background_refresh");
                if (UsesClientPresentationPipeline())
                {
                    await LoadAllEntriesForPresentationAsync(_currentPath);
                }
                else
                {
                    await LoadPageAsync(_currentPath, cursor: 0, append: false);
                }
                if (preserveViewport)
                {
                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        if (_currentViewMode == EntryViewMode.Details)
                        {
                            double maxOffset = Math.Max(0, DetailsEntriesScrollViewer.ScrollableHeight);
                            DetailsEntriesScrollViewer.ChangeView(null, Math.Min(maxOffset, detailsVerticalOffset), null, disableAnimation: true);
                        }
                        else
                        {
                            double maxOffset = Math.Max(0, GroupedEntriesScrollViewer.ScrollableWidth);
                            GroupedEntriesScrollViewer.ChangeView(Math.Min(maxOffset, groupedHorizontalOffset), null, null, disableAnimation: true);
                        }
                    });
                }
            }
            catch
            {
                // Keep local state if background refresh fails; next manual load can recover.
            }
        }
    }
}
