using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NorthFileUI.Workspace;

public sealed class WorkspaceTabController
{
    private readonly WorkspaceSession _session;
    private readonly WorkspaceUiApplier _uiApplier;
    private readonly Func<WorkspacePanelId, string, Task> _navigatePanelToPathAsync;

    public WorkspaceTabController(
        WorkspaceSession session,
        WorkspaceUiApplier uiApplier,
        Func<WorkspacePanelId, string, Task> navigatePanelToPathAsync)
    {
        _session = session;
        _uiApplier = uiApplier;
        _navigatePanelToPathAsync = navigatePanelToPathAsync;
    }

    public event EventHandler? TabsChanged
    {
        add => _session.TabManager.TabsChanged += value;
        remove => _session.TabManager.TabsChanged -= value;
    }

    public event EventHandler<WorkspaceTabChangedEventArgs>? ActiveTabChanged
    {
        add => _session.TabManager.ActiveTabChanged += value;
        remove => _session.TabManager.ActiveTabChanged -= value;
    }

    public WorkspaceTabState ActiveTab => _session.ActiveTab;

    public IReadOnlyList<WorkspaceTabPresentation> BuildTabPresentations(string shellRootTitle, string appTitle)
    {
        return _session.BuildTabPresentations(shellRootTitle, appTitle);
    }

    public WorkspaceTabState? GetAdjacentTab(int delta)
    {
        return _session.TabRouter.GetAdjacentTab(delta);
    }

    public async Task OpenPathInNewTabAsync(string? path)
    {
        using WorkspaceTabPerfScope perf = WorkspaceTabPerf.Begin("open", $"path=\"{path ?? "(default)"}\"");
        WorkspaceTabOpenResult result = _session.TabRouter.OpenPathInNewTab(path);
        perf.Mark("router.opened", $"tabs={_session.TabCount}");
        await _uiApplier.ApplyAsync(_session.CurrentShellState);
        perf.Mark("ui.apply.returned");

        if (!string.IsNullOrWhiteSpace(result.TargetPath))
        {
            perf.Mark("navigate.begin", $"path=\"{result.TargetPath}\"");
            await _navigatePanelToPathAsync(result.TargetPanelId, result.TargetPath);
            perf.Mark("navigate.end");
        }
    }

    public async Task<bool> ActivateAsync(WorkspaceTabState tab)
    {
        using WorkspaceTabPerfScope perf = WorkspaceTabPerf.Begin("activate");
        if (!_session.TabRouter.TryActivate(tab))
        {
            perf.Mark("router.skipped", "already-active");
            return false;
        }

        perf.Mark("router.activated");
        await _uiApplier.ApplyAsync(_session.CurrentShellState);
        perf.Mark("ui.apply.returned");
        return true;
    }

    public async Task<bool> CloseAsync(WorkspaceTabState tab)
    {
        using WorkspaceTabPerfScope perf = WorkspaceTabPerf.Begin("close");
        if (!_session.TabRouter.TryClose(tab, out bool activeTabChanged))
        {
            perf.Mark("router.skipped");
            return false;
        }

        perf.Mark("router.closed", $"activeChanged={activeTabChanged} tabs={_session.TabCount}");
        if (activeTabChanged)
        {
            await _uiApplier.ApplyAsync(_session.CurrentShellState);
            perf.Mark("ui.apply.returned");
        }

        return true;
    }

    public Task<bool> CloseActiveAsync()
    {
        return CloseAsync(ActiveTab);
    }
}
