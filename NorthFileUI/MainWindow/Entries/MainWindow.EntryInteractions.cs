using NorthFileUI.Commands;
using NorthFileUI.Controls;
using NorthFileUI.Services;
using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NorthFileUI
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
                    GetPrimaryGroupExpansionStates()[row.GroupKey] = !row.IsGroupExpanded;
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

            string entryPath = GetPaneEntryPath(WorkspacePanelId.Primary, entry);
            return PrimaryPanelState.SelectedEntryPaths.Contains(entryPath) ||
                string.Equals(_selectedEntryPath, entryPath, StringComparison.OrdinalIgnoreCase);
        }

        private void EntryRow_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not FrameworkElement row || row.DataContext is not EntryViewModel entry)
            {
                return;
            }

            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
            {
                return;
            }

            CancelRenameOverlayForPanelSwitch(WorkspacePanelId.Primary);
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            if (IsShiftPressed())
            {
                SelectPanelEntryRange(
                    WorkspacePanelId.Primary,
                    GetSelectableEntriesInPresentationOrder(),
                    entry);
                UpdateEntrySelectionVisuals();
                UpdateFileCommandStates();
            }
            else if (IsControlPressed())
            {
                TogglePanelSelectionPath(WorkspacePanelId.Primary, GetPaneEntryPath(WorkspacePanelId.Primary, entry));
                UpdateEntrySelectionVisuals();
                UpdateFileCommandStates();
            }
            else if (!IsEntryAlreadySelected(entry) || PrimaryPanelState.SelectedEntryPaths.Count > 1)
            {
                SelectEntryInList(entry, ensureVisible: false);
            }

            FocusEntriesList();
            e.Handled = true;
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

            GetPrimaryGroupExpansionStates()[entry.GroupKey] = !entry.IsGroupExpanded;
            ApplyCurrentPresentation();
            e.Handled = true;
        }

        private IEntriesViewHost? GetActiveEntriesViewHost()
        {
            return GetVisibleEntriesViewHost();
        }

        private async Task OpenEntryAsync(EntryViewModel row, bool clearSelectionBeforeDirectoryNavigation)
        {
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            string targetPath = string.IsNullOrWhiteSpace(row.FullPath)
                ? Path.Combine(currentPath, row.Name)
                : row.FullPath;

            if (row.IsDirectory)
            {
                if (clearSelectionBeforeDirectoryNavigation)
                {
                    ClearListSelection();
                }

                await NavigatePanelToPathAsync(WorkspacePanelId.Primary, targetPath, pushHistory: true);
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
