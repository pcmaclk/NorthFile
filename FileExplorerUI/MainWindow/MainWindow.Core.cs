using FileExplorerUI.Workspace;
using System;
using System.ComponentModel;
using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

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

        public double PrimaryPaneSearchBoxWidth
        {
            get
            {
                if (_isDualPaneEnabled)
                {
                    return 80;
                }

                return ToolbarSearchWidth;
            }
        }

        public Visibility PrimaryPaneSearchVisibility => Visibility.Visible;

        public string DisplayAddressText => GetDisplayPathText(_currentPath);

        public string SecondaryPaneAddressText
        {
            get
            {
                string text = _workspaceLayoutHost.ShellState.Secondary.AddressText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                string path = _workspaceLayoutHost.ShellState.Secondary.CurrentPath;
                return string.IsNullOrWhiteSpace(path)
                    ? "次面板"
                    : GetDisplayPathText(path);
            }
        }

        public string SecondaryPaneAddressEditorText
        {
            get => SecondaryPaneAddressText;
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_workspaceLayoutHost.ShellState.Secondary.AddressText, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _workspaceLayoutHost.ShellState.Secondary.AddressText = normalized;
                RaisePropertyChanged(nameof(SecondaryPaneAddressText), nameof(SecondaryPaneAddressEditorText));
            }
        }

        public string SecondaryPaneSearchPlaceholderText => "搜索次面板";

        public string SecondaryPaneSearchText
        {
            get => _workspaceLayoutHost.ShellState.Secondary.QueryText;
            set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(_workspaceLayoutHost.ShellState.Secondary.QueryText, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _workspaceLayoutHost.ShellState.Secondary.QueryText = normalized;
                RaisePropertyChanged(nameof(SecondaryPaneSearchText));
            }
        }

        public string SecondaryPanePlaceholderText
        {
            get
            {
                string path = _workspaceLayoutHost.ShellState.Secondary.CurrentPath;
                return string.IsNullOrWhiteSpace(path)
                    ? "次面板占位区"
                    : $"次面板占位区: {GetDisplayPathText(path)}";
            }
        }

        public bool IsPrimaryWorkspacePanelActive => !_isDualPaneEnabled || _workspaceLayoutHost.ActivePanel == WorkspacePanelId.Primary;

        public bool IsSecondaryWorkspacePanelActive => _isDualPaneEnabled && _workspaceLayoutHost.ActivePanel == WorkspacePanelId.Secondary;

        public Brush? PrimaryPaneToolbarBackground => GetPanelBodyBackgroundBrush(IsPrimaryWorkspacePanelActive);

        public Brush? PrimaryPaneBodyBackground => GetPanelBodyBackgroundBrush(IsPrimaryWorkspacePanelActive);

        public Brush? PrimaryPaneInputBackground => GetPanelInputBackgroundBrush(IsPrimaryWorkspacePanelActive);

        public Brush? SecondaryPaneToolbarBackground => GetPanelBodyBackgroundBrush(IsSecondaryWorkspacePanelActive);

        public Brush? SecondaryPaneBodyBackground => GetPanelBodyBackgroundBrush(IsSecondaryWorkspacePanelActive);

        public Brush? SecondaryPaneInputBackground => GetPanelInputBackgroundBrush(IsSecondaryWorkspacePanelActive);

        public Brush? PrimaryPaneBorderBrush => GetPanelBorderBrush(IsPrimaryWorkspacePanelActive);

        public Brush? SecondaryPaneBorderBrush => GetPanelBorderBrush(IsSecondaryWorkspacePanelActive);

        public Vector3 PrimaryPaneBodyTranslation => IsPrimaryWorkspacePanelActive
            ? new Vector3(0, 0, 8)
            : Vector3.Zero;

        public Vector3 SecondaryPaneBodyTranslation => IsSecondaryWorkspacePanelActive
            ? new Vector3(0, 0, 8)
            : Vector3.Zero;

        public Visibility AddressTextFallbackVisibility => VisibleBreadcrumbs.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        private static Brush? GetPanelInputBackgroundBrush(bool isActive)
        {
            string key = isActive
                ? "TextControlBackground"
                : "LayerOnMicaBaseAltFillColorSecondaryBrush";

            return Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
                ? brush
                : null;
        }

        private static Brush? GetPanelBodyBackgroundBrush(bool isActive)
        {
            string key = isActive
                ? "LayerFillColorDefaultBrush"
                : "LayerOnMicaBaseAltFillColorSecondaryBrush";

            return Application.Current.Resources.TryGetValue(key, out object? value) && value is Brush brush
                ? brush
                : null;
        }

        private static Brush? GetPanelBorderBrush(bool isActive)
        {
            if (!isActive)
            {
                return new SolidColorBrush(Colors.Transparent);
            }

            return Application.Current.Resources.TryGetValue("ExplorerShellPanelBorderBrush", out object? value) && value is Brush brush
                ? brush
                : null;
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
