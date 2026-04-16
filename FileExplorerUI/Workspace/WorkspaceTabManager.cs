using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FileExplorerUI.Workspace;

public sealed class WorkspaceTabManager
{
    public WorkspaceTabManager(WorkspaceTabState initialTab)
    {
        Tabs.Add(initialTab);
        ActiveTab = initialTab;
    }

    public ObservableCollection<WorkspaceTabState> Tabs { get; } = new();

    public WorkspaceTabState ActiveTab { get; private set; }

    public event EventHandler? TabsChanged;

    public event EventHandler<WorkspaceTabChangedEventArgs>? ActiveTabChanged;

    public int Count => Tabs.Count;

    public bool IsActive(WorkspaceTabState tab) => ReferenceEquals(tab, ActiveTab);

    public void Add(WorkspaceTabState tab)
    {
        Tabs.Add(tab);
        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddAndActivate(WorkspaceTabState tab)
    {
        Tabs.Add(tab);
        ActiveTab = tab;
        WorkspaceTabPerf.Mark("manager.add-and-activate", $"tabs={Tabs.Count}");
        TabsChanged?.Invoke(this, EventArgs.Empty);
        WorkspaceTabPerf.Mark("manager.tabs-changed.returned", $"tabs={Tabs.Count}");
    }

    public void ReplaceAll(IReadOnlyList<WorkspaceTabState> tabs, int activeIndex)
    {
        Tabs.Clear();
        foreach (WorkspaceTabState tab in tabs)
        {
            Tabs.Add(tab);
        }

        if (Tabs.Count == 0)
        {
            var fallback = new WorkspaceTabState();
            Tabs.Add(fallback);
            ActiveTab = fallback;
        }
        else
        {
            ActiveTab = Tabs[Math.Clamp(activeIndex, 0, Tabs.Count - 1)];
        }

        WorkspaceTabPerf.Mark("manager.replace-all", $"tabs={Tabs.Count} activeIndex={activeIndex}");
        TabsChanged?.Invoke(this, EventArgs.Empty);
        ActiveTabChanged?.Invoke(this, new WorkspaceTabChangedEventArgs(ActiveTab, ActiveTab));
    }

    public bool Activate(WorkspaceTabState tab)
    {
        if (ReferenceEquals(tab, ActiveTab))
        {
            return false;
        }

        WorkspaceTabState previousTab = ActiveTab;
        ActiveTab = tab;
        WorkspaceTabPerf.Mark("manager.activate", $"tabs={Tabs.Count}");
        ActiveTabChanged?.Invoke(this, new WorkspaceTabChangedEventArgs(previousTab, tab));
        WorkspaceTabPerf.Mark("manager.active-tab-changed.returned", $"tabs={Tabs.Count}");
        return true;
    }

    public WorkspaceTabState? GetAdjacentActive(int delta)
    {
        if (Tabs.Count <= 1)
        {
            return null;
        }

        int currentIndex = Tabs.IndexOf(ActiveTab);
        if (currentIndex < 0)
        {
            return null;
        }

        int nextIndex = (currentIndex + delta + Tabs.Count) % Tabs.Count;
        return Tabs[nextIndex];
    }

    public bool TryClose(WorkspaceTabState tab, out bool activeTabChanged)
    {
        activeTabChanged = false;

        if (Tabs.Count <= 1)
        {
            return false;
        }

        int closedIndex = Tabs.IndexOf(tab);
        if (closedIndex < 0)
        {
            return false;
        }

        bool wasActive = ReferenceEquals(tab, ActiveTab);
        WorkspaceTabState? replacement = null;
        if (wasActive)
        {
            int replacementIndex = closedIndex < Tabs.Count - 1
                ? closedIndex + 1
                : closedIndex - 1;
            replacement = replacementIndex >= 0 ? Tabs[replacementIndex] : null;
        }

        if (wasActive && replacement is not null)
        {
            ActiveTab = replacement;
            activeTabChanged = true;
        }

        Tabs.RemoveAt(closedIndex);
        WorkspaceTabPerf.Mark("manager.close", $"tabs={Tabs.Count} activeChanged={activeTabChanged}");
        TabsChanged?.Invoke(this, EventArgs.Empty);
        WorkspaceTabPerf.Mark("manager.tabs-changed.returned", $"tabs={Tabs.Count}");
        return true;
    }
}
