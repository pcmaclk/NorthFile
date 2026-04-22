using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FileExplorerUI.Workspace;
using Windows.Graphics;
using WinRT.Interop;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private async void RootElement_PointerPressedPreview(object sender, PointerRoutedEventArgs e)
        {
            if (e.OriginalSource is DependencyObject pointerSource)
            {
                if (IsDescendantOf(pointerSource, StyledSidebarView))
                {
                    SetActiveSelectionSurface(SelectionSurfaceId.Sidebar);
                }
                else if (IsDescendantOf(pointerSource, ExplorerPaneActionRailHost) ||
                    IsDescendantOf(pointerSource, ExplorerPaneActionToolbarHost))
                {
                    // The center rail is a pane-level affordance; using it must not switch panel focus.
                }
                else if (IsDescendantOf(pointerSource, ExplorerBodyGrid))
                {
                    SetActiveSelectionSurface(SelectionSurfaceId.PrimaryPane);
                }
            }

            if (!_inlineEditCoordinator.HasActiveSession)
            {
                return;
            }

            if (e.OriginalSource is DependencyObject source &&
                _inlineEditCoordinator.IsSourceWithinActiveSession(source))
            {
                return;
            }

            if (!_inlineEditCoordinator.ShouldCommitActiveSessionOnExternalClick())
            {
                _inlineEditCoordinator.CancelActiveSession();
                return;
            }

            if (!e.GetCurrentPoint(Content as UIElement).Properties.IsLeftButtonPressed)
            {
                return;
            }

            await _inlineEditCoordinator.CommitActiveSessionAsync();
        }

        private void RegisterSidebarSplitterHandlers(UIElement splitter)
        {
            splitter.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(SidebarSplitter_PointerPressed), true);
            splitter.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(SidebarSplitter_PointerMoved), true);
            splitter.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(SidebarSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(SidebarSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(SidebarSplitter_PointerReleased), true);
        }

        private void RegisterColumnSplitterHandlers(UIElement splitter)
        {
            splitter.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ColumnSplitter_PointerPressed), true);
            splitter.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(ColumnSplitter_PointerMoved), true);
            splitter.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ColumnSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(ColumnSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(ColumnSplitter_PointerReleased), true);
        }

        private void RegisterPaneSplitterHandlers(UIElement splitter)
        {
            splitter.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(PaneSplitter_PointerPressed), true);
            splitter.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PaneSplitter_PointerMoved), true);
            splitter.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(PaneSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(PaneSplitter_PointerReleased), true);
            splitter.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(PaneSplitter_PointerReleased), true);
        }

        private void ColumnSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter || !TryGetColumnSplitterKind(splitter, out ColumnSplitterKind kind))
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            WorkspacePanelId panelId = GetColumnSplitterPanelId(splitter);
            CancelRenameOverlayForPanelSwitch(panelId);
            ActivateWorkspacePanel(panelId);

            _activeSplitterElement = splitter;
            _activeSplitterDragMode = SplitterDragMode.Column;
            _activeColumnSplitterKind = kind;
            _activeColumnSplitterPanelId = panelId;
            _activeColumnResizeState = new ColumnResizeState(
                GetColumnWidthValue(_activeColumnSplitterPanelId, ColumnSplitterKind.Name),
                GetColumnWidthValue(_activeColumnSplitterPanelId, ColumnSplitterKind.Type),
                GetColumnWidthValue(_activeColumnSplitterPanelId, ColumnSplitterKind.Size),
                GetColumnWidthValue(_activeColumnSplitterPanelId, ColumnSplitterKind.Modified),
                GetDetailsContentWidth(_activeColumnSplitterPanelId));
            _splitterDragStartX = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            splitter.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void ColumnSplitterHover_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _splitterHoverCount++;
        }

        private void ColumnSplitterHover_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (_splitterHoverCount > 0)
            {
                _splitterHoverCount--;
            }
        }

        private void ColumnSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter
                || !ReferenceEquals(splitter, _activeSplitterElement)
                || _activeSplitterDragMode != SplitterDragMode.Column
                || _activeColumnSplitterKind is not ColumnSplitterKind kind
                || _activeColumnResizeState is not ColumnResizeState state)
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double x = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            double delta = x - _splitterDragStartX;
            if (Math.Abs(delta) < 0.5)
            {
                return;
            }

            const double minType = 90;
            const double minSize = 80;
            const double minName = 120;
            const double minModified = 120;

            double name = state.Name;
            double type = state.Type;
            double size = state.Size;
            double modified = state.Modified;
            double content = state.ContentWidth;

            switch (kind)
            {
                case ColumnSplitterKind.Name:
                    {
                        name = Math.Max(minName, state.Name + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case ColumnSplitterKind.Type:
                    {
                        type = Math.Max(minType, state.Type + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case ColumnSplitterKind.Size:
                    {
                        size = Math.Max(minSize, state.Size + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
                case ColumnSplitterKind.Modified:
                    {
                        modified = Math.Max(minModified, state.Modified + delta);
                        content = name + 24 + type + size + modified;
                    }
                    break;
            }

            SetPanelColumnWidths(_activeColumnSplitterPanelId, name, type, size, modified, content);
            e.Handled = true;
        }

        private void ColumnSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            EndActiveSplitterDrag(sender as UIElement);
            e.Handled = true;
        }

        private static bool TryGetColumnSplitterKind(UIElement splitter, out ColumnSplitterKind kind)
        {
            kind = default;
            if (splitter is not FrameworkElement element
                || element.Tag is not string tagText)
            {
                return false;
            }

            string normalizedTag = tagText.StartsWith("S", StringComparison.OrdinalIgnoreCase)
                ? tagText[1..]
                : tagText;
            if (!int.TryParse(normalizedTag, out int tagValue)
                || !Enum.IsDefined(typeof(ColumnSplitterKind), tagValue))
            {
                return false;
            }

            kind = (ColumnSplitterKind)tagValue;
            return true;
        }

        private static WorkspacePanelId GetColumnSplitterPanelId(UIElement splitter)
        {
            return splitter is FrameworkElement element &&
                element.Tag is string tagText &&
                tagText.StartsWith("S", StringComparison.OrdinalIgnoreCase)
                    ? WorkspacePanelId.Secondary
                    : WorkspacePanelId.Primary;
        }

        private double GetColumnWidthValue(WorkspacePanelId panelId, ColumnSplitterKind kind)
        {
            return (panelId, kind) switch
            {
                (WorkspacePanelId.Secondary, ColumnSplitterKind.Name) => SecondaryNameColumnWidth.Value,
                (WorkspacePanelId.Secondary, ColumnSplitterKind.Type) => SecondaryTypeColumnWidth.Value,
                (WorkspacePanelId.Secondary, ColumnSplitterKind.Size) => SecondarySizeColumnWidth.Value,
                (WorkspacePanelId.Secondary, ColumnSplitterKind.Modified) => SecondaryModifiedColumnWidth.Value,
                (_, ColumnSplitterKind.Name) => NameColumnWidth.Value,
                (_, ColumnSplitterKind.Type) => TypeColumnWidth.Value,
                (_, ColumnSplitterKind.Size) => SizeColumnWidth.Value,
                _ => ModifiedColumnWidth.Value
            };
        }

        private double GetDetailsContentWidth(WorkspacePanelId panelId)
        {
            return panelId == WorkspacePanelId.Secondary
                ? SecondaryDetailsContentWidth
                : DetailsContentWidth;
        }

        private void SetPanelColumnWidths(
            WorkspacePanelId panelId,
            double name,
            double type,
            double size,
            double modified,
            double content)
        {
            if (panelId == WorkspacePanelId.Secondary)
            {
                SecondaryNameColumnWidth = new GridLength(name);
                SecondaryTypeColumnWidth = new GridLength(type);
                SecondarySizeColumnWidth = new GridLength(size);
                SecondaryModifiedColumnWidth = new GridLength(modified);
                SecondaryDetailsContentWidth = content;
                return;
            }

            NameColumnWidth = new GridLength(name);
            TypeColumnWidth = new GridLength(type);
            SizeColumnWidth = new GridLength(size);
            ModifiedColumnWidth = new GridLength(modified);
            DetailsContentWidth = content;
        }

        private void EndActiveSplitterDrag(UIElement? splitter)
        {
            splitter?.ReleasePointerCaptures();
            _activeSplitterElement = null;
            _activeSplitterDragMode = SplitterDragMode.None;
            _activeColumnSplitterKind = null;
            _activeColumnSplitterPanelId = WorkspacePanelId.Primary;
            _activeColumnResizeState = null;
            _activePaneResizeState = null;
            _sidebarDragStartWidth = null;
            _splitterDragStartX = 0;
        }

        private void ResetColumnSplitterCursorState()
        {
            _splitterHoverCount = 0;
            _activeSplitterElement = null;
            _activeSplitterDragMode = SplitterDragMode.None;
            _activeColumnSplitterKind = null;
            _activeColumnSplitterPanelId = WorkspacePanelId.Primary;
            _activeColumnResizeState = null;
            _activePaneResizeState = null;
            _sidebarDragStartWidth = null;
        }

        private void PaneSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter || !_isDualPaneEnabled)
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double primaryWidth = PrimaryPaneHost?.ActualWidth ?? 0;
            double secondaryWidth = SecondaryPaneHost?.ActualWidth ?? 0;
            double totalWidth = primaryWidth + secondaryWidth;
            if (totalWidth <= ExplorerPaneMinWidth * 2)
            {
                return;
            }

            _activeSplitterElement = splitter;
            _activeSplitterDragMode = SplitterDragMode.Pane;
            _activePaneResizeState = new PaneResizeState(primaryWidth, secondaryWidth, totalWidth);
            _splitterDragStartX = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            splitter.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void PaneSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter
                || !ReferenceEquals(splitter, _activeSplitterElement)
                || _activeSplitterDragMode != SplitterDragMode.Pane
                || _activePaneResizeState is not PaneResizeState state)
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double x = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            double delta = x - _splitterDragStartX;
            if (Math.Abs(delta) < 0.5)
            {
                return;
            }

            double minPaneWidth = Math.Min(ExplorerPaneMinWidth, Math.Max(120, state.TotalWidth / 2 - 1));
            double primaryWidth = Math.Clamp(state.PrimaryWidth + delta, minPaneWidth, state.TotalWidth - minPaneWidth);
            double secondaryWidth = state.TotalWidth - primaryWidth;
            CurrentWorkspaceShellState.SetPaneWidthWeights(primaryWidth, secondaryWidth);
            ApplyExplorerPaneLayout();
            RaisePropertyChanged(
                nameof(ExplorerPanePrimaryColumnWidth),
                nameof(ExplorerPaneSecondaryColumnWidth));
            e.Handled = true;
        }

        private void PaneSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitterDragMode != SplitterDragMode.Pane)
            {
                return;
            }

            EndActiveSplitterDrag(sender as UIElement);
            UpdateVisibleBreadcrumbs(WorkspacePanelId.Primary);
            UpdateVisibleBreadcrumbs(WorkspacePanelId.Secondary);
            e.Handled = true;
        }

        private void SidebarSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter)
            {
                return;
            }

            _activeSplitterElement = splitter;
            _activeSplitterDragMode = SplitterDragMode.Sidebar;
            _splitterDragStartX = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            _sidebarDragStartWidth = SidebarColumnWidth.Value;
            splitter.CapturePointer(e.Pointer);
            e.Handled = true;
        }

        private void SidebarSplitter_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter
                || !ReferenceEquals(splitter, _activeSplitterElement)
                || _activeSplitterDragMode != SplitterDragMode.Sidebar
                || _sidebarDragStartWidth is not double startWidth)
            {
                return;
            }

            if (!e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
            {
                return;
            }

            double x = e.GetCurrentPoint(this.Content as UIElement).Position.X;
            double delta = x - _splitterDragStartX;
            double requestedWidth = startWidth + delta;
            ApplySidebarWidthLayout(requestedWidth, fromUserDrag: true);
            e.Handled = true;
        }

        private void SidebarSplitter_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitterDragMode != SplitterDragMode.Sidebar)
            {
                return;
            }

            EndActiveSplitterDrag(sender as UIElement);
            e.Handled = true;
        }

        private void ApplySidebarWidthLayout(double? requestedWidth = null, bool fromUserDrag = false)
        {
            if (WindowRootGrid is null)
            {
                return;
            }

            double bodyWidth = WindowRootGrid.ActualWidth - WindowRootGrid.Padding.Left - WindowRootGrid.Padding.Right;
            if (bodyWidth <= 0)
            {
                SidebarColumnWidth = new GridLength((_sidebarPinnedCompact || _isSidebarCompact) ? SidebarCompactWidth : _sidebarPreferredExpandedWidth);
                return;
            }

            double maxSidebarWidth = Math.Max(SidebarCompactWidth, bodyWidth - SidebarSplitterWidth - SidebarMinContentWidth);
            if (fromUserDrag)
            {
                double dragWidth = requestedWidth ?? _sidebarPreferredExpandedWidth;
                if (dragWidth <= SidebarCompactThreshold)
                {
                    _sidebarPinnedCompact = true;
                }
                else
                {
                    _sidebarPinnedCompact = false;
                    _sidebarPreferredExpandedWidth = Math.Max(dragWidth, SidebarExpandedMinWidth);
                }
            }

            bool forcedCompact = _isSidebarCompact
                ? maxSidebarWidth <= SidebarCompactExitThreshold
                : maxSidebarWidth <= SidebarCompactThreshold;
            bool compact = forcedCompact || _sidebarPinnedCompact;
            double clampedWidth = Math.Min(_sidebarPreferredExpandedWidth, maxSidebarWidth);
            double finalWidth = compact
                ? SidebarCompactWidth
                : Math.Max(clampedWidth, SidebarExpandedMinWidth);

            SidebarColumnWidth = new GridLength(finalWidth);
            ApplySidebarCompactState(compact);
        }

        private void ApplySidebarCompactState(bool compact)
        {
            if (_isSidebarCompact == compact)
            {
                StyledSidebarView.Margin = compact ? new Thickness(0, 0, 8, 0) : new Thickness(0);
                StyledSidebarView.SetCompact(compact);
                return;
            }

            _isSidebarCompact = compact;
            StyledSidebarView.Margin = compact ? new Thickness(0, 0, 8, 0) : new Thickness(0);
            StyledSidebarView.SetCompact(compact);
            RaisePropertyChanged(
                nameof(ShellTitleBarLeftInsetWidth),
                nameof(SidebarTopChromeMargin),
                nameof(SidebarTopSettingsVisibility),
                nameof(TitleBarSidebarSettingsVisibility),
                nameof(SidebarCollapseButtonToolTipText));
        }

        private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
        {
            bool widthChanged = double.IsNaN(_lastWindowWidth) || Math.Abs(args.Size.Width - _lastWindowWidth) > 0.5;
            _lastWindowWidth = args.Size.Width;
            _lastWindowHeight = args.Size.Height;

            if (widthChanged)
            {
                ApplySidebarWidthLayout();
            }

            if (GetPanelViewMode(WorkspacePanelId.Primary) == EntryViewMode.Details)
            {
                UpdateEstimatedItemHeight();
                RequestViewportWork();
            }
            if (UsesColumnsListPresentation())
            {
                long now = Environment.TickCount64;
                const int liveRefreshIntervalMs = 240;
                if (now - GetPanelLastGroupedColumnsLiveResizeRefreshTick(WorkspacePanelId.Primary) >= liveRefreshIntervalMs)
                {
                    SetPanelLastGroupedColumnsLiveResizeRefreshTick(WorkspacePanelId.Primary, now);
                    RequestGroupedColumnsRefresh(force: false);
                }

                RequestGroupedColumnsRefreshDebounced(delayMs: 90, force: true);
            }
            if (widthChanged)
            {
                UpdateVisibleBreadcrumbs(WorkspacePanelId.Primary);
                if (_isDualPaneEnabled)
                {
                    UpdateVisibleBreadcrumbs(WorkspacePanelId.Secondary);
                }
            }
            UpdateRenameOverlayPosition();
            TryResetSystemCursorToArrow();
            if (widthChanged)
            {
                RaisePropertyChanged(
                    nameof(PrimaryPaneSearchBoxWidth),
                    nameof(PrimaryPaneSearchVisibility),
                    nameof(EntriesHorizontalScrollBarVisibility),
                    nameof(EntriesHorizontalScrollMode),
                    nameof(ToolbarSearchWidth));
            }
        }

        private static void TryResetSystemCursorToArrow()
        {
            IntPtr cursor = NativeMethods.LoadCursor(IntPtr.Zero, IDC_ARROW);
            if (cursor != IntPtr.Zero)
            {
                NativeMethods.SetCursor(cursor);
            }
        }

        private void RootElement_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdateSelectionActivityState();
        }

        private void RootElement_PointerEnteredOrMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_activeSplitterElement is null)
            {
                TryResetSystemCursorToArrow();
            }
        }

        private void TitleBarDragRegion_Loaded(object sender, RoutedEventArgs e)
        {
            QueueTitleBarDragRectangleRefresh();
        }

        private void TitleBarDragRegion_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            QueueTitleBarDragRectangleRefresh();
        }

        private void QueueTitleBarDragRectangleRefresh()
        {
            RefreshTitleBarDragRectangles();
            _ = DispatcherQueue.TryEnqueue(() => RefreshTitleBarDragRectangles());
        }

        private void RefreshTitleBarDragRectangles()
        {
            if (AppWindow is null || WindowRootGrid is null)
            {
                return;
            }

            var rectangles = new List<RectInt32>(3);
            if (_shellMode == ShellMode.Settings)
            {
                TryAddTitleBarDragRectangle(rectangles, SettingsTitleBarDragRegion);
            }
            else
            {
                TryAddTitleBarDragRectangle(rectangles, LeftTitleBarDragRegion);
                TryAddTitleBarDragRectangle(rectangles, WindowTabDragRegion);
            }

            try
            {
                AppWindow.TitleBar.SetDragRectangles(rectangles.ToArray());
            }
            catch
            {
                // Non-fatal: window remains usable even if the platform rejects a transient zero-sized rectangle.
            }
        }

        private void TryAddTitleBarDragRectangle(List<RectInt32> rectangles, FrameworkElement? element)
        {
            if (element is null ||
                element.Visibility != Visibility.Visible ||
                element.ActualWidth <= 1 ||
                element.ActualHeight <= 1 ||
                element.XamlRoot is null)
            {
                return;
            }

            try
            {
                Windows.Foundation.Rect bounds = element
                    .TransformToVisual(WindowRootGrid)
                    .TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
                double scale = element.XamlRoot.RasterizationScale;
                int x = (int)Math.Round(bounds.X * scale);
                int y = (int)Math.Round(bounds.Y * scale);
                int width = (int)Math.Round(bounds.Width * scale);
                int height = (int)Math.Round(bounds.Height * scale);

                if (width <= 1 || height <= 1)
                {
                    return;
                }

                rectangles.Add(new RectInt32(x, y, width, height));
            }
            catch
            {
            }
        }

        private void MainWindowRoot_ActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyTitleBarTheme();
            SyncSidebarTreeRenameOverlayTheme();
            _workspaceChromeCoordinator.RefreshTabVisuals();
            foreach (EntryViewModel entry in PrimaryEntries)
            {
                entry.RefreshThemeDependentBrushes();
            }
            RaiseWorkspacePanelShellPropertiesChanged();
            UpdateDetailsHeaders();
            UpdatePanelDetailsHeaders(WorkspacePanelId.Secondary);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeToggleGlyph)));
        }

        private static void AppendWindowSizeLog(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
            lock (s_windowSizeLogLock)
            {
                File.AppendAllText(s_windowSizeLogPath, line, System.Text.Encoding.UTF8);
            }
        }

        private void TraceWindowSize(string node, string detail)
        {
            string liveSize = AppWindow is null
                ? "null"
                : $"{AppWindow.Size.Width}x{AppWindow.Size.Height}";
            string message =
                $"[WINDOW-SIZE] node={node} detail={detail} settings={_appSettings.WindowWidth}x{_appSettings.WindowHeight} live={liveSize}";
            Debug.WriteLine(message);
            AppendWindowSizeLog(message);
        }

        private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            bool isFirstActivation = !_hasSeenFirstActivation && args.WindowActivationState != WindowActivationState.Deactivated;
            if (isFirstActivation)
            {
                _hasSeenFirstActivation = true;
                TraceWindowSize(
                    "首次激活应用",
                    $"state={args.WindowActivationState} handleReady={_windowHandle != IntPtr.Zero}");
            }

            if (_windowHandle == IntPtr.Zero)
            {
                InstallWindowHook();
            }

            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                TryApplyPendingWindowSizeRestore();
            }

            _selectionSurfaceCoordinator.SetWindowActive(args.WindowActivationState != WindowActivationState.Deactivated);
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                await _inlineEditCoordinator.CommitActiveSessionAsync();
            }

            UpdateSelectionActivityState();
            TryResetSystemCursorToArrow();
        }

        private void ApplyTitleBarTheme()
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            if (Content is not FrameworkElement rootElement)
            {
                AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
                return;
            }

            AppWindow.TitleBar.PreferredTheme = rootElement.ActualTheme switch
            {
                ElementTheme.Light => TitleBarTheme.Light,
                ElementTheme.Dark => TitleBarTheme.Dark,
                _ => TitleBarTheme.UseDefaultAppMode
            };
        }

    }
}
