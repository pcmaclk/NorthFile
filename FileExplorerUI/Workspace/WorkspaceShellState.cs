namespace FileExplorerUI.Workspace;

public sealed class WorkspaceShellState
{
    public WorkspaceLayoutMode LayoutMode { get; set; } = WorkspaceLayoutMode.Single;

    public WorkspacePanelId ActivePanel { get; set; } = WorkspacePanelId.Primary;

    public PanelViewState Primary { get; } = new();

    public PanelViewState Secondary { get; } = new();

    public bool IsSplit => LayoutMode != WorkspaceLayoutMode.Single;

    public string BuildTabSummary()
    {
        if (!IsSplit)
        {
            return Primary.CurrentPath;
        }

        string left = string.IsNullOrWhiteSpace(Primary.CurrentPath) ? "(empty)" : Primary.CurrentPath;
        string right = string.IsNullOrWhiteSpace(Secondary.CurrentPath) ? "(empty)" : Secondary.CurrentPath;
        return ActivePanel == WorkspacePanelId.Primary
            ? $"{left} | {right}"
            : $"{right} | {left}";
    }
}
