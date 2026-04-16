using FileExplorerUI.Collections;
using FileExplorerUI.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace FileExplorerUI.Workspace;

public sealed class PanelDataSession
{
    public BatchObservableCollection<EntryViewModel> Entries { get; } = new();

    public ObservableCollection<GroupedEntryColumnViewModel> GroupedEntryColumns { get; } = new();

    public List<EntryViewModel> PresentationSourceEntries { get; } = [];

    public bool PresentationSourceInitialized { get; set; }

    public List<GroupedEntryColumnViewModel>? GroupedColumnsProjectionCache { get; set; }

    public int PresentationSourceVersion { get; set; }

    public int GroupedColumnsCacheSourceVersion { get; set; } = -1;

    public EntrySortField GroupedColumnsCacheSortField { get; set; } = EntrySortField.Name;

    public SortDirection GroupedColumnsCacheSortDirection { get; set; } = SortDirection.Ascending;

    public EntryGroupField GroupedColumnsCacheGroupField { get; set; } = EntryGroupField.None;

    public int GroupedColumnsCacheRowsPerColumn { get; set; } = -1;

    public ulong NextCursor { get; set; }

    public bool HasMore { get; set; }

    public bool IsLoading { get; set; }

    public uint LastFetchMs { get; set; }

    public uint TotalEntries { get; set; }

    public string? LoadedPath { get; set; }

    public string LoadedQueryText { get; set; } = string.Empty;

    public IEntryResultSet? ActiveEntryResultSet { get; set; }

    public CancellationTokenSource? NavigationLoadCts { get; set; }

    public CancellationTokenSource? DirectoryLoadCts { get; set; }

    public CancellationTokenSource? MetadataPrefetchCts { get; set; }

    public bool GroupedColumnsRefreshQueued { get; set; }

    public CancellationTokenSource? GroupedColumnsResizeDebounceCts { get; set; }

    public int GroupedColumnsRefreshVersion { get; set; }

    public long LastGroupedColumnsRefreshAppliedStamp { get; set; }

    public double LastGroupedViewportHeight { get; set; } = double.NaN;

    public long LastGroupedColumnsLiveResizeRefreshTick { get; set; }

    public double LastDetailsVerticalDelta { get; set; }

    public int LastDetailsViewportStartIndex { get; set; } = -1;

    public int LastDetailsViewportIndexDelta { get; set; }

    public long LastDetailsScrollInteractionTick { get; set; }

    public int MetadataViewportRequestVersion { get; set; }

    public long DirectorySnapshotVersion { get; set; }
}
