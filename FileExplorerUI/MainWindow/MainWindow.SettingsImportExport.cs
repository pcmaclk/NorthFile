using FileExplorerUI.Settings;
using FileExplorerUI.Workspace;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using WinRT.Interop;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void SettingsViewControl_ExportSettingsRequested()
        {
            PersistCurrentWindowSize();
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
            picker.SuggestedFileName = "northfile.settings";
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            CachedFileManager.DeferUpdates(file);
            try
            {
                _appSettingsService.ExportToPath(_appSettings, file.Path);
                FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
                if (status is FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed)
                {
                    UpdateStatusKey("StatusSettingsExported", file.Name);
                    return;
                }

                UpdateStatusKey("StatusSettingsExportFailed", file.Name);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusSettingsExportFailed", ex.Message);
            }
        }

        private async void SettingsViewControl_ImportSettingsRequested()
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".json");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            AppSettings? importedSettings = _appSettingsService.ImportFromPath(file.Path);
            if (importedSettings is null)
            {
                UpdateStatusKey("StatusSettingsImportFailed", file.Name);
                return;
            }

            await ApplyImportedSettingsAsync(importedSettings);
            UpdateStatusKey("StatusSettingsImported", file.Name);
        }

        private async Task ApplyImportedSettingsAsync(AppSettings importedSettings)
        {
            AppSettings previousSettings = _appSettings;
            SettingsController.SettingsDiff settingsDiff = _settingsController.AnalyzeImportDiff(previousSettings, importedSettings);

            _appSettings = importedSettings;
            EnsureFavoritesInitialized();
            _appSettingsService.Save(_appSettings);
            ApplyAppSettingsToPresentationDefaults();
            ApplyAppSettingsToUi();
            EnsureAutoStartRegistration(_appSettings.AutoStartEnabled);
            if (!_appSettings.MinimizeToTrayEnabled)
            {
                RemoveTrayIcon();
            }

            if (settingsDiff.ExtensionsChanged)
            {
                UpdateDisplayedEntryNames();
            }

            if (settingsDiff.FilteringChanged)
            {
                await LoadPanelDataAsync(WorkspacePanelId.Primary);
                return;
            }

            await SetSortAsync(_appSettings.DefaultSortField, GetDefaultSortDirection(_appSettings.DefaultSortField));
            await SetGroupAsync(_appSettings.DefaultGroupField);
        }

        private async void SettingsViewControl_StartupSpecifiedPathBrowseRequested()
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            StorageFolder? folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            _appSettings.StartupSpecifiedPath = folder.Path;
            _appSettingsService.Save(_appSettings);
            SettingsViewControl.SetStartupSpecifiedPath(folder.Path);
        }
    }
}
