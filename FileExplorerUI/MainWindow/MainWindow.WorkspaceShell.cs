using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace FileExplorerUI
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
            GridLength secondaryWidth = _isDualPaneEnabled
                ? new GridLength(1, GridUnitType.Star)
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

            if (PrimaryToolbarPaneColumn is not null)
            {
                PrimaryToolbarPaneColumn.Width = new GridLength(1, GridUnitType.Star);
            }

            if (PrimaryPaneColumn is not null)
            {
                PrimaryPaneColumn.Width = new GridLength(1, GridUnitType.Star);
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
            if (string.Equals(panelState.CurrentPath, ShellMyComputerPath, System.StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus(SF("StatusDriveCount", GetPanelTotalEntries(WorkspacePanelId.Primary)));
            }
            else
            {
                UpdateStatus(SF("StatusCurrentFolderItems", GetPanelTotalEntries(WorkspacePanelId.Primary)));
            }

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
