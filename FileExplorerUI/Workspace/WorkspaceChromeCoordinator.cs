using FileExplorerUI.Controls;
using System.Threading.Tasks;

namespace FileExplorerUI.Workspace;

public sealed class WorkspaceChromeCoordinator
{
    private readonly WorkspaceTabController _tabController;
    private readonly WorkspaceTabStripHost _tabStripHost;

    public WorkspaceChromeCoordinator(
        WorkspaceTabController tabController,
        WorkspaceTabStripHost tabStripHost)
    {
        _tabController = tabController;
        _tabStripHost = tabStripHost;

        _tabController.TabsChanged += (_, _) => RefreshTabs();
        _tabController.ActiveTabChanged += (_, _) => RefreshActiveState();
    }

    public void RefreshTabs()
    {
        _tabStripHost.Refresh();
    }

    public void RefreshTabVisuals()
    {
        _tabStripHost.RefreshVisuals();
    }

    public void RefreshActiveState()
    {
        _tabStripHost.RefreshActiveState();
    }

    public void RefreshActivePresentation()
    {
        _tabStripHost.RefreshActivePresentation();
    }

    public Task HandleSelectionChangedAsync(Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
    {
        return _tabStripHost.HandleSelectionChangedAsync(e);
    }

    public Task ActivateAdjacentAsync(int delta)
    {
        WorkspaceTabState? nextTab = _tabController.GetAdjacentTab(delta);
        return nextTab is null
            ? Task.CompletedTask
            : _tabController.ActivateAsync(nextTab);
    }
}
