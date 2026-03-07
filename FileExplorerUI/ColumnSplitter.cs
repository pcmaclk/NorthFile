using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Runtime.InteropServices;

namespace FileExplorerUI;

public sealed class ColumnSplitter : Grid
{
    private static readonly InputCursor ResizeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    private const int IdcSizeWe = 32644;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    private static readonly IntPtr SizeWeCursorHandle = LoadCursor(IntPtr.Zero, IdcSizeWe);

    public ColumnSplitter()
    {
        Width = 6;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Children.Add(new Border
        {
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 6),
            Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE3, 0xE5, 0xE8))
        });

        PointerEntered += (_, _) => ProtectedCursor = ResizeCursor;
        PointerMoved += (_, _) => ProtectedCursor = ResizeCursor;
        PointerExited += (_, _) => ProtectedCursor = null;
        PointerCaptureLost += (_, _) => ProtectedCursor = null;
    }

    public static bool TryApplyResizeCursor()
    {
        if (SizeWeCursorHandle == IntPtr.Zero)
        {
            return false;
        }

        SetCursor(SizeWeCursorHandle);
        return true;
    }
}
