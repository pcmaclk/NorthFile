using FileExplorerUI.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using System;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void EnterSettingsShell(SettingsSection section = SettingsSection.General)
        {
            _shellMode = ShellMode.Settings;
            RefreshTitleBarDragRectangles();
            ApplyWindowMinimumWidthForShellMode();
            SetCurrentSettingsSection(section, updateSelection: true);
            RaisePropertyChanged(
                nameof(ExplorerChromeVisibility),
                nameof(ExplorerShellVisibility),
                nameof(SettingsShellVisibility),
                nameof(ShellTitleBarLeftInsetWidth),
                nameof(SidebarTopChromeMargin),
                nameof(SidebarTopChromeVisibility),
                nameof(SidebarTopSettingsVisibility),
                nameof(TitleBarSidebarSettingsVisibility));
        }

        private void ExitSettingsShell()
        {
            _shellMode = ShellMode.Explorer;
            RefreshTitleBarDragRectangles();
            ApplyWindowMinimumWidthForShellMode();
            RaisePropertyChanged(
                nameof(ExplorerChromeVisibility),
                nameof(ExplorerShellVisibility),
                nameof(SettingsShellVisibility),
                nameof(ShellTitleBarLeftInsetWidth),
                nameof(SidebarTopChromeMargin),
                nameof(SidebarTopChromeVisibility),
                nameof(SidebarTopSettingsVisibility),
                nameof(TitleBarSidebarSettingsVisibility));
        }

        private void SetCurrentSettingsSection(SettingsSection section, bool updateSelection)
        {
            _currentSettingsSection = section;

            if (updateSelection && SettingsNavigationView is not null)
            {
                _suppressSettingsNavigationSelection = true;
                foreach (object item in SettingsNavigationView.MenuItems)
                {
                    if (item is NavigationViewItem navigationViewItem
                        && navigationViewItem.Tag is string tag
                        && TryParseSettingsSection(tag, out SettingsSection parsed)
                        && parsed == section)
                    {
                        SettingsNavigationView.SelectedItem = navigationViewItem;
                        break;
                    }
                }
                _suppressSettingsNavigationSelection = false;
            }

            if (updateSelection)
            {
                SettingsViewControl?.ScrollToSection(section);
            }
        }

        private void ApplyWindowMinimumWidthForShellMode()
        {
            if (AppWindow.Presenter is not OverlappedPresenter presenter)
            {
                return;
            }

            if (_shellMode == ShellMode.Settings)
            {
                presenter.PreferredMinimumWidth = SettingsShellMinWindowWidth;
            }
            else
            {
                presenter.PreferredMinimumWidth = 0;
            }
        }

        private static bool TryParseSettingsSection(string? tag, out SettingsSection section)
        {
            return Enum.TryParse(tag, ignoreCase: true, out section);
        }

        private void SettingsNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
        {
            if (_suppressSettingsNavigationSelection)
            {
                return;
            }

            if (e.SelectedItemContainer is not NavigationViewItem item
                || item.Tag is not string tag)
            {
                return;
            }

            if (string.Equals(tag, "Back", StringComparison.OrdinalIgnoreCase))
            {
                ExitSettingsShell();
                SetCurrentSettingsSection(_currentSettingsSection, updateSelection: true);
                return;
            }

            if (!TryParseSettingsSection(tag, out SettingsSection section))
            {
                return;
            }

            SetCurrentSettingsSection(section, updateSelection: true);
        }

        private void SettingsViewControl_VisibleSectionChanged(SettingsSection section)
        {
            if (_currentSettingsSection == section)
            {
                return;
            }

            _currentSettingsSection = section;
            if (SettingsNavigationView is null)
            {
                return;
            }

            _suppressSettingsNavigationSelection = true;
            foreach (object item in SettingsNavigationView.MenuItems)
            {
                if (item is NavigationViewItem navigationViewItem
                    && navigationViewItem.Tag is string tag
                    && TryParseSettingsSection(tag, out SettingsSection parsed)
                    && parsed == section)
                {
                    SettingsNavigationView.SelectedItem = navigationViewItem;
                    break;
                }
            }
            _suppressSettingsNavigationSelection = false;
        }
    }
}
