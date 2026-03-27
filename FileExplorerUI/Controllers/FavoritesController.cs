using FileExplorerUI.Commands;
using FileExplorerUI.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileExplorerUI
{
    internal sealed class FavoritesController
    {
        public void NormalizeFavoritesInPlace(AppSettings settings)
        {
            var normalized = new List<FavoriteItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FavoriteItem favorite in settings.Favorites ?? new List<FavoriteItem>())
            {
                if (string.IsNullOrWhiteSpace(favorite.Path))
                {
                    continue;
                }

                string normalizedPath = NormalizeFavoritePath(favorite.Path);
                if (!seen.Add(normalizedPath))
                {
                    continue;
                }

                normalized.Add(new FavoriteItem
                {
                    Path = normalizedPath,
                    Label = favorite.Label ?? string.Empty,
                    Glyph = string.IsNullOrWhiteSpace(favorite.Glyph) ? GetFavoriteGlyph(normalizedPath) : favorite.Glyph,
                    Order = favorite.Order
                });
            }

            settings.Favorites = normalized
                .OrderBy(item => item.Order)
                .ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            for (int i = 0; i < settings.Favorites.Count; i++)
            {
                settings.Favorites[i].Order = i;
            }
        }

        public void ReindexFavoritesInPlace(AppSettings settings)
        {
            for (int i = 0; i < settings.Favorites.Count; i++)
            {
                settings.Favorites[i].Order = i;
            }
        }

        public List<FavoriteItem> CreateDefaultFavoriteItems()
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            return new List<FavoriteItem>
            {
                new() { Path = NormalizeFavoritePath(desktopPath), Label = string.Empty, Glyph = "\uE80F", Order = 0 },
                new() { Path = NormalizeFavoritePath(documentsPath), Label = string.Empty, Glyph = "\uE8A5", Order = 1 },
                new() { Path = NormalizeFavoritePath(downloadsPath), Label = string.Empty, Glyph = "\uE896", Order = 2 },
                new() { Path = NormalizeFavoritePath(picturesPath), Label = string.Empty, Glyph = "\uE91B", Order = 3 }
            };
        }

        public IReadOnlyList<SidebarNavItemModel> BuildSidebarFavoriteModels(AppSettings settings, Func<string, string> localize)
        {
            NormalizeFavoritesInPlace(settings);
            return settings.Favorites
                .OrderBy(item => item.Order)
                .Select(item => new SidebarNavItemModel(
                    key: item.Path,
                    label: ResolveFavoriteLabel(item, localize),
                    path: item.Path,
                    glyph: string.IsNullOrWhiteSpace(item.Glyph) ? GetFavoriteGlyph(item.Path) : item.Glyph))
                .ToList();
        }

        public string ResolveFavoriteLabel(FavoriteItem favorite, Func<string, string> localize)
        {
            if (TryGetLocalizedFavoriteLabel(favorite.Path, localize, out string? localizedLabel))
            {
                return localizedLabel!;
            }

            if (!string.IsNullOrWhiteSpace(favorite.Label))
            {
                return favorite.Label;
            }

            string trimmedPath = favorite.Path.TrimEnd('\\');
            string fileName = Path.GetFileName(trimmedPath);
            return string.IsNullOrWhiteSpace(fileName) ? trimmedPath : fileName;
        }

        public static string NormalizeFavoritePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).TrimEnd('\\');
            }
            catch
            {
                return path.TrimEnd('\\');
            }
        }

        public bool TryGetLocalizedFavoriteLabel(string path, Func<string, string> localize, out string? label)
        {
            string normalizedPath = NormalizeFavoritePath(path);
            string desktopPath = NormalizeFavoritePath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            if (string.Equals(normalizedPath, desktopPath, StringComparison.OrdinalIgnoreCase))
            {
                label = localize("SidebarDesktop");
                return true;
            }

            string documentsPath = NormalizeFavoritePath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            if (string.Equals(normalizedPath, documentsPath, StringComparison.OrdinalIgnoreCase))
            {
                label = localize("SidebarDocuments");
                return true;
            }

            string downloadsPath = NormalizeFavoritePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            if (string.Equals(normalizedPath, downloadsPath, StringComparison.OrdinalIgnoreCase))
            {
                label = localize("SidebarDownloads");
                return true;
            }

            string picturesPath = NormalizeFavoritePath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            if (string.Equals(normalizedPath, picturesPath, StringComparison.OrdinalIgnoreCase))
            {
                label = localize("SidebarPictures");
                return true;
            }

            label = null;
            return false;
        }

        public string GetFavoriteGlyph(string path)
        {
            string normalizedPath = NormalizeFavoritePath(path);
            if (string.Equals(normalizedPath, NormalizeFavoritePath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), StringComparison.OrdinalIgnoreCase))
            {
                return "\uE80F";
            }

            if (string.Equals(normalizedPath, NormalizeFavoritePath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), StringComparison.OrdinalIgnoreCase))
            {
                return "\uE8A5";
            }

            if (string.Equals(normalizedPath, NormalizeFavoritePath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")), StringComparison.OrdinalIgnoreCase))
            {
                return "\uE896";
            }

            if (string.Equals(normalizedPath, NormalizeFavoritePath(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)), StringComparison.OrdinalIgnoreCase))
            {
                return "\uE91B";
            }

            return "\uE8B7";
        }

        public bool IsFavoritePath(AppSettings settings, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalizedPath = NormalizeFavoritePath(path);
            return settings.Favorites.Any(item => string.Equals(item.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }

        public bool ToggleFavoriteForTarget(AppSettings settings, FileCommandTarget target, Func<FileCommandTarget, string> defaultLabelResolver)
        {
            if (string.IsNullOrWhiteSpace(target.Path) || !target.IsDirectory)
            {
                return false;
            }

            string normalizedPath = NormalizeFavoritePath(target.Path);
            int existingIndex = settings.Favorites.FindIndex(item => string.Equals(item.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                settings.Favorites.RemoveAt(existingIndex);
            }
            else
            {
                settings.Favorites.Add(new FavoriteItem
                {
                    Path = normalizedPath,
                    Label = defaultLabelResolver(target),
                    Glyph = GetFavoriteGlyph(normalizedPath),
                    Order = settings.Favorites.Count
                });
            }

            NormalizeFavoritesInPlace(settings);
            settings.FavoritesInitialized = true;
            return true;
        }

        public int FindFavoriteIndex(AppSettings settings, string path)
        {
            string normalizedPath = NormalizeFavoritePath(path);
            return settings.Favorites.FindIndex(item => string.Equals(item.Path, normalizedPath, StringComparison.OrdinalIgnoreCase));
        }

        public string? ResolveFavoriteWatchPath(string? favoritePath, Func<string, bool> directoryExists, Func<string, bool> isDriveRoot)
        {
            if (string.IsNullOrWhiteSpace(favoritePath))
            {
                return null;
            }

            string normalizedPath = NormalizeFavoritePath(favoritePath);
            if (isDriveRoot(normalizedPath))
            {
                return directoryExists(normalizedPath) ? normalizedPath : null;
            }

            string? parentPath = Path.GetDirectoryName(normalizedPath);
            if (string.IsNullOrWhiteSpace(parentPath))
            {
                return null;
            }

            try
            {
                string fullParentPath = Path.GetFullPath(parentPath);
                return directoryExists(fullParentPath) ? fullParentPath : null;
            }
            catch
            {
                return null;
            }
        }

        public bool TryUpdateFavoritePathsForRename(AppSettings settings, string oldPath, string newPath, Func<string, string> storedLabelResolver)
        {
            if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
            {
                return false;
            }

            string normalizedOldPath = NormalizeFavoritePath(oldPath);
            string normalizedNewPath = NormalizeFavoritePath(newPath);
            bool changed = false;

            foreach (FavoriteItem favorite in settings.Favorites)
            {
                if (string.IsNullOrWhiteSpace(favorite.Path))
                {
                    continue;
                }

                string normalizedFavoritePath = NormalizeFavoritePath(favorite.Path);
                if (string.Equals(normalizedFavoritePath, normalizedOldPath, StringComparison.OrdinalIgnoreCase))
                {
                    favorite.Path = normalizedNewPath;
                    favorite.Label = storedLabelResolver(normalizedNewPath);
                    favorite.Glyph = GetFavoriteGlyph(normalizedNewPath);
                    changed = true;
                    continue;
                }

                if (!normalizedFavoritePath.StartsWith(normalizedOldPath + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string suffix = normalizedFavoritePath[normalizedOldPath.Length..];
                favorite.Path = normalizedNewPath + suffix;
                favorite.Label = storedLabelResolver(favorite.Path);
                favorite.Glyph = GetFavoriteGlyph(favorite.Path);
                changed = true;
            }

            if (!changed)
            {
                return false;
            }

            NormalizeFavoritesInPlace(settings);
            settings.FavoritesInitialized = true;
            return true;
        }

        public bool TryRemoveFavoritesForDeletedPath(AppSettings settings, string deletedPath)
        {
            if (string.IsNullOrWhiteSpace(deletedPath))
            {
                return false;
            }

            string normalizedDeletedPath = NormalizeFavoritePath(deletedPath);
            bool removed = false;
            for (int i = settings.Favorites.Count - 1; i >= 0; i--)
            {
                FavoriteItem favorite = settings.Favorites[i];
                if (string.IsNullOrWhiteSpace(favorite.Path))
                {
                    continue;
                }

                string normalizedFavoritePath = NormalizeFavoritePath(favorite.Path);
                bool shouldRemove = string.Equals(normalizedFavoritePath, normalizedDeletedPath, StringComparison.OrdinalIgnoreCase)
                    || normalizedFavoritePath.StartsWith(normalizedDeletedPath + "\\", StringComparison.OrdinalIgnoreCase);
                if (!shouldRemove)
                {
                    continue;
                }

                settings.Favorites.RemoveAt(i);
                removed = true;
            }

            if (!removed)
            {
                return false;
            }

            NormalizeFavoritesInPlace(settings);
            settings.FavoritesInitialized = true;
            return true;
        }

        public bool RemoveFavorite(AppSettings settings, string path)
        {
            int index = FindFavoriteIndex(settings, path);
            if (index < 0)
            {
                return false;
            }

            settings.Favorites.RemoveAt(index);
            NormalizeFavoritesInPlace(settings);
            settings.FavoritesInitialized = true;
            return true;
        }

        public bool MoveFavorite(AppSettings settings, string path, int direction)
        {
            int index = FindFavoriteIndex(settings, path);
            if (index < 0)
            {
                return false;
            }

            int targetIndex = index + direction;
            if (targetIndex < 0 || targetIndex >= settings.Favorites.Count)
            {
                return false;
            }

            FavoriteItem favorite = settings.Favorites[index];
            settings.Favorites.RemoveAt(index);
            settings.Favorites.Insert(targetIndex, favorite);
            ReindexFavoritesInPlace(settings);
            NormalizeFavoritesInPlace(settings);
            settings.FavoritesInitialized = true;
            return true;
        }

        public bool MoveFavoriteToTarget(AppSettings settings, string sourcePath, string? targetPath, bool insertAfter)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            int sourceIndex = FindFavoriteIndex(settings, sourcePath);
            int targetIndex = FindFavoriteIndex(settings, targetPath);
            if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            {
                return false;
            }

            FavoriteItem favorite = settings.Favorites[sourceIndex];
            settings.Favorites.RemoveAt(sourceIndex);
            if (sourceIndex < targetIndex)
            {
                targetIndex--;
            }

            int insertIndex = insertAfter ? targetIndex + 1 : targetIndex;
            insertIndex = Math.Clamp(insertIndex, 0, settings.Favorites.Count);
            settings.Favorites.Insert(insertIndex, favorite);

            ReindexFavoritesInPlace(settings);
            NormalizeFavoritesInPlace(settings);
            settings.FavoritesInitialized = true;
            return true;
        }
    }
}
