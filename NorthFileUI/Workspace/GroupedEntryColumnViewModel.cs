using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace NorthFileUI.Workspace;

public sealed class GroupedEntryColumnViewModel
{
    public string GroupKey { get; set; } = string.Empty;

    public string HeaderText { get; set; } = string.Empty;

    public int ItemCount { get; set; }

    public Visibility HeaderVisibility { get; set; } = Visibility.Visible;

    public string CountText => ItemCount > 0 ? $"({ItemCount})" : string.Empty;

    public IReadOnlyList<GroupedEntryItemColumnViewModel> ItemColumns { get; set; } = [];
}
