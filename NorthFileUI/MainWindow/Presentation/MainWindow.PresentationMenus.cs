using Microsoft.UI.Xaml;
using NorthFileUI.Workspace;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private async void ViewDetailsMenuItem_Click(object sender, RoutedEventArgs e) => await SetViewModeAsync(EntryViewMode.Details);
        private async void ViewListMenuItem_Click(object sender, RoutedEventArgs e) => await SetViewModeAsync(EntryViewMode.List);
        private async void SortByNameMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.Name);
        private async void SortByTypeMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.Type);
        private async void SortBySizeMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.Size);
        private async void SortByModifiedDateMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortAsync(EntrySortField.ModifiedDate);
        private async void SortAscendingMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortDirectionAsync(SortDirection.Ascending);
        private async void SortDescendingMenuItem_Click(object sender, RoutedEventArgs e) => await SetSortDirectionAsync(SortDirection.Descending);
        private async void GroupByNoneMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.None);
        private async void GroupByNameMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.Name);
        private async void GroupByTypeMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.Type);
        private async void GroupByModifiedDateMenuItem_Click(object sender, RoutedEventArgs e) => await SetGroupAsync(EntryGroupField.ModifiedDate);
    }
}
