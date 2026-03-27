using FileExplorerUI.Commands;
using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void GroupedEntriesView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            await HandleEntriesViewDoubleTappedAsync(e);
        }

        private async Task HandleEntriesViewDoubleTappedAsync(DoubleTappedRoutedEventArgs e)
        {
            EntryViewModel? row = GetActiveEntriesViewHost()?.ResolveDoubleTappedEntry(e);
            if (row is not null)
            {
                if (row.IsGroupHeader)
                {
                    _groupExpansionStates[row.GroupKey] = !row.IsGroupExpanded;
                    ApplyCurrentPresentation();
                    e.Handled = true;
                    return;
                }

                SelectEntryInList(row, ensureVisible: false);
            }

            if (row is null || !row.IsLoaded)
            {
                return;
            }

            await OpenEntryAsync(row, clearSelectionBeforeDirectoryNavigation: false);
        }

        private bool IsEntryAlreadySelected(EntryViewModel entry)
        {
            if (entry.IsGroupHeader)
            {
                return false;
            }

            return string.Equals(_selectedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        private void EntryRow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement row || row.DataContext is not EntryViewModel entry)
            {
                return;
            }

            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            var point = e.GetCurrentPoint(row);
            if (!IsEntryAlreadySelected(entry))
            {
                SelectEntryInList(entry, ensureVisible: false);
            }

            FocusEntriesList();

            if (point.Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                return;
            }
        }

        private void GroupHeaderBorder_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement element ||
                element.DataContext is not EntryViewModel entry ||
                !entry.IsGroupHeader ||
                string.IsNullOrWhiteSpace(entry.GroupKey))
            {
                return;
            }

            _groupExpansionStates[entry.GroupKey] = !entry.IsGroupExpanded;
            ApplyCurrentPresentation();
            e.Handled = true;
        }

        private IEntriesViewHost? GetActiveEntriesViewHost()
        {
            return GetVisibleEntriesViewHost();
        }

        private async Task OpenEntryAsync(EntryViewModel row, bool clearSelectionBeforeDirectoryNavigation)
        {
            string targetPath = string.IsNullOrWhiteSpace(row.FullPath)
                ? Path.Combine(_currentPath, row.Name)
                : row.FullPath;

            if (row.IsDirectory)
            {
                if (clearSelectionBeforeDirectoryNavigation)
                {
                    ClearListSelection();
                }

                await NavigateToPathAsync(targetPath, pushHistory: true);
                return;
            }

            FileCommandTarget target = FileCommandTargetResolver.ResolveEntry(targetPath, isDirectory: false);
            if ((target.Traits & FileEntryTraits.Shortcut) != 0)
            {
                await ExecuteOpenTargetCommandAsync(target);
                return;
            }

            try
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = targetPath,
                    UseShellExecute = true
                });
                UpdateStatusKey("StatusOpened", row.Name);
            }
            catch (Exception ex)
            {
                UpdateStatusKey("StatusOpenFailed", FileOperationErrors.ToUserMessage(ex));
            }
        }
    }
}
