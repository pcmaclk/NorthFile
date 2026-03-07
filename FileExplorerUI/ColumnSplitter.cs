using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI;

public sealed class ColumnSplitter : Button
{
    private static readonly InputCursor ResizeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);

    public ColumnSplitter()
    {
        Width = 6;
        Padding = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = new SolidColorBrush(Colors.Transparent);
        BorderThickness = new Thickness(0);
        Content = new Border
        {
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE5, 0xE7, 0xEA))
        };

        PointerEntered += (_, _) => ProtectedCursor = ResizeCursor;
        PointerMoved += (_, _) => ProtectedCursor = ResizeCursor;
        PointerExited += (_, _) => ProtectedCursor = null;
        PointerCaptureLost += (_, _) => ProtectedCursor = null;
    }

    public static bool TryApplyResizeCursor()
    {
        // Keep API compatibility with existing callers; cursor is now handled by control itself.
        return true;
    }
}
