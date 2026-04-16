namespace FileExplorerUI.Workspace;

public sealed class WorkspaceTabOpenResult
{
    public required WorkspaceTabState Tab { get; init; }

    public required WorkspacePanelId TargetPanelId { get; init; }

    public string? TargetPath { get; init; }
}
