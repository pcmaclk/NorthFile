using System;

namespace NorthFileUI
{
    internal sealed class WatcherController
    {
        internal readonly record struct RefreshDecision(bool ShouldRefresh, long NextRefreshTick, bool WasSuppressed);

        public RefreshDecision EvaluateRefresh(
            string snapPath,
            string currentPath,
            bool isLoading,
            long nowTick,
            long lastRefreshTick,
            bool isSuppressedRefresh)
        {
            if (!string.Equals(snapPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return new RefreshDecision(false, lastRefreshTick, false);
            }

            if (isLoading)
            {
                return new RefreshDecision(false, lastRefreshTick, false);
            }

            if (nowTick - lastRefreshTick < 1000)
            {
                return new RefreshDecision(false, lastRefreshTick, false);
            }

            if (isSuppressedRefresh)
            {
                return new RefreshDecision(false, lastRefreshTick, true);
            }

            return new RefreshDecision(true, nowTick, false);
        }
    }
}
