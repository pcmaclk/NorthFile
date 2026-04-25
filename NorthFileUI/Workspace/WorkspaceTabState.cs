using System;

namespace NorthFileUI.Workspace;

public sealed class WorkspaceTabState
{
    public Guid Id { get; } = Guid.NewGuid();

    public string CustomTitle { get; set; } = string.Empty;

    public WorkspaceShellState ShellState { get; } = new();
}
