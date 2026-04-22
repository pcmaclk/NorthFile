using FileExplorerUI.Services;
using FileExplorerUI.Workspace;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void SidebarTreeContextRename_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingSidebarTreeContextEntry is null)
            {
                UpdateStatusKey("StatusRenameFailedSelectTreeNode");
                return;
            }

            SidebarTreeEntry entry = _pendingSidebarTreeContextEntry;
            _pendingSidebarTreeContextEntry = null;
            await BeginSidebarTreeRenameAsync(entry);
        }

        private async Task BeginSidebarTreeRenameAsync(SidebarTreeEntry entry)
        {
            if (_sidebarTreeView is null)
            {
                return;
            }

            _inlineEditCoordinator.CancelActiveSession();

            TreeViewNode? node = FindSidebarTreeNodeByPath(entry.FullPath);
            TreeViewItem? item = node is not null ? FindTreeViewItemForNode(node) : null;
            item ??= FindTreeViewItemByPath(entry.FullPath);
            if (item is null)
            {
                UpdateStatusKey("StatusRenameFailedTreeItemUnavailable");
                return;
            }

            EnsureSidebarTreeRenameOverlay();
            if (_sidebarTreeRenameOverlayCanvas is null || _sidebarTreeRenameOverlayBorder is null || _sidebarTreeRenameTextBox is null)
            {
                UpdateStatusKey("StatusRenameFailedTreeOverlayUnavailable");
                return;
            }

            _activeSidebarTreeRenameEntry = entry;
            _sidebarTreeRenameTextBox!.Text = entry.Name;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(16);
                item.UpdateLayout();
                _sidebarTreeView.UpdateLayout();
                _sidebarTreeRenameOverlayCanvas.UpdateLayout();

                if (FindDescendantByName<TextBlock>(item, "SidebarTreeNameTextBlock") is not TextBlock anchor)
                {
                    continue;
                }

                GeneralTransform transform = anchor.TransformToVisual(_sidebarTreeRenameOverlayCanvas);
                Rect bounds = transform.TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
                if (bounds.Width <= 0 || bounds.Height <= 0)
                {
                    continue;
                }

                double overlayHeight = _sidebarTreeRenameOverlayBorder!.ActualHeight > 0
                    ? _sidebarTreeRenameOverlayBorder.ActualHeight
                    : (_sidebarTreeRenameOverlayBorder.Height > 0 ? _sidebarTreeRenameOverlayBorder.Height : bounds.Height);
                double left = Math.Max(0, bounds.X + SidebarTreeRenameOffsetX);
                double targetWidth = Math.Max(SidebarTreeRenameMinWidth, bounds.Width + SidebarTreeRenameWidthPadding);
                if (_sidebarTreeRenameOverlayCanvas.ActualWidth > 0)
                {
                    double availableWidth = Math.Max(0, _sidebarTreeRenameOverlayCanvas.ActualWidth - left - SidebarTreeRenameRightMargin);
                    if (availableWidth <= 0)
                    {
                        continue;
                    }

                    targetWidth = Math.Min(targetWidth, availableWidth);
                }

                Canvas.SetLeft(_sidebarTreeRenameOverlayBorder, left);
                Canvas.SetTop(_sidebarTreeRenameOverlayBorder, Math.Max(0, bounds.Y + ((bounds.Height - overlayHeight) / 2) + SidebarTreeRenameOffsetY));
                _sidebarTreeRenameOverlayBorder.Width = targetWidth;
                _sidebarTreeRenameOverlayBorder.Visibility = Visibility.Visible;
                _sidebarTreeRenameInlineSession ??= new InlineEditSession(
                    CommitSidebarTreeRenameIfActiveAsync,
                    CancelSidebarTreeRename,
                    source => _sidebarTreeRenameOverlayBorder is not null &&
                        IsDescendantOf(source, _sidebarTreeRenameOverlayBorder));
                _inlineEditCoordinator.BeginSession(_sidebarTreeRenameInlineSession);
                _sidebarTreeRenameTextBox.Focus(FocusState.Programmatic);
                _sidebarTreeRenameTextBox.SelectAll();
                return;
            }

            UpdateStatusKey("StatusRenameFailedTreeTextAnchorUnavailable");
        }

        private void EnsureSidebarTreeRenameOverlay()
        {
            if (_sidebarTreeRenameOverlayCanvas is not null && _sidebarTreeRenameOverlayBorder is not null && _sidebarTreeRenameTextBox is not null)
            {
                SyncSidebarTreeRenameOverlayTheme();
                return;
            }

            if (_sidebarTreeView?.Parent is not Panel hostPanel)
            {
                return;
            }

            _sidebarTreeRenameOverlayCanvas = new Canvas
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Visible
            };
            Canvas.SetZIndex(_sidebarTreeRenameOverlayCanvas, 20);

            _sidebarTreeRenameTextBox = new TextBox
            {
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                FontSize = 14,
                Foreground = RenameOverlayTextBox.Foreground,
                FocusVisualPrimaryThickness = RenameOverlayTextBox.FocusVisualPrimaryThickness,
                FocusVisualSecondaryThickness = RenameOverlayTextBox.FocusVisualSecondaryThickness,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Margin = OffsetTop(RenameOverlayTextBox.Margin, SidebarTreeRenameTextMarginTopOffset),
                MinHeight = 24,
                Padding = new Thickness(0),
                TextAlignment = TextAlignment.Left,
                UseSystemFocusVisuals = RenameOverlayTextBox.UseSystemFocusVisuals,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            if (GetRenameOverlayTextBoxTemplate() is ControlTemplate renameOverlayTextBoxTemplate)
            {
                _sidebarTreeRenameTextBox.Template = renameOverlayTextBoxTemplate;
            }

            _sidebarTreeRenameTextBox.Resources["TextControlBackground"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBackgroundPointerOver"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBackgroundFocused"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBorderBrush"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBorderBrushFocused"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.Resources["TextControlBorderBrushPointerOver"] = new SolidColorBrush(Colors.Transparent);
            _sidebarTreeRenameTextBox.BeforeTextChanging += RenameOverlayTextBox_BeforeTextChanging;
            _sidebarTreeRenameTextBox.TextChanging += RenameOverlayTextBox_TextChanging;
            _sidebarTreeRenameTextBox.KeyDown += SidebarTreeRenameTextBox_KeyDown;
            _sidebarTreeRenameTextBox.LostFocus += SidebarTreeRenameTextBox_LostFocus;

            Brush selectionBrush = RenameOverlayBorder.BorderBrush;
            Brush renameBackgroundBrush = RenameOverlayBorder.Background;
            _sidebarTreeRenameOverlayBorder = new Border
            {
                Width = 160,
                Height = 24,
                Background = renameBackgroundBrush,
                BorderBrush = selectionBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(0),
                Padding = RenameOverlayBorder.Padding,
                Child = _sidebarTreeRenameTextBox,
                Visibility = Visibility.Collapsed
            };

            _sidebarTreeRenameOverlayCanvas.Children.Add(_sidebarTreeRenameOverlayBorder);
            hostPanel.Children.Add(_sidebarTreeRenameOverlayCanvas);
            SyncSidebarTreeRenameOverlayTheme();
        }

        private void SyncSidebarTreeRenameOverlayTheme()
        {
            if (_sidebarTreeRenameOverlayBorder is null || _sidebarTreeRenameTextBox is null)
            {
                return;
            }

            _sidebarTreeRenameTextBox.Foreground = RenameOverlayTextBox.Foreground;
            _sidebarTreeRenameTextBox.FocusVisualPrimaryThickness = RenameOverlayTextBox.FocusVisualPrimaryThickness;
            _sidebarTreeRenameTextBox.FocusVisualSecondaryThickness = RenameOverlayTextBox.FocusVisualSecondaryThickness;
            _sidebarTreeRenameTextBox.Margin = OffsetTop(RenameOverlayTextBox.Margin, SidebarTreeRenameTextMarginTopOffset);
            _sidebarTreeRenameTextBox.UseSystemFocusVisuals = RenameOverlayTextBox.UseSystemFocusVisuals;
            if (GetRenameOverlayTextBoxTemplate() is ControlTemplate renameOverlayTextBoxTemplate)
            {
                _sidebarTreeRenameTextBox.Template = renameOverlayTextBoxTemplate;
            }

            _sidebarTreeRenameOverlayBorder.Background = RenameOverlayBorder.Background;
            _sidebarTreeRenameOverlayBorder.BorderBrush = RenameOverlayBorder.BorderBrush;
            _sidebarTreeRenameOverlayBorder.CornerRadius = new CornerRadius(0);
            _sidebarTreeRenameOverlayBorder.Padding = RenameOverlayBorder.Padding;
        }

        private static Thickness OffsetTop(Thickness source, double topOffset)
        {
            return new Thickness(source.Left, source.Top + topOffset, source.Right, source.Bottom);
        }

        private async void SidebarTreeRenameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await CommitSidebarTreeRenameAsync();
            }
            else if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                CancelSidebarTreeRename();
            }
        }

        private async void SidebarTreeRenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await Task.Yield();
            if (IsFocusedElementWithinSidebarTreeRenameFlyout())
            {
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private bool IsFocusedElementWithinSidebarTreeRenameFlyout()
        {
            if (_sidebarTreeRenameTextBox is null || this.Content is not FrameworkElement root)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focused is not null)
            {
                if (ReferenceEquals(focused, _sidebarTreeRenameTextBox))
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }

        private async Task CommitSidebarTreeRenameAsync()
        {
            if (_isCommittingSidebarTreeRename || _activeSidebarTreeRenameEntry is null || _sidebarTreeRenameTextBox is null)
            {
                return;
            }

            _isCommittingSidebarTreeRename = true;
            try
            {
                SidebarTreeEntry entry = _activeSidebarTreeRenameEntry;
                string newName = _sidebarTreeRenameTextBox.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(newName) || string.Equals(newName, entry.Name, StringComparison.Ordinal))
                {
                    CancelSidebarTreeRename();
                    return;
                }

                if (!TryValidateTreeRename(entry, newName, out string validationError))
                {
                    ShowRenameValidationTeachingTip(_sidebarTreeRenameTextBox, validationError);
                    await Task.Yield();
                    _sidebarTreeRenameTextBox.Focus(FocusState.Programmatic);
                    _sidebarTreeRenameTextBox.SelectAll();
                    return;
                }

                await RenameSidebarTreeEntryAsync(entry, newName);
            }
            finally
            {
                _isCommittingSidebarTreeRename = false;
            }
        }

        private Task CommitSidebarTreeRenameIfActiveAsync()
        {
            if (_activeSidebarTreeRenameEntry is null || _sidebarTreeRenameOverlayBorder?.Visibility != Visibility.Visible)
            {
                return Task.CompletedTask;
            }

            return CommitSidebarTreeRenameAsync();
        }

        private void CancelSidebarTreeRename()
        {
            if (_sidebarTreeRenameInlineSession is not null)
            {
                _inlineEditCoordinator.ClearSession(_sidebarTreeRenameInlineSession);
            }
            _activeSidebarTreeRenameEntry = null;
            if (_sidebarTreeRenameOverlayBorder is not null)
            {
                _sidebarTreeRenameOverlayBorder.Visibility = Visibility.Collapsed;
            }
            _ = DispatcherQueue.TryEnqueue(FocusSidebarSurface);
        }

        private bool TryValidateTreeRename(SidebarTreeEntry entry, string newName, out string error)
        {
            if (string.IsNullOrWhiteSpace(newName))
            {
                error = S("ErrorRenameNameEmpty");
                return false;
            }

            if (newName is "." or "..")
            {
                error = SF("ErrorRenameReservedName", newName);
                return false;
            }

            if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = SF("ErrorRenameInvalidChars", newName);
                return false;
            }

            string parentPath = Path.GetDirectoryName(entry.FullPath.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty;
            string targetPath = Path.Combine(parentPath, newName);
            if (!string.Equals(entry.FullPath, targetPath, StringComparison.OrdinalIgnoreCase) &&
                _explorerService.PathExists(targetPath))
            {
                error = SF("ErrorRenameAlreadyExists", newName);
                return false;
            }

            error = string.Empty;
            return true;
        }

        private async Task RenameSidebarTreeEntryAsync(SidebarTreeEntry entry, string newName)
        {
            string sourcePath = entry.FullPath;
            string parentPath = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)) ?? string.Empty;
            bool wasSelectedInTree = _sidebarTreeView?.SelectedNode?.Content is SidebarTreeEntry selectedEntry &&
                string.Equals(selectedEntry.FullPath, sourcePath, StringComparison.OrdinalIgnoreCase);
            while (true)
            {
                try
                {
                    FileOperationResult<RenamedEntryInfo> renameResult = await _fileManagementCoordinator.TryRenameEntryAsync(parentPath, entry.Name, newName);
                    if (!renameResult.Succeeded)
                    {
                        if (!await ShowRenameFailureDialogAsync(
                            entry.Name,
                            renameResult.Failure?.Error ?? FileOperationError.Unknown,
                            renameResult.Failure?.Message ?? S("ErrorFileOperationUnknown")))
                        {
                            await Task.Yield();
                            _sidebarTreeRenameTextBox?.Focus(FocusState.Programmatic);
                            _sidebarTreeRenameTextBox?.SelectAll();
                            return;
                        }

                        continue;
                    }

                    RenamedEntryInfo renamed = renameResult.Value!;
                    try
                    {
                        _explorerService.MarkPathChanged(parentPath);
                    }
                    catch
                    {
                        EnsurePersistentRefreshFallbackInvalidation(parentPath, "tree-rename");
                    }

                    TreeViewNode? renamedNode = FindSidebarTreeNodeByPath(sourcePath);
                    if (renamedNode is not null)
                    {
                        UpdateSidebarTreeNodePath(renamedNode, renamed.SourcePath, renamed.TargetPath, newName);
                    }
                    else if (FindSidebarTreeNodeByPath(parentPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                    {
                        await PopulateSidebarTreeChildrenAsync(parentNode, parentPath, CancellationToken.None, expandAfterLoad: true);
                    }

                    string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
                    if (IsPathWithin(currentPath, renamed.SourcePath))
                    {
                        string suffix = currentPath.Length > renamed.SourcePath.Length
                            ? currentPath[renamed.SourcePath.Length..]
                            : string.Empty;
                        SetPrimaryPanelNavigationState(renamed.TargetPath + suffix, GetPanelQueryText(WorkspacePanelId.Primary), syncEditors: true);
                        UpdateBreadcrumbs(GetPanelCurrentPath(WorkspacePanelId.Primary));
                        UpdateNavButtonsState();
                        StyledSidebarView.SetSelectedPath(GetPanelCurrentPath(WorkspacePanelId.Primary));
                        await LoadPanelDataAsync(WorkspacePanelId.Primary);
                    }
                    else if (wasSelectedInTree)
                    {
                        await SelectSidebarTreePathAsync(renamed.TargetPath);
                    }

                    TryUpdateFavoritePathsForRename(renamed.SourcePath, renamed.TargetPath);
                    UpdateListEntryNameForCurrentDirectory(renamed.SourcePath, newName);

                    CancelSidebarTreeRename();
                    UpdateStatusKey("StatusRenameSuccess", entry.Name, newName);
                    _ = DispatcherQueue.TryEnqueue(FocusSidebarSurface);
                    return;
                }
                catch (Exception ex)
                {
                    if (!await ShowRenameFailureDialogAsync(entry.Name, FileOperationErrors.Classify(ex), FileOperationErrors.ToUserMessage(ex)))
                    {
                        await Task.Yield();
                        _sidebarTreeRenameTextBox?.Focus(FocusState.Programmatic);
                        _sidebarTreeRenameTextBox?.SelectAll();
                        return;
                    }
                }
            }
        }
    }
}
