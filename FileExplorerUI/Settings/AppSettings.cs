using FileExplorerUI.Workspace;
using System.Collections.Generic;

namespace FileExplorerUI.Settings;

public enum AppThemePreference
{
    UseSystem = 0,
    Light = 1,
    Dark = 2
}

public enum AppLanguagePreference
{
    UseSystem = 0,
    ChineseSimplified = 1,
    English = 2
}

public enum StartupLocationPreference
{
    ThisPc = 0,
    LastLocation = 1,
    SpecifiedLocation = 2
}

public sealed class AppSettings
{
    public bool ShowFavorites { get; set; } = true;

    public bool ShowCloud { get; set; } = true;

    public bool ShowNetwork { get; set; } = true;

    public bool ShowTags { get; set; } = true;

    public bool ShowHiddenEntries { get; set; } = false;

    public bool ShowProtectedSystemEntries { get; set; } = false;

    public bool ShowDotEntries { get; set; } = true;

    public bool ShowFileExtensions { get; set; } = true;

    public AppThemePreference ThemePreference { get; set; } = AppThemePreference.UseSystem;

    public AppLanguagePreference LanguagePreference { get; set; } = AppLanguagePreference.UseSystem;

    public StartupLocationPreference StartupLocationPreference { get; set; } = StartupLocationPreference.ThisPc;

    public string StartupSpecifiedPath { get; set; } = "shell:mycomputer";

    public string LastOpenedPath { get; set; } = "shell:mycomputer";

    public EntrySortField DefaultSortField { get; set; } = EntrySortField.Name;

    public EntryGroupField DefaultGroupField { get; set; } = EntryGroupField.None;

    public bool ConfirmDelete { get; set; } = true;

    public bool AutoStartEnabled { get; set; } = false;

    public bool MinimizeToTrayEnabled { get; set; } = false;

    public int WindowWidth { get; set; } = 0;

    public int WindowHeight { get; set; } = 0;

    public bool FavoritesInitialized { get; set; } = false;

    public List<FavoriteItem> Favorites { get; set; } = new();
}

public sealed class FavoriteItem
{
    public string Path { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string Glyph { get; set; } = "\uE8B7";

    public int Order { get; set; } = 0;
}
