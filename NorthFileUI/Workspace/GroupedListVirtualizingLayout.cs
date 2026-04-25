using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace NorthFileUI.Workspace;

public sealed class GroupedListVirtualizingLayout : VirtualizingLayout
{
    private readonly GroupedListRepeaterLayoutProfile _layoutProfile;
    private readonly List<int> _realizedIndices = [];
    private Rect[] _bounds = Array.Empty<Rect>();

    public GroupedListVirtualizingLayout(GroupedListRepeaterLayoutProfile layoutProfile)
    {
        _layoutProfile = layoutProfile;
    }

    public bool TryGetBoundsForIndex(int index, out Rect bounds)
    {
        if ((uint)index < (uint)_bounds.Length)
        {
            bounds = _bounds[index];
            return true;
        }

        bounds = Rect.Empty;
        return false;
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        IReadOnlyList<EntryViewModel> items = _layoutProfile.ItemsProvider();
        EnsureBounds(items);
        _realizedIndices.Clear();

        Rect realizationRect = context.RealizationRect;
        if (realizationRect.Width <= 0)
        {
            realizationRect.Width = Math.Max(availableSize.Width, _layoutProfile.ItemWidthProvider());
        }

        if (realizationRect.Height <= 0)
        {
            realizationRect.Height = Math.Max(availableSize.Height, _layoutProfile.ViewportHeightProvider());
        }

        for (int index = 0; index < items.Count; index++)
        {
            Rect bounds = _bounds[index];
            if (!Intersects(realizationRect, bounds))
            {
                continue;
            }

            UIElement element = context.GetOrCreateElementAt(index);
            element.Measure(new Size(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)));
            _realizedIndices.Add(index);
        }

        return BuildExtent(items.Count);
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        foreach (int index in _realizedIndices)
        {
            UIElement element = context.GetOrCreateElementAt(index);
            element.Arrange(_bounds[index]);
        }

        return finalSize;
    }

    private void EnsureBounds(IReadOnlyList<EntryViewModel> items)
    {
        if (_bounds.Length != items.Count)
        {
            _bounds = new Rect[items.Count];
        }

        double itemWidth = Math.Max(1, _layoutProfile.ItemWidthProvider());
        double rowExtent = Math.Max(1, _layoutProfile.RowExtentProvider());
        double headerExtent = Math.Max(1, _layoutProfile.HeaderExtentProvider());
        int rowsPerColumn = Math.Max(1, _layoutProfile.RowsPerColumnProvider());
        double groupSpacing = Math.Max(0, _layoutProfile.GroupSpacing);

        double x = 0;
        int index = 0;
        while (index < items.Count)
        {
            if (items[index].IsGroupHeader)
            {
                int headerIndex = index++;
                int itemStart = index;
                while (index < items.Count && !items[index].IsGroupHeader)
                {
                    index++;
                }

                int itemCount = index - itemStart;
                int columnCount = Math.Max(1, (int)Math.Ceiling(itemCount / (double)rowsPerColumn));
                double groupWidth = columnCount * itemWidth;
                _bounds[headerIndex] = new Rect(x, 0, groupWidth, headerExtent);

                for (int offset = 0; offset < itemCount; offset++)
                {
                    int column = offset / rowsPerColumn;
                    int row = offset % rowsPerColumn;
                    _bounds[itemStart + offset] = new Rect(
                        x + (column * itemWidth),
                        headerExtent + (row * rowExtent),
                        itemWidth,
                        rowExtent);
                }

                x += groupWidth + groupSpacing;
                continue;
            }

            int itemStartIndex = index;
            while (index < items.Count && !items[index].IsGroupHeader)
            {
                index++;
            }

            int itemCountWithoutHeader = index - itemStartIndex;
            int columnCountWithoutHeader = Math.Max(1, (int)Math.Ceiling(itemCountWithoutHeader / (double)rowsPerColumn));
            for (int offset = 0; offset < itemCountWithoutHeader; offset++)
            {
                int column = offset / rowsPerColumn;
                int row = offset % rowsPerColumn;
                _bounds[itemStartIndex + offset] = new Rect(
                    x + (column * itemWidth),
                    row * rowExtent,
                    itemWidth,
                    rowExtent);
            }

            x += (columnCountWithoutHeader * itemWidth) + groupSpacing;
        }
    }

    private Size BuildExtent(int itemCount)
    {
        double viewportHeight = Math.Max(1, _layoutProfile.ViewportHeightProvider());
        if (itemCount == 0 || _bounds.Length == 0)
        {
            return new Size(_layoutProfile.ItemWidthProvider(), viewportHeight);
        }

        double maxRight = _layoutProfile.ItemWidthProvider();
        double maxBottom = viewportHeight;
        for (int i = 0; i < itemCount; i++)
        {
            Rect bounds = _bounds[i];
            maxRight = Math.Max(maxRight, bounds.X + bounds.Width);
            maxBottom = Math.Max(maxBottom, bounds.Y + bounds.Height);
        }

        double width = maxRight;
        double height = maxBottom;
        return new Size(width, height);
    }

    private static bool Intersects(Rect realizationRect, Rect bounds)
    {
        return !(bounds.X + bounds.Width < realizationRect.X ||
                 bounds.X > realizationRect.X + realizationRect.Width ||
                 bounds.Y + bounds.Height < realizationRect.Y ||
                 bounds.Y > realizationRect.Y + realizationRect.Height);
    }
}
