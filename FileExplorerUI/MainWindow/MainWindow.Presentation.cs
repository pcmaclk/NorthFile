using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

        private PrimaryPresentationNotificationState CapturePrimaryPresentationNotificationState()
        {
            return new PrimaryPresentationNotificationState(
                GetPanelViewMode(WorkspacePanelId.Primary),
                GetPanelSortField(WorkspacePanelId.Primary),
                GetPanelSortDirection(WorkspacePanelId.Primary),
                GetPanelGroupField(WorkspacePanelId.Primary),
                _currentEntryViewDensityMode);
        }

        private PanelViewState GetActivePanelState()
        {
            return _workspaceLayoutHost.GetActivePanelState();
        }

        private void SyncActivePanelPresentationState()
        {
            PanelViewState activePanel = GetPanelState(WorkspacePanelId.Primary);
            activePanel.QueryText = GetPanelQueryText(WorkspacePanelId.Primary);
            activePanel.CurrentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            activePanel.AddressText = GetPanelAddressText(WorkspacePanelId.Primary);
            activePanel.SelectedEntryPath = _selectedEntryPath;
            activePanel.FocusedEntryPath = _focusedEntryPath;
        }

        private bool UsesColumnsListPresentation()
        {
            return GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List;
        }

        private FrameworkElement GetVisibleEntriesRoot()
        {
            return GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
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
            return GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details
                ? _detailsEntriesViewHost
                : _groupedEntriesViewHost;
        }

        private bool NeedsEntriesHorizontalScroll()
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details)
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

        public IReadOnlyList<GroupedEntryColumnViewModel> GroupedEntryColumns => GetPrimaryGroupedEntryColumns();

        private void NotifyPresentationModeChanged(PrimaryPresentationNotificationState? previousState = null)
        {
            PrimaryPresentationNotificationState currentState = CapturePrimaryPresentationNotificationState();
            bool force = previousState is null;
            PrimaryPresentationNotificationState previous = previousState ?? currentState;
            bool viewModeChanged = force || previous.ViewMode != currentState.ViewMode;
            bool sortChanged = force || previous.SortField != currentState.SortField || previous.SortDirection != currentState.SortDirection;
            bool groupChanged = force || previous.GroupField != currentState.GroupField;
            bool densityChanged = force || previous.DensityMode != currentState.DensityMode;
            bool layoutChanged = viewModeChanged || groupChanged || densityChanged;

            if (viewModeChanged)
            {
                SetPrimaryGroupedListRowsPerColumn(-1);
            }

            if (densityChanged)
            {
                ApplyEntryItemMetricsPreset();
            }
            else if (layoutChanged)
            {
                InvalidateEntriesLayouts();
            }

            if (layoutChanged)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntryContainerWidth)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesListVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupedColumnsVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsItemsVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsHeaderVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameHeaderMargin)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailsNameCellMargin)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollBarVisibility)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EntriesHorizontalScrollMode)));
            }

            if (viewModeChanged || sortChanged || groupChanged)
            {
                UpdateViewCommandStates();
            }

            if (force || layoutChanged || sortChanged)
            {
                SyncActivePanelPresentationState();
            }
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
                ViewDetailsMenuItem.IsChecked = GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details;
            }

            if (ViewListMenuItem is not null)
            {
                ViewListMenuItem.IsChecked = GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List;
            }

            if (SortByNameMenuItem is not null)
            {
                EntrySortField primarySortField = GetPanelSortField(WorkspacePanelId.Primary);
                SortDirection primarySortDirection = GetPanelSortDirection(WorkspacePanelId.Primary);
                SortByNameMenuItem.IsChecked = primarySortField == EntrySortField.Name;
                SortByTypeMenuItem.IsChecked = primarySortField == EntrySortField.Type;
                SortBySizeMenuItem.IsChecked = primarySortField == EntrySortField.Size;
                SortByModifiedDateMenuItem.IsChecked = primarySortField == EntrySortField.ModifiedDate;
                SortAscendingMenuItem.IsChecked = primarySortDirection == SortDirection.Ascending;
                SortDescendingMenuItem.IsChecked = primarySortDirection == SortDirection.Descending;
            }

            if (GroupByNoneMenuItem is not null)
            {
                EntryGroupField primaryGroupField = GetPanelGroupField(WorkspacePanelId.Primary);
                GroupByNoneMenuItem.IsChecked = primaryGroupField == EntryGroupField.None;
                GroupByNameMenuItem.IsChecked = primaryGroupField == EntryGroupField.Name;
                GroupByTypeMenuItem.IsChecked = primaryGroupField == EntryGroupField.Type;
                GroupByModifiedDateMenuItem.IsChecked = primaryGroupField == EntryGroupField.ModifiedDate;
            }

            UpdateDetailsHeaders();
        }

        private async Task SetViewModeAsync(EntryViewMode mode)
        {
            if (GetPanelViewMode(WorkspacePanelId.Primary) == mode)
            {
                UpdateViewCommandStates();
                _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
                return;
            }

            PrimaryPresentationNotificationState previousState = CapturePrimaryPresentationNotificationState();
            SetPrimaryPanelPresentationState(
                mode,
                GetPanelSortField(WorkspacePanelId.Primary),
                GetPanelSortDirection(WorkspacePanelId.Primary),
                GetPanelGroupField(WorkspacePanelId.Primary));
            if (mode == EntryViewMode.List)
            {
                SetPrimaryGroupedListRowsPerColumn(-1);
                SetPanelLastGroupedViewportHeight(WorkspacePanelId.Primary, double.NaN);
            }
            NotifyPresentationModeChanged(previousState);
            await ReloadCurrentPresentationAsync(PresentationReloadReason.ViewModeSwitch);
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                UpdateEntriesContextOverlayTargets();
                GetVisibleEntriesRoot().UpdateLayout();
                if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.List)
                {
                    // Ensure details->list switch gets a correct first layout immediately.
                    RequestGroupedColumnsRefresh(force: true);
                    RequestGroupedColumnsRefreshDebounced(delayMs: 48, force: true);
                }
                FocusEntriesList();
            });
        }

        private Task SetSortAsync(EntrySortField field, SortDirection? explicitDirection = null)
        {
            return SetPanelSortAsync(WorkspacePanelId.Primary, field, explicitDirection);
        }

        private async Task SetPanelSortAsync(
            WorkspacePanelId panelId,
            EntrySortField field,
            SortDirection? explicitDirection = null)
        {
            SortDirection direction = explicitDirection ?? GetPanelDefaultSortDirection(panelId, field);
            if (panelId == WorkspacePanelId.Primary)
            {
                PrimaryPresentationNotificationState previousState = CapturePrimaryPresentationNotificationState();
                SetPanelSortState(panelId, field, direction);
                InvalidateProjectionCaches();
                NotifyPresentationModeChanged(previousState);
                await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
                return;
            }

            SetPanelSortState(panelId, field, direction);
            UpdatePanelDetailsHeaders(panelId);
            await ReloadPanelDataAsync(
                panelId,
                preserveViewport: false,
                ensureSelectionVisible: false,
                focusEntries: false);
        }

        private async Task SetSortDirectionAsync(SortDirection direction)
        {
            PrimaryPresentationNotificationState previousState = CapturePrimaryPresentationNotificationState();
            SetPanelSortState(WorkspacePanelId.Primary, GetPanelSortField(WorkspacePanelId.Primary), direction);
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged(previousState);
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task SetGroupAsync(EntryGroupField field)
        {
            PrimaryPresentationNotificationState previousState = CapturePrimaryPresentationNotificationState();
            SetPanelGroupField(WorkspacePanelId.Primary, field);
            InvalidateProjectionCaches();
            NotifyPresentationModeChanged(previousState);
            await ReloadCurrentPresentationAsync(PresentationReloadReason.PresentationSettingsChange);
        }

        private async Task ReloadCurrentPresentationAsync(PresentationReloadReason reason = PresentationReloadReason.DataRefresh)
        {
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            if (string.Equals(currentPath, ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
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
                await LoadAllEntriesForPresentationAsync(currentPath);
                return;
            }

            await LoadPageAsync(currentPath, cursor: 0, append: false);
        }

        private void ApplyCurrentPresentation(NavigationPerfSession? perf = null)
        {
            LogPrimaryTabDataState("ApplyCurrentPresentation.enter");
            perf?.Mark(
                "apply-presentation.enter",
                $"view={GetPanelViewMode(WorkspacePanelId.Primary)} group={GetPanelGroupField(WorkspacePanelId.Primary)} sort={GetPanelSortField(WorkspacePanelId.Primary)}/{GetPanelSortDirection(WorkspacePanelId.Primary)}");
            List<EntryViewModel> sourceEntries = GetPrimaryPresentationSourceInitialized()
                ? GetPrimaryPresentationSourceEntries().Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList()
                : PrimaryEntries.Where(entry => entry.IsLoaded && !entry.IsGroupHeader).ToList();

            sourceEntries.Sort(CompareEntriesForPresentation);
            perf?.Mark("apply-presentation.sorted", $"count={sourceEntries.Count}");
            string? selectedPath = _selectedEntryPath;
            if (UsesColumnsListPresentation())
            {
                List<EntryViewModel> presentedEntries = BuildPresentedEntries(sourceEntries);
                SetPrimaryGroupedListRowsPerColumn(GetGroupedListRowsPerColumn());
                if (!MatchesCurrentVisibleEntries(presentedEntries))
                {
                    ReplaceVisibleEntries(presentedEntries);
                }

                GetPrimaryGroupedEntryColumns().Clear();
                RequestGroupedColumnsRefresh();
            }
            else
            {
                List<EntryViewModel> presentedEntries = BuildPresentedEntries(sourceEntries);
                perf?.Mark("apply-presentation.rows-built", $"count={presentedEntries.Count}");
                if (!MatchesCurrentVisibleEntries(presentedEntries))
                {
                    ReplaceVisibleEntries(presentedEntries);
                }

                GetPrimaryGroupedEntryColumns().Clear();
            }

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                RestoreListSelectionByPath(ensureVisible: false);
            }

            UpdateEntrySelectionVisuals();
            UpdateViewCommandStates();
            LogPrimaryTabDataState("ApplyCurrentPresentation.exit");
            perf?.Mark("apply-presentation.exit", $"visible={PrimaryEntries.Count}");
        }

        private bool MatchesCurrentVisibleEntries(IReadOnlyList<EntryViewModel> entries)
        {
            if (PrimaryEntries.Count != entries.Count)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!ReferenceEquals(PrimaryEntries[i], entries[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void ReplaceVisibleEntries(IReadOnlyList<EntryViewModel> entries)
        {
            PrimaryEntries.ReplaceAll(entries);
        }

        private List<EntryViewModel> BuildPresentedEntries(IReadOnlyList<EntryViewModel> orderedEntries)
        {
            if (GetPanelGroupField(WorkspacePanelId.Primary) == EntryGroupField.None)
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

            bool isExpanded = !GetPrimaryGroupExpansionStates().TryGetValue(descriptor.StateKey, out bool expanded) || expanded;
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
            EntryViewMode primaryViewMode = GetPanelViewMode(WorkspacePanelId.Primary);
            entry.DetailsRowVisibility = !entry.IsGroupHeader && primaryViewMode == EntryViewMode.Details ? Visibility.Visible : Visibility.Collapsed;
            entry.ListRowVisibility = !entry.IsGroupHeader && primaryViewMode == EntryViewMode.List ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateEntrySelectionVisuals()
        {
            bool isActive = IsPrimaryWorkspacePanelActive;
            var seen = new HashSet<EntryViewModel>();
            IEnumerable<EntryViewModel> allEntries = GetPrimaryPresentationSourceEntries().Concat(PrimaryEntries);
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
                entry.IsSelectionActive = isActive;
            }
        }

        private int CompareEntriesForPresentation(EntryViewModel left, EntryViewModel right)
        {
            return CompareEntriesForPresentation(
                left,
                right,
                GetPanelSortField(WorkspacePanelId.Primary),
                GetPanelSortDirection(WorkspacePanelId.Primary));
        }

        private static int CompareEntriesForPresentation(
            EntryViewModel left,
            EntryViewModel right,
            EntrySortField sortField,
            SortDirection sortDirection)
        {
            if (left.IsDirectory != right.IsDirectory)
            {
                return left.IsDirectory ? -1 : 1;
            }

            int result = sortField switch
            {
                EntrySortField.ModifiedDate => Nullable.Compare(left.ModifiedAt, right.ModifiedAt),
                EntrySortField.Type => StringComparer.CurrentCultureIgnoreCase.Compare(left.Type, right.Type),
                EntrySortField.Size => Nullable.Compare(left.SizeBytes, right.SizeBytes),
                _ => StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name)
            };

            if (result == 0 && sortField != EntrySortField.Name)
            {
                result = StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
            }

            if (sortDirection == SortDirection.Descending)
            {
                result = -result;
            }

            if (result == 0)
            {
                result = StringComparer.CurrentCultureIgnoreCase.Compare(left.FullPath, right.FullPath);
            }

            return result;
        }

        private void ApplySecondaryPanePresentation(PanelViewState panelState)
        {
            List<EntryViewModel> sortedEntries = panelState.DataSession.Entries
                .Where(entry => entry.IsLoaded && !entry.IsGroupHeader)
                .OrderBy(
                    entry => entry,
                    Comparer<EntryViewModel>.Create((left, right) =>
                        CompareEntriesForPresentation(left, right, panelState.SortField, panelState.SortDirection)))
                .ToList();

            panelState.DataSession.Entries.ReplaceAll(sortedEntries);
            panelState.DataSession.PresentationSourceEntries.Clear();
            panelState.DataSession.PresentationSourceEntries.AddRange(sortedEntries);
        }

        private EntryGroupDescriptor GetGroupDescriptor(EntryViewModel entry)
        {
            return GetPanelGroupField(WorkspacePanelId.Primary) switch
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
            PrimaryEntries.Clear();
            _selectedEntryPath = null;
            List<EntryViewModel> drives = CreateMyComputerDriveEntries();

            foreach (EntryViewModel driveEntry in drives)
            {
                PrimaryEntries.Add(driveEntry);
            }

            SetPresentationSourceEntries(drives);

            SetPanelTotalEntries(WorkspacePanelId.Primary, (uint)PrimaryEntries.Count);
            MarkPanelDataLoadedForCurrentNavigation(WorkspacePanelId.Primary);
            InvalidateEntriesLayouts();
            SetPanelNextCursor(WorkspacePanelId.Primary, 0);
            SetPanelHasMore(WorkspacePanelId.Primary, false);
            UpdateFileCommandStates();
            _lastTitleWasReadFailed = false;
            UpdateWindowTitle();
            UpdateStatus(SF("StatusDriveCount", PrimaryEntries.Count));
            LogPrimaryTabDataState("PopulateMyComputerEntries");
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void ShowStatusDialog(string message, bool warning)
        {
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                await _statusDialogSemaphore.WaitAsync();
                try
                {
                    EnsureOperationFeedbackOverlay();
                    if (_operationFeedbackDialog is null)
                    {
                        return;
                    }

                    await _operationFeedbackDialog.ShowAsync(
                        S(warning ? "StatusDialogWarningTitle" : "StatusDialogErrorTitle"),
                        message,
                        S("DialogCancelButton"),
                        string.Empty);
                }
                finally
                {
                    _statusDialogSemaphore.Release();
                }
            });
        }

        private void UpdateDetailsHeaders()
        {
            UpdatePanelDetailsHeaders(WorkspacePanelId.Primary);
        }

        private void UpdatePanelDetailsHeaders(WorkspacePanelId panelId)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                UpdateSecondaryDetailsHeaders();
                return;
            }

            if (NameHeaderTextBlock is null ||
                TypeHeaderTextBlock is null ||
                SizeHeaderTextBlock is null ||
                ModifiedHeaderTextBlock is null ||
                NameHeaderBorder is null ||
                TypeHeaderBorder is null ||
                SizeHeaderBorder is null ||
                ModifiedHeaderBorder is null ||
                NameHeaderSortGlyphTextBlock is null ||
                TypeHeaderSortGlyphTextBlock is null ||
                SizeHeaderSortGlyphTextBlock is null ||
                ModifiedHeaderSortGlyphTextBlock is null)
            {
                return;
            }

            NameHeaderTextBlock.Text = S("ColumnNameHeader");
            TypeHeaderTextBlock.Text = S("ColumnTypeHeader");
            if (string.Equals(GetPanelCurrentPath(WorkspacePanelId.Primary), ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                SizeHeaderTextBlock.Text = S("ColumnTotalSizeHeader");
                ModifiedHeaderTextBlock.Text = S("ColumnFreeSpaceHeader");
            }
            else
            {
                SizeHeaderTextBlock.Text = S("ColumnSizeHeader");
                ModifiedHeaderTextBlock.Text = S("ColumnModifiedHeader");
            }

            UpdateDetailsHeaderVisual(NameHeaderBorder, NameHeaderTextBlock, NameHeaderSortGlyphTextBlock, EntrySortField.Name);
            UpdateDetailsHeaderVisual(TypeHeaderBorder, TypeHeaderTextBlock, TypeHeaderSortGlyphTextBlock, EntrySortField.Type);
            UpdateDetailsHeaderVisual(SizeHeaderBorder, SizeHeaderTextBlock, SizeHeaderSortGlyphTextBlock, EntrySortField.Size);
            UpdateDetailsHeaderVisual(ModifiedHeaderBorder, ModifiedHeaderTextBlock, ModifiedHeaderSortGlyphTextBlock, EntrySortField.ModifiedDate);
        }

        private void UpdateSecondaryDetailsHeaders()
        {
            if (SecondaryNameHeaderTextBlock is null ||
                SecondaryTypeHeaderTextBlock is null ||
                SecondarySizeHeaderTextBlock is null ||
                SecondaryModifiedHeaderTextBlock is null ||
                SecondaryNameHeaderBorder is null ||
                SecondaryTypeHeaderBorder is null ||
                SecondarySizeHeaderBorder is null ||
                SecondaryModifiedHeaderBorder is null ||
                SecondaryNameHeaderSortGlyphTextBlock is null ||
                SecondaryTypeHeaderSortGlyphTextBlock is null ||
                SecondarySizeHeaderSortGlyphTextBlock is null ||
                SecondaryModifiedHeaderSortGlyphTextBlock is null)
            {
                return;
            }

            SecondaryNameHeaderTextBlock.Text = S("ColumnNameHeader");
            SecondaryTypeHeaderTextBlock.Text = S("ColumnTypeHeader");
            if (string.Equals(GetPanelCurrentPath(WorkspacePanelId.Secondary), ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                SecondarySizeHeaderTextBlock.Text = S("ColumnTotalSizeHeader");
                SecondaryModifiedHeaderTextBlock.Text = S("ColumnFreeSpaceHeader");
            }
            else
            {
                SecondarySizeHeaderTextBlock.Text = S("ColumnSizeHeader");
                SecondaryModifiedHeaderTextBlock.Text = S("ColumnModifiedHeader");
            }

            UpdateDetailsHeaderVisual(
                SecondaryNameHeaderBorder,
                SecondaryNameHeaderTextBlock,
                SecondaryNameHeaderSortGlyphTextBlock,
                GetPanelSortField(WorkspacePanelId.Secondary),
                GetPanelSortDirection(WorkspacePanelId.Secondary),
                EntrySortField.Name,
                IsPanelSortAtDefault(WorkspacePanelId.Secondary));
            UpdateDetailsHeaderVisual(
                SecondaryTypeHeaderBorder,
                SecondaryTypeHeaderTextBlock,
                SecondaryTypeHeaderSortGlyphTextBlock,
                GetPanelSortField(WorkspacePanelId.Secondary),
                GetPanelSortDirection(WorkspacePanelId.Secondary),
                EntrySortField.Type,
                IsPanelSortAtDefault(WorkspacePanelId.Secondary));
            UpdateDetailsHeaderVisual(
                SecondarySizeHeaderBorder,
                SecondarySizeHeaderTextBlock,
                SecondarySizeHeaderSortGlyphTextBlock,
                GetPanelSortField(WorkspacePanelId.Secondary),
                GetPanelSortDirection(WorkspacePanelId.Secondary),
                EntrySortField.Size,
                IsPanelSortAtDefault(WorkspacePanelId.Secondary));
            UpdateDetailsHeaderVisual(
                SecondaryModifiedHeaderBorder,
                SecondaryModifiedHeaderTextBlock,
                SecondaryModifiedHeaderSortGlyphTextBlock,
                GetPanelSortField(WorkspacePanelId.Secondary),
                GetPanelSortDirection(WorkspacePanelId.Secondary),
                EntrySortField.ModifiedDate,
                IsPanelSortAtDefault(WorkspacePanelId.Secondary));
        }

        private void UpdateDetailsHeaderVisual(Border border, TextBlock label, TextBlock sortGlyph, EntrySortField field)
        {
            UpdateDetailsHeaderVisual(
                border,
                label,
                sortGlyph,
                GetPanelSortField(WorkspacePanelId.Primary),
                GetPanelSortDirection(WorkspacePanelId.Primary),
                field,
                IsCurrentSortAtDefault());
        }

        private void UpdateDetailsHeaderVisual(
            Border border,
            TextBlock label,
            TextBlock sortGlyph,
            EntrySortField activeSortField,
            SortDirection activeSortDirection,
            EntrySortField field,
            bool isAtDefault)
        {
            bool isActive = activeSortField == field;
            bool showSortGlyph = isActive && !isAtDefault;
            label.Foreground = isActive ? GetDetailsHeaderPrimaryBrush() : GetDetailsHeaderSecondaryBrush();
            label.Opacity = isActive ? 1.0 : 0.88;
            sortGlyph.Text = isActive && activeSortDirection == SortDirection.Descending ? "↓" : "↑";
            sortGlyph.Foreground = isActive ? GetDetailsHeaderPrimaryBrush() : GetDetailsHeaderSecondaryBrush();
            sortGlyph.Visibility = showSortGlyph ? Visibility.Visible : Visibility.Collapsed;
            sortGlyph.Opacity = showSortGlyph ? 0.92 : 0.72;
            border.Background = GetDetailsHeaderRestBrush();
        }

        private async void DetailsHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                !TryGetDetailsHeaderField(element.Tag as string, out EntrySortField field))
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            e.Handled = true;

            SortDirection? explicitDirection = GetPanelSortField(WorkspacePanelId.Primary) == field
                ? (GetPanelSortDirection(WorkspacePanelId.Primary) == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending)
                : null;

            await SetSortAsync(field, explicitDirection);
        }

        private async void SecondaryDetailsHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                !TryGetDetailsHeaderField(element.Tag as string, out EntrySortField field))
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            e.Handled = true;

            SortDirection? explicitDirection = GetPanelSortField(WorkspacePanelId.Secondary) == field
                ? (GetPanelSortDirection(WorkspacePanelId.Secondary) == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending)
                : null;

            await SetPanelSortAsync(WorkspacePanelId.Secondary, field, explicitDirection);
        }

        private void DetailsHeader_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = GetDetailsHeaderHoverBrush();
            }
        }

        private void DetailsHeader_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = GetDetailsHeaderRestBrush();
            }
        }

        private void SecondaryDetailsHeader_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = GetDetailsHeaderHoverBrush();
            }
        }

        private void SecondaryDetailsHeader_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = GetDetailsHeaderRestBrush();
            }
        }

        private bool IsCurrentSortAtDefault()
        {
            EntrySortField defaultField = _appSettings.DefaultSortField;
            SortDirection defaultDirection = GetDefaultSortDirection(defaultField);
            return GetPanelSortField(WorkspacePanelId.Primary) == defaultField &&
                GetPanelSortDirection(WorkspacePanelId.Primary) == defaultDirection;
        }

        private bool IsPanelSortAtDefault(WorkspacePanelId panelId)
        {
            EntrySortField defaultField = GetPanelDefaultSortField(panelId);
            SortDirection defaultDirection = GetPanelDefaultSortDirection(panelId, defaultField);
            return GetPanelSortField(panelId) == defaultField &&
                GetPanelSortDirection(panelId) == defaultDirection;
        }

        private EntrySortField GetPanelDefaultSortField(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Primary
                ? _appSettings.DefaultSortField
                : EntrySortField.Name;
        }

        private SortDirection GetPanelDefaultSortDirection(WorkspacePanelId panelId, EntrySortField field)
        {
            return panelId == WorkspacePanelId.Primary
                ? GetDefaultSortDirection(field)
                : SortDirection.Ascending;
        }

        private static bool TryGetDetailsHeaderField(string? fieldName, out EntrySortField field)
        {
            field = fieldName switch
            {
                nameof(EntrySortField.Name) => EntrySortField.Name,
                nameof(EntrySortField.Type) => EntrySortField.Type,
                nameof(EntrySortField.Size) => EntrySortField.Size,
                nameof(EntrySortField.ModifiedDate) => EntrySortField.ModifiedDate,
                _ => EntrySortField.Name
            };

            return fieldName is nameof(EntrySortField.Name) or
                nameof(EntrySortField.Type) or
                nameof(EntrySortField.Size) or
                nameof(EntrySortField.ModifiedDate);
        }

        private Brush? GetDetailsHeaderHoverBrush()
        {
            if (RightShellColumn?.Resources is not null &&
                RightShellColumn.Resources.TryGetValue("DetailsHeaderHoverBackgroundBrush", out object? localValue) &&
                localValue is Brush localBrush)
            {
                return localBrush;
            }

            if (Application.Current.Resources.TryGetValue("ListViewItemBackgroundPointerOver", out object? value) && value is Brush brush)
            {
                return brush;
            }

            if (Application.Current.Resources.TryGetValue("SubtleFillColorSecondaryBrush", out value) && value is Brush fallbackBrush)
            {
                return fallbackBrush;
            }

            return null;
        }

        private Brush? GetDetailsHeaderRestBrush()
        {
            if (RightShellColumn?.Resources is not null &&
                RightShellColumn.Resources.TryGetValue("DetailsHeaderRestBackgroundBrush", out object? localValue) &&
                localValue is Brush localBrush)
            {
                return localBrush;
            }

            if (Application.Current.Resources.TryGetValue("SubtleFillColorTransparentBrush", out object? value) && value is Brush brush)
            {
                return brush;
            }

            return null;
        }

        private Brush? GetDetailsHeaderPrimaryBrush()
        {
            if (TitleBarPrimaryBrushProbe.Foreground is Brush probeBrush)
            {
                return probeBrush;
            }

            if (Application.Current.Resources.TryGetValue("TextFillColorPrimaryBrush", out object? value) && value is Brush brush)
            {
                return brush;
            }

            return null;
        }

        private Brush? GetDetailsHeaderSecondaryBrush()
        {
            if (DetailsHeaderSecondaryBrushProbe.Foreground is Brush probeBrush)
            {
                return probeBrush;
            }

            if (Application.Current.Resources.TryGetValue("DetailsHeaderSecondaryForegroundBrush", out object? value) && value is Brush brush)
            {
                return brush;
            }

            return GetDetailsHeaderPrimaryBrush();
        }

    }
}
