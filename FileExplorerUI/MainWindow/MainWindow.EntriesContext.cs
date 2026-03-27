using FileExplorerUI.Commands;
using FileExplorerUI.Controls;
using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private void GroupedEntriesView_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            HandleEntriesViewRightTapped(e);
        }

        private void GroupedListHeader_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void GroupedListHeader_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void GroupedListHeader_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void HandleEntriesViewRightTapped(RightTappedRoutedEventArgs e)
        {
            EntriesViewHitResult? hit = GetActiveEntriesViewHost()?.ResolveRightTappedHit(e);
            if (hit is null)
            {
                if (IsEntriesGroupHeaderSource(e.OriginalSource as DependencyObject))
                {
                    e.Handled = true;
                }
                return;
            }

            if (hit.Entry?.IsGroupHeader == true)
            {
                FrameworkElement root = GetVisibleEntriesRoot();
                hit = new EntriesViewHitResult(root, e.GetPosition(root), null, false);
            }

            _lastEntriesContextItem = hit.Entry;
            if (hit.Entry is not null && !IsEntryAlreadySelected(hit.Entry))
            {
                SelectEntryInList(hit.Entry, ensureVisible: false);
            }

            ShowEntriesContextFlyout(new EntriesContextRequest(
                hit.Anchor,
                hit.Position,
                hit.Entry,
                hit.IsItemTarget));
            e.Handled = true;
        }

        private static bool IsEntriesGroupHeaderSource(DependencyObject? source)
        {
            DependencyObject? current = source;
            while (current is not null)
            {
                if (current is EntryGroupHeader)
                {
                    return true;
                }

                if (current is FrameworkElement element &&
                    element.DataContext is EntryViewModel entry &&
                    entry.IsGroupHeader)
                {
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }

        private void ShowEntriesContextFlyout(EntriesContextRequest request)
        {
            CommandMenuFlyout flyout = SelectEntriesContextFlyout(request);
            bool flyoutActive = _entriesFlyoutOpen || (_activeEntriesContextFlyout?.IsOpen ?? false);
            if (flyoutActive)
            {
                if (_pendingEntriesContextRequest is { IsItemTarget: true } &&
                    !request.IsItemTarget)
                {
                    return;
                }

                _pendingEntriesContextRequest = request;
                if (_activeEntriesContextFlyout?.IsOpen == true)
                {
                    _activeEntriesContextFlyout.Hide();
                }
                return;
            }

            _entriesContextRequest = request;
            _lastEntriesContextItem = request.Entry ?? _lastEntriesContextItem;
            _activeEntriesContextFlyout = flyout;
            flyout.SetInvocationContext(request.Anchor, request.Position);
            flyout.ShowAt(request.Anchor, new FlyoutShowOptions
            {
                Position = request.Position
            });
        }

        private CommandMenuFlyout SelectEntriesContextFlyout(EntriesContextRequest request)
        {
            if (!request.IsItemTarget || request.Entry is null)
            {
                return BackgroundEntriesContextFlyout;
            }

            return request.Entry.IsDirectory
                ? FolderEntriesContextFlyout
                : FileEntriesContextFlyout;
        }

        private void HideActiveEntriesContextFlyout()
        {
            if (_entriesFlyoutOpen && _activeEntriesContextFlyout?.IsOpen == true)
            {
                _activeEntriesContextFlyout.Hide();
            }
        }

        private void UpdateConditionalCommandVisibility(CommandMenuFlyout flyout, string commandId, string label, int insertIndex, bool shouldShow)
        {
            int existingIndex = -1;
            for (int i = 0; i < flyout.Commands.Count; i++)
            {
                if (string.Equals(flyout.Commands[i].CommandId, commandId, StringComparison.Ordinal))
                {
                    existingIndex = i;
                    break;
                }
            }

            if (shouldShow)
            {
                if (existingIndex >= 0)
                {
                    return;
                }

                flyout.Commands.Insert(insertIndex, CreateCommandBarItem(commandId, label));
                return;
            }

            if (existingIndex >= 0)
            {
                flyout.Commands.RemoveAt(existingIndex);
            }
        }

        private void UpdateEntriesContextFlyoutState(CommandMenuFlyout flyout)
        {
            if (!TryBuildActiveEntriesContextTarget(out FileCommandTarget target))
            {
                return;
            }

            var availableCommandIds = _fileCommandCatalog.BuildCommands(target)
                .Select(descriptor => descriptor.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (target.Kind is FileCommandTargetKind.FileEntry or FileCommandTargetKind.DirectoryEntry)
            {
                availableCommandIds.Add(FileCommandIds.CompressZip);
                availableCommandIds.Add(FileCommandIds.Share);
            }

            foreach (MenuFlyoutItemBase item in flyout.Items)
            {
                UpdateEntriesContextMenuItemState(item, target, availableCommandIds);
            }

            if (ReferenceEquals(flyout, FileEntriesContextFlyout))
            {
                UpdateFileArchiveActionsSubMenuState(target, availableCommandIds);
            }

            RefreshEntriesContextDynamicLabels(flyout, target);

            foreach (CommandMenuFlyoutItem item in flyout.Commands)
            {
                string commandId = item.CommandId;
                bool enabled = !string.IsNullOrWhiteSpace(commandId) &&
                    availableCommandIds.Contains(commandId) &&
                    CanExecuteEntriesContextCommand(commandId, target);
                item.Command = enabled ? _entriesContextCommand : null;
                item.CommandParameter = enabled ? commandId : null;
                item.IsEnabled = enabled;
            }

            _entriesContextCommand.RaiseCanExecuteChanged();
        }

        private void UpdateFileArchiveActionsSubMenuState(FileCommandTarget target, HashSet<string> availableCommandIds)
        {
            bool showExtractSmart = availableCommandIds.Contains(FileCommandIds.ExtractSmart);
            bool showExtractHere = availableCommandIds.Contains(FileCommandIds.ExtractHere);
            bool showExtractToFolder = availableCommandIds.Contains(FileCommandIds.ExtractToFolder);
            bool showAnyExtract = showExtractSmart || showExtractHere || showExtractToFolder;
            bool showCompress = availableCommandIds.Contains(FileCommandIds.CompressZip);

            FileExtractSmartMenuItem.Visibility = showExtractSmart ? Visibility.Visible : Visibility.Collapsed;
            FileExtractHereMenuItem.Visibility = showExtractHere ? Visibility.Visible : Visibility.Collapsed;
            FileExtractToFolderMenuItem.Visibility = showExtractToFolder ? Visibility.Visible : Visibility.Collapsed;
            FileArchiveExtractSeparator.Visibility = showAnyExtract && showCompress ? Visibility.Visible : Visibility.Collapsed;
            FileCompressZipMenuItem.Visibility = showCompress ? Visibility.Visible : Visibility.Collapsed;
            FileArchiveActionsSubMenu.Visibility = showAnyExtract || showCompress ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshEntriesContextFlyoutText()
        {
            RefreshEntriesContextStaticText(FileEntriesContextFlyout);
            RefreshEntriesContextStaticText(FolderEntriesContextFlyout);
            RefreshEntriesContextStaticText(BackgroundEntriesContextFlyout);
            RefreshEntriesContextCommandLabels(FileEntriesContextFlyout);
            RefreshEntriesContextCommandLabels(FolderEntriesContextFlyout);
            RefreshEntriesContextCommandLabels(BackgroundEntriesContextFlyout);
        }

        private void RefreshEntriesContextStaticText(CommandMenuFlyout flyout)
        {
            FileCommandTarget target = TryBuildActiveEntriesContextTarget(out FileCommandTarget activeTarget)
                ? activeTarget
                : ResolveEntriesContextTarget(null);

            foreach (MenuFlyoutItemBase item in EnumerateMenuItemsRecursive(flyout.Items))
            {
                switch (item)
                {
                    case MenuFlyoutItem menuItem when menuItem.Tag is string commandId:
                        menuItem.Text = GetEntriesContextMenuItemLabel(commandId, target);
                        break;
                    case MenuFlyoutSubItem subItem when ReferenceEquals(subItem, FileArchiveActionsSubMenu):
                        subItem.Text = S("CommonArchiveActions");
                        break;
                }
            }
        }

        private void RefreshEntriesContextCommandLabels(CommandMenuFlyout flyout)
        {
            foreach (CommandMenuFlyoutItem item in flyout.Commands)
            {
                item.Label = GetEntriesContextCommandBarLabel(item.CommandId);
            }
        }

        private void RefreshEntriesContextDynamicLabels(CommandMenuFlyout flyout, FileCommandTarget target)
        {
            foreach (MenuFlyoutItemBase item in EnumerateMenuItemsRecursive(flyout.Items))
            {
                if (item is MenuFlyoutItem menuItem && menuItem.Tag is string commandId)
                {
                    menuItem.Text = GetEntriesContextMenuItemLabel(commandId, target);
                }
            }
        }

        private static IEnumerable<MenuFlyoutItemBase> EnumerateMenuItemsRecursive(IList<MenuFlyoutItemBase> items)
        {
            foreach (MenuFlyoutItemBase item in items)
            {
                yield return item;

                if (item is MenuFlyoutSubItem subItem)
                {
                    foreach (MenuFlyoutItemBase child in EnumerateMenuItemsRecursive(subItem.Items))
                    {
                        yield return child;
                    }
                }
            }
        }

        private void UpdateEntriesContextMenuItemState(
            MenuFlyoutItemBase item,
            FileCommandTarget target,
            HashSet<string> availableCommandIds)
        {
            switch (item)
            {
                case MenuFlyoutItem menuItem when menuItem.Tag is string commandId:
                    menuItem.Text = GetEntriesContextMenuItemLabel(commandId, target);
                    bool visible = availableCommandIds.Contains(commandId);
                    menuItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    menuItem.IsEnabled = visible && CanExecuteEntriesContextCommand(commandId, target);
                    break;

                case MenuFlyoutSubItem subItem:
                    if (ReferenceEquals(subItem, FileArchiveActionsSubMenu))
                    {
                        subItem.Text = S("CommonArchiveActions");
                    }

                    bool hasTaggedChildren = false;
                    bool anyVisibleChild = false;
                    foreach (MenuFlyoutItemBase child in subItem.Items)
                    {
                        if (child is MenuFlyoutItem childMenuItem && childMenuItem.Tag is string)
                        {
                            hasTaggedChildren = true;
                        }

                        UpdateEntriesContextMenuItemState(child, target, availableCommandIds);
                        anyVisibleChild |= child.Visibility == Visibility.Visible;
                    }

                    if (hasTaggedChildren)
                    {
                        subItem.Visibility = anyVisibleChild ? Visibility.Visible : Visibility.Collapsed;
                    }
                    break;
            }
        }

        private string GetEntriesContextMenuItemLabel(string commandId, FileCommandTarget target)
        {
            if (string.Equals(commandId, FileCommandIds.ExtractToFolder, StringComparison.Ordinal))
            {
                return SF("CommonExtractToNamedFolder", GetDefaultExtractedFolderDisplayName(target));
            }

            return commandId switch
            {
                FileCommandIds.Open => S("CommonOpen"),
                FileCommandIds.OpenWith => S("CommonOpenWith"),
                FileCommandIds.OpenTarget => S("CommonOpenTarget"),
                FileCommandIds.RunAsAdministrator => S("CommonRunAsAdministrator"),
                FileCommandIds.Share => S("CommonShare"),
                FileCommandIds.CreateShortcut => S("CommonCreateShortcut"),
                FileCommandIds.CopyPath => target.IsDirectory ? S("CommonCopyFolderPath") : S("CommonCopyFilePath"),
                FileCommandIds.Properties => S("CommonProperties"),
                FileCommandIds.OpenInNewWindow => S("CommonOpenInNewWindow"),
                FileCommandIds.PinToSidebar or FileCommandIds.UnpinFromSidebar => IsFavoritePath(target.Path)
                    ? S("CommonUnpinFromSidebar")
                    : S("CommonPinToSidebar"),
                FileCommandIds.OpenInTerminal => S("CommonOpenInTerminal"),
                FileCommandIds.CompressZip => target.IsDirectory ? S("CommonCompressZip") : S("CommonCompressZipAction"),
                FileCommandIds.ExtractSmart => S("CommonExtractSmart"),
                FileCommandIds.ExtractHere => S("CommonExtractHere"),
                FileCommandIds.NewFile => S("CommonNewFile"),
                FileCommandIds.NewFolder => S("CommonNewFolder"),
                _ => string.Empty
            };
        }

        private string GetEntriesContextCommandBarLabel(string commandId)
        {
            return commandId switch
            {
                FileCommandIds.Cut => S("CommonCut"),
                FileCommandIds.Copy => S("CommonCopy"),
                FileCommandIds.Paste => S("CommonPaste"),
                FileCommandIds.Rename => S("CommonRename"),
                FileCommandIds.Delete => S("CommonDelete"),
                FileCommandIds.PinToSidebar or FileCommandIds.UnpinFromSidebar => S("CommonPinToggle"),
                FileCommandIds.Refresh => S("CommonRefresh"),
                _ => commandId
            };
        }

        private string GetDefaultExtractedFolderDisplayName(FileCommandTarget target)
        {
            string? sourcePath = target.Path;
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                string fileName = Path.GetFileName(sourcePath.TrimEnd('\\'));
                string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
                if (!string.IsNullOrWhiteSpace(withoutExtension))
                {
                    return withoutExtension;
                }
            }

            string displayName = target.DisplayName ?? string.Empty;
            string fallback = Path.GetFileNameWithoutExtension(displayName);
            return string.IsNullOrWhiteSpace(fallback) ? displayName : fallback;
        }

        private CommandMenuFlyoutItem CreateCommandBarItem(string commandId, string label)
        {
            string glyph = label switch
            {
                var value when string.Equals(value, S("CommonPaste"), StringComparison.Ordinal) => "\uE77F",
                var value when string.Equals(value, S("CommonProperties"), StringComparison.Ordinal) => "\uE946",
                var value when string.Equals(value, S("CommonCut"), StringComparison.Ordinal) => "\uE8C6",
                var value when string.Equals(value, S("CommonCopy"), StringComparison.Ordinal) => "\uE8C8",
                var value when string.Equals(value, S("CommonRename"), StringComparison.Ordinal) => "\uE8AC",
                var value when string.Equals(value, S("CommonDelete"), StringComparison.Ordinal) => "\uE74D",
                var value when string.Equals(value, S("CommonOpenInTerminal"), StringComparison.Ordinal) => "\uE756",
                var value when string.Equals(value, S("CommonRefresh"), StringComparison.Ordinal) => "\uE72C",
                _ => "\uE8A5"
            };

            return new CommandMenuFlyoutItem
            {
                CommandId = commandId,
                Command = _entriesContextCommand,
                CommandParameter = commandId,
                Label = label,
                Icon = new FontIcon
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    Glyph = glyph
                }
            };
        }

        private void GroupedEntriesView_PointerPressedPreview(object sender, PointerRoutedEventArgs e)
        {
            HandleEntriesViewPointerPressedPreview(e);
        }

        private void HandleEntriesViewPointerPressedPreview(PointerRoutedEventArgs e)
        {
            SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
            if (!_entriesFlyoutOpen)
            {
                if (GetActiveEntriesViewHost()?.ResolvePressedEntry(e) is null &&
                    !IsEntriesGroupHeaderSource(e.OriginalSource as DependencyObject) &&
                    !string.IsNullOrWhiteSpace(_selectedEntryPath))
                {
                    ClearExplicitSelectionKeepAnchor();
                    FocusEntriesList();
                }

                return;
            }

            FrameworkElement root = GetVisibleEntriesRoot();
            var point = e.GetCurrentPoint(root);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }

            EntryViewModel? entry = GetActiveEntriesViewHost()?.ResolvePressedEntry(e);
            if (entry is not null)
            {
                if (entry.IsGroupHeader)
                {
                    _activeEntriesContextFlyout?.Hide();
                    e.Handled = true;
                    return;
                }

                if (!IsEntryAlreadySelected(entry))
                {
                    SelectEntryInList(entry, ensureVisible: false);
                }
            }
            else if (!IsEntriesGroupHeaderSource(e.OriginalSource as DependencyObject) &&
                !string.IsNullOrWhiteSpace(_selectedEntryPath))
            {
                ClearExplicitSelectionKeepAnchor();
                FocusEntriesList();
            }

            _activeEntriesContextFlyout?.Hide();
        }

        private void StaticEntriesContextFlyout_Opening(object sender, object e)
        {
            _entriesFlyoutOpen = true;
            _activeEntriesContextFlyout = sender as CommandMenuFlyout;
            ResetColumnSplitterCursorState();
            UpdateViewCommandStates();
            RefreshEntriesContextFlyoutText();

            if (ReferenceEquals(sender, FileEntriesContextFlyout) &&
                _entriesContextRequest?.Entry is EntryViewModel fileEntry &&
                !fileEntry.IsDirectory)
            {
                string displayName = GetDefaultExtractedFolderDisplayName(FileCommandTargetResolver.ResolveEntry(fileEntry.FullPath, isDirectory: false));
                FileExtractToFolderMenuItem.Text = SF("CommonExtractToNamedFolder", displayName);
            }

            if (ReferenceEquals(sender, FolderEntriesContextFlyout))
            {
                UpdateConditionalCommandVisibility(FolderEntriesContextFlyout, FileCommandIds.Paste, S("CommonPaste"), 2, _fileManagementCoordinator.HasAvailablePasteItems());
            }
            else if (ReferenceEquals(sender, BackgroundEntriesContextFlyout))
            {
                UpdateConditionalCommandVisibility(BackgroundEntriesContextFlyout, FileCommandIds.Paste, S("CommonPaste"), 0, _fileManagementCoordinator.HasAvailablePasteItems());
            }

            if (sender is CommandMenuFlyout flyout)
            {
                UpdateEntriesContextFlyoutState(flyout);
            }
        }

        private void StaticEntriesContextFlyout_Closed(object sender, object e)
        {
            _entriesFlyoutOpen = false;

            if (_pendingEntriesContextCommand is not null)
            {
                PendingEntriesContextCommand pendingCommand = _pendingEntriesContextCommand;
                _pendingEntriesContextCommand = null;
                _pendingEntriesContextRequest = null;
                _entriesContextRequest = null;
                _lastEntriesContextItem = null;
                _activeEntriesContextFlyout = null;
                _ = ExecuteEntriesContextCommandAsync(pendingCommand.CommandId, pendingCommand.Target);
                return;
            }

            if (_pendingContextRenameEntry is not null)
            {
                EntryViewModel pendingRenameEntry = _pendingContextRenameEntry;
                _pendingContextRenameEntry = null;
                _pendingEntriesContextRequest = null;
                _entriesContextRequest = null;
                _lastEntriesContextItem = null;
                _activeEntriesContextFlyout = null;
                _ = BeginRenameOverlayAsync(pendingRenameEntry);
                return;
            }

            if (_pendingEntriesContextRequest is not null)
            {
                EntriesContextRequest pendingRequest = _pendingEntriesContextRequest;
                _pendingEntriesContextRequest = null;

                _entriesContextRequest = pendingRequest;
                _lastEntriesContextItem = pendingRequest.Entry ?? _lastEntriesContextItem;
                _activeEntriesContextFlyout = SelectEntriesContextFlyout(pendingRequest);
                _activeEntriesContextFlyout.SetInvocationContext(pendingRequest.Anchor, pendingRequest.Position);
                _activeEntriesContextFlyout.ShowAt(pendingRequest.Anchor, new FlyoutShowOptions
                {
                    Position = pendingRequest.Position
                });
                return;
            }

            _entriesContextRequest = null;
            _lastEntriesContextItem = null;
            _activeEntriesContextFlyout = null;
            _pendingContextRenameEntry = null;
        }

        private FileCommandTarget ResolveEntriesContextTarget(EntryViewModel? contextEntry)
        {
            if (contextEntry is { IsLoaded: true })
            {
                if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase) && contextEntry.IsDirectory)
                {
                    return FileCommandTargetResolver.ResolveDriveRoot(contextEntry.FullPath);
                }

                return FileCommandTargetResolver.ResolveEntry(contextEntry.FullPath, contextEntry.IsDirectory);
            }

            if (string.Equals(_currentPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                return FileCommandTargetResolver.ResolveVirtualNode(ShellMyComputerPath, S("SidebarMyComputer"));
            }

            return FileCommandTargetResolver.ResolveListBackground(_currentPath);
        }
    }
}
