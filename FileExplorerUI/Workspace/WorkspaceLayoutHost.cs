namespace FileExplorerUI.Workspace;

public sealed class WorkspaceLayoutHost
{
    private WorkspaceShellState _shellState;

    public WorkspaceLayoutHost(WorkspaceShellState shellState)
    {
        _shellState = shellState;
    }

    public WorkspaceShellState ShellState => _shellState;

    public void SetShellState(WorkspaceShellState shellState)
    {
        _shellState = shellState;
    }

    public WorkspaceLayoutMode LayoutMode
    {
        get => _shellState.LayoutMode;
        set => _shellState.LayoutMode = value;
    }

    public WorkspacePanelId ActivePanel
    {
        get => _shellState.ActivePanel;
        set => _shellState.ActivePanel = value;
    }

    public bool IsSplit => _shellState.IsSplit;

    public void ActivatePanel(WorkspacePanelId panelId)
    {
        _shellState.ActivePanel = panelId;
    }

    public PanelViewState GetActivePanelState()
    {
        return GetPanelState(_shellState.ActivePanel);
    }

    public PanelViewState GetPanelState(WorkspacePanelId panelId)
    {
        return panelId == WorkspacePanelId.Secondary
            ? _shellState.Secondary
            : _shellState.Primary;
    }
}
