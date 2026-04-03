using FileExplorerUI.Workspace;
using System;
using System.ComponentModel;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        public double ToolbarSearchWidth
        {
            get
            {
                double width = !double.IsNaN(_lastWindowWidth) && _lastWindowWidth > 0
                    ? _lastWindowWidth
                    : 1200;

                return Math.Min(width * 0.22, ToolbarSearchMaxWidth);
            }
        }

        private void InitializeWorkspaceShellState()
        {
            _workspaceLayoutHost.LayoutMode = WorkspaceLayoutMode.Single;
            _workspaceLayoutHost.ActivatePanel(WorkspacePanelId.Primary);
            SyncActivePanelPresentationState();
        }

        private void RaisePropertyChanged(params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private bool UsesClientPresentationPipeline()
        {
            return _currentViewMode != EntryViewMode.Details
                || _currentSortField != EntrySortField.Name
                || _currentSortDirection != SortDirection.Ascending
                || _currentGroupField != EntryGroupField.None
                || RequiresClientSideEntryFiltering();
        }

        private bool RequiresClientSideEntryFiltering()
        {
            return _appSettings.ShowHiddenEntries
                || _appSettings.ShowProtectedSystemEntries
                || !_appSettings.ShowDotEntries;
        }
    }
}
