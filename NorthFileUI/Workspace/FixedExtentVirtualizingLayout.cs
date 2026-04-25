using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace NorthFileUI.Workspace;

public sealed class FixedExtentVirtualizingLayout : VirtualizingLayout
{
    private readonly EntriesRepeaterLayoutProfile _layoutProfile;
    private static readonly object s_layoutPerfLock = new();
    private static readonly string s_layoutPerfLogPath = Path.Combine(AppContext.BaseDirectory, "layout-perf.log");
    private int _realizationStartIndex = -1;
    private int _realizationEndIndex = -1;

    public FixedExtentVirtualizingLayout(EntriesRepeaterLayoutProfile layoutProfile)
    {
        _layoutProfile = layoutProfile;
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        long start = Stopwatch.GetTimestamp();
        int loadedItemCount = context.ItemCount;
        int logicalItemCount = Math.Max(loadedItemCount, _layoutProfile.TotalItemCountProvider());
        double itemExtent = Math.Max(1, _layoutProfile.PrimaryItemExtentProvider());
        double crossAxisExtent = Math.Max(1, _layoutProfile.CrossAxisExtentProvider());
        Rect realizationRect = context.RealizationRect;

        if (loadedItemCount <= 0)
        {
            _realizationStartIndex = -1;
            _realizationEndIndex = -1;
            AppendLayoutPerfLog(
                $"stage=measure loaded=0 logical={logicalItemCount} rect=({realizationRect.X:F1},{realizationRect.Y:F1},{realizationRect.Width:F1},{realizationRect.Height:F1}) extent={itemExtent:F1}");
            return BuildExtent(logicalItemCount, itemExtent, crossAxisExtent);
        }

        double viewportStart = _layoutProfile.IsVertical ? realizationRect.Y : realizationRect.X;
        double viewportLength = _layoutProfile.IsVertical ? realizationRect.Height : realizationRect.Width;
        if (viewportLength <= 0)
        {
            viewportLength = itemExtent * 8;
        }

        const int overscanItems = 4;
        int firstIndex = Math.Clamp((int)Math.Floor(viewportStart / itemExtent) - overscanItems, 0, loadedItemCount - 1);
        int lastIndex = Math.Clamp((int)Math.Ceiling((viewportStart + viewportLength) / itemExtent) + overscanItems, firstIndex, loadedItemCount - 1);

        _realizationStartIndex = firstIndex;
        _realizationEndIndex = lastIndex;

        Size measureSize = _layoutProfile.IsVertical
            ? new Size(crossAxisExtent, itemExtent)
            : new Size(itemExtent, crossAxisExtent);

        for (int index = firstIndex; index <= lastIndex; index++)
        {
            UIElement element = context.GetOrCreateElementAt(index);
            element.Measure(measureSize);
        }

        long elapsedMs = (long)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        AppendLayoutPerfLog(
            $"stage=measure loaded={loadedItemCount} logical={logicalItemCount} first={firstIndex} last={lastIndex} realized={lastIndex - firstIndex + 1} rect=({realizationRect.X:F1},{realizationRect.Y:F1},{realizationRect.Width:F1},{realizationRect.Height:F1}) elapsed={elapsedMs}ms");

        return BuildExtent(logicalItemCount, itemExtent, crossAxisExtent);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        long start = Stopwatch.GetTimestamp();
        if (_realizationStartIndex < 0 || _realizationEndIndex < _realizationStartIndex)
        {
            AppendLayoutPerfLog("stage=arrange realized=0");
            return finalSize;
        }

        double itemExtent = Math.Max(1, _layoutProfile.PrimaryItemExtentProvider());
        double crossAxisExtent = Math.Max(1, _layoutProfile.CrossAxisExtentProvider());

        for (int index = _realizationStartIndex; index <= _realizationEndIndex; index++)
        {
            UIElement element = context.GetOrCreateElementAt(index);
            Rect bounds = _layoutProfile.IsVertical
                ? new Rect(0, index * itemExtent, crossAxisExtent, itemExtent)
                : new Rect(index * itemExtent, 0, itemExtent, crossAxisExtent);
            element.Arrange(bounds);
        }

        long elapsedMs = (long)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
        AppendLayoutPerfLog(
            $"stage=arrange first={_realizationStartIndex} last={_realizationEndIndex} realized={_realizationEndIndex - _realizationStartIndex + 1} final=({finalSize.Width:F1},{finalSize.Height:F1}) elapsed={elapsedMs}ms");

        return finalSize;
    }

    private Size BuildExtent(int logicalItemCount, double itemExtent, double crossAxisExtent)
    {
        double primaryExtent = Math.Max(0, logicalItemCount * itemExtent);
        return _layoutProfile.IsVertical
            ? new Size(crossAxisExtent, primaryExtent)
            : new Size(primaryExtent, crossAxisExtent);
    }

    private static void AppendLayoutPerfLog(string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [LAYOUT] {message}{Environment.NewLine}";
        lock (s_layoutPerfLock)
        {
            File.AppendAllText(s_layoutPerfLogPath, line, Encoding.UTF8);
        }
    }
}
