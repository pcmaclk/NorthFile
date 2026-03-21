namespace FileExplorerUI.Workspace;

public sealed class PanelViewState
{
    public string CurrentPath { get; set; } = string.Empty;

    public string QueryText { get; set; } = string.Empty;

    public string? SelectedEntryPath { get; set; }

    public EntryViewMode ViewMode { get; set; } = EntryViewMode.Details;

    public EntrySortField SortField { get; set; } = EntrySortField.Name;

    public SortDirection SortDirection { get; set; } = SortDirection.Ascending;

    public EntryGroupField GroupField { get; set; } = EntryGroupField.None;
}
