using FileExplorerUI.Settings;
using FileExplorerUI.Services;
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
    private readonly Dictionary<SettingsSection, FrameworkElement> _sectionAnchors;
    private readonly DispatcherQueueTimer _sectionSyncTimer;
    private bool _suppressSectionChanged;
    private SettingsSection _lastReportedSection = SettingsSection.General;
    private double _lastObservedVerticalOffset = double.NaN;

    public event Action<SettingsSection>? VisibleSectionChanged;

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
}
