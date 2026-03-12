using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI
{
    public sealed partial class SidebarView : UserControl
    {
        private bool _isPinnedExpanded = true;
        private string? _selectedItem;

        // 选中状态指示器
        private Border _pinnedGroupSelectionIndicator;
        private Border _desktopSelectionIndicator;
        private Border _documentsSelectionIndicator;
        private Border _downloadsSelectionIndicator;
        private Border _picturesSelectionIndicator;

        // 背景
        private Border _pinnedGroupBorder;
        private Border _desktopItemBorder;
        private Border _documentsItemBorder;
        private Border _downloadsItemBorder;
        private Border _picturesItemBorder;

        public SidebarView()
        {
            InitializeComponent();

            // 获取元素引用
            _pinnedGroupSelectionIndicator = PinnedGroupSelectionIndicator;
            _desktopSelectionIndicator = DesktopSelectionIndicator;
            _documentsSelectionIndicator = DocumentsSelectionIndicator;
            _downloadsSelectionIndicator = DownloadsSelectionIndicator;
            _picturesSelectionIndicator = PicturesSelectionIndicator;

            _pinnedGroupBorder = PinnedGroupBorder;
            _desktopItemBorder = DesktopItemBorder;
            _documentsItemBorder = DocumentsItemBorder;
            _downloadsItemBorder = DownloadsItemBorder;
            _picturesItemBorder = PicturesItemBorder;

            // 默认选中桌面
            SelectItem("桌面");
        }

        private void PinnedGroup_Click(object sender, PointerRoutedEventArgs e)
        {
            _isPinnedExpanded = !_isPinnedExpanded;
            PinnedItemsPanel.Visibility = _isPinnedExpanded ? Visibility.Visible : Visibility.Collapsed;
            PinnedChevron.Glyph = _isPinnedExpanded ? "\uE70E" : "\uE70D";

            // 更新固定组标题的选中样式
            if (!_isPinnedExpanded)
            {
                _pinnedGroupSelectionIndicator.Visibility = Visibility.Visible;
                _pinnedGroupBorder.Background = (Brush)Application.Current.Resources["ListViewItemBackgroundSelected"];
            }
            else
            {
                _pinnedGroupSelectionIndicator.Visibility = Visibility.Collapsed;
                _pinnedGroupBorder.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void PinnedItem_Click(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                string? itemName = null;

                // 根据 border 获取项名称
                if (border == _desktopItemBorder) itemName = "桌面";
                else if (border == _documentsItemBorder) itemName = "文档";
                else if (border == _downloadsItemBorder) itemName = "下载";
                else if (border == _picturesItemBorder) itemName = "图片";

                if (itemName != null)
                {
                    SelectItem(itemName);
                }
            }
        }

        private void SelectItem(string itemName)
        {
            // 清除之前的选中状态
            ClearSelection();

            _selectedItem = itemName;

            // 设置新的选中状态
            switch (itemName)
            {
                case "桌面":
                    _desktopSelectionIndicator.Visibility = Visibility.Visible;
                    _desktopItemBorder.Background = (Brush)Application.Current.Resources["ListViewItemBackgroundSelected"];
                    break;
                case "文档":
                    _documentsSelectionIndicator.Visibility = Visibility.Visible;
                    _documentsItemBorder.Background = (Brush)Application.Current.Resources["ListViewItemBackgroundSelected"];
                    break;
                case "下载":
                    _downloadsSelectionIndicator.Visibility = Visibility.Visible;
                    _downloadsItemBorder.Background = (Brush)Application.Current.Resources["ListViewItemBackgroundSelected"];
                    break;
                case "图片":
                    _picturesSelectionIndicator.Visibility = Visibility.Visible;
                    _picturesItemBorder.Background = (Brush)Application.Current.Resources["ListViewItemBackgroundSelected"];
                    break;
            }
        }

        private void ClearSelection()
        {
            // 清除所有选中状态
            _pinnedGroupSelectionIndicator.Visibility = Visibility.Collapsed;
            _desktopSelectionIndicator.Visibility = Visibility.Collapsed;
            _documentsSelectionIndicator.Visibility = Visibility.Collapsed;
            _downloadsSelectionIndicator.Visibility = Visibility.Collapsed;
            _picturesSelectionIndicator.Visibility = Visibility.Collapsed;

            // 清除背景
            _pinnedGroupBorder.Background = new SolidColorBrush(Colors.Transparent);
            _desktopItemBorder.Background = new SolidColorBrush(Colors.Transparent);
            _documentsItemBorder.Background = new SolidColorBrush(Colors.Transparent);
            _downloadsItemBorder.Background = new SolidColorBrush(Colors.Transparent);
            _picturesItemBorder.Background = new SolidColorBrush(Colors.Transparent);
        }
    }
}