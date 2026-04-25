using NorthFileUI.Services;
using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private void InitializeSecondaryPanelStateFromPrimary()
        {
            PanelViewState primary = _workspaceLayoutHost.ShellState.Primary;
            PanelViewState secondary = _workspaceLayoutHost.ShellState.Secondary;

            secondary.CopyNonDataStateFrom(primary);
            secondary.AddressText = string.IsNullOrWhiteSpace(primary.AddressText)
                ? GetDisplayPathText(primary.CurrentPath)
                : primary.AddressText;
            UpdateSecondaryPaneBreadcrumbs(secondary.CurrentPath);
        }

        private void ActivateWorkspacePanel(WorkspacePanelId panelId)
        {
            SetActiveSelectionSurface(panelId == WorkspacePanelId.Primary
                ? SelectionSurfaceId.PrimaryPane
                : SelectionSurfaceId.SecondaryPane);
        }

        private void ApplyExplorerPaneLayout()
        {
            GridLength primaryWidth = GetExplorerPanePrimaryColumnWidth();
            GridLength secondaryWidth = _isDualPaneEnabled
                ? GetExplorerPaneSecondaryColumnWidth()
                : new GridLength(0);

            GridLength actionRailWidth = _isDualPaneEnabled
                ? new GridLength(ExplorerPaneActionRailWidthValue)
                : new GridLength(0);

            if (ToolbarActionRailColumn is not null)
            {
                ToolbarActionRailColumn.Width = actionRailWidth;
            }

            if (ExplorerPaneActionRailColumn is not null)
            {
                ExplorerPaneActionRailColumn.Width = actionRailWidth;
            }

            if (SecondaryToolbarPaneColumn is not null)
            {
                SecondaryToolbarPaneColumn.Width = secondaryWidth;
            }

            if (SecondaryPaneColumn is not null)
            {
                SecondaryPaneColumn.Width = secondaryWidth;
            }

            if (SecondaryStatusPaneColumn is not null)
            {
                SecondaryStatusPaneColumn.Width = secondaryWidth;
            }

            if (PrimaryToolbarPaneColumn is not null)
            {
                PrimaryToolbarPaneColumn.Width = primaryWidth;
            }

            if (PrimaryPaneColumn is not null)
            {
                PrimaryPaneColumn.Width = primaryWidth;
            }

            if (PrimaryStatusPaneColumn is not null)
            {
                PrimaryStatusPaneColumn.Width = primaryWidth;
            }

            if (ExplorerPaneActionRailStatusColumn is not null)
            {
                ExplorerPaneActionRailStatusColumn.Width = actionRailWidth;
            }

            if (ExplorerPaneActionToolbarHost is not null)
            {
                ExplorerPaneActionToolbarHost.Visibility = _isDualPaneEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (ExplorerPaneActionRailHost is not null)
            {
                ExplorerPaneActionRailHost.Visibility = _isDualPaneEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (SecondaryToolbarPaneHost is not null)
            {
                SecondaryToolbarPaneHost.Visibility = _isDualPaneEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (SecondaryPaneHost is not null)
            {
                SecondaryPaneHost.Visibility = _isDualPaneEnabled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void ExplorerPaneToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SetDualPaneEnabled(!_isDualPaneEnabled);
        }

        private async void PrimaryPaneCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            _workspaceLayoutHost.ShellState.Primary.CopyNonDataStateFrom(_workspaceLayoutHost.ShellState.Secondary);
            SetDualPaneEnabled(false);
            await ReloadPanelDataAsync(
                WorkspacePanelId.Primary,
                preserveViewport: true,
                ensureSelectionVisible: false,
                focusEntries: true);
        }

        private void SecondaryPaneCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            SetDualPaneEnabled(false);
        }

        private async void SyncPrimaryPaneToSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            await SyncPanePathAsync(WorkspacePanelId.Primary, WorkspacePanelId.Secondary);
        }

        private async void CopySelectedPrimaryPaneToSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Primary,
                WorkspacePanelId.Secondary,
                transferAllLoadedEntries: false,
                FileTransferMode.Copy);
        }

        private async void CopyAllPrimaryPaneToSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Primary,
                WorkspacePanelId.Secondary,
                transferAllLoadedEntries: true,
                FileTransferMode.Copy);
        }

        private async void MoveSelectedPrimaryPaneToSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Primary,
                WorkspacePanelId.Secondary,
                transferAllLoadedEntries: false,
                FileTransferMode.Cut);
        }

        private async void MoveAllPrimaryPaneToSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Primary,
                WorkspacePanelId.Secondary,
                transferAllLoadedEntries: true,
                FileTransferMode.Cut);
        }

        private async void SyncSecondaryPaneToPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            await SyncPanePathAsync(WorkspacePanelId.Secondary, WorkspacePanelId.Primary);
        }

        private async void CopySelectedSecondaryPaneToPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Secondary,
                WorkspacePanelId.Primary,
                transferAllLoadedEntries: false,
                FileTransferMode.Copy);
        }

        private async void CopyAllSecondaryPaneToPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Secondary,
                WorkspacePanelId.Primary,
                transferAllLoadedEntries: true,
                FileTransferMode.Copy);
        }

        private async void MoveSelectedSecondaryPaneToPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Secondary,
                WorkspacePanelId.Primary,
                transferAllLoadedEntries: false,
                FileTransferMode.Cut);
        }

        private async void MoveAllSecondaryPaneToPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            await TransferPaneEntriesToPaneAsync(
                WorkspacePanelId.Secondary,
                WorkspacePanelId.Primary,
                transferAllLoadedEntries: true,
                FileTransferMode.Cut);
        }

        private async void SwapExplorerPanesButton_Click(object sender, RoutedEventArgs e)
        {
            await SwapExplorerPanesAsync();
        }

        private async Task TransferPaneEntriesToPaneAsync(
            WorkspacePanelId sourcePanelId,
            WorkspacePanelId targetPanelId,
            bool transferAllLoadedEntries,
            FileTransferMode transferMode)
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            if (!TryGetTransferTargetDirectory(targetPanelId, out string targetDirectoryPath))
            {
                return;
            }

            if (transferAllLoadedEntries &&
                !await ConfirmTransferAllPaneEntriesAsync(sourcePanelId, targetDirectoryPath, transferMode))
            {
                return;
            }

            IReadOnlyList<string> sourcePaths = await GetTransferSourcePathsForPaneAsync(
                sourcePanelId,
                transferAllLoadedEntries);

            if (sourcePaths.Count == 0)
            {
                bool isDriveRoot = IsPaneTransferSourceDriveRoot(sourcePanelId);
                string statusKey = transferMode == FileTransferMode.Cut
                    ? (isDriveRoot ? "StatusCutFailedDriveRootsUnsupported" : "StatusCutFailedSelectLoaded")
                    : (isDriveRoot ? "StatusCopyFailedDriveRootsUnsupported" : "StatusCopyFailedSelectLoaded");
                UpdateStatusKey(statusKey);
                return;
            }

            _fileManagementCoordinator.SetClipboard(sourcePaths, transferMode);
            if (!_fileManagementCoordinator.HasClipboardItems)
            {
                UpdateStatusKey(transferMode == FileTransferMode.Cut
                    ? "StatusCutFailedSelectLoaded"
                    : "StatusCopyFailedSelectLoaded");
                return;
            }

            UpdateFileCommandStates();
            bool deferContentRefresh = transferAllLoadedEntries && transferMode == FileTransferMode.Cut;
            string sourceDirectoryPath = GetPanelCurrentPath(sourcePanelId);
            using IDisposable? watcherSuppression = deferContentRefresh
                ? SuppressWatcherRefreshesUntilDisposed(sourceDirectoryPath, targetDirectoryPath)
                : null;

            await ExecutePasteIntoPaneDirectoryCoreAsync(
                targetPanelId,
                targetDirectoryPath,
                selectPastedEntry: true,
                deferContentRefresh: deferContentRefresh);
        }

        private async Task<bool> ConfirmTransferAllPaneEntriesAsync(
            WorkspacePanelId sourcePanelId,
            string targetDirectoryPath,
            FileTransferMode transferMode)
        {
            string sourceDirectoryPath = GetPanelCurrentPath(sourcePanelId);
            if (string.IsNullOrWhiteSpace(sourceDirectoryPath) ||
                string.Equals(sourceDirectoryPath, ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            EnsureOperationFeedbackOverlay();
            if (_operationFeedbackDialog is null)
            {
                return false;
            }

            bool isMove = transferMode == FileTransferMode.Cut;
            Controls.ModalActionDialogResult result = await _operationFeedbackDialog.ShowAsync(
                S(isMove ? "PaneTransferAllMoveConfirmTitle" : "PaneTransferAllCopyConfirmTitle"),
                SF(
                    isMove ? "PaneTransferAllMoveConfirmMessage" : "PaneTransferAllCopyConfirmMessage",
                    GetDisplayPathText(sourceDirectoryPath),
                    GetDisplayPathText(targetDirectoryPath)),
                S(isMove ? "PaneTransferAllMoveConfirmPrimaryButton" : "PaneTransferAllCopyConfirmPrimaryButton"),
                S("DialogCancelButton"));
            return result == Controls.ModalActionDialogResult.Primary;
        }

        private bool TryGetTransferTargetDirectory(WorkspacePanelId targetPanelId, out string targetDirectoryPath)
        {
            targetDirectoryPath = GetPanelCurrentPath(targetPanelId);
            if (string.IsNullOrWhiteSpace(targetDirectoryPath) ||
                string.Equals(targetDirectoryPath, ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatusKey("StatusPasteFailedOpenFolderFirst");
                return false;
            }

            if (!_explorerService.DirectoryExists(targetDirectoryPath))
            {
                UpdateStatusKey("StatusPasteFailedWithReason", S("ErrorCurrentFolderUnavailable"));
                return false;
            }

            return true;
        }

        private IReadOnlyList<string> GetSelectedTransferSourcePathsForPane(WorkspacePanelId sourcePanelId)
        {
            if (string.Equals(GetPanelCurrentPath(sourcePanelId), ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            List<EntryViewModel> selectedEntries = GetSelectedLoadedEntriesForPane(sourcePanelId);
            if (selectedEntries.Count == 0)
            {
                return [];
            }

            return selectedEntries
                .Select(entry => GetPaneEntryPath(sourcePanelId, entry))
                .Where(path => !string.IsNullOrWhiteSpace(path) && !IsDriveRoot(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<IReadOnlyList<string>> GetTransferSourcePathsForPaneAsync(
            WorkspacePanelId sourcePanelId,
            bool transferAllEntries)
        {
            return transferAllEntries
                ? await GetAllTransferSourcePathsForPaneAsync(sourcePanelId)
                : GetSelectedTransferSourcePathsForPane(sourcePanelId);
        }

        private async Task<IReadOnlyList<string>> GetAllTransferSourcePathsForPaneAsync(WorkspacePanelId sourcePanelId)
        {
            string sourceDirectoryPath = GetPanelCurrentPath(sourcePanelId);
            if (string.Equals(sourceDirectoryPath, ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            if (string.IsNullOrWhiteSpace(GetPanelQueryText(sourcePanelId)))
            {
                try
                {
                    return await Task.Run(() => Directory
                        .EnumerateFileSystemEntries(sourceDirectoryPath)
                        .Where(path =>
                        {
                            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                            return !string.IsNullOrWhiteSpace(name) &&
                                ShouldIncludeEntry(path, name) &&
                                !IsDriveRoot(path);
                        })
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList());
                }
                catch (Exception ex)
                {
                    UpdateStatusKey("StatusPasteFailedWithReason", FileOperationErrors.ToUserMessage(ex));
                    return [];
                }
            }

            PanelEntriesLoadResult loadResult = await LoadPanelEntriesSnapshotAsync(
                sourceDirectoryPath,
                GetPanelQueryText(sourcePanelId),
                GetPanelLastFetchMs(sourcePanelId),
                CancellationToken.None,
                perfPrefix: "pane-transfer-source");

            return loadResult.Entries
                .Where(entry => entry.IsLoaded && !entry.IsGroupHeader)
                .Select(entry => GetPaneEntryPath(sourcePanelId, entry))
                .Where(path => !string.IsNullOrWhiteSpace(path) && !IsDriveRoot(path))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool IsPaneTransferSourceDriveRoot(WorkspacePanelId sourcePanelId)
        {
            if (string.Equals(GetPanelCurrentPath(sourcePanelId), ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return TryGetSelectedLoadedEntryForPane(sourcePanelId, out EntryViewModel? entry) &&
                IsDriveRoot(GetPaneEntryPath(sourcePanelId, entry));
        }

        private async Task SyncPanePathAsync(WorkspacePanelId sourcePanelId, WorkspacePanelId targetPanelId)
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            string sourcePath = _workspaceLayoutHost.GetPanelState(sourcePanelId).CurrentPath;
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                sourcePath = ShellMyComputerPath;
            }

            PanelViewState targetPanel = _workspaceLayoutHost.GetPanelState(targetPanelId);
            if (string.Equals(sourcePath, targetPanel.CurrentPath, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await NavigatePanelToPathAsync(
                targetPanelId,
                sourcePath,
                pushHistory: true,
                focusEntriesAfterNavigation: false);
        }

        private async Task SwapExplorerPanesAsync()
        {
            if (!_isDualPaneEnabled)
            {
                return;
            }

            WorkspaceShellState shellState = _workspaceLayoutHost.ShellState;
            PanelViewState primarySnapshot = shellState.Primary.Clone();
            PanelViewState secondarySnapshot = shellState.Secondary.Clone();

            shellState.Primary.CopyNonDataStateFrom(secondarySnapshot);
            shellState.Secondary.CopyNonDataStateFrom(primarySnapshot);
            shellState.ActivePanel = shellState.ActivePanel == WorkspacePanelId.Primary
                ? WorkspacePanelId.Secondary
                : WorkspacePanelId.Primary;

            RebindPrimaryPaneDataSession();
            RebindSecondaryPaneDataSession();
            SetPrimaryPanelNavigationState(
                shellState.Primary.CurrentPath,
                shellState.Primary.QueryText,
                shellState.Primary.AddressText,
                syncEditors: true);
            UpdateBreadcrumbs(shellState.Primary.CurrentPath);
            UpdateSecondaryPaneBreadcrumbs(shellState.Secondary.CurrentPath);
            RaisePanelColumnLayoutPropertiesChanged();
            RaiseSecondaryPaneStateChanged(navigationChanged: true, dataChanged: true);
            RaiseWorkspacePanelShellPropertiesChanged();
            RaisePanelNavigationStateChanged(WorkspacePanelId.Primary);
            RaisePanelNavigationStateChanged(WorkspacePanelId.Secondary);
            NotifyTitleBarTextChanged();

            await RestorePanelStateAsync(
                WorkspacePanelId.Primary,
                preserveViewport: false,
                ensureSelectionVisible: false,
                focusEntries: false);
            await RestorePanelStateAsync(
                WorkspacePanelId.Secondary,
                preserveViewport: false,
                ensureSelectionVisible: false,
                focusEntries: false);

            ActivateWorkspacePanel(shellState.ActivePanel);
        }

        private void SetDualPaneEnabled(bool enabled)
        {
            if (_isDualPaneEnabled == enabled)
            {
                return;
            }

            _isDualPaneEnabled = enabled;
            _workspaceLayoutHost.LayoutMode = enabled
                ? WorkspaceLayoutMode.SplitVertical
                : WorkspaceLayoutMode.Single;

            if (enabled)
            {
                InitializeSecondaryPanelStateFromPrimary();
            }

            if (!enabled)
            {
                DisposeDirectoryWatcher(WorkspacePanelId.Secondary);
                ActivateWorkspacePanel(WorkspacePanelId.Primary);
            }
            else
            {
                NotifyWorkspacePanelVisualStateChanged();
            }

            ApplyExplorerPaneLayout();
            if (enabled)
            {
                _ = ReloadPanelDataAsync(
                    WorkspacePanelId.Secondary,
                    preserveViewport: false,
                    ensureSelectionVisible: false,
                    focusEntries: false);
            }

            RaisePropertyChanged(
                nameof(PrimaryPaneSearchBoxWidth),
                nameof(PrimaryPaneSearchVisibility),
                nameof(ToolbarSearchWidth),
                nameof(SecondaryPaneSearchPlaceholderText),
                nameof(ExplorerPanePrimaryColumnWidth),
                nameof(ExplorerPaneToolbarActionRailColumnWidth),
                nameof(ExplorerPaneActionRailColumnWidth),
                nameof(ExplorerPaneSecondaryColumnWidth),
                nameof(ExplorerPaneToolbarActionRailVisibility),
                nameof(ExplorerPaneActionRailVisibility),
                nameof(ExplorerSecondaryPaneVisibility),
                nameof(ExplorerPaneToggleGlyph),
                nameof(ExplorerPaneToggleToolTipText));
            RaiseSecondaryPaneNavigationPropertiesChanged();
            RaiseWorkspacePanelShellPropertiesChanged();
            NotifyTitleBarTextChanged();
        }

        private GridLength GetExplorerPanePrimaryColumnWidth()
        {
            return new GridLength(NormalizePaneWidthWeight(_workspaceLayoutHost.ShellState.PrimaryPaneWidthWeight), GridUnitType.Star);
        }

        private GridLength GetExplorerPaneSecondaryColumnWidth()
        {
            return new GridLength(NormalizePaneWidthWeight(_workspaceLayoutHost.ShellState.SecondaryPaneWidthWeight), GridUnitType.Star);
        }

        private static double NormalizePaneWidthWeight(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0
                ? 1
                : value;
        }

        private async Task RestorePrimaryPanelStateAsync(PanelViewState panelState)
        {
            await RestorePrimaryPanelStateAsync(
                panelState,
                preserveViewport: true,
                ensureSelectionVisible: false,
                focusEntries: true);
        }

        private async Task RestorePrimaryPanelStateAsync(
            PanelViewState panelState,
            bool preserveViewport,
            bool ensureSelectionVisible,
            bool focusEntries)
        {
            LogPrimaryTabDataState("RestorePrimaryPanelStateAsync.enter");
            double detailsHorizontalOffset = preserveViewport
                ? GetCurrentDetailsHorizontalOffset()
                : panelState.LastDetailsHorizontalOffset;
            double detailsVerticalOffset = preserveViewport
                ? GetCurrentDetailsVerticalOffset()
                : panelState.LastDetailsVerticalOffset;
            double groupedHorizontalOffset = preserveViewport
                ? GetCurrentGroupedHorizontalOffset()
                : panelState.LastGroupedHorizontalOffset;

            RebindPrimaryPaneDataSession();
            SetPrimaryPanelPresentationState(
                panelState.ViewMode,
                panelState.SortField,
                panelState.SortDirection,
                panelState.GroupField);
            SetPrimaryPanelNavigationState(
                panelState.CurrentPath,
                panelState.QueryText,
                panelState.AddressText,
                syncEditors: true);
            UpdateBreadcrumbs(GetPanelCurrentPath(WorkspacePanelId.Primary));
            UpdateNavButtonsState();
            NotifyPresentationModeChanged();
            SyncActivePanelPresentationState();

            if (CanReusePanelDataForRestore(WorkspacePanelId.Primary, panelState))
            {
                WorkspaceTabPerf.Mark("primary.restore.reuse");
                LogPrimaryTabDataState("RestorePrimaryPanelStateAsync.reuse");
                FinalizePrimaryPanelRestore(
                    panelState,
                    preserveViewport,
                    detailsHorizontalOffset,
                    detailsVerticalOffset,
                    groupedHorizontalOffset,
                    ensureSelectionVisible,
                    focusEntries);
                return;
            }

            await LoadPrimaryPanelDataAsync(panelState);
            WorkspaceTabPerf.Mark("primary.restore.reload");
            LogPrimaryTabDataState("RestorePrimaryPanelStateAsync.reload");
            FinalizePrimaryPanelRestore(
                panelState,
                preserveViewport,
                detailsHorizontalOffset,
                detailsVerticalOffset,
                groupedHorizontalOffset,
                ensureSelectionVisible,
                focusEntries);
        }

        private void FinalizePrimaryPanelRestore(
            PanelViewState panelState,
            bool preserveViewport,
            double detailsHorizontalOffset,
            double detailsVerticalOffset,
            double groupedHorizontalOffset,
            bool ensureSelectionVisible,
            bool focusEntries)
        {
            UpdateFileCommandStates();
            _lastTitleWasReadFailed = false;
            UpdateWindowTitle();
            RefreshPanelStatus(WorkspacePanelId.Primary);

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                RestoreCurrentViewportOffsets(
                    detailsHorizontalOffset,
                    detailsVerticalOffset,
                    groupedHorizontalOffset);

                if (ensureSelectionVisible)
                {
                    RestoreListSelectionByPath(ensureVisible: true);
                }
                else
                {
                    RestoreListSelectionByPathRespectingViewport();
                }

                if (focusEntries)
                {
                    FocusEntriesList();
                }
            });
        }
    }
}
