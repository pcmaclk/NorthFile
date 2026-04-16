using FileExplorerUI.Workspace;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FileExplorerUI.Settings;

public static class WorkspaceSessionSnapshot
{
    private const int SnapshotVersion = 1;

    public static string Serialize(WorkspaceSession session)
    {
        var snapshot = new SnapshotDto
        {
            Version = SnapshotVersion,
            ActiveTabIndex = Math.Max(0, session.Tabs.IndexOf(session.ActiveTab)),
            Tabs = session.Tabs.Select(FromTab).ToList()
        };

        return JsonSerializer.Serialize(snapshot);
    }

    public static bool TryRestore(string? json, string shellRootPath, out List<WorkspaceTabState> tabs, out int activeTabIndex)
    {
        tabs = [];
        activeTabIndex = 0;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        SnapshotDto? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<SnapshotDto>(json);
        }
        catch
        {
            return false;
        }

        if (snapshot?.Tabs is null || snapshot.Tabs.Count == 0)
        {
            return false;
        }

        foreach (TabDto tabDto in snapshot.Tabs)
        {
            tabs.Add(ToTab(tabDto, shellRootPath));
        }

        activeTabIndex = Math.Clamp(snapshot.ActiveTabIndex, 0, tabs.Count - 1);
        return true;
    }

    private static TabDto FromTab(WorkspaceTabState tab)
    {
        return new TabDto
        {
            CustomTitle = tab.CustomTitle,
            Shell = FromShell(tab.ShellState)
        };
    }

    private static ShellDto FromShell(WorkspaceShellState shell)
    {
        return new ShellDto
        {
            LayoutMode = shell.LayoutMode,
            ActivePanel = shell.ActivePanel,
            Primary = FromPanel(shell.Primary),
            Secondary = FromPanel(shell.Secondary)
        };
    }

    private static PanelDto FromPanel(PanelViewState panel)
    {
        return new PanelDto
        {
            CurrentPath = panel.CurrentPath,
            AddressText = panel.AddressText,
            QueryText = panel.QueryText,
            BackStack = panel.Navigation.BackStack.ToList(),
            ForwardStack = panel.Navigation.ForwardStack.ToList(),
            SelectedEntryPath = panel.SelectedEntryPath,
            FocusedEntryPath = panel.FocusedEntryPath,
            ViewMode = panel.ViewMode,
            SortField = panel.SortField,
            SortDirection = panel.SortDirection,
            GroupField = panel.GroupField,
            NameColumnWidth = panel.NameColumnWidth,
            TypeColumnWidth = panel.TypeColumnWidth,
            SizeColumnWidth = panel.SizeColumnWidth,
            ModifiedColumnWidth = panel.ModifiedColumnWidth,
            DetailsContentWidth = panel.DetailsContentWidth,
            DetailsRowWidth = panel.DetailsRowWidth
        };
    }

    private static WorkspaceTabState ToTab(TabDto dto, string shellRootPath)
    {
        var tab = new WorkspaceTabState
        {
            CustomTitle = dto.CustomTitle ?? string.Empty
        };
        ApplyShell(tab.ShellState, dto.Shell ?? new ShellDto(), shellRootPath);
        return tab;
    }

    private static void ApplyShell(WorkspaceShellState shell, ShellDto dto, string shellRootPath)
    {
        shell.LayoutMode = dto.LayoutMode;
        shell.ActivePanel = shell.IsSplit ? dto.ActivePanel : WorkspacePanelId.Primary;
        ApplyPanel(shell.Primary, dto.Primary ?? new PanelDto(), shellRootPath);
        ApplyPanel(shell.Secondary, dto.Secondary ?? new PanelDto(), shellRootPath);
    }

    private static void ApplyPanel(PanelViewState panel, PanelDto dto, string shellRootPath)
    {
        string path = NormalizePath(dto.CurrentPath, shellRootPath);
        panel.CurrentPath = path;
        panel.AddressText = string.IsNullOrWhiteSpace(dto.AddressText) ? path : dto.AddressText;
        panel.QueryText = dto.QueryText ?? string.Empty;
        CopyStack(dto.BackStack, panel.Navigation.BackStack, shellRootPath);
        CopyStack(dto.ForwardStack, panel.Navigation.ForwardStack, shellRootPath);
        panel.SelectedEntryPath = dto.SelectedEntryPath;
        panel.FocusedEntryPath = dto.FocusedEntryPath;
        panel.ViewMode = dto.ViewMode;
        panel.SortField = dto.SortField;
        panel.SortDirection = dto.SortDirection;
        panel.GroupField = dto.GroupField;
        panel.NameColumnWidth = ClampColumnWidth(dto.NameColumnWidth, 80, 800, 220);
        panel.TypeColumnWidth = ClampColumnWidth(dto.TypeColumnWidth, 60, 500, 150);
        panel.SizeColumnWidth = ClampColumnWidth(dto.SizeColumnWidth, 60, 400, 120);
        panel.ModifiedColumnWidth = ClampColumnWidth(dto.ModifiedColumnWidth, 80, 500, 180);
        panel.DetailsContentWidth = ClampColumnWidth(dto.DetailsContentWidth, 320, 2400, 694);
        panel.DetailsRowWidth = ClampColumnWidth(dto.DetailsRowWidth, 340, 2600, 714);
    }

    private static void CopyStack(IEnumerable<string>? source, Stack<string> target, string shellRootPath)
    {
        target.Clear();
        if (source is null)
        {
            return;
        }

        foreach (string path in source.Reverse())
        {
            target.Push(NormalizePath(path, shellRootPath));
        }
    }

    private static string NormalizePath(string? path, string shellRootPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return shellRootPath;
        }

        string trimmed = path.Trim();
        if (string.Equals(trimmed, shellRootPath, StringComparison.OrdinalIgnoreCase))
        {
            return shellRootPath;
        }

        return Directory.Exists(trimmed) ? trimmed : shellRootPath;
    }

    private static double ClampColumnWidth(double value, double min, double max, double fallback)
    {
        return double.IsNaN(value) || double.IsInfinity(value)
            ? fallback
            : Math.Clamp(value, min, max);
    }

    private sealed class SnapshotDto
    {
        public int Version { get; set; }
        public int ActiveTabIndex { get; set; }
        public List<TabDto> Tabs { get; set; } = [];
    }

    private sealed class TabDto
    {
        public string CustomTitle { get; set; } = string.Empty;
        public ShellDto? Shell { get; set; }
    }

    private sealed class ShellDto
    {
        public WorkspaceLayoutMode LayoutMode { get; set; } = WorkspaceLayoutMode.Single;
        public WorkspacePanelId ActivePanel { get; set; } = WorkspacePanelId.Primary;
        public PanelDto? Primary { get; set; }
        public PanelDto? Secondary { get; set; }
    }

    private sealed class PanelDto
    {
        public string CurrentPath { get; set; } = "shell:mycomputer";
        public string AddressText { get; set; } = string.Empty;
        public string QueryText { get; set; } = string.Empty;
        public List<string> BackStack { get; set; } = [];
        public List<string> ForwardStack { get; set; } = [];
        public string? SelectedEntryPath { get; set; }
        public string? FocusedEntryPath { get; set; }
        public EntryViewMode ViewMode { get; set; } = EntryViewMode.Details;
        public EntrySortField SortField { get; set; } = EntrySortField.Name;
        public SortDirection SortDirection { get; set; } = SortDirection.Ascending;
        public EntryGroupField GroupField { get; set; } = EntryGroupField.None;
        public double NameColumnWidth { get; set; } = 220;
        public double TypeColumnWidth { get; set; } = 150;
        public double SizeColumnWidth { get; set; } = 120;
        public double ModifiedColumnWidth { get; set; } = 180;
        public double DetailsContentWidth { get; set; } = 694;
        public double DetailsRowWidth { get; set; } = 714;
    }
}
