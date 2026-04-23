using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FileExplorerUI.Commands;
using FileExplorerUI.Workspace;
using FileExplorerUI.Interop;
using FileExplorerUI.Services;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private PanelViewState SecondaryPanelState => _workspaceLayoutHost.ShellState.Secondary;

        private void RaiseSecondaryPaneNavigationStateChanged()
        {
            RaiseSecondaryPaneStateChanged(navigationChanged: true, dataChanged: false);
        }

        private void RaiseSecondaryPaneDataStateChanged()
        {
            RaiseSecondaryPaneStateChanged(navigationChanged: false, dataChanged: true);
        }

        private void SecondaryDetailsRowGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Grid rowGrid)
            {
                ApplySecondaryDetailsRowColumnWidths(rowGrid);
            }
        }

        private void SecondaryEntryRow_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is Controls.EntryItemHost rowHost)
            {
                ApplySecondaryDetailsRowLayout(rowHost);
            }
        }

        private void RefreshRealizedSecondaryDetailsRowColumnWidths()
        {
            if (SecondaryEntriesRepeater is null)
            {
                return;
            }

            for (int i = 0; i < SecondaryPanelState.DataSession.Entries.Count; i++)
            {
                if (SecondaryEntriesRepeater.TryGetElement(i) is not Controls.EntryItemHost rowHost)
                {
                    continue;
                }

                ApplySecondaryDetailsRowLayout(rowHost);
            }
        }

        private void ApplySecondaryDetailsRowLayout(Controls.EntryItemHost rowHost)
        {
            rowHost.Width = SecondaryDetailsRowWidth;

            if (rowHost.Content is Grid rowGrid)
            {
                ApplySecondaryDetailsRowColumnWidths(rowGrid);
            }
        }

        private void ApplySecondaryDetailsRowColumnWidths(Grid rowGrid)
        {
            if (rowGrid.ColumnDefinitions.Count < 7)
            {
                return;
            }

            rowGrid.ColumnDefinitions[0].Width = SecondaryNameColumnWidth;
            rowGrid.ColumnDefinitions[2].Width = SecondaryTypeColumnWidth;
            rowGrid.ColumnDefinitions[4].Width = SecondarySizeColumnWidth;
            rowGrid.ColumnDefinitions[6].Width = SecondaryModifiedColumnWidth;
        }

        private void RaiseSecondaryPaneStateChanged(bool navigationChanged, bool dataChanged)
        {
            if (navigationChanged)
            {
                RaiseSecondaryPaneNavigationPropertiesChanged();
            }

            if (dataChanged)
            {
                RaiseSecondaryPaneDataPropertiesChanged();
            }

            if (navigationChanged || dataChanged)
            {
                UpdatePanelDetailsHeaders(WorkspacePanelId.Secondary);
            }
        }

        private void SecondaryPaneAddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            SetPanelAddressText(WorkspacePanelId.Secondary, textBox.Text);
        }

        private void SecondaryPaneInput_GotFocus(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
        }

        private void SecondaryPaneInput_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (!e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            {
                return;
            }

            CancelRenameOverlayForPanelSwitch(WorkspacePanelId.Secondary);
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
        }

        private void SecondaryPaneSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox)
            {
                return;
            }

            SetPanelQueryText(WorkspacePanelId.Secondary, textBox.Text);
        }

        private void SecondaryPaneSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);

            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                _ = CommitPanelSearchAsync(WorkspacePanelId.Secondary, SecondaryPaneSearchTextBox.Text ?? string.Empty);
                return;
            }

            if (e.Key != Windows.System.VirtualKey.Escape)
            {
                return;
            }

            e.Handled = true;
            _ = ClearPanelSearchAsync(WorkspacePanelId.Secondary);
        }

        private async void SecondaryPaneBackButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            await TryNavigatePanelBackAsync(WorkspacePanelId.Secondary);
        }

        private async void SecondaryPaneForwardButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            await TryNavigatePanelForwardAsync(WorkspacePanelId.Secondary);
        }

        private async void SecondaryPaneUpButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            await TryNavigatePanelUpAsync(WorkspacePanelId.Secondary);
        }

        private async void SecondaryPaneLoadButton_Click(object sender, RoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            await ExecuteRefreshForPaneCoreAsync(WorkspacePanelId.Secondary);
        }

        private async Task RestoreSimplePanelStateAsync(
            WorkspacePanelId panelId,
            PanelViewState panelState,
            bool preserveViewport,
            bool ensureSelectionVisible,
            bool focusEntries)
        {
            if (panelId == WorkspacePanelId.Secondary && !_isDualPaneEnabled)
            {
                return;
            }

            double preservedVerticalOffset = preserveViewport
                ? GetCurrentPanelDetailsVerticalOffset(panelId)
                : 0;

            if (CanReusePanelDataForRestore(panelId, panelState))
            {
                WorkspaceTabPerf.Mark($"{panelId.ToString().ToLowerInvariant()}.restore.reuse");
                RaisePanelNavigationStateChanged(panelId);
                RaiseSimplePanelDataStateChanged(panelId);
                RefreshPanelStatus(panelId);
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (!preserveViewport)
                    {
                        ResetPanelViewportState(panelId);
                    }

                    GetPanelViewportScrollViewer(panelId).UpdateLayout();
                    if (preserveViewport)
                    {
                        RestorePanelDetailsVerticalOffset(panelId, preservedVerticalOffset);
                    }

                    ApplySimplePanelPostLoadUi(panelId, panelState, ensureSelectionVisible, focusEntries);
                });
                return;
            }

            CancellationToken token = BeginSimplePanelRestoreTransition(panelId, panelState);

            try
            {
                SimplePanelLoadResult loadResult = await LoadSimplePanelEntriesAsync(
                    panelId,
                    panelState,
                    token);
                token.ThrowIfCancellationRequested();
                ApplySimplePanelLoadResult(
                    panelId,
                    panelState,
                    loadResult,
                    preserveViewport,
                    preservedVerticalOffset,
                    ensureSelectionVisible,
                    focusEntries);
                WorkspaceTabPerf.Mark($"{panelId.ToString().ToLowerInvariant()}.restore.reload");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                CompleteSimplePanelRestoreTransition(panelId, panelState, token);
            }
        }

        private CancellationToken BeginSimplePanelRestoreTransition(WorkspacePanelId panelId, PanelViewState panelState)
        {
            CancellationTokenSource? activeNavigationLoadCts = panelState.DataSession.NavigationLoadCts;
            CancelAndDispose(ref activeNavigationLoadCts);
            panelState.DataSession.NavigationLoadCts = new CancellationTokenSource();
            panelState.DataSession.IsLoading = true;
            ClearPanelEntriesIfNavigationIsStale(panelId);

            if (panelId == WorkspacePanelId.Secondary)
            {
                RaiseSecondaryPaneNavigationStateChanged();
                RaiseSecondaryPaneDataStateChanged();
            }

            return panelState.DataSession.NavigationLoadCts.Token;
        }

        private void CompleteSimplePanelRestoreTransition(
            WorkspacePanelId panelId,
            PanelViewState panelState,
            CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            panelState.DataSession.IsLoading = false;
            RefreshPanelStatus(panelId);
            if (panelId == WorkspacePanelId.Secondary)
            {
                RaiseSecondaryPaneDataStateChanged();
            }
        }

        private async Task<SimplePanelLoadResult> LoadSimplePanelEntriesAsync(
            WorkspacePanelId panelId,
            PanelViewState panelState,
            CancellationToken token)
        {
            string path = string.IsNullOrWhiteSpace(panelState.CurrentPath)
                ? ShellMyComputerPath
                : panelState.CurrentPath;

            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                List<EntryViewModel> drives = CreateMyComputerDriveEntries();
                return new SimplePanelLoadResult(
                    drives,
                    null,
                    0,
                    false,
                    (uint)drives.Count);
            }

            BeginDirectorySnapshot(panelId);
            SetPanelCurrentPageSize(panelId, InitialPageSize);
            PanelPageLoadCoreResult loadResult = await LoadPanelPageCoreAsync(
                panelId,
                path,
                panelState.QueryText,
                cursor: 0,
                token);

            if (!loadResult.Ok)
            {
                if (IsRustAccessDenied(loadResult.RustErrorCode, loadResult.RustErrorMessage))
                {
                    return new SimplePanelLoadResult([], GetPanelActiveEntryResultSet(panelId), 0, false, 0);
                }

                throw new InvalidOperationException($"Rust error {loadResult.RustErrorCode}: {loadResult.RustErrorMessage}");
            }

            SetPanelLastFetchMs(panelId, (uint)Math.Clamp(loadResult.ElapsedMilliseconds, 0, int.MaxValue));
            return new SimplePanelLoadResult(
                loadResult.VisibleRows.Select(row => CreateLoadedEntryModel(path, row)).ToList(),
                GetPanelActiveEntryResultSet(panelId),
                loadResult.Page.NextCursor,
                loadResult.Page.HasMore,
                !loadResult.Page.HasMore || loadResult.Page.TotalEntries == 0
                    ? (uint)loadResult.VisibleRows.Count
                    : loadResult.Page.TotalEntries);
        }

        private async Task LoadNextSimplePanelPageAsync(WorkspacePanelId panelId)
        {
            if (panelId == WorkspacePanelId.Secondary && !_isDualPaneEnabled)
            {
                return;
            }

            PanelViewState panelState = _workspaceLayoutHost.GetPanelState(panelId);
            if (panelState.DataSession.IsLoading || !panelState.DataSession.HasMore)
            {
                return;
            }

            string path = string.IsNullOrWhiteSpace(panelState.CurrentPath)
                ? ShellMyComputerPath
                : panelState.CurrentPath;
            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            panelState.DataSession.IsLoading = true;
            if (panelId == WorkspacePanelId.Secondary)
            {
                RaiseSecondaryPaneDataStateChanged();
            }

            try
            {
                LogDetailsViewportPerf(
                    panelId == WorkspacePanelId.Secondary ? "secondary-append.begin" : "panel-append.begin",
                    $"cursor={panelState.DataSession.NextCursor} entries={panelState.DataSession.Entries.Count} total={panelState.DataSession.TotalEntries}");
                PanelPageLoadCoreResult loadResult = await LoadPanelPageCoreAsync(
                    panelId,
                    path,
                    panelState.QueryText,
                    panelState.DataSession.NextCursor,
                    CancellationToken.None);

                if (!loadResult.Ok)
                {
                    if (IsRustAccessDenied(loadResult.RustErrorCode, loadResult.RustErrorMessage))
                    {
                        SetPanelHasMore(panelId, false);
                        SetPanelNextCursor(panelId, 0);
                        return;
                    }

                    throw new InvalidOperationException($"Rust error {loadResult.RustErrorCode}: {loadResult.RustErrorMessage}");
                }

                SetPanelLastFetchMs(panelId, (uint)Math.Clamp(loadResult.ElapsedMilliseconds, 0, int.MaxValue));
                uint totalEntries = !loadResult.Page.HasMore || loadResult.Page.TotalEntries == 0
                    ? Math.Max(GetPanelTotalEntries(panelId), checked((uint)Math.Min(uint.MaxValue, panelState.DataSession.NextCursor + (ulong)loadResult.VisibleRows.Count)))
                    : loadResult.Page.TotalEntries;
                SetPanelTotalEntries(panelId, totalEntries);
                ApplyPanelPageRows(panelId, path, loadResult.VisibleRows, append: true);
                SetPanelNextCursor(panelId, loadResult.Page.NextCursor);
                SetPanelHasMore(panelId, loadResult.Page.HasMore);
                RefreshPanelStatus(panelId);
                if (panelId == WorkspacePanelId.Secondary)
                {
                    UpdateSecondaryEntrySelectionVisuals();
                }
                LogDetailsViewportPerf(
                    panelId == WorkspacePanelId.Secondary ? "secondary-append.end" : "panel-append.end",
                    $"rows={loadResult.VisibleRows.Count} entries={panelState.DataSession.Entries.Count} total={panelState.DataSession.TotalEntries} hasMore={panelState.DataSession.HasMore} next={panelState.DataSession.NextCursor}");
                if (panelId == WorkspacePanelId.Secondary)
                {
                    RaiseSecondaryPaneDataStateChanged();
                }
            }
            finally
            {
                panelState.DataSession.IsLoading = false;
                if (panelId == WorkspacePanelId.Secondary)
                {
                    RaiseSecondaryPaneDataStateChanged();
                }
            }
        }

        private void ApplySimplePanelLoadResult(
            WorkspacePanelId panelId,
            PanelViewState panelState,
            SimplePanelLoadResult loadResult,
            bool preserveViewport,
            double preservedVerticalOffset,
            bool ensureSelectionVisible,
            bool focusEntries)
        {
            PanelDataSession session = panelState.DataSession;
            session.GroupedEntryColumns.Clear();
            session.ActiveEntryResultSet = loadResult.ActiveEntryResultSet;
            session.NextCursor = loadResult.NextCursor;
            session.HasMore = loadResult.HasMore;
            session.TotalEntries = loadResult.TotalEntries;
            session.Entries.Clear();
            int logicalCount = checked((int)Math.Min(
                int.MaxValue,
                Math.Max(loadResult.TotalEntries, (uint)loadResult.Entries.Count)));
            EnsurePanelPlaceholderCount(panelId, logicalCount);
            FillPanelLoadedEntries(panelId, 0, loadResult.Entries);
            SetPanelPresentationSourceEntries(panelId, GetLoadedPanelEntries(panelId));
            MarkPanelDataLoadedForCurrentNavigation(panelId);
            InvalidatePanelDetailsViewportRealization(panelId);
            RefreshPanelStatus(panelId);
            if (panelId == WorkspacePanelId.Secondary)
            {
                ConfigureDirectoryWatcher(panelId, panelState.CurrentPath);
            }

            ApplySimplePanelPresentationAfterLoad(panelId, panelState);

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (!preserveViewport)
                {
                    ResetPanelViewportState(panelId);
                }

                GetPanelViewportScrollViewer(panelId).UpdateLayout();
                if (preserveViewport)
                {
                    RestorePanelDetailsVerticalOffset(panelId, preservedVerticalOffset);
                }

                ApplySimplePanelPostLoadUi(panelId, panelState, ensureSelectionVisible, focusEntries);
            });
        }

        private void ApplySimplePanelPresentationAfterLoad(WorkspacePanelId panelId, PanelViewState panelState)
        {
            NormalizePanelSelectionPaths(panelId, panelState);
            ApplySimplePanelPresentation(panelId, panelState);
            UpdateSimplePanelSelectionVisuals(panelId);
            RaiseSimplePanelDataStateChanged(panelId);

            if (panelId == WorkspacePanelId.Secondary)
            {
                _ = DispatcherQueue.TryEnqueue(RefreshRealizedSecondaryDetailsRowColumnWidths);
            }
        }

        private void ApplySimplePanelPostLoadUi(
            WorkspacePanelId panelId,
            PanelViewState panelState,
            bool ensureSelectionVisible,
            bool focusEntries)
        {
            if (!string.IsNullOrWhiteSpace(panelState.SelectedEntryPath))
            {
                RestorePanelSelectionByPath(panelId, ensureSelectionVisible);
            }

            if (focusEntries)
            {
                FocusPanelEntriesList(panelId);
            }
        }

        private void NormalizePanelSelectionPaths(WorkspacePanelId panelId, PanelViewState panelState)
        {
            if (panelId != WorkspacePanelId.Secondary)
            {
                return;
            }

            panelState.SelectedEntryPath = ResolveExistingPanelEntryPath(panelId, panelState.SelectedEntryPath);
            panelState.FocusedEntryPath = ResolveExistingPanelEntryPath(panelId, panelState.FocusedEntryPath);
            panelState.SelectedEntryPaths.RemoveWhere(path => ResolveExistingPanelEntryPath(panelId, path) is null);

            if (string.IsNullOrWhiteSpace(panelState.SelectedEntryPath))
            {
                panelState.FocusedEntryPath = null;
                if (panelState.SelectedEntryPaths.Count > 0)
                {
                    panelState.SelectedEntryPath = panelState.SelectedEntryPaths.FirstOrDefault();
                    panelState.FocusedEntryPath = panelState.SelectedEntryPath;
                }
            }
        }

        private string? ResolveExistingPanelEntryPath(WorkspacePanelId panelId, string? targetPath)
        {
            if (panelId != WorkspacePanelId.Secondary)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return null;
            }

            foreach (EntryViewModel entry in SecondaryPanelState.DataSession.Entries)
            {
                if (!entry.IsGroupHeader &&
                    entry.IsLoaded &&
                    string.Equals(entry.FullPath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.FullPath;
                }
            }

            return null;
        }

        private void ApplySimplePanelPresentation(WorkspacePanelId panelId, PanelViewState panelState)
        {
            if (panelId != WorkspacePanelId.Secondary)
            {
                return;
            }

            ApplySecondaryPanePresentation(panelState);
        }

        private void UpdateSimplePanelSelectionVisuals(WorkspacePanelId panelId)
        {
            if (panelId != WorkspacePanelId.Secondary)
            {
                return;
            }

            UpdateSecondaryEntrySelectionVisuals();
        }

        private void RaiseSimplePanelDataStateChanged(WorkspacePanelId panelId)
        {
            if (panelId != WorkspacePanelId.Secondary)
            {
                return;
            }

            RaiseSecondaryPaneDataStateChanged();
        }

        private void RestorePanelDetailsVerticalOffset(WorkspacePanelId panelId, double verticalOffset)
        {
            double safeOffset = NormalizeViewportOffset(verticalOffset);
            ScrollViewer viewer = GetPanelDetailsScrollViewer(panelId);
            double maxOffset = Math.Max(0, viewer.ScrollableHeight);
            viewer.ChangeView(
                null,
                Math.Min(maxOffset, safeOffset),
                null,
                disableAnimation: true);
        }

        private void RestorePanelSelectionByPath(WorkspacePanelId panelId, bool ensureVisible)
        {
            if (panelId != WorkspacePanelId.Secondary)
            {
                return;
            }

            string? selectedPath = SecondaryPanelState.SelectedEntryPath;
            if (string.IsNullOrWhiteSpace(selectedPath))
            {
                return;
            }

            foreach (EntryViewModel entry in SecondaryPanelState.DataSession.Entries)
            {
                if (!entry.IsGroupHeader &&
                    entry.IsLoaded &&
                    string.Equals(entry.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    SecondarySelectEntryInList(entry);
                    if (ensureVisible && !IsEntryFullyVisible(entry, SecondaryEntriesScrollViewer))
                    {
                        SecondaryScrollEntryIntoView(entry);
                    }

                    return;
                }
            }
        }

        private void FocusPanelEntriesList(WorkspacePanelId panelId)
        {
            if (panelId != WorkspacePanelId.Secondary)
            {
                return;
            }

            FocusSecondaryEntriesList();
        }

        private List<EntryViewModel> CreateMyComputerDriveEntries()
        {
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

            return drives;
        }

        private void SecondarySelectEntryInList(EntryViewModel entry)
        {
            if (entry.IsGroupHeader)
            {
                return;
            }

            string entryPath = GetPaneEntryPath(WorkspacePanelId.Secondary, entry);
            SetPanelSingleSelectionPath(WorkspacePanelId.Secondary, entryPath, entryPath);
            UpdateSecondaryEntrySelectionVisuals();
        }

        private async Task ExecuteRenameSecondaryContextTargetAsync(FileCommandTarget target)
        {
            await ExecuteRenameTargetForPaneCoreAsync(WorkspacePanelId.Secondary, target);
        }

        private async Task<string?> ShowSecondaryRenameDialogAsync(string oldName, string proposedName)
        {
            TextBox textBox = new()
            {
                Text = proposedName
            };

            var panel = new StackPanel
            {
                Spacing = 12
            };
            panel.Children.Add(new TextBlock
            {
                Text = SF("DialogRenameBody", oldName),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(textBox);

            ContentDialog dialog = new()
            {
                Title = S("DialogRenameTitle"),
                Content = panel,
                PrimaryButtonText = S("DialogRenamePrimaryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };

            PrepareWindowDialog(dialog);

            ContentDialogResult result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary ? textBox.Text : null;
        }

        private void UpdateSecondaryEntrySelectionVisuals()
        {
            string? selectedPath = SecondaryPanelState.SelectedEntryPath;
            string? focusedPath = SecondaryPanelState.FocusedEntryPath;
            HashSet<string> selectedPaths = SecondaryPanelState.SelectedEntryPaths;
            bool isActive = IsSecondaryWorkspacePanelActive;

            foreach (EntryViewModel entry in SecondaryPanelState.DataSession.Entries)
            {
                string entryPath = GetPaneEntryPath(WorkspacePanelId.Secondary, entry);
                entry.IsExplicitlySelected = !entry.IsGroupHeader &&
                    ((selectedPaths.Count > 0 && selectedPaths.Contains(entryPath)) ||
                        (selectedPaths.Count == 0 &&
                            !string.IsNullOrWhiteSpace(selectedPath) &&
                            string.Equals(entryPath, selectedPath, StringComparison.OrdinalIgnoreCase)));
                entry.IsKeyboardAnchor = !entry.IsGroupHeader &&
                    !string.IsNullOrWhiteSpace(focusedPath) &&
                    string.Equals(entryPath, focusedPath, StringComparison.OrdinalIgnoreCase);
                entry.IsSelectionActive = isActive;
            }
        }

        private void SecondaryEntryRow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement row || row.DataContext is not EntryViewModel entry)
            {
                return;
            }

            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                return;
            }

            CancelRenameOverlayForPanelSwitch(WorkspacePanelId.Secondary);
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            if (IsShiftPressed())
            {
                SelectPanelEntryRange(
                    WorkspacePanelId.Secondary,
                    GetSecondarySelectableEntriesInPresentationOrder(),
                    entry);
                UpdateSecondaryEntrySelectionVisuals();
                UpdateFileCommandStates();
            }
            else if (IsControlPressed())
            {
                TogglePanelSelectionPath(WorkspacePanelId.Secondary, GetPaneEntryPath(WorkspacePanelId.Secondary, entry));
                UpdateSecondaryEntrySelectionVisuals();
                UpdateFileCommandStates();
            }
            else
            {
                SecondarySelectEntryInList(entry);
            }
            SecondaryEntriesScrollViewer.Focus(FocusState.Pointer);
            e.Handled = true;
        }

        private void SecondaryEntriesView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            _lastEntriesContextItem = null;
            if (!string.IsNullOrWhiteSpace(SecondaryPanelState.SelectedEntryPath) ||
                !string.IsNullOrWhiteSpace(SecondaryPanelState.FocusedEntryPath) ||
                SecondaryPanelState.SelectedEntryPaths.Count > 0)
            {
                SecondaryPanelState.SelectedEntryPath = null;
                SecondaryPanelState.FocusedEntryPath = null;
                SecondaryPanelState.SelectedEntryPaths.Clear();
                UpdateSecondaryEntrySelectionVisuals();
            }

            ShowEntriesContextFlyout(new EntriesContextRequest(
                SecondaryEntriesScrollViewer,
                e.GetPosition(SecondaryEntriesScrollViewer),
                null,
                false,
                EntriesContextOrigin.SecondaryEntriesList));
            e.Handled = true;
        }

        private void SecondaryEntryRow_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            if (sender is not FrameworkElement row || row.DataContext is not EntryViewModel entry)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            if (!SecondaryPanelState.SelectedEntryPaths.Contains(GetPaneEntryPath(WorkspacePanelId.Secondary, entry)))
            {
                SecondarySelectEntryInList(entry);
            }
            _lastEntriesContextItem = entry;

            ShowEntriesContextFlyout(new EntriesContextRequest(
                row,
                e.GetPosition(row),
                entry,
                true,
                EntriesContextOrigin.SecondaryEntriesList));
            e.Handled = true;
        }

        private async void SecondaryEntriesView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            FrameworkElement? row = FindAncestorWithDataContext<EntryViewModel>(source);
            if (row?.DataContext is not EntryViewModel entry || !entry.IsLoaded)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            SecondarySelectEntryInList(entry);
            if (entry.IsDirectory)
            {
                await NavigatePanelToPathAsync(WorkspacePanelId.Secondary, entry.FullPath, pushHistory: true);
                return;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = entry.FullPath,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private async Task ActivateSecondaryEntryAsync(EntryViewModel entry)
        {
            if (entry is null || !entry.IsLoaded)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.SecondaryPane);
            SecondarySelectEntryInList(entry);
            if (entry.IsDirectory)
            {
                await NavigatePanelToPathAsync(WorkspacePanelId.Secondary, entry.FullPath, pushHistory: true);
                return;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = entry.FullPath,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void SecondaryScrollEntryIntoView(EntryViewModel entry)
        {
            if (!TryGetSecondaryEntryAnchor(entry, out FrameworkElement? element))
            {
                return;
            }

            FrameworkElement anchor = element!;
            anchor.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = false
            });
        }

        private bool TryGetSecondaryEntryAnchor(EntryViewModel entry, out FrameworkElement? element)
        {
            element = null;
            int index = SecondaryPanelState.DataSession.Entries.IndexOf(entry);
            if (index < 0)
            {
                return false;
            }

            if (SecondaryEntriesRepeater.TryGetElement(index) is not FrameworkElement realized)
            {
                return false;
            }

            element = realized;
            return true;
        }

        private static FrameworkElement? FindAncestorWithDataContext<T>(DependencyObject source)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is FrameworkElement element && element.DataContext is T)
                {
                    return element;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private sealed record SimplePanelLoadResult(
            List<EntryViewModel> Entries,
            IEntryResultSet? ActiveEntryResultSet,
            ulong NextCursor,
            bool HasMore,
            uint TotalEntries);
    }
}
