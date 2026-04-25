using NorthFileUI.Controls;
using NorthFileUI.Services;
using NorthFileUI.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private ControlTemplate? GetRenameOverlayTextBoxTemplate()
        {
            if (_renameOverlayTextBoxTemplate is not null)
            {
                return _renameOverlayTextBoxTemplate;
            }

            const string xaml =
                "<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" " +
                "TargetType=\"TextBox\">" +
                "<Grid>" +
                "<Border x:Name=\"BorderElement\" " +
                "Background=\"Transparent\" " +
                "BorderBrush=\"Transparent\" " +
                "BorderThickness=\"0\" " +
                "Control.IsTemplateFocusTarget=\"True\" />" +
                "<ScrollViewer x:Name=\"ContentElement\" " +
                "AutomationProperties.AccessibilityView=\"Raw\" " +
                "Foreground=\"{TemplateBinding Foreground}\" " +
                "HorizontalAlignment=\"{TemplateBinding HorizontalContentAlignment}\" " +
                "HorizontalScrollBarVisibility=\"{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}\" " +
                "HorizontalScrollMode=\"{TemplateBinding ScrollViewer.HorizontalScrollMode}\" " +
                "IsDeferredScrollingEnabled=\"{TemplateBinding ScrollViewer.IsDeferredScrollingEnabled}\" " +
                "IsHorizontalRailEnabled=\"{TemplateBinding ScrollViewer.IsHorizontalRailEnabled}\" " +
                "IsTabStop=\"False\" " +
                "IsVerticalRailEnabled=\"{TemplateBinding ScrollViewer.IsVerticalRailEnabled}\" " +
                "Margin=\"{TemplateBinding BorderThickness}\" " +
                "Padding=\"{TemplateBinding Padding}\" " +
                "VerticalAlignment=\"{TemplateBinding VerticalContentAlignment}\" " +
                "VerticalScrollBarVisibility=\"{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}\" " +
                "VerticalScrollMode=\"{TemplateBinding ScrollViewer.VerticalScrollMode}\" " +
                "ZoomMode=\"Disabled\" />" +
                "</Grid>" +
                "</ControlTemplate>";

            try
            {
                _renameOverlayTextBoxTemplate = (ControlTemplate)XamlReader.Load(xaml);
            }
            catch
            {
                _renameOverlayTextBoxTemplate = null;
            }

            return _renameOverlayTextBoxTemplate;
        }

        private async Task<string?> PromptRenameAsync(string oldName)
        {
            var input = new TextBox
            {
                Text = oldName,
                MinWidth = 280
            };

            var dialog = new ContentDialog
            {
                Title = S("DialogRenameTitle"),
                Content = input,
                PrimaryButtonText = S("DialogRenamePrimaryButton"),
                CloseButtonText = S("DialogCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };

            PrepareWindowDialog(dialog);

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return null;
            }

            return input.Text?.Trim();
        }

        private async void RenameOverlayTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            await Task.Delay(1);
            if (IsFocusedElementWithinRenameOverlay())
            {
                return;
            }

            if (_activeRenameOverlayPanelId == WorkspacePanelId.Secondary)
            {
                CancelActiveRenameOverlayWithoutFocusRestore();
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private async void RenameOverlayTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await CommitRenameOverlayAsync();
                return;
            }

            if (e.Key == Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                WorkspacePanelId ownerPanelId = _activeRenameOverlayPanelId;
                HideRenameOverlay();
                FocusRenameOverlayOwnerList(ownerPanelId);
                return;
            }

            if (e.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right
                or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown)
            {
                e.Handled = true;
                WorkspacePanelId ownerPanelId = _activeRenameOverlayPanelId;
                HideRenameOverlay();
                FocusRenameOverlayOwnerList(ownerPanelId);
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (ownerPanelId == WorkspacePanelId.Secondary)
                    {
                        HandleSecondaryEntriesNavigationKey(e.Key);
                    }
                    else
                    {
                        HandleEntriesNavigationKey(e.Key);
                    }
                });
            }
        }

        private void RenameOverlayTextBox_BeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            HandleRenameTextBoxBeforeTextChanging(sender, args);
        }

        private void RenameOverlayTextBox_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
            NormalizeRenameTextBoxInput(sender);
        }

        private void HandleRenameTextBoxBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
        {
            if (string.IsNullOrEmpty(args.NewText) || !ContainsInvalidFileNameChars(args.NewText))
            {
                return;
            }

            args.Cancel = true;
            ShowRenameInputTeachingTip(sender);
        }

        private void NormalizeRenameTextBoxInput(TextBox sender)
        {
            if (_suppressRenameTextFiltering)
            {
                return;
            }

            string currentText = sender.Text ?? string.Empty;
            if (!ContainsInvalidFileNameChars(currentText))
            {
                HideRenameInputTeachingTip();
                return;
            }

            int selectionStart = sender.SelectionStart;
            int removedBeforeSelection = CountInvalidFileNameChars(currentText.AsSpan(0, Math.Min(selectionStart, currentText.Length)));
            string sanitizedText = RemoveInvalidFileNameChars(currentText);

            _suppressRenameTextFiltering = true;
            try
            {
                sender.Text = sanitizedText;
                sender.SelectionStart = Math.Max(0, Math.Min(sanitizedText.Length, selectionStart - removedBeforeSelection));
                sender.SelectionLength = 0;
            }
            finally
            {
                _suppressRenameTextFiltering = false;
            }

            ShowRenameInputTeachingTip(sender);
        }

        private void ShowRenameInputTeachingTip(FrameworkElement target)
        {
            ShowRenameTeachingTip(
                target,
                S("RenameInvalidCharTeachingTipTitle"),
                S("RenameInvalidCharTeachingTipMessage"));
        }

        private void ShowRenameValidationTeachingTip(FrameworkElement target, string message)
        {
            ShowRenameTeachingTip(
                target,
                S("RenameValidationTeachingTipTitle"),
                message);
        }

        private void ShowRenameTeachingTip(FrameworkElement target, string title, string message)
        {
            if (RenameInputTeachingTip is null)
            {
                return;
            }

            EnsureRenameInputTeachingTipTimer();
            _renameInputTeachingTipTimer!.Stop();
            RenameInputTeachingTip.IsOpen = false;
            RenameInputTeachingTip.Target = target;
            RenameInputTeachingTip.Title = title;
            RenameInputTeachingTip.Subtitle = message;
            RenameInputTeachingTip.IsOpen = true;
            _renameInputTeachingTipTimer.Interval = TimeSpan.FromSeconds(5);
            _renameInputTeachingTipTimer.Start();
        }

        private void EnsureRenameInputTeachingTipTimer()
        {
            if (_renameInputTeachingTipTimer is not null)
            {
                return;
            }

            _renameInputTeachingTipTimer = new DispatcherTimer();
            _renameInputTeachingTipTimer.Tick += RenameInputTeachingTipTimer_Tick;
        }

        private void RenameInputTeachingTipTimer_Tick(object? sender, object e)
        {
            HideRenameInputTeachingTip();
        }

        private void HideRenameInputTeachingTip()
        {
            _renameInputTeachingTipTimer?.Stop();
            if (RenameInputTeachingTip is not null)
            {
                RenameInputTeachingTip.IsOpen = false;
            }
        }

        private static bool ContainsInvalidFileNameChars(string text) => text.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;

        private static int CountInvalidFileNameChars(ReadOnlySpan<char> text)
        {
            int count = 0;
            foreach (char ch in text)
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), ch) >= 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static string RemoveInvalidFileNameChars(string text)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return new string(Array.FindAll(text.ToCharArray(), ch => Array.IndexOf(invalidChars, ch) < 0));
        }

        private async Task<bool> StartRenameForCreatedEntryAsync(EntryViewModel entry, int insertIndex)
        {
            await Task.Delay(16);
            GetVisibleEntriesRoot().UpdateLayout();

            bool renameStarted = await BeginRenameOverlayAsync(entry, ensureVisible: false, updateSelection: false);
            if (renameStarted)
            {
                return true;
            }

            ScrollEntryIntoView(entry);
            await Task.Delay(16);
            GetVisibleEntriesRoot().UpdateLayout();
            bool retryStarted = await BeginRenameOverlayAsync(entry, ensureVisible: false, updateSelection: false);
            return retryStarted;
        }

        private int FindInsertIndexForEntry(EntryViewModel entry)
        {
            return _entriesPresentationBuilder.FindInsertIndex(
                PrimaryEntries,
                entry,
                GetPanelSortField(WorkspacePanelId.Primary),
                GetPanelSortDirection(WorkspacePanelId.Primary));
        }

        private int CompareEntries(EntryViewModel left, EntryViewModel right)
        {
            return _entriesPresentationBuilder.Compare(
                left,
                right,
                GetPanelSortField(WorkspacePanelId.Primary),
                GetPanelSortDirection(WorkspacePanelId.Primary));
        }

        private async Task<bool> BeginRenameOverlayAsync(
            EntryViewModel entry,
            bool ensureVisible = true,
            bool updateSelection = true,
            WorkspacePanelId panelId = WorkspacePanelId.Primary)
        {
            _inlineEditCoordinator.CancelActiveSession();
            HideRenameOverlay();
            bool alreadySelected = panelId == WorkspacePanelId.Secondary
                ? string.Equals(SecondaryPanelState.SelectedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase)
                : string.Equals(_selectedEntryPath, entry.FullPath, StringComparison.OrdinalIgnoreCase);

            if (updateSelection && !alreadySelected)
            {
                if (panelId == WorkspacePanelId.Secondary)
                {
                    SecondarySelectEntryInList(entry);
                    if (ensureVisible)
                    {
                        SecondaryScrollEntryIntoView(entry);
                    }
                }
                else
                {
                    SelectEntryInList(entry, ensureVisible);
                }
            }
            bool scrolledIntoViewForRetry = false;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(16);
                GetRenameOverlayHost(panelId).UpdateLayout();
                if (!TryPositionRenameOverlay(entry, panelId))
                {
                    if (!scrolledIntoViewForRetry && attempt >= 1)
                    {
                        scrolledIntoViewForRetry = true;
                        if (panelId == WorkspacePanelId.Secondary)
                        {
                            SecondaryScrollEntryIntoView(entry);
                        }
                        else
                        {
                            ScrollEntryIntoView(entry);
                        }
                    }
                    continue;
                }

                _activeRenameOverlayPanelId = panelId;
                _activeRenameOverlayEntry = entry;
                entry.IsNameEditing = true;
                RenameOverlayTextBox.Text = entry.Name;
                RenameOverlayBorder.Visibility = Visibility.Visible;
                _entriesRenameInlineSession ??= new InlineEditSession(
                    () => CommitRenameOverlayIfActiveAsync(),
                    () =>
                    {
                        WorkspacePanelId ownerPanelId = _activeRenameOverlayPanelId;
                        HideRenameOverlay();
                        if (!_suppressRenameOverlayFocusRestoreOnCancel)
                        {
                            FocusRenameOverlayOwnerList(ownerPanelId);
                        }
                    },
                    source => IsDescendantOf(source, RenameOverlayBorder));
                _inlineEditCoordinator.BeginSession(_entriesRenameInlineSession);
                RenameOverlayTextBox.Focus(FocusState.Programmatic);
                SelectRenameTargetText(RenameOverlayTextBox, entry);
                return true;
            }

            UpdateStatusKey("StatusRenameFailedCouldNotStartInlineEditor");
            return false;
        }

        private bool TryPositionRenameOverlay(EntryViewModel entry, WorkspacePanelId panelId)
        {
            if (!TryGetEntryAnchor(entry, panelId, out EntryNameCell? anchor))
            {
                return false;
            }

            FrameworkElement textAnchor = anchor.NameTextElement;
            GeneralTransform cellTransform = anchor.TransformToVisual(RenameOverlayCanvas);
            Rect cellBounds = cellTransform.TransformBounds(new Rect(0, 0, anchor.ActualWidth, anchor.ActualHeight));
            GeneralTransform textTransform = textAnchor.TransformToVisual(RenameOverlayCanvas);
            Rect textBounds = textTransform.TransformBounds(new Rect(0, 0, textAnchor.ActualWidth, textAnchor.ActualHeight));
            if (cellBounds.Width <= 0 || cellBounds.Height <= 0 || textBounds.Width <= 0 || textBounds.Height <= 0)
            {
                return false;
            }

            double overlayHeight = RenameOverlayBorder.ActualHeight > 0
                ? RenameOverlayBorder.ActualHeight
                : (RenameOverlayBorder.Height > 0 ? RenameOverlayBorder.Height : textBounds.Height);

            double left = Math.Max(0, textBounds.X - 1);
            double top = Math.Max(0, cellBounds.Y + ((cellBounds.Height - overlayHeight) / 2));
            const double renameRightMargin = 12;
            const double renameWidthPadding = 24;
            const double renameMinWidth = 88;
            double canvasAvailableWidth = RenameOverlayCanvas.ActualWidth > 0
                ? RenameOverlayCanvas.ActualWidth - left - renameRightMargin
                : cellBounds.Right - left - renameRightMargin;
            double availableWidth = Math.Max(renameMinWidth, canvasAvailableWidth);
            double desiredWidth = textBounds.Width + renameWidthPadding;

            Canvas.SetLeft(RenameOverlayBorder, left);
            Canvas.SetTop(RenameOverlayBorder, top);
            RenameOverlayBorder.Width = Math.Min(availableWidth, Math.Max(renameMinWidth, desiredWidth));
            return true;
        }

        private void UpdateRenameOverlayPosition()
        {
            if (_activeRenameOverlayEntry is null)
            {
                return;
            }

            if (!TryPositionRenameOverlay(_activeRenameOverlayEntry, _activeRenameOverlayPanelId))
            {
                HideRenameOverlay();
            }
        }

        private bool TryGetEntryAnchor<T>(EntryViewModel entry, WorkspacePanelId panelId, [NotNullWhen(true)] out T? anchor) where T : FrameworkElement
        {
            anchor = null;
            if (entry is null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return false;
            }

            FrameworkElement? found = null;
            if (panelId == WorkspacePanelId.Secondary)
            {
                SecondaryEntriesScrollViewer.UpdateLayout();
                if (TryGetSecondaryEntryAnchor(entry, out FrameworkElement? element))
                {
                    found = typeof(T) == typeof(EntryNameCell)
                        ? element is null ? null : FindDescendant<EntryNameCell>(element)
                        : element;
                }
            }
            else
            {
                IEntriesViewHost? host = GetActiveEntriesViewHost();
                if (host is null)
                {
                    return false;
                }

                GetVisibleEntriesRoot().UpdateLayout();
                found = typeof(T) == typeof(EntryNameCell)
                    ? host.FindEntryNameCell(entry.FullPath)
                    : host.FindEntryContainer(entry.FullPath);
            }

            if (found is T typed)
            {
                anchor = typed;
                return true;
            }

            return false;
        }

        private void ScrollEntryIntoView(EntryViewModel entry)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.FullPath))
            {
                return;
            }

            if (GetActiveEntriesViewHost()?.ScrollEntryIntoView(entry.FullPath) == true)
            {
                return;
            }
        }

        private async Task CommitRenameOverlayAsync()
        {
            if (_isCommittingRenameOverlay || _activeRenameOverlayEntry is null)
            {
                return;
            }

            _isCommittingRenameOverlay = true;
            try
            {
                EntryViewModel entry = _activeRenameOverlayEntry;
                string proposedName = RenameOverlayTextBox.Text?.Trim() ?? string.Empty;
                int primaryIndex = _activeRenameOverlayPanelId == WorkspacePanelId.Primary
                    ? PrimaryEntries.IndexOf(entry)
                    : -1;

                if (entry.IsPendingCreate)
                {
                    if (string.IsNullOrWhiteSpace(proposedName))
                    {
                        CancelPendingCreateEntry(primaryIndex);
                        HideRenameOverlay();
                        FocusEntriesList();
                        return;
                    }

                    if (!_fileManagementCoordinator.TryValidateName(
                            GetPanelCurrentPath(WorkspacePanelId.Primary),
                            entry.Name,
                            proposedName,
                            out string pendingValidationError))
                    {
                        ShowRenameValidationTeachingTip(RenameOverlayTextBox, pendingValidationError);
                        RenameOverlayTextBox.Focus(FocusState.Programmatic);
                        SelectRenameTargetText(RenameOverlayTextBox, entry);
                        return;
                    }

                    await CommitPendingCreateAsync(entry, primaryIndex, proposedName);
                    return;
                }

                if (string.Equals(proposedName, entry.Name, StringComparison.Ordinal))
                {
                    WorkspacePanelId ownerPanelId = _activeRenameOverlayPanelId;
                    HideRenameOverlay();
                    if (ReferenceEquals(_pendingCreatedEntrySelection, entry))
                    {
                        CompleteCreatedEntrySelectionIfPending(entry, ensureVisible: false);
                    }
                    else
                    {
                        _ = DispatcherQueue.TryEnqueue(() => FocusRenameOverlayOwnerList(ownerPanelId));
                    }
                    return;
                }

                string renameDirectoryPath = _activeRenameOverlayPanelId == WorkspacePanelId.Secondary
                    ? SecondaryPanelState.CurrentPath
                    : GetPanelCurrentPath(WorkspacePanelId.Primary);
                if (!_fileManagementCoordinator.TryValidateName(renameDirectoryPath, entry.Name, proposedName, out string validationError))
                {
                    ShowRenameValidationTeachingTip(RenameOverlayTextBox, validationError);
                    RenameOverlayTextBox.Focus(FocusState.Programmatic);
                    SelectRenameTargetText(RenameOverlayTextBox, entry);
                    return;
                }

                if (_activeRenameOverlayPanelId == WorkspacePanelId.Primary && primaryIndex < 0)
                {
                    HideRenameOverlay();
                    return;
                }

                if (_activeRenameOverlayPanelId == WorkspacePanelId.Secondary)
                {
                    await RenameSecondaryEntryInlineAsync(entry, proposedName);
                }
                else
                {
                    await RenameEntryAsync(entry, primaryIndex, proposedName);
                }
            }
            finally
            {
                _isCommittingRenameOverlay = false;
            }
        }

        private async Task CommitRenameOverlayIfActiveAsync(bool focusEntriesList = true)
        {
            if (RenameOverlayBorder.Visibility != Visibility.Visible || _activeRenameOverlayEntry is null)
            {
                return;
            }

            WorkspacePanelId ownerPanelId = _activeRenameOverlayPanelId;
            await CommitRenameOverlayAsync();

            if (focusEntriesList && RenameOverlayBorder.Visibility != Visibility.Visible)
            {
                FocusRenameOverlayOwnerList(ownerPanelId);
            }
        }

        private async Task RenameSecondaryEntryInlineAsync(EntryViewModel entry, string newName)
        {
            string oldName = entry.Name;
            string currentDirectoryPath = SecondaryPanelState.CurrentPath;
            if (string.IsNullOrWhiteSpace(currentDirectoryPath) ||
                string.Equals(currentDirectoryPath, ShellMyComputerPath, StringComparison.OrdinalIgnoreCase))
            {
                HideRenameOverlay();
                UpdateStatusKey("StatusRenameFailedSelectLoaded");
                return;
            }

            TreeViewNode? renamedTreeNode = entry.IsDirectory ? FindSidebarTreeNodeByPath(entry.FullPath) : null;
            while (true)
            {
                try
                {
                    FileOperationResult<RenamedEntryInfo> renameResult =
                        await _fileManagementCoordinator.TryRenameEntryAsync(currentDirectoryPath, entry.Name, newName);
                    FileOperationsController.RenameDecision renameDecision = _fileOperationsController.AnalyzeRenameResult(
                        renameResult,
                        S("ErrorFileOperationUnknown"));
                    if (!renameDecision.Succeeded || renameDecision.RenamedInfo is null)
                    {
                        if (!await ShowRenameFailureDialogAsync(
                                oldName,
                                renameResult.Failure?.Error ?? FileOperationError.Unknown,
                                renameDecision.FailureMessage ?? S("ErrorFileOperationUnknown")))
                        {
                            RenameOverlayTextBox.Focus(FocusState.Programmatic);
                            SelectRenameTargetText(RenameOverlayTextBox, entry);
                            return;
                        }

                        continue;
                    }

                    RenamedEntryInfo renamed = renameDecision.RenamedInfo.Value;
                    HideRenameOverlay();
                    TryUpdateFavoritePathsForRename(renamed.SourcePath, renamed.TargetPath);

                    if (entry.IsDirectory)
                    {
                        if (renamedTreeNode is not null)
                        {
                            UpdateSidebarTreeNodePath(renamedTreeNode, renamed.SourcePath, renamed.TargetPath, newName);
                        }
                        else if (FindSidebarTreeNodeByPath(currentDirectoryPath) is TreeViewNode parentNode && parentNode.IsExpanded)
                        {
                            await PopulateSidebarTreeChildrenAsync(parentNode, currentDirectoryPath, CancellationToken.None, expandAfterLoad: true);
                        }
                    }

                    await ReloadPanelWithSelectionAsync(WorkspacePanelId.Secondary, renamed.TargetPath);
                    UpdateStatusKey("StatusRenameSuccess", oldName, newName);
                    return;
                }
                catch (Exception ex)
                {
                    if (!await ShowRenameFailureDialogAsync(oldName, FileOperationErrors.Classify(ex), FileOperationErrors.ToUserMessage(ex)))
                    {
                        RenameOverlayTextBox.Focus(FocusState.Programmatic);
                        SelectRenameTargetText(RenameOverlayTextBox, entry);
                        return;
                    }
                }
            }
        }

        private async Task CommitPendingCreateAsync(EntryViewModel entry, int index, string proposedName)
        {
            string currentPath = GetPanelCurrentPath(WorkspacePanelId.Primary);
            string targetPath = Path.Combine(currentPath, proposedName);
            try
            {
                if (entry.PendingCreateIsDirectory)
                {
                    await _explorerService.CreateDirectoryAsync(targetPath);
                }
                else
                {
                    await _explorerService.CreateEmptyFileAsync(targetPath);
                }

                try
                {
                    _explorerService.MarkPathChanged(currentPath);
                }
                catch
                {
                    EnsurePersistentRefreshFallbackInvalidation(
                        currentPath,
                        entry.PendingCreateIsDirectory ? "create-folder" : "create-file");
                }

                HideRenameOverlay();

                if (index < 0 || index >= PrimaryEntries.Count)
                {
                    return;
                }

                entry.Name = proposedName;
                entry.DisplayName = GetEntryDisplayName(proposedName, entry.PendingCreateIsDirectory);
                entry.PendingName = proposedName;
                entry.FullPath = targetPath;
                entry.Type = GetEntryTypeText(proposedName, entry.PendingCreateIsDirectory, isLink: false);
                entry.IconGlyph = GetEntryIconGlyph(entry.PendingCreateIsDirectory, isLink: false, proposedName);
                entry.IconForeground = GetEntryIconBrush(entry.PendingCreateIsDirectory, isLink: false, proposedName);
                entry.SizeText = entry.PendingCreateIsDirectory ? string.Empty : "0 B";
                entry.ModifiedText = DateTime.Now.ToString("g", CultureInfo.CurrentCulture);
                entry.IsPendingCreate = false;
                entry.MftRef = 0;
                entry.IsLoaded = true;
                entry.IsMetadataLoaded = true;
                InvalidatePresentationSourceCache();

                if (entry.PendingCreateIsDirectory &&
                    FindSidebarTreeNodeByPath(currentPath) is TreeViewNode parentNode &&
                    parentNode.IsExpanded)
                {
                    await PopulateSidebarTreeChildrenAsync(parentNode, currentPath, CancellationToken.None, expandAfterLoad: true);
                }

                UpdateStatusKey("StatusCreateSuccess", proposedName);
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    SelectEntryInList(entry, ensureVisible: true);
                    FocusEntriesList();
                });
            }
            catch (Exception ex)
            {
                if (!await ShowCreateFailureDialogAsync(
                    proposedName,
                    FileOperationErrors.Classify(ex),
                    FileOperationErrors.ToUserMessage(ex)))
                {
                    RenameOverlayTextBox.Focus(FocusState.Programmatic);
                    SelectRenameTargetText(RenameOverlayTextBox, entry);
                    return;
                }

                await CommitPendingCreateAsync(entry, index, proposedName);
            }
        }

        private void CancelPendingCreateEntry(int index)
        {
            if (index < 0 || index >= PrimaryEntries.Count)
            {
                return;
            }

            PrimaryEntries.RemoveAt(index);
            InvalidatePresentationSourceCache();
        }

        private void HideRenameOverlay()
        {
            if (_entriesRenameInlineSession is not null)
            {
                _inlineEditCoordinator.ClearSession(_entriesRenameInlineSession);
            }

            if (_activeRenameOverlayEntry is not null)
            {
                _activeRenameOverlayEntry.IsNameEditing = false;
            }
            _activeRenameOverlayEntry = null;
            _activeRenameOverlayPanelId = WorkspacePanelId.Primary;
            RenameOverlayBorder.Visibility = Visibility.Collapsed;
        }

        private void CancelActiveRenameOverlayWithoutFocusRestore()
        {
            _suppressRenameOverlayFocusRestoreOnCancel = true;
            try
            {
                _inlineEditCoordinator.CancelActiveSession();
            }
            finally
            {
                _suppressRenameOverlayFocusRestoreOnCancel = false;
            }
        }

        private void CancelRenameOverlayForPanelSwitch(WorkspacePanelId targetPanelId)
        {
            if (RenameOverlayBorder.Visibility != Visibility.Visible ||
                _activeRenameOverlayEntry is null ||
                _activeRenameOverlayPanelId == targetPanelId)
            {
                return;
            }

            CancelActiveRenameOverlayWithoutFocusRestore();
        }

        private FrameworkElement GetRenameOverlayHost(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary
                ? SecondaryEntriesScrollViewer
                : GetVisibleEntriesRoot();
        }

        private void FocusRenameOverlayOwnerList(WorkspacePanelId? ownerPanelId = null)
        {
            WorkspacePanelId panelId = ownerPanelId ?? _activeRenameOverlayPanelId;
            if (panelId == WorkspacePanelId.Secondary)
            {
                FocusSecondaryEntriesList();
            }
            else
            {
                FocusEntriesList();
            }
        }

        private bool IsFocusedElementWithinRenameOverlay()
        {
            if (Content is not FrameworkElement root || root.XamlRoot is null)
            {
                return false;
            }

            DependencyObject? focused = FocusManager.GetFocusedElement(root.XamlRoot) as DependencyObject;
            while (focused is not null)
            {
                if (ReferenceEquals(focused, RenameOverlayBorder) || ReferenceEquals(focused, RenameOverlayTextBox))
                {
                    return true;
                }

                focused = VisualTreeHelper.GetParent(focused);
            }

            return false;
        }

        private static void SelectRenameTargetText(TextBox textBox, EntryViewModel entry)
        {
            string text = textBox.Text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (entry.IsDirectory)
            {
                textBox.SelectAll();
                return;
            }

            int extensionStart = text.LastIndexOf('.');
            if (extensionStart > 0)
            {
                textBox.SelectionStart = 0;
                textBox.SelectionLength = extensionStart;
                return;
            }

            textBox.SelectAll();
        }

        private void CompleteCreatedEntrySelectionIfPending(EntryViewModel entry, bool ensureVisible)
        {
            if (!ReferenceEquals(_pendingCreatedEntrySelection, entry))
            {
                return;
            }

            _pendingCreatedEntrySelection = null;
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                SelectEntryInList(entry, ensureVisible);
                FocusEntriesList();
            });
        }
    }
}
