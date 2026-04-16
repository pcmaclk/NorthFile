using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void SuppressNextWatcherRefresh(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _suppressedWatcherRefreshPaths.Add(path);
            }
        }

        private bool ConsumeSuppressedWatcherRefresh(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && _suppressedWatcherRefreshPaths.Remove(path);
        }

        private void ConfigureDirectoryWatcher(string path)
        {
            _dirWatcher?.Dispose();
            _dirWatcher = null;

            if (!_explorerService.DirectoryExists(path))
            {
                return;
            }

            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                watcher.Changed += Watcher_OnChanged;
                watcher.Created += Watcher_OnChanged;
                watcher.Deleted += Watcher_OnChanged;
                watcher.Renamed += Watcher_OnRenamed;
                _dirWatcher = watcher;
            }
            catch
            {
                // Non-fatal: we can still rely on manual refresh + TTL.
            }
        }

        private static bool IsDriveRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string full = Path.GetFullPath(path).TrimEnd('\\');
            return full.Length == 2 && full[1] == ':';
        }

        private void EnsureRefreshFallbackInvalidation(string path, string reason)
        {
            // Week4 strategy: if USN capability is unavailable, force invalidate to keep consistency.
            if (_usnCapability.available != 0)
            {
                return;
            }

            try
            {
                _explorerService.InvalidateMemorySessionDirectory(path);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPathInvalidateWarning", path, reason, FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void EnsurePersistentRefreshFallbackInvalidation(string path, string reason)
        {
            if (_usnCapability.available != 0)
            {
                return;
            }

            try
            {
                _explorerService.InvalidateMemoryDirectory(path);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusPathInvalidateWarning", path, reason, FileOperationErrors.ToUserMessage(ex));
            }
        }

        private void UpdateUsnCapability(string path)
        {
            try
            {
                _usnCapability = _explorerService.ProbeUsnCapability(path);
            }
            catch
            {
                _usnCapability = default;
            }
        }

        private void Watcher_OnChanged(object sender, FileSystemEventArgs e)
        {
            ScheduleIncrementalRefreshFromWatcher("changed");
        }

        private void Watcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            ScheduleIncrementalRefreshFromWatcher("renamed");
        }

        private void ScheduleIncrementalRefreshFromWatcher(string reason)
        {
            string snapPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            _watcherDebounceCts?.Cancel();
            _watcherDebounceCts = new CancellationTokenSource();
            CancellationToken token = _watcherDebounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, token);
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    _ = DispatcherQueue.TryEnqueue(() =>
                    {
                        long now = Environment.TickCount64;
                        string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
                        WatcherController.RefreshDecision refreshDecision = _watcherController.EvaluateRefresh(
                            snapPath,
                            currentPath,
                            GetPanelIsLoading(WorkspacePanelId.Primary),
                            now,
                            _lastWatcherRefreshTick,
                            ConsumeSuppressedWatcherRefresh(currentPath));
                        if (refreshDecision.WasSuppressed)
                        {
                            Debug.WriteLine($"[Watcher] Suppressed self-refresh for {currentPath}");
                            return;
                        }
                        if (!refreshDecision.ShouldRefresh)
                        {
                            return;
                        }
                        _lastWatcherRefreshTick = refreshDecision.NextRefreshTick;

                        try
                        {
                            _explorerService.MarkPathChanged(currentPath);
                        }
                        catch
                        {
                            // Ignore mark failures; background refresh will still attempt to recover.
                        }

                        EnsurePersistentRefreshFallbackInvalidation(currentPath, $"watcher_{reason}");
                        RefreshSidebarFavorites(refreshSelection: false);
                        _ = RefreshCurrentDirectoryInBackgroundAsync();
                    });
                }
                catch (TaskCanceledException)
                {
                    // Ignore debounce cancellation.
                }
            });
        }
    }
}
