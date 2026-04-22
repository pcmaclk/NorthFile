using System;

namespace FileExplorerUI.Workspace;

public sealed class WorkspaceTabRouter
{
    private readonly WorkspaceTabManager _manager;
    private readonly string _shellRootPath;

    public WorkspaceTabRouter(
        WorkspaceTabManager manager,
        string shellRootPath)
    {
        _manager = manager;
        _shellRootPath = shellRootPath;
    }

    public WorkspaceTabState CreateDefaultTab()
    {
        var tab = new WorkspaceTabState();
        tab.ShellState.Primary.CurrentPath = _shellRootPath;
        tab.ShellState.Secondary.CurrentPath = _shellRootPath;
        return tab;
    }

    public WorkspaceTabOpenResult OpenPathInNewTab(string? path)
    {
        string targetPath = string.IsNullOrWhiteSpace(path)
            ? _shellRootPath
            : path.Trim();

        WorkspaceTabState tab = CreateDefaultTab();
        _manager.AddAndActivate(tab);

        WorkspacePanelId targetPanelId = tab.ShellState.ActivePanel;
        PanelViewState targetPanel = tab.ShellState.GetPanelState(targetPanelId);
        string? navigationTarget = string.Equals(targetPath, targetPanel.CurrentPath, StringComparison.OrdinalIgnoreCase)
            ? null
            : targetPath;

        return new WorkspaceTabOpenResult
        {
            Tab = tab,
            TargetPanelId = targetPanelId,
            TargetPath = navigationTarget
        };
    }

    public WorkspaceTabState? GetAdjacentTab(int delta)
    {
        return _manager.GetAdjacentActive(delta);
    }

    public WorkspaceTabState ActiveTab => _manager.ActiveTab;

    public bool TryActivate(WorkspaceTabState tab)
    {
        return _manager.Activate(tab);
    }

    public bool TryClose(WorkspaceTabState tab, out bool activeTabChanged)
    {
        return _manager.TryClose(tab, out activeTabChanged);
    }

    public bool TryCloseActive(out bool activeTabChanged)
    {
        return _manager.TryClose(_manager.ActiveTab, out activeTabChanged);
    }
}
