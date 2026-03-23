using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FileExplorerUI.Workspace;

public sealed class FixedExtentVirtualizingLayout : VirtualizingLayout
{
    private readonly EntriesRepeaterLayoutProfile _layoutProfile;
    private int _realizationStartIndex = -1;
    private int _realizationEndIndex = -1;

    public FixedExtentVirtualizingLayout(EntriesRepeaterLayoutProfile layoutProfile)
    {
        _layoutProfile = layoutProfile;
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        int loadedItemCount = context.ItemCount;
        int logicalItemCount = Math.Max(loadedItemCount, _layoutProfile.TotalItemCountProvider());
        double itemExtent = Math.Max(1, _layoutProfile.PrimaryItemExtentProvider());
        double crossAxisExtent = Math.Max(1, _layoutProfile.CrossAxisExtentProvider());
        Rect realizationRect = context.RealizationRect;

        if (loadedItemCount <= 0)
        {
            _realizationStartIndex = -1;
            _realizationEndIndex = -1;
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

        return BuildExtent(logicalItemCount, itemExtent, crossAxisExtent);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        if (_realizationStartIndex < 0 || _realizationEndIndex < _realizationStartIndex)
        {
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

        return finalSize;
    }

    private Size BuildExtent(int logicalItemCount, double itemExtent, double crossAxisExtent)
    {
        double primaryExtent = Math.Max(0, logicalItemCount * itemExtent);
        return _layoutProfile.IsVertical
            ? new Size(crossAxisExtent, primaryExtent)
            : new Size(primaryExtent, crossAxisExtent);
    }
}
