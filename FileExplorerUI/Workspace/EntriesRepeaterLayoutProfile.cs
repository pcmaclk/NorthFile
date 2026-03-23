using System;

namespace FileExplorerUI.Workspace;

public sealed class EntriesRepeaterLayoutProfile
{
    public EntriesRepeaterLayoutProfile(
        bool isVertical,
        Func<double> primaryItemExtentProvider,
        Func<int> totalItemCountProvider,
        Func<double> crossAxisExtentProvider)
    {
        IsVertical = isVertical;
        PrimaryItemExtentProvider = primaryItemExtentProvider;
        TotalItemCountProvider = totalItemCountProvider;
        CrossAxisExtentProvider = crossAxisExtentProvider;
    }

    public bool IsVertical { get; }

    public Func<double> PrimaryItemExtentProvider { get; }

    public Func<int> TotalItemCountProvider { get; }

    public Func<double> CrossAxisExtentProvider { get; }
}
