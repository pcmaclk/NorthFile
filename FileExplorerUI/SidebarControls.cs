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
        bool IsEnabled = true);

    internal sealed class CompactSidebarMenuController
    {
        private readonly List<Popup> _popups = new();
        private readonly Brush _backgroundBrush;
        private readonly Brush _borderBrush;
        private readonly Brush _textBrush;
        private readonly Brush _hoverBrush;
        private readonly Brush _submenuHoverBrush;
        private readonly Brush _normalItemBrush;
        private readonly CornerRadius _cornerRadius;

        public event EventHandler<string>? NavigateRequested;

        public CompactSidebarMenuController()
        {
            _backgroundBrush = EnsureOpaqueBrush(ResolveBrush("LayerFillColorDefaultBrush", "CardBackgroundFillColorDefaultBrush"));
            _borderBrush = ResolveBrush("MenuFlyoutPresenterBorderBrush", "CardStrokeColorDefaultBrush");
            _textBrush = ResolveBrush("MenuFlyoutItemForeground", "TextFillColorPrimaryBrush");
            _hoverBrush = ResolveBrush("MenuFlyoutItemBackgroundPointerOver", "ListViewItemBackgroundPointerOver");
            _submenuHoverBrush = ResolveBrush("MenuFlyoutSubItemBackgroundSubMenuOpened", "MenuFlyoutItemBackgroundPointerOver");
            _normalItemBrush = EnsureOpaqueBrush(ResolveBrush("MenuFlyoutItemBackground", "SystemControlTransparentBrush"), fallbackAlpha: 0x01);
            _cornerRadius = ResolveCornerRadius("OverlayCornerRadius", 8);
        }

        public void Show(FrameworkElement anchor, IReadOnlyList<CompactSidebarMenuItem> items)
        {
            Hide();

            Popup popup = CreatePopup(anchor, items, depth: 0);
            _popups.Add(popup);
            popup.IsOpen = true;
        }

        public void Hide()
        {
            Popup[] snapshot = _popups.ToArray();
            _popups.Clear();
            foreach (Popup popup in snapshot)
            {
                popup.Closed -= Popup_Closed;
                popup.IsOpen = false;
            }
        }

        private Popup CreatePopup(FrameworkElement anchor, IReadOnlyList<CompactSidebarMenuItem> items, int depth)
        {
            var panel = new StackPanel
            {
                Spacing = 2
            };

            foreach (CompactSidebarMenuItem item in items)
            {
                panel.Children.Add(CreateRow(item, depth));
            }

            var viewer = new ScrollViewer
            {
                MaxHeight = 420,
                MinWidth = 240,
                Background = _backgroundBrush,
                VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
                VerticalScrollMode = ScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Disabled,
                Content = panel
            };

            var border = new Border
            {
                Background = _backgroundBrush,
                BorderBrush = _borderBrush,
                BorderThickness = new Thickness(1),
                BackgroundSizing = BackgroundSizing.InnerBorderEdge,
                CornerRadius = _cornerRadius,
                Padding = new Thickness(4, 4, 0, 4),
                Shadow = new ThemeShadow(),
                Translation = new Vector3(0, 0, 16),
                Child = viewer
            };

            void UpdateScrollSpacing()
            {
                bool hasVerticalOverflow = viewer.ScrollableHeight > 0
                    || viewer.ExtentHeight > viewer.ViewportHeight + 0.5;
                viewer.Padding = hasVerticalOverflow
                    ? new Thickness(0, 0, 10, 0)
                    : new Thickness(0);
                border.Padding = hasVerticalOverflow
                    ? new Thickness(4, 4, 0, 4)
                    : new Thickness(4);
            }

            viewer.Loaded += (_, _) =>
            {
                viewer.UpdateLayout();
                UpdateScrollSpacing();
            };
            viewer.SizeChanged += (_, _) => UpdateScrollSpacing();
            panel.SizeChanged += (_, _) => UpdateScrollSpacing();

            Point point = GetPopupAnchorPoint(anchor, depth);
            double width = GetPopupAnchorWidth(anchor, depth);
            double popupHeight = EstimatePopupHeight(items);
            double viewportHeight = anchor.XamlRoot?.Size.Height ?? 0;
            double verticalOffset = point.Y;
            if (viewportHeight > 0)
            {
                const double viewportMargin = 20;
                double maxTop = Math.Max(viewportMargin, viewportHeight - popupHeight - viewportMargin);
                verticalOffset = Math.Clamp(verticalOffset, viewportMargin, maxTop);
            }

            var popup = new Popup
            {
                XamlRoot = anchor.XamlRoot,
                Child = border,
                HorizontalOffset = point.X + width + (depth == 0 ? 4 : -3),
                VerticalOffset = verticalOffset,
                IsLightDismissEnabled = depth == 0
            };
            popup.Closed += Popup_Closed;
            return popup;
        }

        private FrameworkElement CreateRow(CompactSidebarMenuItem item, int depth)
        {
            var icon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = item.Glyph,
                FontSize = 12,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = item.Label,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = _textBrush
            };

            var grid = new Grid
            {
                ColumnSpacing = 8,
                MinWidth = 220
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(icon, 0);
            Grid.SetColumn(text, 1);
            grid.Children.Add(icon);
            grid.Children.Add(text);

            if (item.Children is { Count: > 0 })
            {
                var chevron = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = "\uE76C",
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(chevron, 2);
                grid.Children.Add(chevron);
            }

            var background = new Border
            {
                Background = _normalItemBrush,
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 6, 8, 6),
                Child = grid,
                Opacity = item.IsEnabled ? 1 : 0.55
            };

            background.PointerEntered += (_, _) =>
            {
                background.Background = item.IsEnabled
                    ? (item.Children is { Count: > 0 } ? _submenuHoverBrush : _hoverBrush)
                    : _normalItemBrush;
                OpenChildrenForRow(background, item, depth + 1);
            };
            background.PointerExited += (_, _) =>
            {
                background.Background = _normalItemBrush;
            };
            background.Tapped += (_, _) =>
            {
                if (!item.IsEnabled || string.IsNullOrWhiteSpace(item.Path))
                {
                    return;
                }

                Hide();
                NavigateRequested?.Invoke(this, item.Path);
            };

            return background;
        }

        private void OpenChildrenForRow(FrameworkElement row, CompactSidebarMenuItem item, int depth)
        {
            ClosePopupsFromDepth(depth);

            if (item.Children is not { Count: > 0 })
            {
                return;
            }

            Popup popup = CreatePopup(row, item.Children, depth);
            _popups.Add(popup);
            popup.IsOpen = true;
        }

        private void ClosePopupsFromDepth(int depth)
        {
            while (_popups.Count > depth)
            {
                int index = _popups.Count - 1;
                Popup popup = _popups[index];
                _popups.RemoveAt(index);
                popup.Closed -= Popup_Closed;
                popup.IsOpen = false;
            }
        }

        private void Popup_Closed(object? sender, object e)
        {
            if (sender is not Popup popup)
            {
                return;
            }

            popup.Closed -= Popup_Closed;
            if (_popups.Contains(popup))
            {
                Hide();
            }
        }

        private static Point GetAnchorPoint(FrameworkElement element)
        {
            GeneralTransform transform = element.TransformToVisual(null);
            return transform.TransformPoint(new Point(0, 0));
        }

        private static Point GetPopupAnchorPoint(FrameworkElement anchor, int depth)
        {
            if (depth > 0)
            {
                Point rowPoint = GetAnchorPoint(anchor);
                FrameworkElement? current = anchor;
                while (current is not null)
                {
                    if (current.Parent is Popup popup && popup.Child is FrameworkElement popupChild)
                    {
                        Point popupPoint = GetAnchorPoint(popupChild);
                        return new Point(popupPoint.X, rowPoint.Y);
                    }

                    current = current.Parent as FrameworkElement;
                }
            }

            return GetAnchorPoint(anchor);
        }

        private static double GetPopupAnchorWidth(FrameworkElement anchor, int depth)
        {
            if (depth > 0)
            {
                FrameworkElement? current = anchor;
                while (current is not null)
                {
                    if (current.Parent is Popup popup && popup.Child is FrameworkElement popupChild && popupChild.ActualWidth > 0)
                    {
                        return popupChild.ActualWidth;
                    }

                    current = current.Parent as FrameworkElement;
                }
            }

            return anchor.ActualWidth > 0 ? anchor.ActualWidth : 32;
        }

        private static double EstimatePopupHeight(IReadOnlyList<CompactSidebarMenuItem> items)
        {
            const double rowHeight = 40;
            const double spacing = 2;
            const double chrome = 20;
            if (items.Count == 0)
            {
                return chrome;
            }

            return Math.Min(420, chrome + (items.Count * rowHeight) + ((items.Count - 1) * spacing));
        }

        private static Brush ResolveBrush(string primaryKey, string fallbackKey)
        {
            if (Application.Current.Resources.TryGetValue(primaryKey, out object? primary) && primary is Brush primaryBrush)
            {
                return primaryBrush;
            }

            if (Application.Current.Resources.TryGetValue(fallbackKey, out object? fallback) && fallback is Brush fallbackBrush)
            {
                return fallbackBrush;
            }

            if (primaryKey.Contains("Background", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xF7, 0xF7, 0xF7));
            }

            if (primaryKey.Contains("Border", StringComparison.OrdinalIgnoreCase))
            {
                return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xD8, 0xD8, 0xD8));
            }

            return new SolidColorBrush(Microsoft.UI.Colors.Black);
        }

        private static CornerRadius ResolveCornerRadius(string key, double fallback)
        {
            if (Application.Current.Resources.TryGetValue(key, out object? value) && value is CornerRadius resolved)
            {
                return resolved;
            }

            return new CornerRadius(fallback);
        }

        private static Brush EnsureOpaqueBrush(Brush brush, byte fallbackAlpha = 0xFF)
        {
            if (brush is SolidColorBrush solid)
            {
                Windows.UI.Color color = solid.Color;
                if (color.A == 0)
                {
                    color = Microsoft.UI.ColorHelper.FromArgb(fallbackAlpha, 0xF7, 0xF7, 0xF7);
                    return new SolidColorBrush(color);
                }

                if (color.A < 0xE8)
                {
                    color = Microsoft.UI.ColorHelper.FromArgb(0xF8, color.R, color.G, color.B);
                    return new SolidColorBrush(color);
                }
            }

            return brush;
        }
    }
}
