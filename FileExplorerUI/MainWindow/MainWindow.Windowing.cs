using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Win32;
using System;
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

        private void ColumnSplitter_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not UIElement splitter || !TryGetColumnSplitterKind(splitter, out ColumnSplitterKind kind))
            {
                return;
            }

            _activeSplitterElement = splitter;
            _activeSplitterDragMode = SplitterDragMode.Column;
            _activeColumnSplitterKind = kind;
            _activeColumnResizeState = new ColumnResizeState(
                NameColumnWidth.Value,
                TypeColumnWidth.Value,
                SizeColumnWidth.Value,
                ModifiedColumnWidth.Value,
                DetailsContentWidth);
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

            NameColumnWidth = new GridLength(name);
            TypeColumnWidth = new GridLength(type);
            SizeColumnWidth = new GridLength(size);
            ModifiedColumnWidth = new GridLength(modified);
            DetailsContentWidth = content;
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
                || element.Tag is not string tagText
                || !int.TryParse(tagText, out int tagValue)
                || !Enum.IsDefined(typeof(ColumnSplitterKind), tagValue))
            {
                return false;
            }

            kind = (ColumnSplitterKind)tagValue;
            return true;
        }

        private void EndActiveSplitterDrag(UIElement? splitter)
        {
            splitter?.ReleasePointerCaptures();
            _activeSplitterElement = null;
            _activeSplitterDragMode = SplitterDragMode.None;
            _activeColumnSplitterKind = null;
            _activeColumnResizeState = null;
            _sidebarDragStartWidth = null;
            _splitterDragStartX = 0;
        }

        private void ResetColumnSplitterCursorState()
        {
            _splitterHoverCount = 0;
            _activeSplitterElement = null;
            _activeSplitterDragMode = SplitterDragMode.None;
            _activeColumnSplitterKind = null;
            _activeColumnResizeState = null;
            _sidebarDragStartWidth = null;
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
            if (ExplorerShellGrid is null)
            {
                return;
            }

            double bodyWidth = ExplorerShellGrid.ActualWidth;
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
                StyledSidebarView.SetCompact(compact);
                return;
            }

            _isSidebarCompact = compact;
            StyledSidebarView.SetCompact(compact);
            if (SidebarNavView is not null)
            {
                SidebarNavView.PaneDisplayMode = compact ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
                SidebarNavView.IsPaneOpen = !compact;
                SidebarNavView.CompactPaneLength = SidebarCompactWidth;
                SidebarNavView.OpenPaneLength = compact ? SidebarCompactWidth : SidebarColumnWidth.Value;
            }
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

            if (_currentViewMode == EntryViewMode.Details)
            {
                UpdateEstimatedItemHeight();
                RequestViewportWork();
            }
            if (UsesColumnsListPresentation())
            {
                long now = Environment.TickCount64;
                const int liveRefreshIntervalMs = 240;
                if (now - _lastGroupedColumnsLiveResizeRefreshTick >= liveRefreshIntervalMs)
                {
                    _lastGroupedColumnsLiveResizeRefreshTick = now;
                    RequestGroupedColumnsRefresh(force: false);
                }

                RequestGroupedColumnsRefreshDebounced(delayMs: 90, force: true);
            }
            if (widthChanged)
            {
                UpdateVisibleBreadcrumbs();
            }
            UpdateRenameOverlayPosition();
            TryResetSystemCursorToArrow();
            if (widthChanged)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(EntriesHorizontalScrollBarVisibility)));
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(EntriesHorizontalScrollMode)));
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

        private void MainWindowRoot_ActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyTitleBarTheme();
            SyncSidebarTreeRenameOverlayTheme();
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
