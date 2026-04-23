using FileExplorerUI.Workspace;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using System;
using System.Numerics;

namespace FileExplorerUI.Controls;

public sealed class WorkspaceTabViewFactory
{
    private static readonly Thickness TabHeaderPadding = new(16, 0, 0, 0);
    private readonly FrameworkElement _resourceHost;
    private readonly double _glyphSize;
    private readonly RoutedEventHandler _closeHandler;
    private readonly string _closeToolTipText;
    private readonly Visibility _tabVisibility;
    private readonly Func<Brush?> _activeBackgroundProvider;
    private readonly Func<Brush?> _activeForegroundProvider;
    private readonly Func<Brush?> _inactiveForegroundProvider;

    public WorkspaceTabViewFactory(
        FrameworkElement resourceHost,
        double glyphSize,
        RoutedEventHandler closeHandler,
        string closeToolTipText,
        Visibility tabVisibility,
        Func<Brush?> activeBackgroundProvider,
        Func<Brush?> activeForegroundProvider,
        Func<Brush?> inactiveForegroundProvider)
    {
        _resourceHost = resourceHost;
        _glyphSize = glyphSize;
        _closeHandler = closeHandler;
        _closeToolTipText = closeToolTipText;
        _tabVisibility = tabVisibility;
        _activeBackgroundProvider = activeBackgroundProvider;
        _activeForegroundProvider = activeForegroundProvider;
        _inactiveForegroundProvider = inactiveForegroundProvider;
    }

    public TabViewItem Create(WorkspaceTabPresentation presentation)
    {
        var item = new TabViewItem
        {
            Tag = presentation.Tab
        };

        Apply(item, presentation);
        return item;
    }

    public void Apply(TabViewItem item, WorkspaceTabPresentation presentation)
    {
        item.Tag = presentation.Tab;
        item.DataContext = presentation;
        item.IsClosable = false;
        item.Margin = new Thickness(0);
        item.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        item.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        item.BorderThickness = new Thickness(0);
        item.Transitions = new TransitionCollection();
        item.Visibility = _tabVisibility;
        item.Style = (Style)_resourceHost.Resources[presentation.IsActive
            ? "TitleBarSingleTabItemStyle"
            : "TitleBarInactiveTabItemStyle"];
        item.Header = CreateHeader(presentation);
        item.IsSelected = presentation.IsActive;
    }

    private object CreateHeader(WorkspaceTabPresentation presentation)
    {
        var shadowReceiver = new Grid();
        var border = new Border
        {
            Height = 32,
            Padding = TabHeaderPadding,
            Background = presentation.IsActive
                ? _activeBackgroundProvider() ?? ResolveThemeBrush("TitleBarActiveTabPillBackgroundBrush", "LayerFillColorDefaultBrush")
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = presentation.IsActive
                ? ResolveThemeBrush("TitleBarActiveTabPillBorderBrush", "ExplorerShellPanelBorderBrush")
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = presentation.IsActive ? new Thickness(1) : new Thickness(0),
            CornerRadius = presentation.IsActive ? new CornerRadius(8) : new CornerRadius(0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Translation = presentation.IsActive ? new Vector3(0, 0, 6) : Vector3.Zero
        };
        if (presentation.IsActive)
        {
            var shadow = new ThemeShadow();
            shadow.Receivers.Add(shadowReceiver);
            border.Shadow = shadow;
        }

        var content = CreateHeaderGrid();
        var leading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = _glyphSize,
            Glyph = presentation.Glyph,
            Foreground = GetTabForegroundBrush(presentation.IsActive),
            Visibility = presentation.IsActive ? Visibility.Visible : Visibility.Collapsed
        };
        leading.Children.Add(icon);

        leading.Children.Add(new TextBlock
        {
            Text = presentation.Title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = GetTabForegroundBrush(presentation.IsActive)
        });

        content.Children.Add(leading);
        content.Children.Add(CreateCloseButton(presentation, 1));
        content.Children.Add(CreateTrailingSeparator(presentation, 2));
        border.Child = content;
        if (!presentation.IsActive)
        {
            return border;
        }

        var host = new Grid();
        host.Children.Add(shadowReceiver);
        host.Children.Add(border);
        return host;
    }

    private static Grid CreateHeaderGrid()
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        return grid;
    }

    private Button CreateCloseButton(WorkspaceTabPresentation presentation, int column)
    {
        var button = new Button
        {
            Tag = presentation.Tab,
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 2, 0, 0),
            MinWidth = 24,
            MinHeight = 24,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Visibility = presentation.CanClose ? Visibility.Visible : Visibility.Collapsed,
            Content = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = _glyphSize,
                Glyph = "\uE711",
                Foreground = presentation.IsActive
                    ? GetTabForegroundBrush(isActive: true)
                    : GetTabForegroundBrush(isActive: false)
            }
        };

        Grid.SetColumn(button, column);
        ToolTipService.SetToolTip(button, new ToolTip { Content = _closeToolTipText });
        button.Click += _closeHandler;
        return button;
    }

    private Border CreateTrailingSeparator(WorkspaceTabPresentation presentation, int column)
    {
        var border = new Border
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(0, 0, -2, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = ResolveThemeBrush("TabViewItemSeparator"),
            Visibility = presentation.ShowTrailingSeparator ? Visibility.Visible : Visibility.Collapsed
        };

        Grid.SetColumn(border, column);
        return border;
    }

    private Brush ResolveThemeBrush(string key, string? appFallbackKey = null)
    {
        Application app = Application.Current!;
        if (TryResolveThemeBrush(_resourceHost.Resources, key, out Brush? localBrush) &&
            localBrush is not null)
        {
            return localBrush;
        }

        if (appFallbackKey is not null &&
            TryResolveThemeBrush(app.Resources, appFallbackKey, out Brush? fallbackBrush) &&
            fallbackBrush is not null)
        {
            return fallbackBrush;
        }

        if (app.Resources.TryGetValue(appFallbackKey ?? key, out object? appValue) && appValue is Brush appBrush)
        {
            return appBrush;
        }

        return new SolidColorBrush(Colors.Transparent);
    }

    private bool TryResolveThemeBrush(ResourceDictionary resources, string key, out Brush? brush)
    {
        string themeKey = GetThemeDictionaryKey();
        if (resources.ThemeDictionaries.TryGetValue(themeKey, out object? themedObject) &&
            themedObject is ResourceDictionary themedDictionary &&
            themedDictionary.TryGetValue(key, out object? themedValue) &&
            themedValue is Brush themedBrush)
        {
            brush = themedBrush;
            return true;
        }

        if (resources.TryGetValue(key, out object? value) && value is Brush directBrush)
        {
            brush = directBrush;
            return true;
        }

        brush = null;
        return false;
    }

    private string GetThemeDictionaryKey()
    {
        Application app = Application.Current!;
        ElementTheme actualTheme = _resourceHost.ActualTheme;
        if (actualTheme == ElementTheme.Default)
        {
            actualTheme = app.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }

        return actualTheme == ElementTheme.Dark ? "Dark" : "Light";
    }

    private Brush GetTabForegroundBrush(bool isActive)
    {
        Brush? explicitBrush = isActive
            ? _activeForegroundProvider()
            : _inactiveForegroundProvider();

        if (explicitBrush is not null)
        {
            return explicitBrush;
        }

        return isActive
            ? ResolveThemeBrush("TitleBarActiveTabForegroundBrush", "TextFillColorPrimaryBrush")
            : ResolveThemeBrush("TitleBarInactiveTabForegroundBrush", "TextFillColorSecondaryBrush");
    }
}
