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
        private const int WatcherSelfMutationSuppressionMs = 2000;

        private void SuppressNextWatcherRefresh(string path)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                _suppressedWatcherRefreshPaths[path] = Environment.TickCount64 + WatcherSelfMutationSuppressionMs;
            }
        }

        private bool ConsumeSuppressedWatcherRefresh(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !_suppressedWatcherRefreshPaths.TryGetValue(path, out long suppressUntilTick))
            {
                return false;
            }

            if (Environment.TickCount64 <= suppressUntilTick)
            {
                return true;
            }

            _suppressedWatcherRefreshPaths.Remove(path);
            return false;
        }

        private void ConfigureDirectoryWatcher(string path)
        {
            ConfigureDirectoryWatcher(WorkspacePanelId.Primary, path);
        }

        private void ConfigureDirectoryWatcher(WorkspacePanelId panelId, string path)
        {
            DisposeDirectoryWatcher(panelId);

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

                watcher.Changed += (_, _) => ScheduleIncrementalRefreshFromWatcher(panelId, "changed");
                watcher.Created += (_, _) => ScheduleIncrementalRefreshFromWatcher(panelId, "changed");
                watcher.Deleted += (_, _) => ScheduleIncrementalRefreshFromWatcher(panelId, "changed");
                watcher.Renamed += (_, _) => ScheduleIncrementalRefreshFromWatcher(panelId, "renamed");
                SetDirectoryWatcher(panelId, watcher);
            }
            catch
            {
                // Non-fatal: we can still rely on manual refresh + TTL.
            }
        }

        private void DisposeDirectoryWatcher(WorkspacePanelId panelId)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                _secondaryDirWatcher?.Dispose();
                _secondaryDirWatcher = null;
                return;
            }

            _dirWatcher?.Dispose();
            _dirWatcher = null;
        }

        private void SetDirectoryWatcher(WorkspacePanelId panelId, FileSystemWatcher watcher)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                _secondaryDirWatcher = watcher;
                return;
            }

            _dirWatcher = watcher;
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

        private void ScheduleIncrementalRefreshFromWatcher(WorkspacePanelId panelId, string reason)
        {
            string snapPath = GetPanelCurrentPath(panelId);
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
                        string currentPath = GetPanelCurrentPath(panelId);
                        WatcherController.RefreshDecision refreshDecision = _watcherController.EvaluateRefresh(
                            snapPath,
                            currentPath,
                            GetPanelIsLoading(panelId),
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

                        RefreshSidebarFavorites(refreshSelection: false);
                        _ = RefreshPanelsForDirectoryChangeAsync(currentPath, $"watcher_{reason}");
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
