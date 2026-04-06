using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private static void CopyPanelState(PanelViewState source, PanelViewState target)
        {
            target.CurrentPath = source.CurrentPath;
            target.AddressText = source.AddressText;
            target.QueryText = source.QueryText;
            target.SelectedEntryPath = source.SelectedEntryPath;
            target.ViewMode = source.ViewMode;
            target.SortField = source.SortField;
            target.SortDirection = source.SortDirection;
            target.GroupField = source.GroupField;
        }

        private void InitializeSecondaryPanelStateFromPrimary()
        {
            PanelViewState primary = _workspaceLayoutHost.ShellState.Primary;
            PanelViewState secondary = _workspaceLayoutHost.ShellState.Secondary;

            CopyPanelState(primary, secondary);
            secondary.AddressText = string.IsNullOrWhiteSpace(primary.AddressText)
                ? GetDisplayPathText(primary.CurrentPath)
                : primary.AddressText;
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

            CopyPanelState(_workspaceLayoutHost.ShellState.Secondary, _workspaceLayoutHost.ShellState.Primary);
            SetDualPaneEnabled(false);
            await RestorePrimaryPanelStateAsync(_workspaceLayoutHost.ShellState.Primary);
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
            RaisePropertyChanged(
                nameof(PrimaryPaneSearchBoxWidth),
                nameof(PrimaryPaneSearchVisibility),
                nameof(ToolbarSearchWidth),
                nameof(SecondaryPaneAddressText),
                nameof(SecondaryPaneAddressEditorText),
                nameof(SecondaryPaneSearchPlaceholderText),
                nameof(SecondaryPaneSearchText),
                nameof(SecondaryPanePlaceholderText),
                nameof(ExplorerPaneToolbarActionRailColumnWidth),
                nameof(ExplorerPaneActionRailColumnWidth),
                nameof(ExplorerPaneSecondaryColumnWidth),
                nameof(ExplorerPaneToolbarActionRailVisibility),
                nameof(ExplorerPaneActionRailVisibility),
                nameof(ExplorerSecondaryPaneVisibility),
                nameof(ExplorerPaneToggleGlyph),
                nameof(ExplorerPaneToggleToolTipText));
        }

        private async Task RestorePrimaryPanelStateAsync(PanelViewState panelState)
        {
            _currentViewMode = panelState.ViewMode;
            _currentSortField = panelState.SortField;
            _currentSortDirection = panelState.SortDirection;
            _currentGroupField = panelState.GroupField;
            _currentPath = string.IsNullOrWhiteSpace(panelState.CurrentPath)
                ? ShellMyComputerPath
                : panelState.CurrentPath;
            _currentQuery = panelState.QueryText ?? string.Empty;
            PathTextBox.Text = string.IsNullOrWhiteSpace(panelState.AddressText)
                ? GetDisplayPathText(_currentPath)
                : panelState.AddressText;
            SearchTextBox.Text = _currentQuery;
            UpdateBreadcrumbs(_currentPath);
            UpdateNavButtonsState();
            NotifyPresentationModeChanged();
            SyncActivePanelPresentationState();
            await LoadFirstPageAsync();
            ClearListSelection();
            _ = DispatcherQueue.TryEnqueue(FocusEntriesList);
        }
    }
}
