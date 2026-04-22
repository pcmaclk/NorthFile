using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI;

public sealed class ColumnSplitter : Grid
{
    private static readonly InputCursor ResizeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    public static readonly DependencyProperty GuideBrushProperty =
        DependencyProperty.Register(
            nameof(GuideBrush),
            typeof(Brush),
            typeof(ColumnSplitter),
            new PropertyMetadata(null, OnGuideBrushChanged));

    private readonly Border _guide;

    public ColumnSplitter()
    {
        Width = 6;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _guide = new Border
        {
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE3, 0xE5, 0xE8))
        };
        Children.Add(_guide);

        // Keep cursor behavior local to splitter surface; do not force global SetCursor.
        PointerEntered += (_, _) => ProtectedCursor = ResizeCursor;
        PointerMoved += (_, _) => ProtectedCursor = ResizeCursor;
        PointerExited += (_, _) => ProtectedCursor = null;
        PointerCaptureLost += (_, _) => ProtectedCursor = null;
    }

    public Brush? GuideBrush
    {
        get => (Brush?)GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    public bool ShowGuide
    {
        get => _guide.Visibility == Visibility.Visible;
        set => _guide.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnGuideBrushChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColumnSplitter splitter)
        {
            splitter._guide.Background = e.NewValue as Brush ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE3, 0xE5, 0xE8));
        }
    }
}
