using System;
using System.Threading.Tasks;

namespace FileExplorerUI.Workspace;

public sealed class WorkspaceUiApplier
{
    private readonly WorkspaceLayoutHost _layoutHost;
    private readonly Action<WorkspaceShellState> _applyShellStateToUi;
    private readonly Action<WorkspaceShellState> _prepareShellStateForRestore;
    private readonly Func<PanelViewState, Task> _restorePrimaryPanelStateAsync;
    private readonly Func<PanelViewState, Task> _restoreSecondaryPanelStateAsync;
    private readonly Action<WorkspacePanelId> _activatePanel;
    private readonly Action _raiseSecondaryPaneNavigationStateChanged;
    private readonly Action _notifyWorkspacePanelVisualStateChanged;
    private readonly Action _notifyTitleBarTextChanged;
    private int _restoreVersion;

    public WorkspaceUiApplier(
        WorkspaceLayoutHost layoutHost,
        Action<WorkspaceShellState> applyShellStateToUi,
        Action<WorkspaceShellState> prepareShellStateForRestore,
        Func<PanelViewState, Task> restorePrimaryPanelStateAsync,
        Func<PanelViewState, Task> restoreSecondaryPanelStateAsync,
        Action<WorkspacePanelId> activatePanel,
        Action raiseSecondaryPaneNavigationStateChanged,
        Action notifyWorkspacePanelVisualStateChanged,
        Action notifyTitleBarTextChanged)
    {
        _layoutHost = layoutHost;
        _applyShellStateToUi = applyShellStateToUi;
        _prepareShellStateForRestore = prepareShellStateForRestore;
        _restorePrimaryPanelStateAsync = restorePrimaryPanelStateAsync;
        _restoreSecondaryPanelStateAsync = restoreSecondaryPanelStateAsync;
        _activatePanel = activatePanel;
        _raiseSecondaryPaneNavigationStateChanged = raiseSecondaryPaneNavigationStateChanged;
        _notifyWorkspacePanelVisualStateChanged = notifyWorkspacePanelVisualStateChanged;
        _notifyTitleBarTextChanged = notifyTitleBarTextChanged;
    }

    public Task ApplyAsync(WorkspaceShellState shellState)
    {
        WorkspaceTabPerf.Mark("ui.apply.enter", $"split={shellState.IsSplit} active={shellState.ActivePanel}");
        _layoutHost.SetShellState(shellState);
        _applyShellStateToUi(shellState);
        WorkspaceTabPerf.Mark("ui.apply.shell");
        _prepareShellStateForRestore(shellState);
        WorkspaceTabPerf.Mark("ui.apply.prepare");
        _activatePanel(shellState.ActivePanel);
        WorkspaceTabPerf.Mark("ui.apply.activate");
        if (shellState.IsSplit)
        {
            WorkspaceTabPerf.Mark("ui.apply.secondary-nav.deferred");
        }
        else
        {
            WorkspaceTabPerf.Mark("ui.apply.secondary-nav.skip");
        }

        int restoreVersion = ++_restoreVersion;
        _ = RestorePanelsInBackgroundAsync(shellState, restoreVersion);
        WorkspaceTabPerf.Mark("ui.apply.restore-scheduled", $"version={restoreVersion}");
        return Task.CompletedTask;
    }

    private async Task RestorePanelsInBackgroundAsync(WorkspaceShellState shellState, int restoreVersion)
    {
        await Task.Yield();
        WorkspaceTabPerf.Mark("ui.restore.enter", $"version={restoreVersion}");
        if (restoreVersion != _restoreVersion ||
            !ReferenceEquals(_layoutHost.ShellState, shellState))
        {
            WorkspaceTabPerf.Mark("ui.restore.skipped.primary", $"version={restoreVersion}");
            return;
        }

        await _restorePrimaryPanelStateAsync(shellState.Primary);
        WorkspaceTabPerf.Mark("ui.restore.primary", $"version={restoreVersion}");
        if (restoreVersion != _restoreVersion ||
            !ReferenceEquals(_layoutHost.ShellState, shellState))
        {
            WorkspaceTabPerf.Mark("ui.restore.skipped.secondary", $"version={restoreVersion}");
            return;
        }

        if (shellState.IsSplit)
        {
            await _restoreSecondaryPanelStateAsync(shellState.Secondary);
            WorkspaceTabPerf.Mark("ui.restore.secondary", $"version={restoreVersion}");
        }
        if (restoreVersion != _restoreVersion ||
            !ReferenceEquals(_layoutHost.ShellState, shellState))
        {
            WorkspaceTabPerf.Mark("ui.restore.skipped.finalize", $"version={restoreVersion}");
            return;
        }

        _activatePanel(shellState.ActivePanel);
        WorkspaceTabPerf.Mark("ui.restore.activate", $"version={restoreVersion}");
        if (shellState.IsSplit)
        {
            WorkspaceTabPerf.Mark("ui.restore.secondary-nav.deferred", $"version={restoreVersion}");
        }
        else
        {
            WorkspaceTabPerf.Mark("ui.restore.secondary-nav.skip", $"version={restoreVersion}");
        }
        WorkspaceTabPerf.Mark("ui.restore.finalized", $"version={restoreVersion}");
    }
}
