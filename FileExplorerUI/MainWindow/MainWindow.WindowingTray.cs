using Microsoft.Win32;
using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace FileExplorerUI
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
            if (width < MinPersistedWindowWidth || height < MinPersistedWindowHeight)
            {
                TraceWindowSize("启动恢复", $"skip-invalid width={width} height={height}");
                return;
            }

            _lastRestoredWindowWidth = width;
            _lastRestoredWindowHeight = height;
            _windowSizeRestorePending = true;
            TraceWindowSize("启动恢复", $"scheduled width={width} height={height}");
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
                TraceWindowSize("启动恢复", $"apply width={_lastRestoredWindowWidth} height={_lastRestoredWindowHeight}");
                AppWindow.Resize(new SizeInt32(_lastRestoredWindowWidth, _lastRestoredWindowHeight));
                SizeInt32 appliedSize = AppWindow.Size;
                TraceWindowSize("启动恢复", $"applied-result width={appliedSize.Width} height={appliedSize.Height}");
            }
            catch (Exception ex)
            {
                _windowSizeRestorePending = true;
                TraceWindowSize("启动恢复", $"apply-failed type={ex.GetType().Name} message=\"{ex.Message}\"");
            }
        }

        private void PersistCurrentWindowSize()
        {
            if (AppWindow is null)
            {
                TraceWindowSize("保存到设置", "skip-appwindow-null");
                return;
            }

            SizeInt32 liveSize = AppWindow.Size;
            if (liveSize.Width < MinPersistedWindowWidth || liveSize.Height < MinPersistedWindowHeight)
            {
                TraceWindowSize(
                    "保存到设置",
                    $"skip-live-too-small width={liveSize.Width} height={liveSize.Height}");
                return;
            }

            if (_appSettings.WindowWidth == liveSize.Width && _appSettings.WindowHeight == liveSize.Height)
            {
                TraceWindowSize(
                    "保存到设置",
                    $"skip-unchanged width={liveSize.Width} height={liveSize.Height}");
                return;
            }

            _appSettings.WindowWidth = liveSize.Width;
            _appSettings.WindowHeight = liveSize.Height;
            TraceWindowSize(
                "保存到设置",
                $"save-request width={_appSettings.WindowWidth} height={_appSettings.WindowHeight}");
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

            PersistCurrentWindowSize();
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
            PersistCurrentWindowSize();
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
