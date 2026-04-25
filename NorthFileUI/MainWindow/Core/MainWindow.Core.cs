using NorthFileUI.Workspace;
using System;
using System.ComponentModel;
using System.Numerics;
using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NorthFileUI.Collections;

namespace NorthFileUI
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

        public string DisplayAddressText => GetDisplayPathText(GetPanelCurrentPath(WorkspacePanelId.Primary));

        public ObservableCollection<BreadcrumbItemViewModel> SecondaryPaneBreadcrumbs => SecondaryPanelState.Navigation.Breadcrumbs;

        public ObservableCollection<BreadcrumbItemViewModel> SecondaryPaneVisibleBreadcrumbs => SecondaryPanelState.Navigation.VisibleBreadcrumbs;

        public string SecondaryPaneDisplayAddressText => GetDisplayPathText(SecondaryPanelState.CurrentPath);

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
                SetPanelAddressText(WorkspacePanelId.Secondary, value);
            }
        }

        public string SecondaryPaneSearchPlaceholderText => "搜索次面板";

        public string SecondaryPaneSearchText
        {
            get => GetPanelQueryText(WorkspacePanelId.Secondary);
            set => SetPanelQueryText(WorkspacePanelId.Secondary, value);
        }

        public string SecondaryPanePlaceholderText
        {
            get
            {
                if (SecondaryPanelState.DataSession.IsLoading &&
                    SecondaryPaneEntries.Count == 0)
                {
                    return S("SecondaryPanePlaceholderLoading");
                }

                string path = _workspaceLayoutHost.ShellState.Secondary.CurrentPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return S("SecondaryPanePlaceholderIdle");
                }

                return S("SecondaryPanePlaceholderEmptyFolder");
            }
        }

        public BatchObservableCollection<EntryViewModel> SecondaryPaneEntries => SecondaryPanelState.DataSession.Entries;

        public Visibility SecondaryPaneItemsVisibility => SecondaryPaneEntries.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility SecondaryPanePlaceholderVisibility
        {
            get
            {
                if (SecondaryPanelState.DataSession.IsLoading &&
                    SecondaryPaneEntries.Count == 0)
                {
                    return Visibility.Visible;
                }

                string path = _workspaceLayoutHost.ShellState.Secondary.CurrentPath;
                return string.IsNullOrWhiteSpace(path) || SecondaryPaneEntries.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public Visibility SecondaryDetailsHeaderVisibility => SecondaryPaneEntries.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        public bool SecondaryPaneCanGoBack => _workspaceLayoutHost.ShellState.Secondary.Navigation.BackStack.Count > 0;

        public bool SecondaryPaneCanGoForward => _workspaceLayoutHost.ShellState.Secondary.Navigation.ForwardStack.Count > 0;

        public bool SecondaryPaneCanGoUp
        {
            get
            {
                string path = _workspaceLayoutHost.ShellState.Secondary.CurrentPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                return !string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsPrimaryWorkspacePanelActive => IsWorkspacePanelActive(WorkspacePanelId.Primary);

        public bool IsSecondaryWorkspacePanelActive => IsWorkspacePanelActive(WorkspacePanelId.Secondary);

        public string PrimaryPaneStatusText => _primaryPaneStatusText;

        public string SecondaryPaneStatusText => _secondaryPaneStatusText;

        public Brush? PrimaryPaneToolbarBackground => GetPanelToolbarBackgroundBrush(WorkspacePanelId.Primary);

        public Brush? PrimaryPaneBodyBackground => GetPanelBodyBackgroundBrush(WorkspacePanelId.Primary);

        public Brush? SecondaryPaneToolbarBackground => GetPanelToolbarBackgroundBrush(WorkspacePanelId.Secondary);

        public Brush? SecondaryPaneBodyBackground => GetPanelBodyBackgroundBrush(WorkspacePanelId.Secondary);

        public Brush? PrimaryPaneToolbarBorderBrush => GetPanelToolbarBorderBrush(WorkspacePanelId.Primary);

        public Brush? PrimaryPaneBodyBorderBrush => GetPanelBodyBorderBrush(WorkspacePanelId.Primary);

        public Brush? SecondaryPaneToolbarBorderBrush => GetPanelToolbarBorderBrush(WorkspacePanelId.Secondary);

        public Brush? SecondaryPaneBodyBorderBrush => GetPanelBodyBorderBrush(WorkspacePanelId.Secondary);

        public Thickness PrimaryPaneToolbarBorderThickness => GetPanelToolbarBorderThickness(WorkspacePanelId.Primary);

        public Thickness SecondaryPaneToolbarBorderThickness => GetPanelToolbarBorderThickness(WorkspacePanelId.Secondary);

        public Vector3 PrimaryPaneToolbarTranslation => GetPanelToolbarTranslation(WorkspacePanelId.Primary);

        public Vector3 SecondaryPaneToolbarTranslation => GetPanelToolbarTranslation(WorkspacePanelId.Secondary);

        public Thickness PrimaryPaneBodyBorderThickness => GetPanelBodyBorderThickness(WorkspacePanelId.Primary);

        public Thickness SecondaryPaneBodyBorderThickness => GetPanelBodyBorderThickness(WorkspacePanelId.Secondary);

        public Vector3 PrimaryPaneBodyTranslation => GetPanelBodyTranslation(WorkspacePanelId.Primary);

        public Vector3 SecondaryPaneBodyTranslation => GetPanelBodyTranslation(WorkspacePanelId.Secondary);

        public Visibility AddressTextFallbackVisibility => VisibleBreadcrumbs.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        public Visibility SecondaryPaneAddressTextFallbackVisibility => SecondaryPaneVisibleBreadcrumbs.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        private Brush? GetPanelToolbarBackgroundBrush(bool isActive)
        {
            Border? probe = isActive ? ActivePaneToolbarBrushProbe : InactivePaneToolbarBrushProbe;
            if (probe?.Background is Brush brush)
            {
                return brush;
            }

            string fallbackKey = isActive
                ? "LayerFillColorDefaultBrush"
                : "LayerOnMicaBaseAltFillColorSecondaryBrush";

            return Application.Current.Resources.TryGetValue(fallbackKey, out object? value) && value is Brush fallbackBrush
                ? fallbackBrush
                : null;
        }

        private Brush? GetPanelBodyBackgroundBrush(bool isActive)
        {
            Border? probe = isActive ? ActivePaneBodyBrushProbe : InactivePaneBodyBrushProbe;
            if (probe?.Background is Brush brush)
            {
                return brush;
            }

            string fallbackKey = isActive
                ? "LayerFillColorDefaultBrush"
                : "LayerOnMicaBaseAltFillColorSecondaryBrush";

            return Application.Current.Resources.TryGetValue(fallbackKey, out object? value) && value is Brush fallbackBrush
                ? fallbackBrush
                : null;
        }

        private Brush? GetPanelToolbarBorderBrush(bool isActive)
        {
            Border? probe = isActive ? ActivePaneToolbarBrushProbe : InactivePaneToolbarBrushProbe;
            if (probe?.BorderBrush is Brush probeBrush)
            {
                return probeBrush;
            }

            string fallbackKey = isActive
                ? "ExplorerShellPanelBorderBrush"
                : "InactivePaneBorderBrush";

            return Application.Current.Resources.TryGetValue(fallbackKey, out object? value) && value is Brush fallbackBrush
                ? fallbackBrush
                : null;
        }

        private Brush? GetPanelBodyBorderBrush(bool isActive)
        {
            Border? probe = isActive ? ActivePaneBodyBrushProbe : InactivePaneBodyBrushProbe;
            if (probe?.BorderBrush is Brush probeBrush)
            {
                return probeBrush;
            }

            string fallbackKey = isActive
                ? "ExplorerShellPanelBorderBrush"
                : "InactivePaneBorderBrush";

            return Application.Current.Resources.TryGetValue(fallbackKey, out object? value) && value is Brush fallbackBrush
                ? fallbackBrush
                : null;
        }

        private static Thickness GetPanelToolbarBorderThickness(bool isActive)
        {
            return new Thickness(1);
        }

        private static Thickness GetPanelBodyBorderThickness(bool isActive)
        {
            return new Thickness(1);
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
            return GetPanelViewMode(WorkspacePanelId.Primary) != EntryViewMode.Details
                || GetPanelSortField(WorkspacePanelId.Primary) != EntrySortField.Name
                || GetPanelSortDirection(WorkspacePanelId.Primary) != SortDirection.Ascending
                || GetPanelGroupField(WorkspacePanelId.Primary) != EntryGroupField.None
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
