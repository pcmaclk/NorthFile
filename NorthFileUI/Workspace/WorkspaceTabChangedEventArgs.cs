using System;

namespace NorthFileUI.Workspace;

public sealed class WorkspaceTabChangedEventArgs : EventArgs
{
    public WorkspaceTabChangedEventArgs(WorkspaceTabState previousTab, WorkspaceTabState currentTab)
    {
        PreviousTab = previousTab;
        CurrentTab = currentTab;
    }

    public WorkspaceTabState PreviousTab { get; }

    public WorkspaceTabState CurrentTab { get; }
}
