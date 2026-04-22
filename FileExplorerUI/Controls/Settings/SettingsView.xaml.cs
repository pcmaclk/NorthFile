using FileExplorerUI.Settings;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Windows.Foundation;

namespace FileExplorerUI.Controls;

public sealed partial class SettingsView : UserControl
{
    private static string S(string key) => LocalizedStrings.Instance.Get(key);
    private bool _suppressSidebarSectionEvents;
    private readonly Dictionary<SettingsSection, FrameworkElement> _sectionAnchors;
    private readonly DispatcherQueueTimer _sectionSyncTimer;
    private bool _suppressSectionChanged;
    private SettingsSection _lastReportedSection = SettingsSection.General;
    private double _lastObservedVerticalOffset = double.NaN;

    public event Action<SettingsSection>? VisibleSectionChanged;
    public event Action<bool, bool, bool, bool>? SidebarSectionVisibilityChanged;
    public event Action<bool, bool, bool, bool>? FileDisplaySettingsChanged;
    public event Action<AppThemePreference>? ThemePreferenceChanged;
    public event Action<AppLanguagePreference>? LanguagePreferenceChanged;
    public event Action<StartupLocationPreference>? StartupLocationPreferenceChanged;
    public event Action<string>? StartupSpecifiedPathChanged;
    public event Action? StartupSpecifiedPathBrowseRequested;
    public event Action<EntrySortField>? DefaultSortFieldChanged;
    public event Action<EntryGroupField>? DefaultGroupFieldChanged;
    public event Action<bool>? DeleteConfirmationChanged;
    public event Action<bool>? AutoStartChanged;
    public event Action<bool>? MinimizeToTrayChanged;
    public event Action? ExportSettingsRequested;
    public event Action? ImportSettingsRequested;

    public SettingsView()
    {
        InitializeComponent();
        _sectionAnchors = new Dictionary<SettingsSection, FrameworkElement>
        {
            [SettingsSection.General] = GeneralSection,
            [SettingsSection.Appearance] = AppearanceSection,
            [SettingsSection.FilesAndFolders] = FilesAndFoldersSection,
            [SettingsSection.Shortcuts] = ShortcutsSection,
            [SettingsSection.Tags] = TagsSection,
            [SettingsSection.Advanced] = AdvancedSection,
            [SettingsSection.About] = AboutSection
        };

        _sectionSyncTimer = DispatcherQueue.CreateTimer();
        _sectionSyncTimer.Interval = TimeSpan.FromMilliseconds(150);
        _sectionSyncTimer.Tick += SectionSyncTimer_Tick;

        Loaded += SettingsView_Loaded;
        Unloaded += SettingsView_Unloaded;
        RefreshAboutText();
    }

    public string EngineVersionText => string.Format(
        LocalizedStrings.Instance.Get("SettingsAboutEngineVersion"),
        new ExplorerService().GetEngineVersion());

