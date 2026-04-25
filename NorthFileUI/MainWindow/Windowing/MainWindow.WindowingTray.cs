using Microsoft.Win32;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace NorthFileUI
{
    public sealed partial class MainWindow
    {
        private void InstallWindowHook()
        {
            try
            {
                _windowHandle = WindowNative.GetWindowHandle(this);
                if (_windowHandle == IntPtr.Zero)
                {
                    return;
                }

                _wndProcDelegate = WindowProc;
                IntPtr newProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
                _originalWndProc = NativeMethods.SetWindowLongPtr(_windowHandle, GWL_WNDPROC, newProc);
            }
            catch
            {
                // Non-fatal: flyout still supports normal light-dismiss behavior.
            }
        }

        private void EnsureAutoStartRegistration(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(AutoStartRegistryPath, writable: true);
                if (key is null)
                {
                    return;
                }

                if (!enabled)
                {
                    key.DeleteValue(AutoStartRegistryValueName, throwOnMissingValue: false);
                    return;
                }

                string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    return;
                }

                key.SetValue(AutoStartRegistryValueName, $"\"{exePath}\"", RegistryValueKind.String);
            }
            catch
            {
            }
        }

        private void RestoreWindowSizeFromSettings()
        {
            int width = _appSettings.WindowWidth;
            int height = _appSettings.WindowHeight;
            bool hasValidSize = width >= MinPersistedWindowWidth && height >= MinPersistedWindowHeight;
            bool hasValidPosition = _appSettings.WindowPosX != UnsetWindowPosition &&
                _appSettings.WindowPosY != UnsetWindowPosition;

            if (!hasValidSize && !_appSettings.WindowMaximized)
            {
                TraceWindowSize(
                    "启动恢复",
                    $"skip-invalid width={width} height={height} x={_appSettings.WindowPosX} y={_appSettings.WindowPosY} maximized={_appSettings.WindowMaximized}");
                return;
            }

            _lastRestoredWindowWidth = width;
            _lastRestoredWindowHeight = height;
            _lastRestoredWindowPosX = hasValidPosition ? _appSettings.WindowPosX : UnsetWindowPosition;
            _lastRestoredWindowPosY = hasValidPosition ? _appSettings.WindowPosY : UnsetWindowPosition;
            _lastRestoredWindowMaximized = _appSettings.WindowMaximized;
            _windowSizeRestorePending = true;
            TraceWindowSize(
                "启动恢复",
                $"scheduled width={width} height={height} x={_lastRestoredWindowPosX} y={_lastRestoredWindowPosY} maximized={_lastRestoredWindowMaximized}");
        }

        private void TryApplyPendingWindowSizeRestore()
        {
            if (!_windowSizeRestorePending || AppWindow is null)
            {
                if (_windowSizeRestorePending)
                {
                    TraceWindowSize("启动恢复", "pending-but-appwindow-null");
                }
                return;
            }

            _windowSizeRestorePending = false;
            try
            {
                TraceWindowSize(
                    "启动恢复",
                    $"apply width={_lastRestoredWindowWidth} height={_lastRestoredWindowHeight} x={_lastRestoredWindowPosX} y={_lastRestoredWindowPosY} maximized={_lastRestoredWindowMaximized}");
                ApplyRestoredWindowBounds();
                ApplyRestoredWindowPresenterState();
                SizeInt32 appliedSize = AppWindow.Size;
                PointInt32 appliedPosition = AppWindow.Position;
                TraceWindowSize(
                    "启动恢复",
                    $"applied-result width={appliedSize.Width} height={appliedSize.Height} x={appliedPosition.X} y={appliedPosition.Y} maximized={IsWindowMaximized()}");
            }
            catch (Exception ex)
            {
                _windowSizeRestorePending = true;
                TraceWindowSize("启动恢复", $"apply-failed type={ex.GetType().Name} message=\"{ex.Message}\"");
            }
        }

        private void ApplyRestoredWindowBounds()
        {
            bool hasValidSize = _lastRestoredWindowWidth >= MinPersistedWindowWidth &&
                _lastRestoredWindowHeight >= MinPersistedWindowHeight;
            bool hasValidPosition = _lastRestoredWindowPosX != UnsetWindowPosition &&
                _lastRestoredWindowPosY != UnsetWindowPosition;

            if (hasValidSize && hasValidPosition)
            {
                AppWindow!.MoveAndResize(new RectInt32(
                    _lastRestoredWindowPosX,
                    _lastRestoredWindowPosY,
                    _lastRestoredWindowWidth,
                    _lastRestoredWindowHeight));
                return;
            }

            if (hasValidSize)
            {
                AppWindow!.Resize(new SizeInt32(_lastRestoredWindowWidth, _lastRestoredWindowHeight));
                return;
            }

            if (hasValidPosition)
            {
                AppWindow!.Move(new PointInt32(_lastRestoredWindowPosX, _lastRestoredWindowPosY));
            }
        }

        private void ApplyRestoredWindowPresenterState()
        {
            if (!_lastRestoredWindowMaximized ||
                AppWindow?.Presenter is not OverlappedPresenter presenter ||
                presenter.State == OverlappedPresenterState.Maximized)
            {
                return;
            }

            presenter.Maximize();
        }

        private bool IsWindowMaximized()
        {
            return AppWindow?.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Maximized;
        }

        private void PersistCurrentWindowPlacement()
        {
            if (AppWindow is null)
            {
                TraceWindowSize("保存到设置", "skip-appwindow-null");
                return;
            }

            SizeInt32 liveSize = AppWindow.Size;
            PointInt32 livePosition = AppWindow.Position;
            bool isMaximized = IsWindowMaximized();
            bool hasValidSize = liveSize.Width >= MinPersistedWindowWidth &&
                liveSize.Height >= MinPersistedWindowHeight;

            if (!hasValidSize && !isMaximized)
            {
                TraceWindowSize(
                    "保存到设置",
                    $"skip-live-too-small width={liveSize.Width} height={liveSize.Height} x={livePosition.X} y={livePosition.Y} maximized={isMaximized}");
                return;
            }

            bool changed = _appSettings.WindowMaximized != isMaximized;
            if (!isMaximized && hasValidSize)
            {
                changed =
                    changed ||
                    _appSettings.WindowWidth != liveSize.Width ||
                    _appSettings.WindowHeight != liveSize.Height ||
                    _appSettings.WindowPosX != livePosition.X ||
                    _appSettings.WindowPosY != livePosition.Y;
            }

            if (!changed)
            {
                TraceWindowSize(
                    "保存到设置",
                    $"skip-unchanged width={liveSize.Width} height={liveSize.Height} x={livePosition.X} y={livePosition.Y} maximized={isMaximized}");
                return;
            }

            if (!isMaximized && hasValidSize)
            {
                _appSettings.WindowWidth = liveSize.Width;
                _appSettings.WindowHeight = liveSize.Height;
                _appSettings.WindowPosX = livePosition.X;
                _appSettings.WindowPosY = livePosition.Y;
            }

            _appSettings.WindowMaximized = isMaximized;
            TraceWindowSize(
                "保存到设置",
                $"save-request width={_appSettings.WindowWidth} height={_appSettings.WindowHeight} x={_appSettings.WindowPosX} y={_appSettings.WindowPosY} maximized={_appSettings.WindowMaximized}");
            _appSettingsService.Save(_appSettings);
        }

        private void UpdateTrayIcon()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (!_appSettings.MinimizeToTrayEnabled)
            {
                RemoveTrayIcon();
                return;
            }

            if (!_isHiddenToTray)
            {
                return;
            }

            var data = CreateTrayIconData();
            NativeMethods.Shell_NotifyIcon(_trayIconAdded ? NIM_MODIFY : NIM_ADD, ref data);
            _trayIconAdded = true;
        }

        private void RemoveTrayIcon()
        {
            if (!_trayIconAdded || _windowHandle == IntPtr.Zero)
            {
                return;
            }

            var data = CreateTrayIconData();
            NativeMethods.Shell_NotifyIcon(NIM_DELETE, ref data);
            _trayIconAdded = false;
        }

        private NativeMethods.NOTIFYICONDATA CreateTrayIconData()
        {
            return new NativeMethods.NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _windowHandle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = TrayCallbackMessage,
                hIcon = NativeMethods.LoadIcon(IntPtr.Zero, IDI_APPLICATION),
                szTip = "NorthFile"
            };
        }

        private void HideToTray()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            PersistCurrentWindowPlacement();
            _isHiddenToTray = true;
            UpdateTrayIcon();
            NativeMethods.ShowWindow(_windowHandle, SW_HIDE);
        }

        private void RestoreFromTray()
        {
            if (_windowHandle == IntPtr.Zero)
            {
                return;
            }

            _isHiddenToTray = false;
            NativeMethods.ShowWindow(_windowHandle, SW_RESTORE);
            Activate();
            RemoveTrayIcon();
        }

        private void ShowTrayContextMenu()
        {
            IntPtr menu = NativeMethods.CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return;
            }

            try
            {
                NativeMethods.AppendMenu(menu, MF_STRING, TrayOpenCommandId, S("CommonOpen"));
                NativeMethods.AppendMenu(menu, MF_STRING, TrayExitCommandId, S("TrayMenuExit"));
                NativeMethods.GetCursorPos(out NativeMethods.POINT point);
                NativeMethods.SetForegroundWindow(_windowHandle);
                uint command = NativeMethods.TrackPopupMenu(menu, TPM_RETURNCMD | TPM_NONOTIFY, point.X, point.Y, 0, _windowHandle, IntPtr.Zero);
                if (command == TrayOpenCommandId)
                {
                    RestoreFromTray();
                }
                else if (command == TrayExitCommandId)
                {
                    _allowActualClose = true;
                    RemoveTrayIcon();
                    Close();
                }
            }
            finally
            {
                NativeMethods.DestroyMenu(menu);
            }
        }

        private void MainWindow_Closed(object sender, WindowEventArgs args)
        {
            PersistLastWorkspaceSession();
            PersistCurrentWindowPlacement();
            RemoveTrayIcon();
            DisposeFavoriteWatchers();
        }

        private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (_appSettings.MinimizeToTrayEnabled && msg == WM_CLOSE && !_allowActualClose)
            {
                _ = DispatcherQueue.TryEnqueue(HideToTray);
                return IntPtr.Zero;
            }

            if (msg == TrayCallbackMessage)
            {
                int notification = unchecked((int)lParam.ToInt64());
                if (notification == WM_LBUTTONDBLCLK)
                {
                    _ = DispatcherQueue.TryEnqueue(RestoreFromTray);
                    return IntPtr.Zero;
                }

                if (notification == WM_RBUTTONUP)
                {
                    _ = DispatcherQueue.TryEnqueue(ShowTrayContextMenu);
                    return IntPtr.Zero;
                }
            }

            if (msg == WM_NCLBUTTONDOWN || msg == WM_NCRBUTTONDOWN)
            {
                _ = DispatcherQueue.TryEnqueue(CloseActiveBreadcrumbFlyout);
            }

            return NativeMethods.CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }
    }
}
