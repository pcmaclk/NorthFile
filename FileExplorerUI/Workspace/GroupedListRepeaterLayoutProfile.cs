using System;
using System.Collections.Generic;

namespace FileExplorerUI.Workspace;

public sealed class GroupedListRepeaterLayoutProfile
{
    public GroupedListRepeaterLayoutProfile(
        Func<IReadOnlyList<EntryViewModel>> itemsProvider,
        Func<double> itemWidthProvider,
        Func<double> rowExtentProvider,
        Func<double> headerExtentProvider,
        Func<int> rowsPerColumnProvider,
        Func<double> viewportHeightProvider,
        double groupSpacing = 16)
    {
        ItemsProvider = itemsProvider;
        ItemWidthProvider = itemWidthProvider;
        RowExtentProvider = rowExtentProvider;
        HeaderExtentProvider = headerExtentProvider;
        RowsPerColumnProvider = rowsPerColumnProvider;
        ViewportHeightProvider = viewportHeightProvider;
        GroupSpacing = groupSpacing;
    }

    public Func<IReadOnlyList<EntryViewModel>> ItemsProvider { get; }

    public Func<double> ItemWidthProvider { get; }

    public Func<double> RowExtentProvider { get; }

    public Func<double> HeaderExtentProvider { get; }

    public Func<int> RowsPerColumnProvider { get; }

    public Func<double> ViewportHeightProvider { get; }

    public double GroupSpacing { get; }
}
