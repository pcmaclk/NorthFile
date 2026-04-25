using NorthFileUI.Settings;
using NorthFileUI.Workspace;
using System;

namespace NorthFileUI
{
    internal sealed class SettingsController
    {
        internal readonly record struct SettingsDiff(
            bool FilteringChanged,
            bool ExtensionsChanged);

        public SortDirection GetDefaultSortDirection(EntrySortField field)
        {
            return field switch
            {
                EntrySortField.ModifiedDate or EntrySortField.Size => SortDirection.Descending,
                _ => SortDirection.Ascending
            };
        }

        public string ResolveLanguageTag(AppLanguagePreference preference, string currentUiCultureName)
        {
            return preference switch
            {
                AppLanguagePreference.ChineseSimplified => "zh-CN",
                AppLanguagePreference.English => "en-US",
                _ => ResolveSystemLanguageTag(currentUiCultureName)
            };
        }

        public string ResolveSystemLanguageTag(string currentUiCultureName)
        {
            return currentUiCultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : "en-US";
        }

        public SettingsDiff AnalyzeImportDiff(AppSettings previousSettings, AppSettings importedSettings)
        {
            bool filteringChanged =
                previousSettings.ShowHiddenEntries != importedSettings.ShowHiddenEntries ||
                previousSettings.ShowProtectedSystemEntries != importedSettings.ShowProtectedSystemEntries ||
                previousSettings.ShowDotEntries != importedSettings.ShowDotEntries;
            bool extensionsChanged = previousSettings.ShowFileExtensions != importedSettings.ShowFileExtensions;
            return new SettingsDiff(filteringChanged, extensionsChanged);
        }

        public SettingsDiff AnalyzeFileDisplayDiff(
            AppSettings currentSettings,
            bool showHidden,
            bool showProtectedSystem,
            bool showDotFiles,
            bool showFileExtensions)
        {
            bool filteringChanged =
                currentSettings.ShowHiddenEntries != showHidden ||
                currentSettings.ShowProtectedSystemEntries != showProtectedSystem ||
                currentSettings.ShowDotEntries != showDotFiles;
            bool extensionsChanged = currentSettings.ShowFileExtensions != showFileExtensions;
            return new SettingsDiff(filteringChanged, extensionsChanged);
        }
    }
}
