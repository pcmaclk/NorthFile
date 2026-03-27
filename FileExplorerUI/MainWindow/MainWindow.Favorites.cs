using FileExplorerUI.Commands;
using FileExplorerUI.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void EnsureFavoritesInitialized()
        {
            NormalizeFavoritesInPlace();
            if (_appSettings.FavoritesInitialized)
            {
                SyncFavoriteWatchers();
                return;
            }

            if (_appSettings.Favorites.Count > 0)
            {
                _appSettings.FavoritesInitialized = true;
                _appSettingsService.Save(_appSettings);
                SyncFavoriteWatchers();
                return;
            }

            _appSettings.Favorites = CreateDefaultFavoriteItems();
            _appSettings.FavoritesInitialized = true;
            _appSettingsService.Save(_appSettings);
            SyncFavoriteWatchers();
        }

        private void NormalizeFavoritesInPlace()
        {
            _favoritesController.NormalizeFavoritesInPlace(_appSettings);
        }

        private void ReindexFavoritesInPlace()
        {
            _favoritesController.ReindexFavoritesInPlace(_appSettings);
        }

        private List<FavoriteItem> CreateDefaultFavoriteItems()
        {
            List<FavoriteItem> defaults = _favoritesController.CreateDefaultFavoriteItems();
            foreach (FavoriteItem item in defaults)
            {
                item.Label = ResolveFavoriteStoredLabel(item.Path);
                item.Glyph = _favoritesController.GetFavoriteGlyph(item.Path);
            }

            return defaults;
        }

        private IReadOnlyList<SidebarNavItemModel> BuildSidebarFavoriteModels()
        {
            return _favoritesController.BuildSidebarFavoriteModels(_appSettings, S);
        }

        private string ResolveFavoriteLabel(FavoriteItem favorite)
        {
            return _favoritesController.ResolveFavoriteLabel(favorite, S);
        }

        private static string NormalizeFavoritePath(string path)
        {
            return FavoritesController.NormalizeFavoritePath(path);
        }

        private bool TryGetLocalizedFavoriteLabel(string path, out string? label)
        {
            return _favoritesController.TryGetLocalizedFavoriteLabel(path, S, out label);
        }

        private string GetFavoriteGlyph(string path)
        {
            return _favoritesController.GetFavoriteGlyph(path);
        }

        private bool IsFavoritePath(string? path)
        {
            return _favoritesController.IsFavoritePath(_appSettings, path);
        }

        private void RefreshSidebarFavorites(bool refreshSelection)
        {
            SyncFavoriteWatchers();
            StyledSidebarView.SetPinnedItems(BuildSidebarFavoriteModels(), refreshSelection);
            UpdateSidebarSelectionOnly();
        }

        private bool ToggleFavoriteForTarget(FileCommandTarget target)
        {
            bool changed = _favoritesController.ToggleFavoriteForTarget(_appSettings, target, ResolveDefaultFavoriteAddLabel);
            if (!changed)
            {
                return false;
            }

            _appSettingsService.Save(_appSettings);
            RefreshSidebarFavorites(refreshSelection: false);
            return true;
        }

        private int FindFavoriteIndex(string path)
        {
            return _favoritesController.FindFavoriteIndex(_appSettings, path);
        }

        private void SyncFavoriteWatchers()
        {
            var desiredWatchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FavoriteItem favorite in _appSettings.Favorites)
            {
                string? watchPath = ResolveFavoriteWatchPath(favorite.Path);
                if (!string.IsNullOrWhiteSpace(watchPath))
                {
                    desiredWatchPaths.Add(watchPath);
                }
            }

            foreach ((string path, FileSystemWatcher watcher) in _favoriteWatchers.ToArray())
            {
                if (desiredWatchPaths.Contains(path))
                {
                    continue;
                }

                watcher.Dispose();
                _favoriteWatchers.Remove(path);
            }

            foreach (string watchPath in desiredWatchPaths)
            {
                if (_favoriteWatchers.ContainsKey(watchPath))
                {
                    continue;
                }

                try
                {
                    var watcher = new FileSystemWatcher(watchPath)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        EnableRaisingEvents = true
                    };

                    watcher.Deleted += FavoriteWatcher_OnChanged;
                    watcher.Renamed += FavoriteWatcher_OnRenamed;
                    _favoriteWatchers.Add(watchPath, watcher);
                }
                catch
                {
                    // Favorites can still update on navigation and explicit refresh.
                }
            }
        }

        private string? ResolveFavoriteWatchPath(string? favoritePath)
        {
            return _favoritesController.ResolveFavoriteWatchPath(
                favoritePath,
                _explorerService.DirectoryExists,
                IsDriveRoot);
        }

        private void FavoriteWatcher_OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Deleted)
            {
                return;
            }

            string deletedPath = e.FullPath;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                TryRemoveFavoritesForDeletedPath(deletedPath);
            });
        }

        private void FavoriteWatcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            string oldPath = e.OldFullPath;
            string newPath = e.FullPath;

            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (TryUpdateFavoritePathsForRename(oldPath, newPath))
                {
                    return;
                }
            });
        }

        private bool TryUpdateFavoritePathsForRename(string oldPath, string newPath)
        {
            bool changed = _favoritesController.TryUpdateFavoritePathsForRename(
                _appSettings,
                oldPath,
                newPath,
                ResolveFavoriteStoredLabel);
            if (!changed)
            {
                return false;
            }

            _appSettingsService.Save(_appSettings);
            RefreshSidebarFavorites(refreshSelection: false);
            return true;
        }

        private bool TryRemoveFavoritesForDeletedPath(string deletedPath)
        {
            bool removed = _favoritesController.TryRemoveFavoritesForDeletedPath(_appSettings, deletedPath);
            if (!removed)
            {
                return false;
            }

            _appSettingsService.Save(_appSettings);
            RefreshSidebarFavorites(refreshSelection: false);
            return true;
        }

        private void DisposeFavoriteWatchers()
        {
            foreach (FileSystemWatcher watcher in _favoriteWatchers.Values)
            {
                watcher.Dispose();
            }

            _favoriteWatchers.Clear();
        }

        private bool RemoveFavorite(string path)
        {
            bool changed = _favoritesController.RemoveFavorite(_appSettings, path);
            if (!changed)
            {
                return false;
            }

            _appSettingsService.Save(_appSettings);
            RefreshSidebarFavorites(refreshSelection: false);
            return true;
        }

        private bool MoveFavorite(string path, int direction)
        {
            bool changed = _favoritesController.MoveFavorite(_appSettings, path, direction);
            if (!changed)
            {
                return false;
            }

            _appSettingsService.Save(_appSettings);
            RefreshSidebarFavorites(refreshSelection: false);
            return true;
        }

        private bool MoveFavoriteToTarget(string sourcePath, string? targetPath, bool insertAfter)
        {
            bool changed = _favoritesController.MoveFavoriteToTarget(_appSettings, sourcePath, targetPath, insertAfter);
            if (!changed)
            {
                return false;
            }

            _appSettingsService.Save(_appSettings);
            RefreshSidebarFavorites(refreshSelection: false);
            return true;
        }

        private string ResolveDefaultFavoriteAddLabel(FileCommandTarget target)
        {
            if (!string.IsNullOrWhiteSpace(target.Path) && TryGetLocalizedFavoriteLabel(target.Path, out string? localizedLabel))
            {
                return localizedLabel!;
            }

            if (!string.IsNullOrWhiteSpace(target.DisplayName))
            {
                return target.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(target.Path))
            {
                return string.Empty;
            }

            string trimmedPath = target.Path.TrimEnd('\\');
            string fileName = Path.GetFileName(trimmedPath);
            return string.IsNullOrWhiteSpace(fileName) ? trimmedPath : fileName;
        }

        private string ResolveFavoriteStoredLabel(string path)
        {
            if (TryGetLocalizedFavoriteLabel(path, out string? localizedLabel))
            {
                return localizedLabel!;
            }

            string trimmedPath = path.TrimEnd('\\');
            string fileName = Path.GetFileName(trimmedPath);
            return string.IsNullOrWhiteSpace(fileName) ? trimmedPath : fileName;
        }

        private void StyledSidebarView_FavoriteActionRequested(object? sender, SidebarFavoriteActionRequestedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Path))
            {
                return;
            }

            switch (e.Action)
            {
                case SidebarFavoriteAction.Remove:
                    RemoveFavorite(e.Path);
                    break;
                case SidebarFavoriteAction.MoveUp:
                    MoveFavorite(e.Path, -1);
                    break;
                case SidebarFavoriteAction.MoveDown:
                    MoveFavorite(e.Path, 1);
                    break;
                case SidebarFavoriteAction.Reorder:
                    MoveFavoriteToTarget(e.Path, e.TargetPath, e.InsertAfter);
                    break;
            }
        }
    }
}
