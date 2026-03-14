using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace FileExplorerUI
{
    internal sealed class SidebarGroupControl : StackPanel
    {
        private readonly Brush _normalBackground;
        private readonly Brush _hoverBackground;
        private readonly Brush _selectedBackground;
        private readonly Brush _indicatorBrush;
        private readonly Border _headerBorder;
        private readonly Border _selectionIndicator;
        private readonly FontIcon _chevronIcon;
        private readonly TextBlock _headerText;
        private bool _expanded;
        private bool _compact;
        private bool _selected;
        private bool _pointerOver;

        public StackPanel Body { get; }
        public bool IsExpanded => _expanded;
        public event EventHandler? ExpandedChanged;

        public SidebarGroupControl(string text, Brush normalBackground, Brush hoverBackground, Brush selectedBackground, Brush indicatorBrush, Brush textBrush, bool expanded = true, double headerHeight = 24)
        {
            _normalBackground = normalBackground;
            _hoverBackground = hoverBackground;
            _selectedBackground = selectedBackground;
            _indicatorBrush = indicatorBrush;
            _expanded = expanded;

            Orientation = Orientation.Vertical;
            Spacing = 0;
            HorizontalAlignment = HorizontalAlignment.Stretch;

            var headerGrid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } }
            };

            _headerText = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Foreground = textBrush
            };
            headerGrid.Children.Add(_headerText);
            _headerText.TextTrimming = TextTrimming.CharacterEllipsis;

            _chevronIcon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = _expanded ? "\uE70D" : "\uE76C",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_chevronIcon, 1);
            headerGrid.Children.Add(_chevronIcon);

            _selectionIndicator = new Border
            {
                Width = 3,
                Background = _indicatorBrush,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(1, 7, 0, 7),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = 0
            };

            _headerBorder = new Border
            {
                Height = headerHeight,
                Background = _normalBackground,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 0, 8, 0),
                Child = headerGrid
            };
            _headerBorder.PointerEntered += (_, __) =>
            {
                _pointerOver = true;
                UpdateHeaderBackground();
            };
            _headerBorder.PointerExited += (_, __) =>
            {
                _pointerOver = false;
                UpdateHeaderBackground();
            };
            _headerBorder.PointerPressed += (_, __) =>
            {
                _pointerOver = true;
                UpdateHeaderBackground();
            };
            _headerBorder.PointerReleased += (_, __) =>
            {
                _pointerOver = true;
                UpdateHeaderBackground();
            };
            _headerBorder.Tapped += (_, __) => Toggle();

            Body = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed
            };

            var headerContainer = new Grid();
            headerContainer.Children.Add(_headerBorder);
            headerContainer.Children.Add(_selectionIndicator);

            Children.Add(headerContainer);
            Children.Add(Body);
        }

        public void Toggle()
        {
            if (_compact)
            {
                return;
            }

            _expanded = !_expanded;
            Body.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            _chevronIcon.Glyph = _expanded ? "\uE70D" : "\uE76C";
            UpdateHeaderBackground();
            ExpandedChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetCompact(bool compact)
        {
            if (_compact == compact)
            {
                return;
            }

            _compact = compact;
            _headerBorder.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            Body.Margin = compact ? new Thickness(0) : new Thickness(0, 4, 0, 0);
            Body.Visibility = compact || _expanded ? Visibility.Visible : Visibility.Collapsed;
            UpdateHeaderBackground();
        }

        public void ApplySelection(bool selected)
        {
            _selected = selected;
            UpdateHeaderBackground();
        }

        private void UpdateHeaderBackground()
        {
            if (_selected && !_expanded && !_compact)
            {
                _headerBorder.Background = _selectedBackground;
                _selectionIndicator.Opacity = 1;
                return;
            }

            _selectionIndicator.Opacity = 0;
            _headerBorder.Background = _pointerOver ? _hoverBackground : _normalBackground;
        }
    }

    internal sealed class SidebarItemButton : Button
    {
        private readonly Brush _normalBackground;
        private readonly Brush _hoverBackground;
        private readonly Brush _selectedBackground;
        private readonly Border _backgroundLayer;
        private readonly Border _selectionIndicator;
        private readonly Grid _rootGrid;
        private readonly Grid _contentGrid;
        private readonly FontIcon _iconBlock;
        private readonly TextBlock _labelBlock;
        private bool _selected;
        private bool _pointerOver;
        private bool _compact;

        public SidebarItemButton(
            string label,
            string iconGlyph,
            string path,
            Brush normalBackground,
            Brush hoverBackground,
            Brush selectedBackground,
            Brush textBrush,
            Brush transparentBrush,
            Brush indicatorBrush)
        {
            _normalBackground = normalBackground;
            _hoverBackground = hoverBackground;
            _selectedBackground = selectedBackground;

            Tag = path;
            Height = double.NaN;
            MinHeight = 28;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(0);
            Padding = new Thickness(0);
            MinWidth = 0;
            BorderThickness = new Thickness(0);
            BorderBrush = transparentBrush;
            Background = _normalBackground;
            CornerRadius = new CornerRadius(6);
            Foreground = textBrush;
            ToolTipService.SetToolTip(this, label);
            Resources["ButtonBackground"] = transparentBrush;
            Resources["ButtonBackgroundPointerOver"] = transparentBrush;
            Resources["ButtonBackgroundPressed"] = transparentBrush;
            Resources["ButtonBackgroundDisabled"] = transparentBrush;
            Resources["ButtonBorderBrush"] = transparentBrush;
            Resources["ButtonBorderBrushPointerOver"] = transparentBrush;
            Resources["ButtonBorderBrushPressed"] = transparentBrush;
            Resources["ButtonBorderBrushDisabled"] = transparentBrush;
            Resources["ButtonForeground"] = textBrush;
            Resources["ButtonForegroundPointerOver"] = textBrush;
            Resources["ButtonForegroundPressed"] = textBrush;
            Resources["ButtonForegroundDisabled"] = textBrush;

            _selectionIndicator = new Border
            {
                Width = 3,
                Background = indicatorBrush,
                CornerRadius = new CornerRadius(1),
                Margin = new Thickness(0, 6, 0, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Opacity = 0,
            };

            _labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = textBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            _iconBlock = new FontIcon
            {
                Glyph = iconGlyph,
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 14,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _contentGrid = new Grid
            {
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
                }
            };
            _contentGrid.Children.Add(_iconBlock);
            Grid.SetColumn(_labelBlock, 1);
            _contentGrid.Children.Add(_labelBlock);

            _backgroundLayer = new Border
            {
                Background = _normalBackground,
                CornerRadius = new CornerRadius(6)
            };

            _rootGrid = new Grid
            {
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center
            };
            _rootGrid.Children.Add(_backgroundLayer);
            _rootGrid.Children.Add(_contentGrid);
            _rootGrid.Children.Add(_selectionIndicator);

            Content = _rootGrid;

            PointerEntered += (_, __) =>
            {
                _pointerOver = true;
                UpdateBackground();
            };
            PointerExited += (_, __) =>
            {
                _pointerOver = false;
                UpdateBackground();
            };
        }

        public void ApplySelection(bool selected)
        {
            _selected = selected;
            _selectionIndicator.Opacity = selected ? 1 : 0;
            UpdateBackground();
        }

        private void UpdateBackground()
        {
            if (_selected)
            {
                _backgroundLayer.Background = _selectedBackground;
                return;
            }

            _backgroundLayer.Background = _pointerOver ? _hoverBackground : _normalBackground;
        }

        public void SetCompact(bool compact)
        {
            if (_compact == compact)
            {
                return;
            }

            _compact = compact;
            _labelBlock.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            _contentGrid.Margin = compact ? new Thickness(0) : new Thickness(8, 0, 8, 0);
            _contentGrid.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            _contentGrid.ColumnSpacing = compact ? 0 : 8;
            _iconBlock.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            Width = compact ? 32 : double.NaN;
            Height = compact ? 32 : double.NaN;
            MinHeight = compact ? 32 : 28;
            MinWidth = compact ? 32 : 0;
            _rootGrid.Width = compact ? 32 : double.NaN;
            _rootGrid.Height = compact ? 32 : double.NaN;
            _selectionIndicator.Visibility = Visibility.Visible;
            _selectionIndicator.Height = compact ? 16 : double.NaN;
            _selectionIndicator.VerticalAlignment = compact ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            _selectionIndicator.Margin = compact ? new Thickness(1, 0, 0, 0) : new Thickness(1, 7, 0, 7);
            _selectionIndicator.Opacity = _selected ? 1 : 0;
            UpdateBackground();
        }
    }

    internal sealed record CompactSidebarMenuItem(
        string Label,
        string Glyph,
        string? Path,
        IReadOnlyList<CompactSidebarMenuItem>? Children = null,
        bool IsEnabled = true,
        Func<IReadOnlyList<CompactSidebarMenuItem>>? ChildrenLoader = null)
    {
        public bool HasChildren => (Children?.Count ?? 0) > 0 || ChildrenLoader is not null;
    }

    internal sealed class CompactSidebarMenuController
    {
        private readonly List<MenuFlyout> _flyouts = new();
        private Style? _menuFlyoutPresenterStyle;

        public event EventHandler<string>? NavigateRequested;

        public void Show(FrameworkElement anchor, IReadOnlyList<CompactSidebarMenuItem> items)
        {
            Hide();

            MenuFlyout rootFlyout = CreateFlyout(items, depth: 0);
            _flyouts.Add(rootFlyout);
            rootFlyout.Closed += Flyout_Closed;
            rootFlyout.ShowAt(anchor, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.RightEdgeAlignedTop
            });
        }

        public void Hide()
        {
            MenuFlyout[] snapshot = _flyouts.ToArray();
            _flyouts.Clear();
            foreach (MenuFlyout flyout in snapshot)
            {
                flyout.Closed -= Flyout_Closed;
                flyout.Hide();
            }
        }

        private MenuFlyout CreateFlyout(IReadOnlyList<CompactSidebarMenuItem> items, int depth)
        {
            var flyout = new MenuFlyout();
            if (TryGetMenuFlyoutPresenterStyle(out Style? presenterStyle))
            {
                flyout.MenuFlyoutPresenterStyle = presenterStyle;
            }
            foreach (CompactSidebarMenuItem item in items)
            {
                flyout.Items.Add(CreateMenuItemBase(item, depth));
            }
            return flyout;
        }

        private MenuFlyoutItemBase CreateMenuItemBase(CompactSidebarMenuItem item, int depth, bool materializeChildren = true)
        {
            if (item.HasChildren)
            {
                var subItem = new MenuFlyoutSubItem
                {
                    Text = item.Label,
                    IsEnabled = item.IsEnabled
                };

                subItem.Icon = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = item.Glyph,
                    FontSize = 12
                };

                if (materializeChildren)
                {
                    PopulateSubItem(subItem, item, depth);
                }
                else
                {
                    subItem.Items.Add(new MenuFlyoutItem
                    {
                        Text = "Loading...",
                        IsEnabled = false
                    });

                    bool populated = false;
                    void PopulateOnDemand()
                    {
                        if (populated)
                        {
                            return;
                        }

                        populated = true;
                        PopulateSubItem(subItem, item, depth);
                    }

                    subItem.AddHandler(
                        UIElement.PointerEnteredEvent,
                        new PointerEventHandler((_, _) => PopulateOnDemand()),
                        handledEventsToo: true);
                    subItem.AddHandler(
                        UIElement.PointerPressedEvent,
                        new PointerEventHandler((_, _) => PopulateOnDemand()),
                        handledEventsToo: true);
                }

                subItem.AddHandler(
                    UIElement.TappedEvent,
                    new TappedEventHandler((_, e) =>
                    {
                        if (string.IsNullOrWhiteSpace(item.Path))
                        {
                            return;
                        }

                        e.Handled = true;
                        Hide();
                        NavigateRequested?.Invoke(this, item.Path);
                    }),
                    handledEventsToo: true);

                return subItem;
            }

            return CreateLeafMenuItem(item.Label, item.Glyph, item.Path, item.IsEnabled);
        }

        private void PopulateSubItem(MenuFlyoutSubItem subItem, CompactSidebarMenuItem item, int depth)
        {
            subItem.Items.Clear();

            IReadOnlyList<CompactSidebarMenuItem> children = item.Children
                ?? item.ChildrenLoader?.Invoke()
                ?? Array.Empty<CompactSidebarMenuItem>();

            foreach (CompactSidebarMenuItem child in children)
            {
                subItem.Items.Add(CreateMenuItemBase(child, depth + 1, materializeChildren: false));
            }
        }

        private MenuFlyoutItem CreateLeafMenuItem(string label, string glyph, string? path, bool isEnabled)
        {
            var menuItem = new MenuFlyoutItem
            {
                Text = label,
                IsEnabled = isEnabled
            };

            menuItem.Icon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = glyph,
                FontSize = 12
            };

            menuItem.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                Hide();
                NavigateRequested?.Invoke(this, path);
            };

            return menuItem;
        }

        private bool TryGetMenuFlyoutPresenterStyle(out Style? presenterStyle)
        {
            if (_menuFlyoutPresenterStyle is not null)
            {
                presenterStyle = _menuFlyoutPresenterStyle;
                return true;
            }

            if (Application.Current.Resources.TryGetValue("CompactSidebarMenuFlyoutPresenterStyle", out object? styleObj) &&
                styleObj is Style style)
            {
                _menuFlyoutPresenterStyle = style;
                presenterStyle = style;
                return true;
            }

            presenterStyle = null;
            return false;
        }

        private void Flyout_Closed(object? sender, object e)
        {
            if (sender is not MenuFlyout flyout)
            {
                return;
            }

            flyout.Closed -= Flyout_Closed;
            _flyouts.Remove(flyout);
        }
    }
}
