using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void WireRootAndViewportEvents()
        {
            if (Content is FrameworkElement rootElement)
            {
                rootElement.ActualThemeChanged += MainWindowRoot_ActualThemeChanged;
                rootElement.PointerEntered += RootElement_PointerEnteredOrMoved;
                rootElement.PointerMoved += RootElement_PointerEnteredOrMoved;
                rootElement.AddHandler(
                    UIElement.PointerPressedEvent,
                    new PointerEventHandler(RootElement_PointerPressedPreview),
                    true);
                rootElement.AddHandler(
                    UIElement.PreviewKeyDownEvent,
                    new KeyEventHandler(RootElement_PreviewKeyDown),
                    true);
                rootElement.GotFocus += RootElement_GotFocus;
            }

            PathTextBox.Text = _initialPath;
            RegisterColumnSplitterHandlers(HeaderSplitter1);
            RegisterColumnSplitterHandlers(HeaderSplitter2);
            RegisterColumnSplitterHandlers(HeaderSplitter3);
            RegisterColumnSplitterHandlers(HeaderSplitter4);
            RegisterColumnSplitterHandlers(SecondaryHeaderSplitter1);
            RegisterColumnSplitterHandlers(SecondaryHeaderSplitter2);
            RegisterColumnSplitterHandlers(SecondaryHeaderSplitter3);
            RegisterColumnSplitterHandlers(SecondaryHeaderSplitter4);
            RegisterSidebarSplitterHandlers(SidebarSplitter);
            DetailsEntriesRepeater.Layout = _detailsVirtualizingLayout;
            SecondaryEntriesRepeater.Layout = _secondaryDetailsVirtualizingLayout;
            GroupedEntriesRepeater.Layout = _groupedVirtualizingLayout;
            RegisterEntriesKeyHandlers(DetailsEntriesScrollViewer);
            RegisterEntriesKeyHandlers(GroupedEntriesScrollViewer);
            RegisterEntriesKeyHandlers(SecondaryEntriesScrollViewer);
            DetailsEntriesScrollViewer.ViewChanged += DetailsEntriesScrollViewer_ViewChanged;
            GroupedEntriesScrollViewer.ViewChanged += GroupedEntriesScrollViewer_ViewChanged;
            SecondaryEntriesScrollViewer.ViewChanged += SecondaryEntriesScrollViewer_ViewChanged;
        }

        private void InitializeViewHostsAndSettings()
        {
            _detailsEntriesViewHost = new RepeaterEntriesViewHost(
                DetailsEntriesScrollViewer,
                DetailsEntriesRepeater,
                _detailsRepeaterLayoutProfile);
            _groupedEntriesViewHost = new GroupedRepeaterEntriesViewHost(
                GroupedEntriesScrollViewer,
                GroupedEntriesRepeater,
                _groupedVirtualizingLayout);
            RebindPrimaryPaneDataSession();
            UpdateEntriesContextOverlayTargets();
            SizeChanged += MainWindow_SizeChanged;
            Activated += MainWindow_Activated;
            _appSettings = _appSettingsService.Load();
            EnsureFavoritesInitialized();
            ApplyAppSettingsToPresentationDefaults();
            LocalizedStrings.Instance.PropertyChanged += LocalizedStrings_PropertyChanged;
            InitializeWorkspaceShellState();
        }

        private void WireShellCommandsAndStartup()
        {
            UpdateNavButtonsState();
            UpdateWindowTitle();
            ApplyTitleBarTheme();
            StyledSidebarView.NavigateRequested += StyledSidebarView_NavigateRequested;
            StyledSidebarView.FavoriteActionRequested += StyledSidebarView_FavoriteActionRequested;
            StyledSidebarView.PinnedContextRequested += StyledSidebarView_PinnedContextRequested;
            StyledSidebarView.SettingsRequested += StyledSidebarView_SettingsRequested;
            SettingsViewControl.VisibleSectionChanged += SettingsViewControl_VisibleSectionChanged;
            SettingsViewControl.SidebarSectionVisibilityChanged += SettingsViewControl_SidebarSectionVisibilityChanged;
            SettingsViewControl.FileDisplaySettingsChanged += SettingsViewControl_FileDisplaySettingsChanged;
            SettingsViewControl.ThemePreferenceChanged += SettingsViewControl_ThemePreferenceChanged;
            SettingsViewControl.LanguagePreferenceChanged += SettingsViewControl_LanguagePreferenceChanged;
            SettingsViewControl.StartupLocationPreferenceChanged += SettingsViewControl_StartupLocationPreferenceChanged;
            SettingsViewControl.StartupSpecifiedPathChanged += SettingsViewControl_StartupSpecifiedPathChanged;
            SettingsViewControl.StartupSpecifiedPathBrowseRequested += SettingsViewControl_StartupSpecifiedPathBrowseRequested;
            SettingsViewControl.DefaultSortFieldChanged += SettingsViewControl_DefaultSortFieldChanged;
            SettingsViewControl.DefaultGroupFieldChanged += SettingsViewControl_DefaultGroupFieldChanged;
            SettingsViewControl.DeleteConfirmationChanged += SettingsViewControl_DeleteConfirmationChanged;
            SettingsViewControl.AutoStartChanged += SettingsViewControl_AutoStartChanged;
            SettingsViewControl.MinimizeToTrayChanged += SettingsViewControl_MinimizeToTrayChanged;
            SettingsViewControl.ExportSettingsRequested += SettingsViewControl_ExportSettingsRequested;
            SettingsViewControl.ImportSettingsRequested += SettingsViewControl_ImportSettingsRequested;
            Closed += MainWindow_Closed;
            InstallWindowHook();
            BuildSidebarItems();
            ApplyAppSettingsToUi();
            EnsureAutoStartRegistration(_appSettings.AutoStartEnabled);
            RestoreWindowSizeFromSettings();
            InitializeStartupPathFromSettings();
            _sidebarInitialized = true;
            ApplyCommandDockLayout();
            if (!_startupWorkspaceSessionRestored)
            {
                _ = LoadPanelDataAsync(WorkspacePanelId.Primary);
            }
        }

        private void RebindPrimaryPaneDataSession()
        {
            bool detailsSourceChanged = false;
            if (DetailsEntriesRepeater is not null)
            {
                if (!ReferenceEquals(DetailsEntriesRepeater.ItemsSource, PrimaryEntries))
                {
                    ResetEntriesViewport();
                    DetailsEntriesRepeater.ItemsSource = null;
                    DetailsEntriesRepeater.UpdateLayout();
                    DetailsEntriesRepeater.ItemsSource = PrimaryEntries;
                    detailsSourceChanged = true;
                }
            }

            bool groupedSourceChanged = false;
            if (GroupedEntriesRepeater is not null)
            {
                if (!ReferenceEquals(GroupedEntriesRepeater.ItemsSource, PrimaryEntries))
                {
                    GroupedEntriesScrollViewer.ChangeView(0, null, null, disableAnimation: true);
                    GroupedEntriesRepeater.ItemsSource = null;
                    GroupedEntriesRepeater.UpdateLayout();
                    GroupedEntriesRepeater.ItemsSource = PrimaryEntries;
                    groupedSourceChanged = true;
                }
            }

            _detailsEntriesViewHost?.SetItems(PrimaryEntries);
            _groupedEntriesViewHost?.SetItems(PrimaryEntries);
            if (detailsSourceChanged)
            {
                DetailsEntriesRepeater?.InvalidateMeasure();
                DetailsEntriesScrollViewer?.UpdateLayout();
            }

            if (groupedSourceChanged)
            {
                GroupedEntriesRepeater?.InvalidateMeasure();
                GroupedEntriesScrollViewer?.UpdateLayout();
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("PrimaryEntries"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GroupedEntryColumns)));
        }

        private void RebindSecondaryPaneDataSession()
        {
            if (SecondaryEntriesRepeater is not null &&
                !ReferenceEquals(SecondaryEntriesRepeater.ItemsSource, SecondaryPaneEntries))
            {
                SecondaryEntriesRepeater.ItemsSource = SecondaryPaneEntries;
            }
        }
    }
}
