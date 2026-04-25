using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private PanelColumnLayoutNotificationState CapturePanelColumnLayoutNotificationState()
        {
            return new PanelColumnLayoutNotificationState(
                _isDualPaneEnabled,
                PrimaryPanelState.NameColumnWidth,
                PrimaryPanelState.TypeColumnWidth,
                PrimaryPanelState.SizeColumnWidth,
                PrimaryPanelState.ModifiedColumnWidth,
                PrimaryPanelState.DetailsContentWidth,
                PrimaryPanelState.DetailsRowWidth,
                SecondaryPanelState.NameColumnWidth,
                SecondaryPanelState.TypeColumnWidth,
                SecondaryPanelState.SizeColumnWidth,
                SecondaryPanelState.ModifiedColumnWidth,
                SecondaryPanelState.DetailsContentWidth,
                SecondaryPanelState.DetailsRowWidth);
        }

        private void InitializeWorkspaceTabs()
        {
            _workspaceChromeCoordinator.RefreshTabs();
        }

        private void RefreshTitleBarTabs()
        {
            if (SingleTabTitleBarView is null)
            {
                return;
            }

            _workspaceChromeCoordinator.RefreshTabs();
        }

        private void RefreshActiveTitleBarTab()
        {
            if (SingleTabTitleBarView is null)
            {
                return;
            }

            _workspaceChromeCoordinator.RefreshActivePresentation();
        }

        private async void SingleTabTitleBarView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            await _workspaceChromeCoordinator.HandleSelectionChangedAsync(e);
        }

        private void ApplyWorkspaceShellStateToUi(WorkspaceShellState shellState)
        {
            LogPrimaryTabDataState("ApplyWorkspaceShellStateToUi.enter");
            WorkspaceTabPerf.Mark("mainwindow.apply-shell.enter", $"split={shellState.IsSplit}");
            bool splitModeChanged = _isDualPaneEnabled != shellState.IsSplit;
            _isDualPaneEnabled = shellState.IsSplit;
            ApplyExplorerPaneLayout();
            WorkspaceTabPerf.Mark("mainwindow.apply-shell.layout");
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
            WorkspaceTabPerf.Mark("mainwindow.apply-shell.properties");
            if (splitModeChanged)
            {
                RaisePropertyChanged(
                    nameof(IsPrimaryWorkspacePanelActive),
                    nameof(IsSecondaryWorkspacePanelActive));
                RaiseWorkspacePanelShellPropertiesChanged();
                WorkspaceTabPerf.Mark("mainwindow.apply-shell.visual");
            }

            if (shellState.IsSplit)
            {
                WorkspaceTabPerf.Mark("mainwindow.apply-shell.secondary-nav.deferred");
            }
            else
            {
                WorkspaceTabPerf.Mark("mainwindow.apply-shell.secondary-nav.skip");
            }
            LogPrimaryTabDataState("ApplyWorkspaceShellStateToUi.exit");
        }

        private void PrepareWorkspaceShellStateForRestore(WorkspaceShellState shellState)
        {
            LogPrimaryTabDataState("PrepareWorkspaceShellStateForRestore.enter");
            WorkspaceTabPerf.Mark("mainwindow.prepare.enter", $"split={shellState.IsSplit}");
            RebindPrimaryPaneDataSession();
            WorkspaceTabPerf.Mark("mainwindow.prepare.rebind-primary");
            PrimaryPresentationNotificationState previousPresentationState = CapturePrimaryPresentationNotificationState();
            PanelColumnLayoutNotificationState previousColumnLayoutState = CapturePanelColumnLayoutNotificationState();
            SetPrimaryPanelPresentationState(
                shellState.Primary.ViewMode,
                shellState.Primary.SortField,
                shellState.Primary.SortDirection,
                shellState.Primary.GroupField);
            WorkspaceTabPerf.Mark("mainwindow.prepare.presentation-state");
            SetPrimaryPanelNavigationState(
                shellState.Primary.CurrentPath,
                shellState.Primary.QueryText,
                shellState.Primary.AddressText,
                syncEditors: true);
            WorkspaceTabPerf.Mark("mainwindow.prepare.navigation-state");
            ClearPanelEntriesIfNavigationIsStale(WorkspacePanelId.Primary);
            WorkspaceTabPerf.Mark("mainwindow.prepare.clear-stale-primary");
            UpdateBreadcrumbs(GetPanelCurrentPath(WorkspacePanelId.Primary));
            WorkspaceTabPerf.Mark("mainwindow.prepare.breadcrumbs-primary");
            UpdateNavButtonsState();
            WorkspaceTabPerf.Mark("mainwindow.prepare.nav-buttons");
            UpdateDetailsHeaders();
            WorkspaceTabPerf.Mark("mainwindow.prepare.details-headers");
            RaisePanelColumnLayoutPropertiesChanged(previousColumnLayoutState);
            WorkspaceTabPerf.Mark("mainwindow.prepare.columns");
            NotifyPresentationModeChanged(previousPresentationState);
            WorkspaceTabPerf.Mark("mainwindow.prepare.presentation-mode");
            SyncActivePanelPresentationState();
            WorkspaceTabPerf.Mark("mainwindow.prepare.active-presentation");
            RefreshPanelStatus(WorkspacePanelId.Primary);
            WorkspaceTabPerf.Mark("mainwindow.prepare.status-primary");
            LogPrimaryTabDataState("PrepareWorkspaceShellStateForRestore.after-primary");

            if (!shellState.IsSplit)
            {
                WorkspaceTabPerf.Mark("mainwindow.prepare.secondary-single.skip");
                return;
            }

            RebindSecondaryPaneDataSession();
            WorkspaceTabPerf.Mark("mainwindow.prepare.secondary-rebind");
            ClearPanelEntriesIfNavigationIsStale(WorkspacePanelId.Secondary);
            WorkspaceTabPerf.Mark("mainwindow.prepare.clear-stale-secondary");
            UpdateSecondaryPaneBreadcrumbs(shellState.Secondary.CurrentPath);
            WorkspaceTabPerf.Mark("mainwindow.prepare.breadcrumbs-secondary");
            ApplySecondaryPanePresentation(shellState.Secondary);
            WorkspaceTabPerf.Mark("mainwindow.prepare.secondary-presentation");
            RaiseSecondaryPaneStateChanged(navigationChanged: true, dataChanged: true);
            WorkspaceTabPerf.Mark("mainwindow.prepare.secondary-state");
            RefreshPanelStatus(WorkspacePanelId.Secondary);
            WorkspaceTabPerf.Mark("mainwindow.prepare.status-secondary");
        }

        private void RaisePanelColumnLayoutPropertiesChanged(PanelColumnLayoutNotificationState? previousState = null)
        {
            if (previousState is null)
            {
                RaisePropertyChanged(
                    nameof(NameColumnWidth),
                    nameof(TypeColumnWidth),
                    nameof(SizeColumnWidth),
                    nameof(ModifiedColumnWidth),
                    nameof(DetailsContentWidth),
                    nameof(DetailsRowWidth),
                    nameof(SecondaryNameColumnWidth),
                    nameof(SecondaryTypeColumnWidth),
                    nameof(SecondarySizeColumnWidth),
                    nameof(SecondaryModifiedColumnWidth),
                    nameof(SecondaryDetailsContentWidth),
                    nameof(SecondaryDetailsRowWidth),
                    nameof(EntriesHorizontalScrollBarVisibility),
                    nameof(EntriesHorizontalScrollMode));
                return;
            }

            PanelColumnLayoutNotificationState currentState = CapturePanelColumnLayoutNotificationState();

            if (!AreClose(previousState.Value.PrimaryNameColumnWidth, currentState.PrimaryNameColumnWidth))
            {
                RaisePropertyChanged(nameof(NameColumnWidth));
            }

            if (!AreClose(previousState.Value.PrimaryTypeColumnWidth, currentState.PrimaryTypeColumnWidth))
            {
                RaisePropertyChanged(nameof(TypeColumnWidth));
            }

            if (!AreClose(previousState.Value.PrimarySizeColumnWidth, currentState.PrimarySizeColumnWidth))
            {
                RaisePropertyChanged(nameof(SizeColumnWidth));
            }

            if (!AreClose(previousState.Value.PrimaryModifiedColumnWidth, currentState.PrimaryModifiedColumnWidth))
            {
                RaisePropertyChanged(nameof(ModifiedColumnWidth));
            }

            bool primaryDetailsContentChanged = !AreClose(previousState.Value.PrimaryDetailsContentWidth, currentState.PrimaryDetailsContentWidth);
            bool primaryDetailsRowChanged = !AreClose(previousState.Value.PrimaryDetailsRowWidth, currentState.PrimaryDetailsRowWidth);
            if (primaryDetailsContentChanged)
            {
                RaisePropertyChanged(nameof(DetailsContentWidth));
            }

            if (primaryDetailsRowChanged)
            {
                RaisePropertyChanged(
                    nameof(DetailsRowWidth),
                    nameof(EntriesHorizontalScrollBarVisibility),
                    nameof(EntriesHorizontalScrollMode));
            }

            bool secondaryVisibleNow = currentState.IsSplit;
            bool secondaryVisibilityChanged = previousState.Value.IsSplit != currentState.IsSplit;
            if (secondaryVisibleNow)
            {
                if (secondaryVisibilityChanged || !AreClose(previousState.Value.SecondaryNameColumnWidth, currentState.SecondaryNameColumnWidth))
                {
                    RaisePropertyChanged(nameof(SecondaryNameColumnWidth));
                }

                if (secondaryVisibilityChanged || !AreClose(previousState.Value.SecondaryTypeColumnWidth, currentState.SecondaryTypeColumnWidth))
                {
                    RaisePropertyChanged(nameof(SecondaryTypeColumnWidth));
                }

                if (secondaryVisibilityChanged || !AreClose(previousState.Value.SecondarySizeColumnWidth, currentState.SecondarySizeColumnWidth))
                {
                    RaisePropertyChanged(nameof(SecondarySizeColumnWidth));
                }

                if (secondaryVisibilityChanged || !AreClose(previousState.Value.SecondaryModifiedColumnWidth, currentState.SecondaryModifiedColumnWidth))
                {
                    RaisePropertyChanged(nameof(SecondaryModifiedColumnWidth));
                }

                if (secondaryVisibilityChanged || !AreClose(previousState.Value.SecondaryDetailsContentWidth, currentState.SecondaryDetailsContentWidth))
                {
                    RaisePropertyChanged(nameof(SecondaryDetailsContentWidth));
                }

                if (secondaryVisibilityChanged || !AreClose(previousState.Value.SecondaryDetailsRowWidth, currentState.SecondaryDetailsRowWidth))
                {
                    RaisePropertyChanged(nameof(SecondaryDetailsRowWidth));
                }
            }
        }

        private static bool AreClose(double left, double right)
        {
            return Math.Abs(left - right) < 0.1;
        }

        private void AddWorkspaceTabButton_Click(object sender, RoutedEventArgs e)
        {
            _ = _workspaceTabController.OpenPathInNewTabAsync(null);
        }

        private void CloseWorkspaceTabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not WorkspaceTabState tab)
            {
                return;
            }

            _ = _workspaceTabController.CloseAsync(tab);
        }

    }
}
