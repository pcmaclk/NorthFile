using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

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
            RegisterSidebarSplitterHandlers(SidebarSplitter);
            DetailsEntriesRepeater.Layout = _detailsVirtualizingLayout;
            GroupedEntriesRepeater.Layout = _groupedVirtualizingLayout;
            RegisterEntriesKeyHandlers(DetailsEntriesScrollViewer);
            RegisterEntriesKeyHandlers(GroupedEntriesScrollViewer);
            DetailsEntriesScrollViewer.ViewChanged += DetailsEntriesScrollViewer_ViewChanged;
            GroupedEntriesScrollViewer.ViewChanged += GroupedEntriesScrollViewer_ViewChanged;
        }

        private void InitializeViewHostsAndSettings()
        {
            _detailsEntriesViewHost = new RepeaterEntriesViewHost(
                DetailsEntriesScrollViewer,
                DetailsEntriesRepeater,
                _detailsRepeaterLayoutProfile);
            _detailsEntriesViewHost.SetItems(_entries);
            _groupedEntriesViewHost = new GroupedRepeaterEntriesViewHost(
                GroupedEntriesScrollViewer,
                GroupedEntriesRepeater,
                _groupedVirtualizingLayout);
            _groupedEntriesViewHost.SetItems(_entries);
            UpdateEntriesContextOverlayTargets();
            SizeChanged += MainWindow_SizeChanged;
            Activated += MainWindow_Activated;
            _appSettings = _appSettingsService.Load();
            EnsureFavoritesInitialized();
            ApplyAppSettingsToPresentationDefaults();
            LocalizedStrings.Instance.PropertyChanged += LocalizedStrings_PropertyChanged;
            InitializeWorkspaceShellState();
#if !DEBUG
            LanguageToggleButton.Visibility = Visibility.Collapsed;
#endif
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
            _ = LoadFirstPageAsync();
        }
    }
}
