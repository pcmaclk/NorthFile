using FileExplorerUI.Settings;
using System;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void InitializeStartupPathFromSettings()
        {
            string startupPath = ResolveStartupPath();
            _currentPath = startupPath;
            PathTextBox.Text = GetDisplayPathText(startupPath);
        }

        private string ResolveStartupPath()
        {
            string candidate = _appSettings.StartupLocationPreference switch
            {
                StartupLocationPreference.ThisPc => ShellMyComputerPath,
                StartupLocationPreference.SpecifiedLocation => _appSettings.StartupSpecifiedPath,
                _ => _appSettings.LastOpenedPath
            };

            return IsValidStartupPath(candidate) ? candidate : ShellMyComputerPath;
        }

        private bool IsValidStartupPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _explorerService.DirectoryExists(path);
        }

        private string GetDisplayPathText(string? path)
        {
            return string.Equals(path, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase)
                ? S("SidebarMyComputer")
                : path ?? string.Empty;
        }

        private string NormalizeAddressInputPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string trimmed = path.Trim();
            return string.Equals(trimmed, S("SidebarMyComputer"), StringComparison.CurrentCultureIgnoreCase)
                ? ShellMyComputerPath
                : trimmed;
        }

        private void PersistLastOpenedPathIfNeeded()
        {
            if (!IsValidStartupPath(_currentPath) ||
                string.Equals(_appSettings.LastOpenedPath, _currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _appSettings.LastOpenedPath = _currentPath;
            _appSettingsService.Save(_appSettings);
        }
    }
}
