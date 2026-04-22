namespace FileExplorerUI.Workspace;

public sealed class WorkspaceTabPresentation
{
    public required WorkspaceTabState Tab { get; init; }

    public required string Title { get; init; }

    public required string Glyph { get; init; }

    public required bool IsActive { get; init; }

    public required bool CanClose { get; init; }

    public required bool ShowTrailingSeparator { get; init; }
}
