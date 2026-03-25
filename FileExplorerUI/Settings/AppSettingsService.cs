using System;
using System.IO;
using System.Text.Json;
using Windows.Storage;

namespace FileExplorerUI.Settings;

public sealed class AppSettingsService
{
    private readonly string _fallbackSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NorthFile",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            ApplicationDataContainer? localSettings = TryGetLocalSettings();
            if (localSettings is not null)
            {
                return new AppSettings
                {
                    ShowFavorites = ReadBool(localSettings, nameof(AppSettings.ShowFavorites), true),
                    ShowCloud = ReadBool(localSettings, nameof(AppSettings.ShowCloud), true),
                    ShowNetwork = ReadBool(localSettings, nameof(AppSettings.ShowNetwork), true),
                    ShowTags = ReadBool(localSettings, nameof(AppSettings.ShowTags), true),
                    ConfirmDelete = ReadBool(localSettings, nameof(AppSettings.ConfirmDelete), true)
                };
            }
        }
        catch
        {
        }

        return LoadFromFile();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            ApplicationDataContainer? localSettings = TryGetLocalSettings();
            if (localSettings is not null)
            {
                localSettings.Values[nameof(AppSettings.ShowFavorites)] = settings.ShowFavorites;
                localSettings.Values[nameof(AppSettings.ShowCloud)] = settings.ShowCloud;
                localSettings.Values[nameof(AppSettings.ShowNetwork)] = settings.ShowNetwork;
                localSettings.Values[nameof(AppSettings.ShowTags)] = settings.ShowTags;
                localSettings.Values[nameof(AppSettings.ConfirmDelete)] = settings.ConfirmDelete;
                return;
            }
        }
        catch
        {
        }

        SaveToFile(settings);
    }

    private static ApplicationDataContainer? TryGetLocalSettings()
    {
        try
        {
            return ApplicationData.Current.LocalSettings;
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadBool(ApplicationDataContainer localSettings, string key, bool fallback)
    {
        object? value = localSettings.Values[key];
        return value is bool flag ? flag : fallback;
    }

    private AppSettings LoadFromFile()
    {
        try
        {
            if (!File.Exists(_fallbackSettingsPath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(_fallbackSettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private void SaveToFile(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(_fallbackSettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(_fallbackSettingsPath, json);
    }
}
