using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI
{
    public sealed partial class SidebarView : UserControl
    {
        public SidebarView()
        {
            InitializeComponent();
            PinnedGroupToggleButton.Checked += PinnedGroupToggleButton_Checked;
            PinnedGroupToggleButton.Unchecked += PinnedGroupToggleButton_Unchecked;
            Loaded += SidebarView_Loaded;
        }

        private void SidebarView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= SidebarView_Loaded;
            UpdatePinnedPanel(true);
        }

        private void PinnedGroupToggleButton_Checked(object sender, RoutedEventArgs e)
        {
            UpdatePinnedPanel(false);
        }

        private void PinnedGroupToggleButton_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdatePinnedPanel(true);
        }

        private void UpdatePinnedPanel(bool expanded)
        {
            if (PinnedGroupToggleButton is null || PinnedItemsListView is null || PinnedGroupChevron is null)
            {
                return;
            }

            PinnedItemsListView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            PinnedGroupChevron.Glyph = expanded ? "\uE70E" : "\uE70D";
            PinnedGroupSelectionIndicator.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            PinnedGroupBackgroundBorder.Background = (Brush)Application.Current.Resources[expanded ? "ListViewItemBackground" : "ListViewItemBackgroundSelected"];

            bool shouldBeChecked = !expanded;
            if (PinnedGroupToggleButton.IsChecked != shouldBeChecked)
            {
                PinnedGroupToggleButton.IsChecked = shouldBeChecked;
            }

            if (PinnedItemsListView.SelectedIndex < 0 && PinnedItemsListView.Items.Count > 0)
            {
                PinnedItemsListView.SelectedIndex = 0;
            }
        }
    }
}
