using System;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using WinRT;
using WinRT.Interop;

namespace FileExplorerUI
{
    internal static partial class NativeMethods
    {
        internal const uint OAIF_EXEC = 0x00000004;
        internal const uint OAIF_HIDE_REGISTRATION = 0x00000020;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct OpenAsInfo
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            internal string FilePath;

            [MarshalAs(UnmanagedType.LPWStr)]
            internal string? ClassName;

            internal uint Flags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct NOTIFYICONDATA
        {
            internal uint cbSize;
            internal IntPtr hWnd;
            internal uint uID;
            internal uint uFlags;
            internal uint uCallbackMessage;
            internal IntPtr hIcon;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            internal string szTip;

            internal uint dwState;
            internal uint dwStateMask;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            internal string szInfo;

            internal uint uTimeoutOrVersion;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            internal string szInfoTitle;

            internal uint dwInfoFlags;
            internal Guid guidItem;
            internal IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            internal int X;
            internal int Y;
        }

        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow(IntPtr appWindow, in Guid riid);
            void ShowShareUIForWindow(IntPtr appWindow);
        }

        [LibraryImport("user32.dll", EntryPoint = "LoadCursorW", SetLastError = true)]
        internal static partial IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [LibraryImport("user32.dll", EntryPoint = "LoadIconW", SetLastError = true)]
        internal static partial IntPtr LoadIcon(IntPtr hInstance, int lpIconName);

        [LibraryImport("user32.dll", EntryPoint = "SetCursor", SetLastError = true)]
        internal static partial IntPtr SetCursor(IntPtr hCursor);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        internal static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
        internal static partial IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetForegroundWindow(IntPtr hWnd);

        [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu", SetLastError = true)]
        internal static partial IntPtr CreatePopupMenu();

        [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool AppendMenu(IntPtr hMenu, uint uFlags, uint uIDNewItem, string lpNewItem);

        [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenu", SetLastError = true)]
        internal static partial uint TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [LibraryImport("user32.dll", EntryPoint = "DestroyMenu", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyMenu(IntPtr hMenu);

        [LibraryImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool GetCursorPos(out POINT point);

        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern int SHOpenWithDialog(IntPtr hwndParent, ref OpenAsInfo openAsInfo);

        internal static DataTransferManager GetDataTransferManagerForWindow(IntPtr hwnd)
        {
            const string runtimeClassName = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            Guid iid = new("A5CAEE9B-8708-49D1-8D36-67D25A8DA00C");
            using var factory = ActivationFactory.Get(runtimeClassName);
            var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
            IntPtr result = interop.GetForWindow(hwnd, iid);
            return MarshalInterface<DataTransferManager>.FromAbi(result);
        }

        internal static void ShowShareUIForWindow(IntPtr hwnd)
        {
            const string runtimeClassName = "Windows.ApplicationModel.DataTransfer.DataTransferManager";
            using var factory = ActivationFactory.Get(runtimeClassName);
            var interop = (IDataTransferManagerInterop)Marshal.GetObjectForIUnknown(factory.ThisPtr);
            interop.ShowShareUIForWindow(hwnd);
        }
    }
}
