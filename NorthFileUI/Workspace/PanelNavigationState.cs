using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NorthFileUI.Workspace;

public sealed class PanelNavigationState
{
    public string CurrentPath { get; set; } = "shell:mycomputer";

    public string AddressText { get; set; } = string.Empty;

    public string QueryText { get; set; } = string.Empty;

    public Stack<string> BackStack { get; } = new();

    public Stack<string> ForwardStack { get; } = new();

    public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = new();

    public ObservableCollection<BreadcrumbItemViewModel> VisibleBreadcrumbs { get; } = new();

    public List<BreadcrumbItemViewModel> HiddenBreadcrumbItems { get; } = new();

    public bool BreadcrumbWidthsReady { get; set; }

    public int BreadcrumbVisibleStartIndex { get; set; } = -1;

    public void CopyFrom(PanelNavigationState source)
    {
        CurrentPath = source.CurrentPath;
        AddressText = source.AddressText;
        QueryText = source.QueryText;
        CopyStack(source.BackStack, BackStack);
        CopyStack(source.ForwardStack, ForwardStack);
        CopyCollection(source.Breadcrumbs, Breadcrumbs);
        CopyCollection(source.VisibleBreadcrumbs, VisibleBreadcrumbs);
        CopyList(source.HiddenBreadcrumbItems, HiddenBreadcrumbItems);
        BreadcrumbWidthsReady = source.BreadcrumbWidthsReady;
        BreadcrumbVisibleStartIndex = source.BreadcrumbVisibleStartIndex;
    }

    public PanelNavigationState Clone()
    {
        var clone = new PanelNavigationState();
        clone.CopyFrom(this);
        return clone;
    }

    private static void CopyStack(Stack<string> source, Stack<string> target)
    {
        target.Clear();
        foreach (string item in source.Reverse())
        {
            target.Push(item);
        }
    }

    private static void CopyCollection(
        IEnumerable<BreadcrumbItemViewModel> source,
        ObservableCollection<BreadcrumbItemViewModel> target)
    {
        target.Clear();
        foreach (BreadcrumbItemViewModel item in source)
        {
            target.Add(item.Clone());
        }
    }

    private static void CopyList(
        IEnumerable<BreadcrumbItemViewModel> source,
        List<BreadcrumbItemViewModel> target)
    {
        target.Clear();
        foreach (BreadcrumbItemViewModel item in source)
        {
            target.Add(item.Clone());
        }
    }
}
