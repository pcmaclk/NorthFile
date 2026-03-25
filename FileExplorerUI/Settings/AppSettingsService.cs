using Windows.Storage;

namespace FileExplorerUI.Settings;

public sealed class AppSettingsService
{
    private const string ShowFavoritesKey = "Settings.ShowFavorites";
    private const string ShowCloudKey = "Settings.ShowCloud";
    private const string ShowNetworkKey = "Settings.ShowNetwork";
    private const string ShowTagsKey = "Settings.ShowTags";

    private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

    public AppSettings Load()
    {
        return new AppSettings
        {
            ShowFavorites = ReadBool(ShowFavoritesKey, true),
            ShowCloud = ReadBool(ShowCloudKey, true),
            ShowNetwork = ReadBool(ShowNetworkKey, true),
            ShowTags = ReadBool(ShowTagsKey, true)
        };
    }

    public void Save(AppSettings settings)
    {
        _localSettings.Values[ShowFavoritesKey] = settings.ShowFavorites;
        _localSettings.Values[ShowCloudKey] = settings.ShowCloud;
        _localSettings.Values[ShowNetworkKey] = settings.ShowNetwork;
        _localSettings.Values[ShowTagsKey] = settings.ShowTags;
    }

    private bool ReadBool(string key, bool fallback)
    {
        object? value = _localSettings.Values[key];
        return value is bool flag ? flag : fallback;
    }
}
