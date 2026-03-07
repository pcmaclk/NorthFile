using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI
{
    internal sealed class SidebarGroupControl : StackPanel
    {
        private readonly Brush _normalBackground;
        private readonly Brush _hoverBackground;
        private readonly Border _headerBorder;
        private readonly FontIcon _chevronIcon;
        private bool _expanded;

        public StackPanel Body { get; }

        public SidebarGroupControl(string text, Brush normalBackground, Brush hoverBackground, Brush textBrush, bool expanded = true, double headerHeight = 24)
        {
            _normalBackground = normalBackground;
            _hoverBackground = hoverBackground;
            _expanded = expanded;

            Orientation = Orientation.Vertical;
            Spacing = 0;
            HorizontalAlignment = HorizontalAlignment.Stretch;

            var headerGrid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } }
            };

            headerGrid.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Foreground = textBrush
            });

            _chevronIcon = new FontIcon
            {
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                Glyph = _expanded ? "\uE70D" : "\uE76C",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_chevronIcon, 1);
            headerGrid.Children.Add(_chevronIcon);

            _headerBorder = new Border
            {
                Height = headerHeight,
                Background = _normalBackground,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 0, 8, 0),
                Child = headerGrid
            };
            _headerBorder.PointerEntered += (_, __) => _headerBorder.Background = _hoverBackground;
            _headerBorder.PointerExited += (_, __) => _headerBorder.Background = _normalBackground;
            _headerBorder.PointerPressed += (_, __) => _headerBorder.Background = _hoverBackground;
            _headerBorder.PointerReleased += (_, __) => _headerBorder.Background = _hoverBackground;
            _headerBorder.Tapped += (_, __) => Toggle();

            Body = new StackPanel
            {
                Spacing = 4,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed
            };

            Children.Add(_headerBorder);
            Children.Add(Body);
        }

        public void Toggle()
        {
            _expanded = !_expanded;
            Body.Visibility = _expanded ? Visibility.Visible : Visibility.Collapsed;
            _chevronIcon.Glyph = _expanded ? "\uE70D" : "\uE76C";
        }
    }

    internal sealed class SidebarItemButton : Button
    {
        private readonly Brush _normalBackground;
        private readonly Brush _hoverBackground;
        private readonly Brush _selectedBackground;
        private readonly Border _backgroundLayer;
        private readonly Border _selectionIndicator;
        private bool _selected;
        private bool _pointerOver;

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

            _selectionIndicator = new Border
            {
                Width = 3,
                Background = indicatorBrush,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 7, 0, 7),
                HorizontalAlignment = HorizontalAlignment.Left,
                Opacity = 0,
            };

            var rowPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = iconGlyph,
                        FontFamily = new FontFamily("Segoe Fluent Icons"),
                        FontSize = 14,
                        Width = 16,
                        Height = 16,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = label,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = textBrush
                    }
                }
            };

            _backgroundLayer = new Border
            {
                Background = _normalBackground,
                CornerRadius = new CornerRadius(6)
            };

            var rootGrid = new Grid
            {
                MinHeight = 28,
                VerticalAlignment = VerticalAlignment.Center
            };
            rootGrid.Children.Add(_backgroundLayer);
            rootGrid.Children.Add(_selectionIndicator);
            rootGrid.Children.Add(rowPanel);

            Content = rootGrid;

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
    }
}