    public string AppVersionText
    {
        get
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
            return string.Format(LocalizedStrings.Instance.Get("SettingsAboutAppVersion"), version);
        }
    }

    public void SetSidebarSectionVisibility(bool showFavorites, bool showCloud, bool showNetwork, bool showTags)
    {
        _suppressSidebarSectionEvents = true;
        FavoritesToggleSwitch.IsOn = showFavorites;
        CloudToggleSwitch.IsOn = showCloud;
        NetworkToggleSwitch.IsOn = showNetwork;
        TagsToggleSwitch.IsOn = showTags;
        _suppressSidebarSectionEvents = false;
    }

    public void SetDeleteConfirmationEnabled(bool enabled)
    {
        _suppressSidebarSectionEvents = true;
        DeleteConfirmToggleSwitch.IsOn = enabled;
        _suppressSidebarSectionEvents = false;
    }

    public void SetAdvancedSettings(bool autoStartEnabled, bool minimizeToTrayEnabled)
    {
        _suppressSidebarSectionEvents = true;
        AutoStartToggleSwitch.IsOn = autoStartEnabled;
        MinimizeToTrayToggleSwitch.IsOn = minimizeToTrayEnabled;
        _suppressSidebarSectionEvents = false;
    }

    public void SetAppearanceSettings(AppThemePreference themePreference, EntrySortField defaultSortField, EntryGroupField defaultGroupField)
    {
        _suppressSidebarSectionEvents = true;
        ThemePreferenceComboBox.SelectedIndex = (int)themePreference;
        DefaultSortComboBox.SelectedIndex = defaultSortField switch
        {
            EntrySortField.Name => 0,
            EntrySortField.ModifiedDate => 1,
            EntrySortField.Type => 2,
            EntrySortField.Size => 3,
            _ => 0
        };
        DefaultGroupComboBox.SelectedIndex = defaultGroupField switch
        {
            EntryGroupField.None => 0,
            EntryGroupField.Name => 1,
            EntryGroupField.Type => 2,
            EntryGroupField.ModifiedDate => 3,
            _ => 0
        };
        _suppressSidebarSectionEvents = false;
    }

    public void SetGeneralSettings(
        AppLanguagePreference languagePreference,
        StartupLocationPreference startupLocationPreference,
        string startupSpecifiedPath)
    {
        _suppressSidebarSectionEvents = true;
        LanguagePreferenceComboBox.SelectedIndex = languagePreference switch
        {
            AppLanguagePreference.ChineseSimplified => 1,
            AppLanguagePreference.English => 2,
            _ => 0
        };
        StartupLocationComboBox.SelectedIndex = startupLocationPreference switch
        {
            StartupLocationPreference.LastLocation => 1,
            StartupLocationPreference.SpecifiedLocation => 2,
            _ => 0
        };
        StartupSpecifiedPathTextBox.Text = startupSpecifiedPath;
        UpdateStartupSpecifiedPathVisibility();
        _suppressSidebarSectionEvents = false;
    }

    public void RefreshLocalizedText()
    {
        LanguageUseSystemComboBoxItem.Content = S("SettingsUseSystem");
        LanguageChineseSimplifiedComboBoxItem.Content = S("SettingsLanguageChineseSimplified");
        LanguageEnglishComboBoxItem.Content = "English";
        StartupThisPcComboBoxItem.Content = S("SettingsStartupUseThisPc");
        StartupLastLocationComboBoxItem.Content = S("SettingsStartupLastLocation");
        StartupSpecifiedLocationComboBoxItem.Content = S("SettingsStartupSpecifiedLocation");
        StartupSpecifiedPathTextBox.PlaceholderText = S("SettingsStartupSpecifiedPathPlaceholder");
        StartupBrowseButton.Content = S("SettingsBrowseFolder");
        ThemeUseSystemComboBoxItem.Content = S("SettingsUseSystem");
        ThemeLightComboBoxItem.Content = S("SettingsThemeLight");
        ThemeDarkComboBoxItem.Content = S("SettingsThemeDark");
        SortByNameComboBoxItem.Content = S("CommonSortByName");
        SortByModifiedDateComboBoxItem.Content = S("CommonSortByModifiedDate");
        SortByTypeComboBoxItem.Content = S("CommonSortByType");
        SortBySizeComboBoxItem.Content = S("CommonSortBySize");
        GroupByNoneComboBoxItem.Content = S("CommonGroupByNone");
        GroupByNameComboBoxItem.Content = S("CommonGroupByName");
        GroupByTypeComboBoxItem.Content = S("CommonGroupByType");
        GroupByModifiedDateComboBoxItem.Content = S("CommonGroupByModifiedDate");

        int languageIndex = LanguagePreferenceComboBox.SelectedIndex;
        int startupIndex = StartupLocationComboBox.SelectedIndex;
        int themeIndex = ThemePreferenceComboBox.SelectedIndex;
        int sortIndex = DefaultSortComboBox.SelectedIndex;
        int groupIndex = DefaultGroupComboBox.SelectedIndex;

        LanguagePreferenceComboBox.SelectedIndex = -1;
        LanguagePreferenceComboBox.SelectedIndex = languageIndex;
        StartupLocationComboBox.SelectedIndex = -1;
        StartupLocationComboBox.SelectedIndex = startupIndex;
        UpdateStartupSpecifiedPathVisibility();
        ThemePreferenceComboBox.SelectedIndex = -1;
        ThemePreferenceComboBox.SelectedIndex = themeIndex;
        DefaultSortComboBox.SelectedIndex = -1;
        DefaultSortComboBox.SelectedIndex = sortIndex;
        DefaultGroupComboBox.SelectedIndex = -1;
        DefaultGroupComboBox.SelectedIndex = groupIndex;
        RefreshAboutText();
    }

    private void RefreshAboutText()
    {
        EngineVersionTextBlock.Text = EngineVersionText;
        AppVersionTextBlock.Text = AppVersionText;
    }

    public void SetFileDisplaySettings(bool showHidden, bool showProtectedSystem, bool showDotFiles, bool showFileExtensions)
    {
        _suppressSidebarSectionEvents = true;
        ShowHiddenToggleSwitch.IsOn = showHidden;
        ShowProtectedSystemToggleSwitch.IsOn = showProtectedSystem;
        ShowDotFilesToggleSwitch.IsOn = showDotFiles;
        ShowFileExtensionsToggleSwitch.IsOn = showFileExtensions;
        _suppressSidebarSectionEvents = false;
    }

    public void ScrollToSection(SettingsSection section)
    {
        if (!_sectionAnchors.TryGetValue(section, out FrameworkElement? target))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            _suppressSectionChanged = true;
            Point targetPoint = target.TransformToVisual(SettingsSectionsHost).TransformPoint(new Point(0, 0));
            double offset = targetPoint.Y;
            double targetOffset = offset <= 24 ? 0 : offset - 24;
            RootScrollViewer.ChangeView(null, targetOffset, null, true);
            _lastReportedSection = section;
            VisibleSectionChanged?.Invoke(section);
            _suppressSectionChanged = false;
        });
    }

    private void RootScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
    {
        EvaluateVisibleSection();
    }

    private void SettingsView_Loaded(object sender, RoutedEventArgs e)
    {
        _sectionSyncTimer.Start();
    }

    private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
    {
        _sectionSyncTimer.Stop();
    }

    private void SectionSyncTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        if (Math.Abs(RootScrollViewer.VerticalOffset - _lastObservedVerticalOffset) < 0.5)
        {
            return;
        }

        EvaluateVisibleSection();
    }

    private void EvaluateVisibleSection()
    {
        if (_suppressSectionChanged)
        {
            return;
        }

        _lastObservedVerticalOffset = RootScrollViewer.VerticalOffset;
        double probe = RootScrollViewer.VerticalOffset + 64;
        SettingsSection activeSection = SettingsSection.General;

        foreach ((SettingsSection section, FrameworkElement anchor) in _sectionAnchors.OrderBy(pair => pair.Key))
        {
            Point targetPoint = anchor.TransformToVisual(SettingsSectionsHost).TransformPoint(new Point(0, 0));
            if (targetPoint.Y <= probe)
            {
                activeSection = section;
            }
            else
            {
                break;
            }
        }

        if (_lastReportedSection == activeSection)
        {
            return;
        }

        _lastReportedSection = activeSection;
        VisibleSectionChanged?.Invoke(activeSection);
    }

    private void SidebarSectionToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        SidebarSectionVisibilityChanged?.Invoke(
            FavoritesToggleSwitch.IsOn,
            CloudToggleSwitch.IsOn,
            NetworkToggleSwitch.IsOn,
            TagsToggleSwitch.IsOn);
    }

    private void DeleteConfirmToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        DeleteConfirmationChanged?.Invoke(DeleteConfirmToggleSwitch.IsOn);
    }

    private void AutoStartToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        AutoStartChanged?.Invoke(AutoStartToggleSwitch.IsOn);
    }

    private void MinimizeToTrayToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        MinimizeToTrayChanged?.Invoke(MinimizeToTrayToggleSwitch.IsOn);
    }

    private void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ExportSettingsRequested?.Invoke();
    }

    private void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ImportSettingsRequested?.Invoke();
    }

    private void FileDisplayToggleSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        FileDisplaySettingsChanged?.Invoke(
            ShowHiddenToggleSwitch.IsOn,
            ShowProtectedSystemToggleSwitch.IsOn,
            ShowDotFilesToggleSwitch.IsOn,
            ShowFileExtensionsToggleSwitch.IsOn);
    }

    private void ThemePreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        ThemePreferenceChanged?.Invoke(ThemePreferenceComboBox.SelectedIndex switch
        {
            1 => AppThemePreference.Light,
            2 => AppThemePreference.Dark,
            _ => AppThemePreference.UseSystem
        });
    }

    private void LanguagePreferenceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        LanguagePreferenceChanged?.Invoke(LanguagePreferenceComboBox.SelectedIndex switch
        {
            1 => AppLanguagePreference.ChineseSimplified,
            2 => AppLanguagePreference.English,
            _ => AppLanguagePreference.UseSystem
        });
    }

    private void StartupLocationComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        UpdateStartupSpecifiedPathVisibility();
        StartupLocationPreferenceChanged?.Invoke(StartupLocationComboBox.SelectedIndex switch
        {
            1 => StartupLocationPreference.LastLocation,
            2 => StartupLocationPreference.SpecifiedLocation,
            _ => StartupLocationPreference.ThisPc
        });
    }

    private void StartupSpecifiedPathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        StartupSpecifiedPathChanged?.Invoke(StartupSpecifiedPathTextBox.Text.Trim());
    }

    private void StartupBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        StartupSpecifiedPathBrowseRequested?.Invoke();
    }

    public void SetStartupSpecifiedPath(string path)
    {
        _suppressSidebarSectionEvents = true;
        StartupSpecifiedPathTextBox.Text = path;
        _suppressSidebarSectionEvents = false;
    }

    private void UpdateStartupSpecifiedPathVisibility()
    {
        if (StartupSpecifiedPathPanel is null || StartupLocationComboBox is null)
        {
            return;
        }

        StartupSpecifiedPathPanel.Visibility = StartupLocationComboBox.SelectedIndex == 1
            ? Visibility.Collapsed
            : StartupLocationComboBox.SelectedIndex == 2
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (StartupSpecifiedCustomPathPanel is not null)
        {
            StartupSpecifiedCustomPathPanel.Visibility = StartupSpecifiedPathPanel.Visibility;
        }
    }

    private void DefaultSortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        DefaultSortFieldChanged?.Invoke(DefaultSortComboBox.SelectedIndex switch
        {
            1 => EntrySortField.ModifiedDate,
            2 => EntrySortField.Type,
            3 => EntrySortField.Size,
            _ => EntrySortField.Name
        });
    }

    private void DefaultGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSidebarSectionEvents)
        {
            return;
        }

        DefaultGroupFieldChanged?.Invoke(DefaultGroupComboBox.SelectedIndex switch
        {
            1 => EntryGroupField.Name,
            2 => EntryGroupField.Type,
            3 => EntryGroupField.ModifiedDate,
            _ => EntryGroupField.None
        });
    }
}
