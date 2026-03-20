namespace FileExplorerUI.Workspace;

public sealed class PanelViewState
{
    public string CurrentPath { get; set; } = string.Empty;

    public string QueryText { get; set; } = string.Empty;

    public string? SelectedEntryPath { get; set; }
}
