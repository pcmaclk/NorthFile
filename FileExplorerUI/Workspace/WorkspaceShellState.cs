namespace FileExplorerUI.Workspace;

public sealed class WorkspaceShellState
{
    public WorkspaceLayoutMode LayoutMode { get; set; } = WorkspaceLayoutMode.Single;

    public WorkspacePanelId ActivePanel { get; set; } = WorkspacePanelId.Primary;

    public PanelViewState Primary { get; } = new();

    public PanelViewState Secondary { get; } = new();

    public bool IsSplit => LayoutMode != WorkspaceLayoutMode.Single;

    public PanelViewState GetPanelState(WorkspacePanelId panelId)
    {
        return panelId == WorkspacePanelId.Secondary
            ? Secondary
            : Primary;
    }

    public void CopyNonDataStateFrom(WorkspaceShellState source)
    {
        LayoutMode = source.LayoutMode;
        ActivePanel = source.ActivePanel;
        Primary.CopyNonDataStateFrom(source.Primary);
        Secondary.CopyNonDataStateFrom(source.Secondary);
    }

    public WorkspaceShellState Clone()
    {
        var clone = new WorkspaceShellState();
        clone.CopyNonDataStateFrom(this);
        return clone;
    }

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
