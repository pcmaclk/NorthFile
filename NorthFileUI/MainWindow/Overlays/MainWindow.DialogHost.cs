using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private XamlRoot? GetWindowDialogXamlRoot()
        {
            if (WindowRootGrid?.XamlRoot is not null)
            {
                return WindowRootGrid.XamlRoot;
            }

            return (Content as FrameworkElement)?.XamlRoot;
        }

        private void PrepareWindowDialog(ContentDialog dialog)
        {
            if (GetWindowDialogXamlRoot() is XamlRoot xamlRoot)
            {
                dialog.XamlRoot = xamlRoot;
            }
        }

        private Grid? GetWindowDialogHostGrid()
        {
            if (WindowRootGrid is not null)
            {
                return WindowRootGrid;
            }

            return Content as Grid;
        }

        private void AttachWindowOverlay(FrameworkElement overlay, int zIndex = 200)
        {
            Grid? hostGrid = GetWindowDialogHostGrid();
            if (hostGrid is null)
            {
                return;
            }

            if (!hostGrid.Children.Contains(overlay))
            {
                hostGrid.Children.Add(overlay);
            }

            Grid.SetRow(overlay, 0);
            Grid.SetColumn(overlay, 0);
            Grid.SetRowSpan(overlay, hostGrid.RowDefinitions.Count > 0 ? hostGrid.RowDefinitions.Count : 1);
            Grid.SetColumnSpan(overlay, hostGrid.ColumnDefinitions.Count > 0 ? hostGrid.ColumnDefinitions.Count : 1);
            Canvas.SetZIndex(overlay, zIndex);
        }
    }
}
