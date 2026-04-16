using System.Collections.Generic;
using System.IO;

namespace FileExplorerUI.Workspace;

public sealed class WorkspaceSession
{
    private readonly string _shellRootPath;

    public WorkspaceSession(WorkspaceTabState initialTab, string shellRootPath)
    {
        _shellRootPath = shellRootPath;
        TabManager = new WorkspaceTabManager(initialTab);
        TabRouter = new WorkspaceTabRouter(TabManager, shellRootPath);
        LayoutHost = new WorkspaceLayoutHost(CurrentShellState);
    }

    public WorkspaceTabManager TabManager { get; }

    public WorkspaceTabRouter TabRouter { get; }

    public WorkspaceLayoutHost LayoutHost { get; }

    public System.Collections.ObjectModel.ObservableCollection<WorkspaceTabState> Tabs => TabManager.Tabs;

    public int TabCount => TabManager.Count;

    public WorkspaceTabState ActiveTab => TabManager.ActiveTab;

    public WorkspaceShellState CurrentShellState => ActiveTab.ShellState;

    public bool IsActiveTab(WorkspaceTabState tab) => TabManager.IsActive(tab);

    public WorkspaceTabPresentation GetActiveTabPresentation(string shellRootTitle, string appTitle)
    {
        return BuildPresentation(ActiveTab, shellRootTitle, appTitle);
    }

    public IReadOnlyList<WorkspaceTabPresentation> BuildTabPresentations(string shellRootTitle, string appTitle)
    {
        var presentations = new List<WorkspaceTabPresentation>(Tabs.Count);
        int activeIndex = Tabs.IndexOf(ActiveTab);
        foreach (WorkspaceTabState tab in Tabs)
        {
            presentations.Add(BuildPresentation(
                tab,
                shellRootTitle,
                appTitle,
                showTrailingSeparator: Tabs.IndexOf(tab) != activeIndex - 1));
        }

        return presentations;
    }

    public void RestoreTabs(IReadOnlyList<WorkspaceTabState> tabs, int activeIndex)
    {
        TabManager.ReplaceAll(tabs, activeIndex);
        LayoutHost.SetShellState(CurrentShellState);
    }

    private WorkspaceTabPresentation BuildPresentation(
        WorkspaceTabState tab,
        string shellRootTitle,
        string appTitle,
        bool showTrailingSeparator = true)
    {
        string primaryPath = tab.ShellState.Primary.CurrentPath;
        string title = !string.IsNullOrWhiteSpace(tab.CustomTitle)
            ? tab.CustomTitle
            : BuildShellTitle(tab.ShellState, shellRootTitle, appTitle);

        return new WorkspaceTabPresentation
        {
            Tab = tab,
            Title = title,
            Glyph = string.Equals(primaryPath, _shellRootPath, System.StringComparison.OrdinalIgnoreCase)
                ? "\uE7F4"
                : "\uE8B7",
            IsActive = IsActiveTab(tab),
            CanClose = TabCount > 1,
            ShowTrailingSeparator = !IsActiveTab(tab) && showTrailingSeparator
        };
    }

    private string BuildShellTitle(WorkspaceShellState shellState, string shellRootTitle, string appTitle)
    {
        if (!shellState.IsSplit)
        {
            return BuildPanelTitle(shellState.Primary.CurrentPath, shellRootTitle, appTitle);
        }

        return string.Join(
            " | ",
            BuildPanelTitle(shellState.Primary.CurrentPath, shellRootTitle, appTitle),
            BuildPanelTitle(shellState.Secondary.CurrentPath, shellRootTitle, appTitle));
    }

    private string BuildPanelTitle(string path, string shellRootTitle, string appTitle)
    {
        if (string.Equals(path, _shellRootPath, System.StringComparison.OrdinalIgnoreCase))
        {
            return shellRootTitle;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return appTitle;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return path;
        }

        string name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }
}
