using FileExplorerUI.Settings;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using System.Globalization;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void ApplyAppSettingsToPresentationDefaults()
        {
            _currentSortField = _appSettings.DefaultSortField;
            _currentSortDirection = GetDefaultSortDirection(_currentSortField);
            _currentGroupField = _appSettings.DefaultGroupField;
        }

        private SortDirection GetDefaultSortDirection(EntrySortField field)
        {
            return _settingsController.GetDefaultSortDirection(field);
        }

        private void ApplyAppSettingsToUi()
        {
            SettingsViewControl.SetGeneralSettings(
                _appSettings.LanguagePreference,
                _appSettings.StartupLocationPreference,
                _appSettings.StartupSpecifiedPath);
            SettingsViewControl.SetSidebarSectionVisibility(
                _appSettings.ShowFavorites,
                _appSettings.ShowCloud,
                _appSettings.ShowNetwork,
                _appSettings.ShowTags);
            SettingsViewControl.SetAppearanceSettings(
                _appSettings.ThemePreference,
                _appSettings.DefaultSortField,
                _appSettings.DefaultGroupField);
            SettingsViewControl.SetFileDisplaySettings(
                _appSettings.ShowHiddenEntries,
                _appSettings.ShowProtectedSystemEntries,
                _appSettings.ShowDotEntries,
                _appSettings.ShowFileExtensions);
            SettingsViewControl.SetDeleteConfirmationEnabled(_appSettings.ConfirmDelete);
            SettingsViewControl.SetAdvancedSettings(
                _appSettings.AutoStartEnabled,
                _appSettings.MinimizeToTrayEnabled);

            StyledSidebarView.SetSectionVisibility(
                _appSettings.ShowFavorites,
                _appSettings.ShowCloud,
                _appSettings.ShowNetwork,
                _appSettings.ShowTags);
            ApplyLanguagePreference(_appSettings.LanguagePreference);
            RefreshSidebarFavorites(refreshSelection: true);
            ApplyThemePreference(_appSettings.ThemePreference);
        }

        private void ApplyThemePreference(AppThemePreference preference)
        {
            if (Content is not FrameworkElement rootElement)
            {
                return;
            }

            rootElement.RequestedTheme = preference switch
            {
                AppThemePreference.Light => ElementTheme.Light,
                AppThemePreference.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };

            ApplyTitleBarTheme();
        }

        private void ApplyLanguagePreference(AppLanguagePreference preference)
        {
            string languageTag = _settingsController.ResolveLanguageTag(
                preference,
                CultureInfo.CurrentUICulture.Name);

            ApplyRuntimeLanguageOverrides(languageTag);
            LocalizedStrings.Instance.SetLanguage(languageTag);
        }

        private static void ApplyRuntimeLanguageOverrides(string languageTag)
        {
            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(languageTag);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
            }

            try
            {
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = languageTag;
            }
            catch
            {
            }
        }

        private void SettingsViewControl_SidebarSectionVisibilityChanged(bool showFavorites, bool showCloud, bool showNetwork, bool showTags)
        {
            _appSettings.ShowFavorites = showFavorites;
            _appSettings.ShowCloud = showCloud;
            _appSettings.ShowNetwork = showNetwork;
            _appSettings.ShowTags = showTags;

            _appSettingsService.Save(_appSettings);
            StyledSidebarView.SetSectionVisibility(showFavorites, showCloud, showNetwork, showTags);
        }

        private void SettingsViewControl_DeleteConfirmationChanged(bool enabled)
        {
            _appSettings.ConfirmDelete = enabled;
            _appSettingsService.Save(_appSettings);
        }

        private void SettingsViewControl_AutoStartChanged(bool enabled)
        {
            _appSettings.AutoStartEnabled = enabled;
            _appSettingsService.Save(_appSettings);
            EnsureAutoStartRegistration(enabled);
        }

        private void SettingsViewControl_MinimizeToTrayChanged(bool enabled)
        {
            _appSettings.MinimizeToTrayEnabled = enabled;
            _appSettingsService.Save(_appSettings);
            if (!enabled)
            {
                RemoveTrayIcon();
            }
        }


        private async void SettingsViewControl_FileDisplaySettingsChanged(bool showHidden, bool showProtectedSystem, bool showDotFiles, bool showFileExtensions)
        {
            SettingsController.SettingsDiff settingsDiff = _settingsController.AnalyzeFileDisplayDiff(
                _appSettings,
                showHidden,
                showProtectedSystem,
                showDotFiles,
                showFileExtensions);

            _appSettings.ShowHiddenEntries = showHidden;
            _appSettings.ShowProtectedSystemEntries = showProtectedSystem;
            _appSettings.ShowDotEntries = showDotFiles;
            _appSettings.ShowFileExtensions = showFileExtensions;
            _appSettingsService.Save(_appSettings);

            if (settingsDiff.ExtensionsChanged)
            {
                UpdateDisplayedEntryNames();
            }

            if (settingsDiff.FilteringChanged)
            {
                await LoadFirstPageAsync();
            }
        }

        private void SettingsViewControl_ThemePreferenceChanged(AppThemePreference preference)
        {
            _appSettings.ThemePreference = preference;
            _appSettingsService.Save(_appSettings);
            ApplyThemePreference(preference);
        }

        private void SettingsViewControl_LanguagePreferenceChanged(AppLanguagePreference preference)
        {
            _appSettings.LanguagePreference = preference;
            _appSettingsService.Save(_appSettings);
            ApplyLanguagePreference(preference);
            RefreshSidebarFavorites(refreshSelection: true);
        }

        private void SettingsViewControl_StartupLocationPreferenceChanged(StartupLocationPreference preference)
        {
            _appSettings.StartupLocationPreference = preference;
            _appSettingsService.Save(_appSettings);
        }

        private void SettingsViewControl_StartupSpecifiedPathChanged(string path)
        {
            _appSettings.StartupSpecifiedPath = path;
            _appSettingsService.Save(_appSettings);
        }

        private async void SettingsViewControl_DefaultSortFieldChanged(EntrySortField field)
        {
            _appSettings.DefaultSortField = field;
            _appSettingsService.Save(_appSettings);
            await SetSortAsync(field, GetDefaultSortDirection(field));
        }

        private async void SettingsViewControl_DefaultGroupFieldChanged(EntryGroupField field)
        {
            _appSettings.DefaultGroupField = field;
            _appSettingsService.Save(_appSettings);
            await SetGroupAsync(field);
        }
    }
}
