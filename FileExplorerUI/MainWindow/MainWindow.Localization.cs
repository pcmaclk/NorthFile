using FileExplorerUI.Settings;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void LocalizedStrings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "Item[]")
            {
                return;
            }

            ScheduleLocalizedUiRefresh();
        }

        private void ScheduleLocalizedUiRefresh()
        {
            if (_localizedUiRefreshScheduled)
            {
                _localizedUiRefreshPending = true;
                return;
            }

            _localizedUiRefreshScheduled = true;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                do
                {
                    _localizedUiRefreshPending = false;
                    RefreshLocalizedUi();
                }
                while (_localizedUiRefreshPending);

                _localizedUiRefreshScheduled = false;
            });
        }

        private void RefreshLocalizedUi()
        {
            SettingsViewControl.RefreshLocalizedText();
            UpdateWindowTitle();
            ScheduleDeferredLocalizedUiRefresh(++_localizedUiRefreshVersion);
        }

        private void ScheduleDeferredLocalizedUiRefresh(int version)
        {
            if (_localizedUiDeferredRefreshVersion >= version)
            {
                return;
            }

            _localizedUiDeferredRefreshVersion = version;
            _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (version != _localizedUiRefreshVersion)
                {
                    return;
                }

                RefreshLocalizedChromeControls();
                RefreshToolTipResources();
                RefreshEntriesContextFlyoutText();
                StyledSidebarView.RefreshLocalizedText();
                RefreshLocalizedEntryPresentation();
                UpdateDetailsHeaders();

                if (_isLoading)
                {
                    UpdateStatus(S("StatusLoading"));
                    return;
                }

                if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
                {
                    UpdateStatus(SF("StatusDriveCount", _entries.Count));
                    return;
                }

                UpdateStatus(SF("StatusCurrentFolderItems", _totalEntries));
            });
        }

        private void UpdateWindowTitle()
        {
            if (AppWindow is null)
            {
                return;
            }

            if (_lastTitleWasReadFailed)
            {
                this.AppWindow.Title = SF("WindowTitleReadFailedFormat", _engineVersion);
                NotifyTitleBarTextChanged();
                return;
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                this.AppWindow.Title = SF("WindowTitleDrivesFormat", _engineVersion, _entries.Count);
                NotifyTitleBarTextChanged();
                return;
            }

            this.AppWindow.Title = SF("WindowTitleItemsFormat", _engineVersion, _entries.Count);
            NotifyTitleBarTextChanged();
        }

        public string CurrentTabTitleText => GetCurrentTabTitleText();

        public string TitleBarTabGlyph => string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase)
            ? "\uE7F4"
            : "\uE8B7";

        private string GetCurrentTabTitleText()
        {
            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return S("SidebarMyComputer");
            }

            if (string.IsNullOrWhiteSpace(_currentPath))
            {
                return "NorthFile";
            }

            string trimmed = _currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return _currentPath;
            }

            string name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? trimmed : name;
        }

        private void NotifyTitleBarTextChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTabTitleText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TitleBarTabGlyph)));
        }

        private void RefreshLocalizedEntryPresentation()
        {
            EntryViewModel[] presentationEntries = _presentationSourceEntries.ToArray();
            EntryViewModel[] loadedEntries = _entries.ToArray();
            var seen = new HashSet<EntryViewModel>();
            foreach (EntryViewModel entry in presentationEntries.Concat(loadedEntries))
            {
                if (!seen.Add(entry) || !entry.IsLoaded)
                {
                    continue;
                }

                if (!entry.IsGroupHeader)
                {
                    entry.DisplayName = GetEntryDisplayName(entry.Name, entry.IsDirectory);
                    entry.Type = GetEntryTypeText(entry.Name, entry.IsDirectory, entry.IsLink);
                    entry.ModifiedText = FormatModifiedTime(entry.ModifiedAt);
                }
            }

            bool requiresPresentationRebuild =
                _currentSortField == EntrySortField.Type ||
                _currentGroupField == EntryGroupField.Type;

            if (requiresPresentationRebuild)
            {
                ApplyCurrentPresentation();
            }
            else
            {
                UpdateEntrySelectionVisuals();
            }
        }

        private void UpdateDisplayedEntryNames()
        {
            foreach (EntryViewModel entry in _entries)
            {
                if (!entry.IsLoaded || entry.IsGroupHeader)
                {
                    continue;
                }

                entry.DisplayName = GetEntryDisplayName(entry.Name, entry.IsDirectory);
            }

            foreach (EntryViewModel entry in _presentationSourceEntries)
            {
                if (!entry.IsLoaded || entry.IsGroupHeader)
                {
                    continue;
                }

                entry.DisplayName = GetEntryDisplayName(entry.Name, entry.IsDirectory);
            }

            InvalidatePresentationSourceCache();
        }

        public string ThemeToggleGlyph =>
            (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark
                ? "\uE706"
                : "\uE708";

        private void LanguageToggleButton_Click(object sender, RoutedEventArgs e)
        {
            AppThemePreference nextPreference = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark
                ? AppThemePreference.Light
                : AppThemePreference.Dark;

            _appSettings.ThemePreference = nextPreference;
            _appSettingsService.Save(_appSettings);
            SettingsViewControl.SetAppearanceSettings(
                _appSettings.ThemePreference,
                _appSettings.DefaultSortField,
                _appSettings.DefaultGroupField);
            ApplyThemePreference(nextPreference);
        }

        private void RefreshLocalizedChromeControls()
        {
            PathTextBox.PlaceholderText = S("PathTextBox.PlaceholderText");
            SearchTextBox.PlaceholderText = S("SearchTextBox.PlaceholderText");
            CommandDockTitleTextBlock.Text = S("CommandDockTitleTextBlock.Text");
            RenameButton.Content = S("RenameButton.Content");
            DeleteButton.Content = S("DeleteButton.Content");
            NextButton.Content = S("NextButton.Content");
            RenameTextBox.PlaceholderText = S("RenameTextBox.PlaceholderText");
            RecursiveDeleteCheckBox.Content = S("RecursiveDeleteCheckBox.Content");
            DockTopRadioButton.Content = S("DockTopRadioButton.Content");
            DockRightRadioButton.Content = S("DockRightRadioButton.Content");
            DockBottomRadioButton.Content = S("DockBottomRadioButton.Content");
            CommandAutoHideSwitch.Header = S("CommandAutoHideSwitch.Header");
            CommandPeekButton.Content = S("CommandPeekButton.Content");
        }

        private void RefreshToolTipResources()
        {
            RefreshToolTip(BackButton, "ToolTipBack");
            RefreshToolTip(ForwardButton, "ToolTipForward");
            RefreshToolTip(UpButton, "ToolTipUp");
            RefreshToolTip(LoadButton, "ToolTipRefresh");
            RefreshToolTip(NewFileButton, "ToolTipNewFile");
            RefreshToolTip(NewFolderButton, "ToolTipNewFolder");
            RefreshToolTip(CopyButton, "ToolTipCopy");
            RefreshToolTip(CutButton, "ToolTipCut");
            RefreshToolTip(PasteButton, "ToolTipPaste");
            RefreshToolTip(LanguageToggleButton, "ToolTipToggleTheme");
            RefreshToolTip(OverflowBreadcrumbButton, "ToolTipShowHiddenPathSegments");
        }

        private static void RefreshToolTip(FrameworkElement element, string resourceKey)
        {
            if (ToolTipService.GetToolTip(element) is ToolTip toolTip)
            {
                toolTip.Content = LocalizedStrings.Instance.Get(resourceKey);
            }
        }
    }
}
