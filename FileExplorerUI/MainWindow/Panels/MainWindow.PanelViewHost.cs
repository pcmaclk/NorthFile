using FileExplorerUI.Collections;
using FileExplorerUI.Interop;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private BatchObservableCollection<EntryViewModel> GetPanelEntries(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).DataSession.Entries;
        }

        private BatchObservableCollection<EntryViewModel> PrimaryEntries => GetPanelEntries(WorkspacePanelId.Primary);

        private ObservableCollection<GroupedEntryColumnViewModel> GetPrimaryGroupedEntryColumns()
        {
            return PrimaryPanelDataSession.GroupedEntryColumns;
        }

        private int GetPrimaryGroupedListRowsPerColumn()
        {
            return PrimaryPanelDataSession.GroupedColumnsCacheRowsPerColumn;
        }

        private void SetPrimaryGroupedListRowsPerColumn(int value)
        {
            PrimaryPanelDataSession.GroupedColumnsCacheRowsPerColumn = value;
        }

        private List<EntryViewModel> GetPrimaryPresentationSourceEntries()
        {
            return PrimaryPanelDataSession.PresentationSourceEntries;
        }

        private bool GetPrimaryPresentationSourceInitialized()
        {
            return PrimaryPanelDataSession.PresentationSourceInitialized;
        }

        private void SetPrimaryPresentationSourceInitialized(bool value)
        {
            PrimaryPanelDataSession.PresentationSourceInitialized = value;
        }

        private List<GroupedEntryColumnViewModel>? GetPrimaryGroupedColumnsProjectionCache()
        {
            return PrimaryPanelDataSession.GroupedColumnsProjectionCache;
        }

        private void SetPrimaryGroupedColumnsProjectionCache(List<GroupedEntryColumnViewModel>? value)
        {
            PrimaryPanelDataSession.GroupedColumnsProjectionCache = value;
        }

        private int GetPrimaryPresentationSourceVersion()
        {
            return PrimaryPanelDataSession.PresentationSourceVersion;
        }

        private void SetPrimaryPresentationSourceVersion(int value)
        {
            PrimaryPanelDataSession.PresentationSourceVersion = value;
        }

        private int IncrementPrimaryPresentationSourceVersion()
        {
            return ++PrimaryPanelDataSession.PresentationSourceVersion;
        }

        private int GetPrimaryGroupedColumnsCacheSourceVersion()
        {
            return PrimaryPanelDataSession.GroupedColumnsCacheSourceVersion;
        }

        private void SetPrimaryGroupedColumnsCacheSourceVersion(int value)
        {
            PrimaryPanelDataSession.GroupedColumnsCacheSourceVersion = value;
        }

        private EntrySortField GetPrimaryGroupedColumnsCacheSortField()
        {
            return PrimaryPanelDataSession.GroupedColumnsCacheSortField;
        }

        private void SetPrimaryGroupedColumnsCacheSortField(EntrySortField value)
        {
            PrimaryPanelDataSession.GroupedColumnsCacheSortField = value;
        }

        private SortDirection GetPrimaryGroupedColumnsCacheSortDirection()
        {
            return PrimaryPanelDataSession.GroupedColumnsCacheSortDirection;
        }

        private void SetPrimaryGroupedColumnsCacheSortDirection(SortDirection value)
        {
            PrimaryPanelDataSession.GroupedColumnsCacheSortDirection = value;
        }

        private EntryGroupField GetPrimaryGroupedColumnsCacheGroupField()
        {
            return PrimaryPanelDataSession.GroupedColumnsCacheGroupField;
        }

        private void SetPrimaryGroupedColumnsCacheGroupField(EntryGroupField value)
        {
            PrimaryPanelDataSession.GroupedColumnsCacheGroupField = value;
        }

        private IReadOnlyDictionary<string, DirectoryViewState> GetPrimaryDirectoryViewStates()
        {
            return PrimaryPanelState.DirectoryViewStates;
        }

        private Dictionary<string, DirectoryViewState> GetMutablePrimaryDirectoryViewStates()
        {
            return PrimaryPanelState.DirectoryViewStates;
        }

        private Dictionary<string, bool> GetPrimaryGroupExpansionStates()
        {
            return PrimaryPanelState.GroupExpansionStates;
        }

        private PanelViewState GetPanelState(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId);
        }

        private string GetPaneEntryPath(WorkspacePanelId panelId, EntryViewModel entry)
        {
            if (!string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return entry.FullPath;
            }

            return System.IO.Path.Combine(GetPanelCurrentPath(panelId), entry.Name);
        }

        private static DirectorySortMode GetPanelDirectorySortMode(WorkspacePanelId panelId)
        {
            _ = panelId;
            return DirectorySortMode.FolderFirstNameAsc;
        }

        private bool TryGetPaneLoadedEntryByPath(
            WorkspacePanelId panelId,
            string targetPath,
            [NotNullWhen(true)]
            out EntryViewModel? entry)
        {
            foreach (EntryViewModel candidate in GetPanelEntries(panelId))
            {
                if (!candidate.IsLoaded || candidate.IsGroupHeader)
                {
                    continue;
                }

                string candidatePath = GetPaneEntryPath(panelId, candidate);
                if (string.Equals(candidatePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    entry = candidate;
                    return true;
                }
            }

            entry = null;
            return false;
        }

        private bool TryGetSelectedLoadedEntryForPane(
            WorkspacePanelId panelId,
            [NotNullWhen(true)]
            out EntryViewModel? entry)
        {
            string? selectedPath = GetPanelSelectedEntryPath(panelId);
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                entry = null;
                return false;
            }

            return TryGetPaneLoadedEntryByPath(panelId, selectedPath, out entry);
        }

        private List<EntryViewModel> GetSelectedLoadedEntriesForPane(WorkspacePanelId panelId)
        {
            PanelViewState panelState = GetPanelState(panelId);
            var entries = new List<EntryViewModel>();
            if (panelState.SelectedEntryPaths.Count > 0)
            {
                foreach (EntryViewModel entry in GetPanelEntries(panelId))
                {
                    if (!entry.IsLoaded || entry.IsGroupHeader)
                    {
                        continue;
                    }

                    string entryPath = GetPaneEntryPath(panelId, entry);
                    if (panelState.SelectedEntryPaths.Contains(entryPath))
                    {
                        entries.Add(entry);
                    }
                }
            }

            if (entries.Count == 0 &&
                !string.IsNullOrWhiteSpace(panelState.SelectedEntryPath) &&
                TryGetPaneLoadedEntryByPath(panelId, panelState.SelectedEntryPath, out EntryViewModel? singleEntry))
            {
                entries.Add(singleEntry);
            }

            return entries;
        }

        private int GetSelectedLoadedEntryCountForPane(WorkspacePanelId panelId)
        {
            return GetSelectedLoadedEntriesForPane(panelId).Count;
        }

        private string? GetPanelSelectedEntryPath(WorkspacePanelId panelId)
        {
            return _workspaceLayoutHost.GetPanelState(panelId).SelectedEntryPath;
        }

        private void SetPanelSelectedEntryPath(WorkspacePanelId panelId, string? path)
        {
            SetPanelSingleSelectionPath(panelId, path, path);
        }

        private void SetPanelSingleSelectionPath(WorkspacePanelId panelId, string? selectedPath, string? focusedPath)
        {
            PanelViewState panelState = GetPanelState(panelId);
            panelState.SelectedEntryPath = selectedPath;
            panelState.FocusedEntryPath = focusedPath;
            panelState.SelectedEntryPaths.Clear();
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                panelState.SelectedEntryPaths.Add(selectedPath);
            }

            if (panelId == WorkspacePanelId.Primary)
            {
                _selectedEntryPath = selectedPath;
                _focusedEntryPath = focusedPath;
            }
        }

        private void TogglePanelSelectionPath(WorkspacePanelId panelId, string path)
        {
            PanelViewState panelState = GetPanelState(panelId);
            if (!panelState.SelectedEntryPaths.Remove(path))
            {
                panelState.SelectedEntryPaths.Add(path);
                panelState.SelectedEntryPath = path;
            }
            else if (string.Equals(panelState.SelectedEntryPath, path, StringComparison.OrdinalIgnoreCase))
            {
                panelState.SelectedEntryPath = panelState.SelectedEntryPaths.FirstOrDefault();
            }

            panelState.FocusedEntryPath = path;
            if (panelId == WorkspacePanelId.Primary)
            {
                _selectedEntryPath = panelState.SelectedEntryPath;
                _focusedEntryPath = panelState.FocusedEntryPath;
            }
        }

        private void SelectPanelEntryRange(
            WorkspacePanelId panelId,
            IReadOnlyList<EntryViewModel> orderedEntries,
            EntryViewModel targetEntry)
        {
            if (targetEntry.IsGroupHeader)
            {
                return;
            }

            PanelViewState panelState = GetPanelState(panelId);
            string targetPath = GetPaneEntryPath(panelId, targetEntry);
            string anchorPath = !string.IsNullOrWhiteSpace(panelState.FocusedEntryPath)
                ? panelState.FocusedEntryPath
                : panelState.SelectedEntryPath ?? targetPath;

            int anchorIndex = FindEntryIndexByPath(orderedEntries, anchorPath);
            int targetIndex = FindEntryIndexByPath(orderedEntries, targetPath);
            if (anchorIndex < 0 || targetIndex < 0)
            {
                SetPanelSingleSelectionPath(panelId, targetPath, targetPath);
                return;
            }

            int start = Math.Min(anchorIndex, targetIndex);
            int end = Math.Max(anchorIndex, targetIndex);
            panelState.SelectedEntryPaths.Clear();
            for (int i = start; i <= end; i++)
            {
                string path = GetPaneEntryPath(panelId, orderedEntries[i]);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    panelState.SelectedEntryPaths.Add(path);
                }
            }

            panelState.SelectedEntryPath = targetPath;
            panelState.FocusedEntryPath = targetPath;
            if (panelId == WorkspacePanelId.Primary)
            {
                _selectedEntryPath = panelState.SelectedEntryPath;
                _focusedEntryPath = panelState.FocusedEntryPath;
            }
        }

        private static int FindEntryIndexByPath(IReadOnlyList<EntryViewModel> entries, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return -1;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].FullPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private void SelectAllLoadedEntriesForPane(WorkspacePanelId panelId)
        {
            PanelViewState panelState = GetPanelState(panelId);
            panelState.SelectedEntryPaths.Clear();
            foreach (EntryViewModel entry in GetPanelEntries(panelId))
            {
                if (entry.IsLoaded && !entry.IsGroupHeader)
                {
                    panelState.SelectedEntryPaths.Add(GetPaneEntryPath(panelId, entry));
                }
            }

            if (panelState.SelectedEntryPaths.Count == 0)
            {
                panelState.SelectedEntryPath = null;
                panelState.FocusedEntryPath = null;
                if (panelId == WorkspacePanelId.Primary)
                {
                    _selectedEntryPath = null;
                    _focusedEntryPath = null;
                }
                return;
            }

            panelState.SelectedEntryPath ??= panelState.SelectedEntryPaths.FirstOrDefault();
            panelState.FocusedEntryPath ??= panelState.SelectedEntryPath;
            if (panelId == WorkspacePanelId.Primary)
            {
                _selectedEntryPath = panelState.SelectedEntryPath;
                _focusedEntryPath = panelState.FocusedEntryPath;
            }
        }

        private string? GetPanelFocusedEntryPath(WorkspacePanelId panelId)
        {
            return GetPanelState(panelId).FocusedEntryPath;
        }

        private string GetPanelCurrentPath(WorkspacePanelId panelId)
        {
            return GetPanelState(panelId).CurrentPath;
        }

        private EntrySortField GetPanelSortField(WorkspacePanelId panelId)
        {
            return GetPanelState(panelId).SortField;
        }

        private EntryViewMode GetPanelViewMode(WorkspacePanelId panelId)
        {
            return GetPanelState(panelId).ViewMode;
        }

        private SortDirection GetPanelSortDirection(WorkspacePanelId panelId)
        {
            return GetPanelState(panelId).SortDirection;
        }

        private EntryGroupField GetPanelGroupField(WorkspacePanelId panelId)
        {
            return GetPanelState(panelId).GroupField;
        }

        private void SetPanelSortState(
            WorkspacePanelId panelId,
            EntrySortField sortField,
            SortDirection sortDirection)
        {
            PanelViewState panel = GetPanelState(panelId);
            panel.SortField = sortField;
            panel.SortDirection = sortDirection;
        }

        private void SetPanelCurrentPath(WorkspacePanelId panelId, string path)
        {
            PanelViewState panel = GetPanelState(panelId);
            panel.CurrentPath = path;
            panel.AddressText = GetDisplayPathText(path);
            NotifyTitleBarTextChanged();
            UpdateSidebarSelectionForPanelPathChange(panelId);

            if (panelId == WorkspacePanelId.Primary &&
                PathTextBox.Text != GetDisplayPathText(path))
            {
                PathTextBox.Text = GetDisplayPathText(path);
            }
        }

        private bool IsPanelDataLoadedForCurrentNavigation(WorkspacePanelId panelId)
        {
            PanelViewState panel = GetPanelState(panelId);
            PanelDataSession session = panel.DataSession;
            string currentPath = string.IsNullOrWhiteSpace(panel.CurrentPath)
                ? ShellMyComputerPath
                : panel.CurrentPath;
            string currentQuery = panel.QueryText?.Trim() ?? string.Empty;

            return string.Equals(session.LoadedPath, currentPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(session.LoadedQueryText, currentQuery, StringComparison.Ordinal) &&
                IsPanelEntryCollectionConsistentWithCurrentNavigation(panelId, currentPath);
        }

        private bool CanReusePanelDataForRestore(WorkspacePanelId panelId, PanelViewState panelState)
        {
            if (!IsPanelDataLoadedForCurrentNavigation(panelId))
            {
                return false;
            }

            PanelDataSession session = panelState.DataSession;
            if (session.IsLoading)
            {
                return false;
            }

            string currentPath = string.IsNullOrWhiteSpace(panelState.CurrentPath)
                ? ShellMyComputerPath
                : panelState.CurrentPath;

            return string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(session.LoadedPath);
        }

        private bool IsPanelEntryCollectionConsistentWithCurrentNavigation(WorkspacePanelId panelId, string currentPath)
        {
            foreach (EntryViewModel entry in GetPanelEntries(panelId))
            {
                if (!entry.IsLoaded || entry.IsGroupHeader)
                {
                    continue;
                }

                if (!DoesEntryBelongToPanelPath(currentPath, entry))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool DoesEntryBelongToPanelPath(string currentPath, EntryViewModel entry)
        {
            if (string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return false;
            }

            if (string.Equals(currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                string? root = System.IO.Path.GetPathRoot(entry.FullPath);
                return !string.IsNullOrWhiteSpace(root) &&
                    string.Equals(
                        root.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                        entry.FullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase);
            }

            string? parent = System.IO.Path.GetDirectoryName(
                entry.FullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
            return !string.IsNullOrWhiteSpace(parent) &&
                string.Equals(
                    parent.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                    currentPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
        }

        private void MarkPanelDataLoadedForCurrentNavigation(WorkspacePanelId panelId)
        {
            PanelViewState panel = GetPanelState(panelId);
            PanelDataSession session = panel.DataSession;
            session.LoadedPath = string.IsNullOrWhiteSpace(panel.CurrentPath)
                ? ShellMyComputerPath
                : panel.CurrentPath;
            session.LoadedQueryText = panel.QueryText?.Trim() ?? string.Empty;
        }

        private void InvalidatePanelDataLoadedForCurrentNavigation(WorkspacePanelId panelId)
        {
            PanelDataSession session = GetPanelState(panelId).DataSession;
            session.LoadedPath = string.Empty;
            session.LoadedQueryText = string.Empty;
        }

        private void ClearPanelEntriesIfNavigationIsStale(WorkspacePanelId panelId)
        {
            if (IsPanelDataLoadedForCurrentNavigation(panelId))
            {
                return;
            }

            PanelDataSession session = GetPanelState(panelId).DataSession;
            if (session.Entries.Count == 0 &&
                session.GroupedEntryColumns.Count == 0 &&
                session.PresentationSourceEntries.Count == 0 &&
                !session.PresentationSourceInitialized &&
                session.GroupedColumnsProjectionCache is null &&
                session.ActiveEntryResultSet is null &&
                session.NextCursor == 0 &&
                !session.HasMore &&
                session.TotalEntries == 0)
            {
                return;
            }

            session.Entries.Clear();
            session.GroupedEntryColumns.Clear();
            session.PresentationSourceEntries.Clear();
            session.PresentationSourceInitialized = false;
            session.GroupedColumnsProjectionCache = null;
            session.ActiveEntryResultSet = null;
            session.NextCursor = 0;
            session.HasMore = false;
            session.TotalEntries = 0;

            if (panelId == WorkspacePanelId.Primary)
            {
                InvalidateEntriesLayouts();
                NotifyPresentationModeChanged();
            }
            else
            {
                RaiseSecondaryPaneDataPropertiesChanged();
            }
        }

        private string GetPanelAddressText(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Primary
                ? PathTextBox.Text ?? string.Empty
                : GetPanelState(panelId).AddressText;
        }

        private void SetPanelAddressText(WorkspacePanelId panelId, string? addressText, bool syncEditor = false)
        {
            string normalized = addressText ?? string.Empty;
            if (panelId == WorkspacePanelId.Primary)
            {
                GetPanelState(panelId).AddressText = normalized;
                if (syncEditor && !string.Equals(PathTextBox.Text, normalized, StringComparison.Ordinal))
                {
                    PathTextBox.Text = normalized;
                }

                return;
            }

            PanelViewState panel = GetPanelState(panelId);
            if (string.Equals(panel.AddressText, normalized, StringComparison.Ordinal))
            {
                return;
            }

            panel.AddressText = normalized;
            RaisePaneAddressPropertiesChanged(panelId);
        }

        private void SetPrimaryPanelNavigationState(
            string path,
            string? queryText = null,
            string? addressText = null,
            bool syncEditors = true)
        {
            string normalizedPath = string.IsNullOrWhiteSpace(path)
                ? ShellMyComputerPath
                : path;
            string normalizedAddressText = string.IsNullOrWhiteSpace(addressText)
                ? GetDisplayPathText(normalizedPath)
                : addressText;

            SetPanelCurrentPath(WorkspacePanelId.Primary, normalizedPath);
            SetPanelQueryText(WorkspacePanelId.Primary, queryText ?? string.Empty, syncEditors);
            SetPanelAddressText(WorkspacePanelId.Primary, normalizedAddressText, syncEditors);
        }

        private void SetPrimaryPanelPresentationState(
            EntryViewMode viewMode,
            EntrySortField sortField,
            SortDirection sortDirection,
            EntryGroupField groupField)
        {
            PanelViewState panel = GetPanelState(WorkspacePanelId.Primary);
            panel.ViewMode = viewMode;
            panel.SortField = sortField;
            panel.SortDirection = sortDirection;
            panel.GroupField = groupField;
        }

        private void SetPanelGroupField(WorkspacePanelId panelId, EntryGroupField groupField)
        {
            GetPanelState(panelId).GroupField = groupField;
        }

        private void SetPanelFocusedEntryPath(WorkspacePanelId panelId, string? path)
        {
            _workspaceLayoutHost.GetPanelState(panelId).FocusedEntryPath = path;
        }

        private string GetPanelQueryText(WorkspacePanelId panelId)
        {
            return GetPanelState(panelId).QueryText;
        }

        private void SetPanelQueryText(WorkspacePanelId panelId, string? queryText, bool syncEditor = false)
        {
            string normalized = queryText?.Trim() ?? string.Empty;
            PanelViewState panel = GetPanelState(panelId);
            if (string.Equals(panel.QueryText, normalized, StringComparison.Ordinal))
            {
                if (panelId == WorkspacePanelId.Primary && syncEditor && SearchTextBox.Text != normalized)
                {
                    SearchTextBox.Text = normalized;
                }

                return;
            }

            panel.QueryText = normalized;
            if (panelId == WorkspacePanelId.Primary)
            {
                if (syncEditor && SearchTextBox.Text != normalized)
                {
                    SearchTextBox.Text = normalized;
                }

                return;
            }

            RaisePaneSearchPropertiesChanged(panelId);
        }

        private ScrollViewer GetPanelDetailsScrollViewer(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary
                ? SecondaryEntriesScrollViewer
                : DetailsEntriesScrollViewer;
        }

        private ScrollViewer? GetPanelGroupedScrollViewer(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary
                ? null
                : GroupedEntriesScrollViewer;
        }

        private ScrollViewer GetPanelActiveScrollViewer(WorkspacePanelId panelId, EntryViewMode viewMode)
        {
            if (panelId == WorkspacePanelId.Secondary || viewMode == EntryViewMode.Details)
            {
                return GetPanelDetailsScrollViewer(panelId);
            }

            return GroupedEntriesScrollViewer;
        }

        private void FocusPanelEntries(WorkspacePanelId panelId)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                FocusSecondaryEntriesList();
                return;
            }

            FocusEntriesList();
        }

        private void ClearPanelSelection(WorkspacePanelId panelId, bool clearAnchor)
        {
            PanelViewState panelState = GetPanelState(panelId);
            panelState.SelectedEntryPath = null;
            panelState.SelectedEntryPaths.Clear();
            if (clearAnchor)
            {
                panelState.FocusedEntryPath = null;
            }

            if (panelId == WorkspacePanelId.Primary)
            {
                _selectedEntryPath = null;
                if (clearAnchor)
                {
                    _focusedEntryPath = null;
                }
            }

            SyncActivePanelPresentationState();
            if (panelId == WorkspacePanelId.Secondary)
            {
                UpdateSecondaryEntrySelectionVisuals();
            }
            else
            {
                UpdateEntrySelectionVisuals();
            }

            UpdateFileCommandStates();
        }

        private bool IsPanelDetailsMode(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary ||
                _workspaceLayoutHost.GetPanelState(panelId).ViewMode == EntryViewMode.Details;
        }
    }
}
