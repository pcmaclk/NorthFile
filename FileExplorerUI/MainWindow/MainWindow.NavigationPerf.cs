using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.IO;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private static void LogPerfSnapshot(
            string mode,
            string path,
            string query,
            string source,
            int loaded,
            uint total,
            uint scanned,
            uint matched,
            long fetchMs,
            uint batch,
            bool hasMore,
            ulong nextCursor,
            string usn
        )
        {
            double hitRate = scanned > 0 ? matched * 100.0 / scanned : 0.0;
            string q = string.IsNullOrWhiteSpace(query) ? "-" : query;
            Debug.WriteLine(
                $"[PERF] mode={mode} path=\"{path}\" query=\"{q}\" source={source} loaded={loaded} total={total} scanned={scanned} matched={matched} hit={hitRate:F1}% fetch_ms={fetchMs} batch={batch} has_more={hasMore} next={nextCursor} usn={usn}"
            );
        }

        private NavigationPerfSession BeginNavigationPerfSession(string targetPath, string trigger)
        {
            var session = new NavigationPerfSession(targetPath, trigger);
            _activeNavigationPerfSession = session;
            return session;
        }

        private NavigationPerfSession? TryGetCurrentNavigationPerfSession()
        {
            return _activeNavigationPerfSession;
        }

        private void EndNavigationPerfSession(NavigationPerfSession session)
        {
            if (ReferenceEquals(_activeNavigationPerfSession, session))
            {
                _activeNavigationPerfSession = null;
            }
        }

        private static void AppendNavigationPerfLog(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (s_navigationPerfLogLock)
            {
                File.AppendAllText(s_navigationPerfLogPath, line, System.Text.Encoding.UTF8);
            }
        }

        private void ScheduleNavigationPerfFirstFrameMark(NavigationPerfSession perf, string stage)
        {
            void OnRendering(object? sender, object args)
            {
                CompositionTarget.Rendering -= OnRendering;
                perf.Mark(stage);
            }

            CompositionTarget.Rendering += OnRendering;
        }

        private void ScheduleAfterNextFrame(Action action)
        {
            void OnRendering(object? sender, object args)
            {
                CompositionTarget.Rendering -= OnRendering;
                _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => action());
            }

            CompositionTarget.Rendering += OnRendering;
        }
    }
}
