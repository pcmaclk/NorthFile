using NorthFileUI.Settings;
using NorthFileUI.Workspace;
using System;
using System.Collections.Generic;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private void InitializeStartupPathFromSettings()
        {
            if (!_hasExplicitInitialPath &&
                _appSettings.StartupLocationPreference == StartupLocationPreference.LastLocation &&
                TryRestoreLastWorkspaceSession())
            {
                return;
            }

            string startupPath = _hasExplicitInitialPath && IsValidStartupPath(_initialPath)
                ? _initialPath
                : ResolveStartupPath();
            SetPrimaryPanelNavigationState(startupPath, syncEditors: true);
        }

        private bool TryRestoreLastWorkspaceSession()
        {
            if (!WorkspaceSessionSnapshot.TryRestore(
                    _appSettings.LastWorkspaceSessionJson,
                    ShellMyComputerPath,
                    out List<WorkspaceTabState> tabs,
                    out int activeTabIndex))
            {
                return false;
            }

            _workspaceSession.RestoreTabs(tabs, activeTabIndex);
            _workspaceLayoutHost.SetShellState(_workspaceSession.CurrentShellState);
            _startupWorkspaceSessionRestored = true;
            _ = _workspaceUiApplier.ApplyAsync(_workspaceSession.CurrentShellState);
            return true;
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
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            if (!IsValidStartupPath(currentPath) ||
                string.Equals(_appSettings.LastOpenedPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _appSettings.LastOpenedPath = currentPath;
            _appSettingsService.Save(_appSettings);
        }

        private void PersistLastWorkspaceSession()
        {
            _appSettings.LastWorkspaceSessionJson = WorkspaceSessionSnapshot.Serialize(_workspaceSession);
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            if (IsValidStartupPath(currentPath))
            {
                _appSettings.LastOpenedPath = currentPath;
            }

            _appSettingsService.Save(_appSettings);
        }
    }
}
