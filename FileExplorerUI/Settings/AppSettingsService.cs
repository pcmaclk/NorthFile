using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using Windows.Storage;
using FileExplorerUI.Workspace;

namespace FileExplorerUI.Settings;

public sealed class AppSettingsService
{
    private static readonly AppJsonSerializerContext s_indentedJsonContext = new(new JsonSerializerOptions
    {
        WriteIndented = true
    });

    private readonly string _fallbackSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NorthFile",
        "settings.json");
    private static readonly object s_windowSizeLogLock = new();
    private static readonly string s_windowSizeLogPath = Path.Combine(
        AppContext.BaseDirectory,
        "window-size.log");

    public AppSettings Load()
    {
        try
        {
            ApplicationDataContainer? localSettings = TryGetLocalSettings();
            if (localSettings is not null)
            {
                AppSettings settings = new()
                {
                    ShowFavorites = ReadBool(localSettings, nameof(AppSettings.ShowFavorites), true),
                    ShowCloud = ReadBool(localSettings, nameof(AppSettings.ShowCloud), true),
                    ShowNetwork = ReadBool(localSettings, nameof(AppSettings.ShowNetwork), true),
                    ShowTags = ReadBool(localSettings, nameof(AppSettings.ShowTags), true),
                    ShowHiddenEntries = ReadBool(localSettings, nameof(AppSettings.ShowHiddenEntries), false),
                    ShowProtectedSystemEntries = ReadBool(localSettings, nameof(AppSettings.ShowProtectedSystemEntries), false),
                    ShowDotEntries = ReadBool(localSettings, nameof(AppSettings.ShowDotEntries), true),
                    ShowFileExtensions = ReadBool(localSettings, nameof(AppSettings.ShowFileExtensions), true),
                    ThemePreference = ReadEnum(localSettings, nameof(AppSettings.ThemePreference), AppThemePreference.UseSystem),
                    LanguagePreference = ReadEnum(localSettings, nameof(AppSettings.LanguagePreference), AppLanguagePreference.UseSystem),
                    StartupLocationPreference = ReadEnum(localSettings, nameof(AppSettings.StartupLocationPreference), StartupLocationPreference.ThisPc),
                    StartupSpecifiedPath = ReadString(localSettings, nameof(AppSettings.StartupSpecifiedPath), "shell:mycomputer"),
                    LastOpenedPath = ReadString(localSettings, nameof(AppSettings.LastOpenedPath), "shell:mycomputer"),
                    LastWorkspaceSessionJson = ReadString(localSettings, nameof(AppSettings.LastWorkspaceSessionJson), string.Empty),
                    DefaultSortField = ReadEnum(localSettings, nameof(AppSettings.DefaultSortField), EntrySortField.Name),
                    DefaultGroupField = ReadEnum(localSettings, nameof(AppSettings.DefaultGroupField), EntryGroupField.None),
                    ConfirmDelete = ReadBool(localSettings, nameof(AppSettings.ConfirmDelete), true),
                    AutoStartEnabled = ReadBool(localSettings, nameof(AppSettings.AutoStartEnabled), false),
                    MinimizeToTrayEnabled = ReadBool(localSettings, nameof(AppSettings.MinimizeToTrayEnabled), false),
                    WindowWidth = ReadInt(localSettings, nameof(AppSettings.WindowWidth), 0),
                    WindowHeight = ReadInt(localSettings, nameof(AppSettings.WindowHeight), 0),
                    WindowPosX = ReadInt(localSettings, nameof(AppSettings.WindowPosX), int.MinValue),
                    WindowPosY = ReadInt(localSettings, nameof(AppSettings.WindowPosY), int.MinValue),
                    WindowMaximized = ReadBool(localSettings, nameof(AppSettings.WindowMaximized), false),
                    FavoritesInitialized = ReadBool(localSettings, nameof(AppSettings.FavoritesInitialized), false),
                    Favorites = ReadFavorites(localSettings)
                };
                TraceWindowSizeSettings(
                    "加载设置",
                    $"source=local-settings width={settings.WindowWidth} height={settings.WindowHeight} x={settings.WindowPosX} y={settings.WindowPosY} maximized={settings.WindowMaximized}");
                return settings;
            }
        }
        catch (Exception ex)
        {
            TraceWindowSizeSettings("加载设置", $"local-settings-failed type={ex.GetType().Name} message=\"{ex.Message}\"");
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
                TraceWindowSizeSettings(
                    "保存到设置",
                    $"target=local-settings width={settings.WindowWidth} height={settings.WindowHeight} x={settings.WindowPosX} y={settings.WindowPosY} maximized={settings.WindowMaximized}");
                localSettings.Values[nameof(AppSettings.ShowFavorites)] = settings.ShowFavorites;
                localSettings.Values[nameof(AppSettings.ShowCloud)] = settings.ShowCloud;
                localSettings.Values[nameof(AppSettings.ShowNetwork)] = settings.ShowNetwork;
                localSettings.Values[nameof(AppSettings.ShowTags)] = settings.ShowTags;
                localSettings.Values[nameof(AppSettings.ShowHiddenEntries)] = settings.ShowHiddenEntries;
                localSettings.Values[nameof(AppSettings.ShowProtectedSystemEntries)] = settings.ShowProtectedSystemEntries;
                localSettings.Values[nameof(AppSettings.ShowDotEntries)] = settings.ShowDotEntries;
                localSettings.Values[nameof(AppSettings.ShowFileExtensions)] = settings.ShowFileExtensions;
                localSettings.Values[nameof(AppSettings.ThemePreference)] = (int)settings.ThemePreference;
                localSettings.Values[nameof(AppSettings.LanguagePreference)] = (int)settings.LanguagePreference;
                localSettings.Values[nameof(AppSettings.StartupLocationPreference)] = (int)settings.StartupLocationPreference;
                localSettings.Values[nameof(AppSettings.StartupSpecifiedPath)] = settings.StartupSpecifiedPath;
                localSettings.Values[nameof(AppSettings.LastOpenedPath)] = settings.LastOpenedPath;
                localSettings.Values[nameof(AppSettings.LastWorkspaceSessionJson)] = settings.LastWorkspaceSessionJson ?? string.Empty;
                localSettings.Values[nameof(AppSettings.DefaultSortField)] = (int)settings.DefaultSortField;
                localSettings.Values[nameof(AppSettings.DefaultGroupField)] = (int)settings.DefaultGroupField;
                localSettings.Values[nameof(AppSettings.ConfirmDelete)] = settings.ConfirmDelete;
                localSettings.Values[nameof(AppSettings.AutoStartEnabled)] = settings.AutoStartEnabled;
                localSettings.Values[nameof(AppSettings.MinimizeToTrayEnabled)] = settings.MinimizeToTrayEnabled;
                localSettings.Values[nameof(AppSettings.WindowWidth)] = settings.WindowWidth;
                localSettings.Values[nameof(AppSettings.WindowHeight)] = settings.WindowHeight;
                localSettings.Values[nameof(AppSettings.WindowPosX)] = settings.WindowPosX;
                localSettings.Values[nameof(AppSettings.WindowPosY)] = settings.WindowPosY;
                localSettings.Values[nameof(AppSettings.WindowMaximized)] = settings.WindowMaximized;
                localSettings.Values[nameof(AppSettings.FavoritesInitialized)] = settings.FavoritesInitialized;
                localSettings.Values[nameof(AppSettings.Favorites)] = JsonSerializer.Serialize(
                    settings.Favorites ?? new List<FavoriteItem>(),
                    AppJsonSerializerContext.Default.ListFavoriteItem);
                return;
            }
        }
        catch (Exception ex)
        {
            TraceWindowSizeSettings("保存到设置", $"local-settings-failed type={ex.GetType().Name} message=\"{ex.Message}\"");
        }

        SaveToFile(settings);
    }

    public void ExportToPath(AppSettings settings, string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(settings, s_indentedJsonContext.AppSettings);
        File.WriteAllText(path, json);
    }

    public AppSettings? ImportFromPath(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppSettings);
        }
        catch
        {
            return null;
        }
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

    private static string ReadString(ApplicationDataContainer localSettings, string key, string fallback)
    {
        object? value = localSettings.Values[key];
        return value as string ?? fallback;
    }

    private static int ReadInt(ApplicationDataContainer localSettings, string key, int fallback)
    {
        object? value = localSettings.Values[key];
        return value is int intValue ? intValue : fallback;
    }

    private static TEnum ReadEnum<TEnum>(ApplicationDataContainer localSettings, string key, TEnum fallback)
        where TEnum : struct, Enum
    {
        object? value = localSettings.Values[key];
        if (value is int intValue && Enum.IsDefined(typeof(TEnum), intValue))
        {
            return (TEnum)Enum.ToObject(typeof(TEnum), intValue);
        }

        return fallback;
    }

    private static List<FavoriteItem> ReadFavorites(ApplicationDataContainer localSettings)
    {
        object? value = localSettings.Values[nameof(AppSettings.Favorites)];
        if (value is not string json || string.IsNullOrWhiteSpace(json))
        {
            return new List<FavoriteItem>();
        }

        try
        {
            return JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListFavoriteItem) ?? new List<FavoriteItem>();
        }
        catch
        {
            return new List<FavoriteItem>();
        }
    }

    private AppSettings LoadFromFile()
    {
        try
        {
            if (!File.Exists(_fallbackSettingsPath))
            {
                TraceWindowSizeSettings("加载设置", $"source=file-missing path=\"{_fallbackSettingsPath}\"");
                return new AppSettings();
            }

            string json = File.ReadAllText(_fallbackSettingsPath);
            AppSettings settings = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.AppSettings) ?? new AppSettings();
            TraceWindowSizeSettings(
                "加载设置",
                $"source=file width={settings.WindowWidth} height={settings.WindowHeight} x={settings.WindowPosX} y={settings.WindowPosY} maximized={settings.WindowMaximized} path=\"{_fallbackSettingsPath}\"");
            return settings;
        }
        catch (Exception ex)
        {
            TraceWindowSizeSettings("加载设置", $"file-failed type={ex.GetType().Name} message=\"{ex.Message}\"");
            return new AppSettings();
        }
    }

    private void SaveToFile(AppSettings settings)
    {
        TraceWindowSizeSettings(
            "保存到设置",
            $"target=file width={settings.WindowWidth} height={settings.WindowHeight} x={settings.WindowPosX} y={settings.WindowPosY} maximized={settings.WindowMaximized} path=\"{_fallbackSettingsPath}\"");
        ExportToPath(settings, _fallbackSettingsPath);
    }

    private static void TraceWindowSizeSettings(string node, string detail)
    {
        string message = $"[WINDOW-SIZE] node={node} detail={detail}";
        Debug.WriteLine(message);

        try
        {
            lock (s_windowSizeLogLock)
            {
                File.AppendAllText(
                    s_windowSizeLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
