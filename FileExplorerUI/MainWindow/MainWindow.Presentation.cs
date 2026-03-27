using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FileExplorerUI.Controls;
using FileExplorerUI.Workspace;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private int _groupedColumnsCacheRowsPerColumn = -1;

        private PanelViewState GetActivePanelState()
        {
            return _workspaceLayoutHost.GetActivePanelState();
        }

        private void SyncActivePanelPresentationState()
        {
            PanelViewState activePanel = GetActivePanelState();
            activePanel.ViewMode = _currentViewMode;
            activePanel.SortField = _currentSortField;
            activePanel.SortDirection = _currentSortDirection;
            activePanel.GroupField = _currentGroupField;
            activePanel.QueryText = _currentQuery;
            activePanel.CurrentPath = _currentPath;
            activePanel.SelectedEntryPath = _selectedEntryPath;
        }

        private bool UsesColumnsListPresentation()
        {
            return _currentViewMode == EntryViewMode.List;
        }

        private FrameworkElement GetVisibleEntriesRoot()
        {
            return _currentViewMode == EntryViewMode.Details
                ? DetailsEntriesScrollViewer
                : GroupedEntriesScrollViewer;
        }

        private void UpdateEntriesContextOverlayTargets()
        {
            UIElement overlayTarget = GetVisibleEntriesRoot();
            FileEntriesContextFlyout.OverlayInputPassThroughElement = overlayTarget;
            FolderEntriesContextFlyout.OverlayInputPassThroughElement = overlayTarget;
            BackgroundEntriesContextFlyout.OverlayInputPassThroughElement = overlayTarget;
        }

        private IEntriesViewHost? GetVisibleEntriesViewHost()
        {
            return _currentViewMode == EntryViewMode.Details
                ? _detailsEntriesViewHost
                : _groupedEntriesViewHost;
        }

        private bool NeedsEntriesHorizontalScroll()
        {
            if (_currentViewMode != EntryViewMode.Details)
            {
                return true;
            }

            double viewportWidth = DetailsEntriesScrollViewer.ViewportWidth > 0
                ? DetailsEntriesScrollViewer.ViewportWidth
                : DetailsEntriesScrollViewer.ActualWidth;
            if (viewportWidth <= 0)
            {
                return true;
            }

            return DetailsRowWidth > viewportWidth - 1;
        }

        public IReadOnlyList<GroupedEntryColumnViewModel> GroupedEntryColumns => _groupedEntryColumns;

        private void NotifyPresentationModeChanged()
        {
            _groupedListRowsPerColumn = -1;
            ApplyEntryItemMetricsPreset();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryContainerWidth)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesListVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupedColumnsVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsHeaderVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameHeaderMargin)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsNameCellMargin)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollBarVisibility)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollMode)));
            UpdateViewCommandStates();
            SyncActivePanelPresentationState();
        }

        private void ApplyEntryItemMetricsPreset()
        {
            EntryItemMetrics = Controls.EntryItemMetrics.CreatePreset(_currentEntryViewDensityMode);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryItemMetrics)));
            InvalidateEntriesLayouts();
        }

        private void UpdateViewCommandStates()
        {
            if (ViewDetailsMenuItem is not null)
            {
                ViewDetailsMenuItem.IsChecked = _currentViewMode == EntryViewMode.Details;
            }

            if (ViewListMenuItem is not null)
            {
                ViewListMenuItem.IsChecked = _currentViewMode == EntryViewMode.List;
            }

            if (SortByNameMenuItem is not null)
            {
                SortByNameMenuItem.IsChecked = _currentSortField == EntrySortField.Name;
                SortByTypeMenuItem.IsChecked = _currentSortField == EntrySortField.Type;
                SortBySizeMenuItem.IsChecked = _currentSortField == EntrySortField.Size;
                SortByModifiedDateMenuItem.IsChecked = _currentSortField == EntrySortField.ModifiedDate;
                SortAscendingMenuItem.IsChecked = _currentSortDirection == SortDirection.Ascending;
                SortDescendingMenuItem.IsChecked = _currentSortDirection == SortDirection.Descending;
            }

            if (GroupByNoneMenuItem is not null)
            {
                GroupByNoneMenuItem.IsChecked = _currentGroupField == EntryGroupField.None;
                GroupByNameMenuItem.IsChecked = _currentGroupField == EntryGroupField.Name;
                GroupByTypeMenuItem.IsChecked = _currentGroupField == EntryGroupField.Type;
                GroupByModifiedDateMenuItem.IsChecked = _currentGroupField == EntryGroupField.ModifiedDate;
            }
        }

        private async Task SetViewModeAsync(EntryViewMode mode)
        {
            if (_currentViewMode == mode)
            {
                UpdateViewCommandStates();
                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                return;
            }

            _currentViewMode = mode;
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.ViewModeSwitch);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEntriesContextOverlayTargets();
                GetVisibleEntriesRoot().UpdateLayout();
                FocusEntriesList();
            });
        }

        private async Task SetSortAsync(EntrySortField field, SortDirection? explicitDirection = null)
        {
            _currentSortField = field;
            _currentSortDirection = explicitDirection ?? GetDefaultSortDirection(field);
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task SetSortDirectionAsync(SortDirection direction)
        {
            _currentSortDirection = direction;
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task SetGroupAsync(EntryGroupField field)
        {
            _currentGroupField = field;
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged();
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task ReloadCurrentPresentationAsync(PresentationReloadReason reason = PresentationReloadReason.DataRefresh)
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                PopulateMyComputerEntries();
                ApplyCurrentPresentation();
                return;
            }

            if (TryApplyPresentationFastPath(reason))
            {
                return;
            }

            if (UsesClientPresentationPipeline())
            {
                await LoadAllEntriesForPresentationAsync(_currentPath);
                return;
            }

            await LoadPageAsync(_currentPath, cursor: 0, append: false);
        }

        private void ApplyCurrentPresentation(NavigationPerfSession? perf = null)
        {
            perf?.Mark("apply-presentation.enter", $"view={_currentViewMode} group={_currentGroupField} sort={_currentSortField}/{_currentSortDirection}");
            List<EntryViewModel> sourceEntries = _presentationSourceEntries.Count > 0
                ? _presentationSourceEntries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList()
                : _entries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList();

            sourceEntries.Sort(CompareEntriesForPresentation);
            perf?.Mark("apply-presentation.sorted", $"count={sourceEntries.Count}");
            string? selectedPath = _selectedEntryPath;
            if (UsesColumnsListPresentation())
            {
                _groupedListRowsPerColumn = GetGroupedListRowsPerColumn();
                ApplyEntryViewState(sourceEntries);

                if (!MatchesCurrentVisibleEntries(sourceEntries))
                {
                    ReplaceVisibleEntries(sourceEntries);
                }

                if (TryUseGroupedColumnsCache())
                {
                    ApplyGroupedColumnsProjection(_groupedColumnsProjectionCache!);
                    perf?.Mark("apply-presentation.columns-cache-hit", $"columns={_groupedEntryColumns.Count}");
                }
                else
                {
                    RebuildGroupedEntryColumns(sourceEntries);
                    perf?.Mark("apply-presentation.columns-rebuilt", $"columns={_groupedEntryColumns.Count}");
                }
                _ = DispatcherQueue.TryEnqueue(RefreshGroupedColumnsForViewport);
            }
            else
            {
                List<EntryViewModel> presentedEntries = BuildPresentedEntries(sourceEntries);
                perf?.Mark("apply-presentation.rows-built", $"count={presentedEntries.Count}");
                if (!MatchesCurrentVisibleEntries(presentedEntries))
                {
                    ReplaceVisibleEntries(presentedEntries);
                }

                _groupedEntryColumns.Clear();
            }

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                RestoreListSelectionByPath(ensureVisible: false);
            }

            UpdateEntrySelectionVisuals();
            UpdateViewCommandStates();
            perf?.Mark("apply-presentation.exit", $"visible={_entries.Count}");
        }

        private bool MatchesCurrentVisibleEntries(IReadOnlyList<EntryViewModel> entries)
        {
            if (_entries.Count != entries.Count)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!ReferenceEquals(_entries[i], entries[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void ReplaceVisibleEntries(IReadOnlyList<EntryViewModel> entries)
        {
            _entries.ReplaceAll(entries);
        }

        private List<EntryViewModel> BuildPresentedEntries(IReadOnlyList<EntryViewModel> orderedEntries)
        {
            if (_currentGroupField == EntryGroupField.None)
            {
                ApplyEntryViewState(orderedEntries);
                return orderedEntries.ToList();
            }

            var buckets = new Dictionary<string, EntryGroupBucket>(StringComparer.OrdinalIgnoreCase);
            foreach (EntryViewModel entry in orderedEntries)
            {
                EntryGroupDescriptor descriptor = GetGroupDescriptor(entry);
                if (!buckets.TryGetValue(descriptor.BucketKey, out EntryGroupBucket? bucket))
                {
                    bucket = new EntryGroupBucket { Descriptor = descriptor };
                    buckets.Add(descriptor.BucketKey, bucket);
                }

                bucket.Items.Add(entry);
            }

            List<EntryGroupBucket> orderedBuckets = buckets.Values
                .OrderBy(bucket => bucket.Descriptor.OrderKey, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(bucket => bucket.Descriptor.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var presentedEntries = new List<EntryViewModel>(orderedEntries.Count + orderedBuckets.Count);
            foreach (EntryGroupBucket bucket in orderedBuckets)
            {
                AppendGroup(presentedEntries, bucket.Descriptor, bucket.Items);
            }

            return presentedEntries;
        }

        private void AppendGroup(ICollection<EntryViewModel> presentedEntries, EntryGroupDescriptor descriptor, IReadOnlyList<EntryViewModel> groupEntries)
        {
            if (groupEntries.Count == 0)
            {
                return;
            }

            bool isExpanded = !_groupExpansionStates.TryGetValue(descriptor.StateKey, out bool expanded) || expanded;
            EntryViewModel headerEntry = EntryViewModel.CreateGroupHeader(descriptor.StateKey, descriptor.Label, groupEntries.Count, isExpanded);
            headerEntry.DetailsGroupHeaderMargin = presentedEntries.Count == 0 ? new Thickness(0) : new Thickness(0, 6, 0, 0);
            ApplyEntryLayoutState(headerEntry);
            presentedEntries.Add(headerEntry);

            if (!isExpanded)
            {
                return;
            }

            ApplyEntryViewState(groupEntries);
            foreach (EntryViewModel entry in groupEntries)
            {
                presentedEntries.Add(entry);
            }
        }

        private void ApplyEntryViewState(IReadOnlyList<EntryViewModel> entries)
        {
            foreach (EntryViewModel entry in entries)
            {
                entry.IsGroupHeader = false;
                entry.GroupKey = string.Empty;
                entry.GroupItemCount = 0;
                entry.IsGroupExpanded = false;
                entry.GroupHeaderText = string.Empty;
                ApplyEntryLayoutState(entry);
            }
        }

        private void ApplyEntryLayoutState(EntryViewModel entry)
        {
            entry.HeaderRowVisibility = entry.IsGroupHeader ? Visibility.Visible : Visibility.Collapsed;
            entry.DetailsRowVisibility = !entry.IsGroupHeader && _currentViewMode == EntryViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
            entry.ListRowVisibility = !entry.IsGroupHeader && _currentViewMode == EntryViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateEntrySelectionVisuals()
        {
            var seen = new HashSet<EntryViewModel>();
            IEnumerable<EntryViewModel> allEntries = _presentationSourceEntries.Concat(_entries);
            foreach (EntryViewModel entry in allEntries)
            {
                if (!seen.Add(entry))
                {
                    continue;
                }

                entry.IsExplicitlySelected = !entry.IsGroupHeader &&
                    !string.IsNullOrWhiteSpace(_selectedEntryPath) &&
                    string.Equals(entry.FullPath, _selectedEntryPath, StringComparison.OrdinalIgnoreCase);
                entry.IsKeyboardAnchor = !entry.IsGroupHeader &&
                    !string.IsNullOrWhiteSpace(_focusedEntryPath) &&
                    string.Equals(entry.FullPath, _focusedEntryPath, StringComparison.OrdinalIgnoreCase);
                entry.IsSelectionActive = _isEntriesSelectionActive;
            }
        }

        private int CompareEntriesForPresentation(EntryViewModel left, EntryViewModel right)
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            int result = _currentSortField switch
            {
                EntrySortField.ModifiedDate => Nullable.Compare(left.ModifiedAt, right.ModifiedAt),
                EntrySortField.Type => StringComparer.CurrentCultureIgnoreCase.Compare(left.Type, right.Type),
                EntrySortField.Size => Nullable.Compare(left.SizeBytes, right.SizeBytes),
                _ => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name)
            };

            if (result == 0 && _currentSortField != EntrySortField.Name)
            {
                result = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
            }

            if (_currentSortDirection == SortDirection.Descending)
            {
                result = -result;
            }

            if (result == 0)
            {
                result = StringComparer.CurrentCultureIgnoreCase.Compare(left.FullPath, right.FullPath);
            }

            return result;
        }

        private EntryGroupDescriptor GetGroupDescriptor(EntryViewModel entry)
        {
            return _currentGroupField switch
            {
                EntryGroupField.Name => GetNameGroupDescriptor(entry),
                EntryGroupField.Type => GetTypeGroupDescriptor(entry),
                EntryGroupField.ModifiedDate => GetModifiedDateGroupDescriptor(entry),
                _ => new EntryGroupDescriptor(string.Empty, string.Empty, string.Empty, string.Empty)
            };
        }

        private static EntryGroupDescriptor GetNameGroupDescriptor(EntryViewModel entry)
        {
            string label = string.IsNullOrWhiteSpace(entry.Name) ? "#" : char.ToUpperInvariant(entry.Name[0]).ToString();
            return new EntryGroupDescriptor($"name:{label}", $"name:{label}", label, label);
        }

        private static EntryGroupDescriptor GetTypeGroupDescriptor(EntryViewModel entry)
        {
            string label = string.IsNullOrWhiteSpace(entry.Type) ? "-" : entry.Type;
            string orderKey = entry.IsDirectory
                ? $"0000:{label}"
                : $"1000:{label}";
            return new EntryGroupDescriptor($"type:{label}", $"type:{label}", label, orderKey);
        }

        private static EntryGroupDescriptor GetModifiedDateGroupDescriptor(EntryViewModel entry)
        {
            string label = entry.ModifiedAt?.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? "-";
            return new EntryGroupDescriptor($"modified:{label}", $"modified:{label}", label, label);
        }

        private void PopulateMyComputerEntries()
        {
            _entries.Clear();
            _selectedEntryPath = null;
            var drives = new List<EntryViewModel>();
            foreach (DriveInfo drive in _explorerService.GetReadyDrives())
            {
                string root = drive.RootDirectory.FullName;
                string label = drive.Name.TrimEnd('\\');
                string type = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                    ? S("DriveTypeLocalDisk")
                    : SF("DriveTypeVolumeFormat", drive.VolumeLabel, drive.DriveFormat);

                drives.Add(new EntryViewModel
                {
                    Name = label,
                    DisplayName = label,
                    PendingName = label,
                    FullPath = root,
                    Type = type,
                    IconGlyph = "\uE7F8",
                    IconForeground = FolderIconBrush,
                    MftRef = 0,
                    SizeText = FormatBytes(drive.TotalSize),
                    ModifiedText = FormatBytes(drive.AvailableFreeSpace),
                    IsDirectory = true,
                    IsLink = false,
                    IsLoaded = true,
                    IsMetadataLoaded = true
                });
            }

            foreach (EntryViewModel driveEntry in drives)
            {
                _entries.Add(driveEntry);
            }

            SetPresentationSourceEntries(drives);

            _totalEntries = (uint)_entries.Count;
            InvalidateEntriesLayouts();
            _nextCursor = 0;
            _hasMore = false;
            UpdateFileCommandStates();
            _lastTitleWasReadFailed = false;
            UpdateWindowTitle();
            UpdateStatus(SF("StatusDriveCount", _entries.Count));
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void UpdateDetailsHeaders()
        {
            if (NameHeaderTextBlock is null || TypeHeaderTextBlock is null || SizeHeaderTextBlock is null || ModifiedHeaderTextBlock is null)
            {
                return;
            }

            NameHeaderTextBlock.Text = S("ColumnNameHeader");
            TypeHeaderTextBlock.Text = S("ColumnTypeHeader");
            if (string.Equals(_currentPath, ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                SizeHeaderTextBlock.Text = S("ColumnTotalSizeHeader");
                ModifiedHeaderTextBlock.Text = S("ColumnFreeSpaceHeader");
                return;
            }

            SizeHeaderTextBlock.Text = S("ColumnSizeHeader");
            ModifiedHeaderTextBlock.Text = S("ColumnModifiedHeader");
        }

    }
}
