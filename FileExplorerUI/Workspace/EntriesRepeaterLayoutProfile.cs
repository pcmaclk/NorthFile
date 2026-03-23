using System;

namespace FileExplorerUI.Workspace;

public sealed class EntriesRepeaterLayoutProfile
{
    public EntriesRepeaterLayoutProfile(
        bool isVertical,
        Func<double> primaryItemExtentProvider,
        Func<int> totalItemCountProvider,
        Func<double> crossAxisExtentProvider,
        Func<double> viewportPrimaryExtentProvider)
    {
        IsVertical = isVertical;
        PrimaryItemExtentProvider = primaryItemExtentProvider;
        TotalItemCountProvider = totalItemCountProvider;
        CrossAxisExtentProvider = crossAxisExtentProvider;
        ViewportPrimaryExtentProvider = viewportPrimaryExtentProvider;
    }

    public bool IsVertical { get; }

    public Func<double> PrimaryItemExtentProvider { get; }

    public Func<int> TotalItemCountProvider { get; }

    public Func<double> CrossAxisExtentProvider { get; }

    public Func<double> ViewportPrimaryExtentProvider { get; }
}
