using System;
using System.Collections.Generic;

namespace FileExplorerUI.Workspace;

public sealed class PanelViewState
{
    public PanelNavigationState Navigation { get; } = new();

    public PanelDataSession DataSession { get; } = new();

    public string CurrentPath
    {
        get => Navigation.CurrentPath;
        set => Navigation.CurrentPath = value;
    }

    public string AddressText
    {
        get => Navigation.AddressText;
        set => Navigation.AddressText = value;
    }

    public string QueryText
    {
        get => Navigation.QueryText;
        set => Navigation.QueryText = value;
    }

    public string? SelectedEntryPath { get; set; }

    public string? FocusedEntryPath { get; set; }

    internal Dictionary<string, DirectoryViewState> DirectoryViewStates { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    internal Dictionary<string, bool> GroupExpansionStates { get; } =
        new(StringComparer.Ordinal);

    public string? PendingHistoryStateRestorePath { get; set; }

    public string? PendingParentReturnAnchorPath { get; set; }

    public uint CurrentPageSize { get; set; } = 96;

    public double LastDetailsHorizontalOffset { get; set; } = double.NaN;

    public double LastDetailsVerticalOffset { get; set; } = double.NaN;

    public double LastGroupedHorizontalOffset { get; set; } = double.NaN;

    public double LastGroupedVerticalOffset { get; set; } = double.NaN;

    public EntryViewMode ViewMode { get; set; } = EntryViewMode.Details;

    public EntrySortField SortField { get; set; } = EntrySortField.Name;

    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;

    public EntryGroupField GroupField { get; set; } = EntryGroupField.None;

    public double NameColumnWidth { get; set; } = 220;

    public double TypeColumnWidth { get; set; } = 150;

    public double SizeColumnWidth { get; set; } = 120;

    public double ModifiedColumnWidth { get; set; } = 180;

    public double DetailsContentWidth { get; set; } = 694;

    public double DetailsRowWidth { get; set; } = 714;

    public void CopyNonDataStateFrom(PanelViewState source)
    {
        // DataSession owns live UI collections and result-set handles; tab/pane copies should reload data into their own session.
        Navigation.CopyFrom(source.Navigation);
        SelectedEntryPath = source.SelectedEntryPath;
        FocusedEntryPath = source.FocusedEntryPath;
        DirectoryViewStates.Clear();
        foreach ((string path, DirectoryViewState state) in source.DirectoryViewStates)
        {
            DirectoryViewStates[path] = state.Clone();
        }
        GroupExpansionStates.Clear();
        foreach ((string key, bool isExpanded) in source.GroupExpansionStates)
        {
            GroupExpansionStates[key] = isExpanded;
        }
        PendingHistoryStateRestorePath = source.PendingHistoryStateRestorePath;
        PendingParentReturnAnchorPath = source.PendingParentReturnAnchorPath;
        CurrentPageSize = source.CurrentPageSize;
        LastDetailsHorizontalOffset = source.LastDetailsHorizontalOffset;
        LastDetailsVerticalOffset = source.LastDetailsVerticalOffset;
        LastGroupedHorizontalOffset = source.LastGroupedHorizontalOffset;
        LastGroupedVerticalOffset = source.LastGroupedVerticalOffset;
        ViewMode = source.ViewMode;
        SortField = source.SortField;
        SortDirection = source.SortDirection;
        GroupField = source.GroupField;
        NameColumnWidth = source.NameColumnWidth;
        TypeColumnWidth = source.TypeColumnWidth;
        SizeColumnWidth = source.SizeColumnWidth;
        ModifiedColumnWidth = source.ModifiedColumnWidth;
        DetailsContentWidth = source.DetailsContentWidth;
        DetailsRowWidth = source.DetailsRowWidth;
    }

    public PanelViewState Clone()
    {
        var clone = new PanelViewState();
        clone.CopyNonDataStateFrom(this);
        return clone;
    }
}
