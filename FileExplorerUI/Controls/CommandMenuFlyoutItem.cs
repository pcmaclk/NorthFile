using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;

namespace FileExplorerUI.Controls
{
    public sealed class CommandMenuFlyoutItem : DependencyObject
    {
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(
                nameof(Icon),
                typeof(IconElement),
                typeof(CommandMenuFlyoutItem),
                new PropertyMetadata(null));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(
                nameof(Label),
                typeof(string),
                typeof(CommandMenuFlyoutItem),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CommandIdProperty =
            DependencyProperty.Register(
                nameof(CommandId),
                typeof(string),
                typeof(CommandMenuFlyoutItem),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(
                nameof(Command),
                typeof(ICommand),
                typeof(CommandMenuFlyoutItem),
                new PropertyMetadata(null));

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.Register(
                nameof(CommandParameter),
                typeof(object),
                typeof(CommandMenuFlyoutItem),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.Register(
                nameof(IsEnabled),
                typeof(bool),
                typeof(CommandMenuFlyoutItem),
                new PropertyMetadata(true));

        public static readonly DependencyProperty SeparatorVisibilityProperty =
            DependencyProperty.Register(
                nameof(SeparatorVisibility),
                typeof(Visibility),
                typeof(CommandMenuFlyoutItem),
                new PropertyMetadata(Visibility.Visible));

        public IconElement? Icon
        {
            get => (IconElement?)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }

        public string CommandId
        {
            get => (string)GetValue(CommandIdProperty);
            set => SetValue(CommandIdProperty, value);
        }

        public Visibility SeparatorVisibility
        {
            get => (Visibility)GetValue(SeparatorVisibilityProperty);
            set => SetValue(SeparatorVisibilityProperty, value);
        }

        public ICommand? Command
        {
            get => (ICommand?)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public object? CommandParameter
        {
            get => GetValue(CommandParameterProperty);
            set => SetValue(CommandParameterProperty, value);
        }

        public bool IsEnabled
        {
            get => (bool)GetValue(IsEnabledProperty);
            set => SetValue(IsEnabledProperty, value);
        }

        internal IconElement? CreateIconElement()
        {
            return Icon switch
            {
                FontIcon fontIcon => new FontIcon
                {
                    Glyph = fontIcon.Glyph,
                    FontSize = fontIcon.FontSize,
                    FontFamily = fontIcon.FontFamily,
                    FontWeight = fontIcon.FontWeight,
                    IsTextScaleFactorEnabled = fontIcon.IsTextScaleFactorEnabled,
                    MirroredWhenRightToLeft = fontIcon.MirroredWhenRightToLeft,
                    Foreground = CloneBrush(fontIcon.Foreground)
                },
                SymbolIcon symbolIcon => new SymbolIcon
                {
                    Symbol = symbolIcon.Symbol,
                    Foreground = CloneBrush(symbolIcon.Foreground)
                },
                BitmapIcon bitmapIcon => new BitmapIcon
                {
                    UriSource = bitmapIcon.UriSource,
                    ShowAsMonochrome = bitmapIcon.ShowAsMonochrome
                },
                PathIcon pathIcon => new PathIcon
                {
                    Data = pathIcon.Data,
                    Foreground = CloneBrush(pathIcon.Foreground)
                },
                _ => null
            };
        }

        public string? IconGlyph => Icon switch
        {
            FontIcon fontIcon => fontIcon.Glyph,
            SymbolIcon symbolIcon => char.ConvertFromUtf32((int)symbolIcon.Symbol),
            _ => null
        };

        public string IconFontFamily => Icon is FontIcon fontIcon && fontIcon.FontFamily is not null
            ? fontIcon.FontFamily.Source
            : "Segoe Fluent Icons";

        public string? IconSourceMarkup
        {
            get
            {
                IconElement? icon = CreateIconElement();
                return icon is null ? null : XamlBindingHelper.ConvertValue(typeof(string), icon)?.ToString();
            }
        }

        private static Brush? CloneBrush(Brush? brush)
        {
            return brush switch
            {
                SolidColorBrush solid => new SolidColorBrush(solid.Color),
                _ => brush
            };
        }
    }
}
