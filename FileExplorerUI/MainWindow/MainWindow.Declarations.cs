using FileExplorerUI.Commands;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Windows.Foundation;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private sealed class NavigationPerfSession
        {
            private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
            private long _lastElapsedMs;

            public NavigationPerfSession(string targetPath, string trigger)
            {
                TargetPath = targetPath;
                Trigger = trigger;
                Id = Interlocked.Increment(ref s_navigationPerfSequence);
                Mark("session.start");
            }

            public int Id { get; }

            public string TargetPath { get; }

            public string Trigger { get; }

            public void Mark(string stage, string? detail = null)
            {
                long totalMs = _stopwatch.ElapsedMilliseconds;
                long deltaMs = totalMs - _lastElapsedMs;
                _lastElapsedMs = totalMs;

                string message = $"[NAV-PERF #{Id}] total={totalMs}ms delta={deltaMs}ms stage={stage} trigger={Trigger} path=\"{TargetPath}\"";
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    message += $" detail={detail}";
                }

                Debug.WriteLine(message);
                AppendNavigationPerfLog(message);
            }
        }

        private enum PresentationReloadReason
        {
            ViewModeSwitch,
            PresentationSettingsChange,
            DataRefresh
        }

        private enum ShellMode
        {
            Explorer,
            Settings
        }

        private enum EntriesContextOrigin
        {
            EntriesList,
            SidebarPinned,
            SidebarTree
        }

        private sealed record EntriesContextRequest(
            UIElement Anchor,
            Point Position,
            EntryViewModel? Entry,
            bool IsItemTarget,
            EntriesContextOrigin Origin = EntriesContextOrigin.EntriesList);

        private sealed record PendingEntriesContextCommand(
            string CommandId,
            FileCommandTarget Target,
            EntriesContextOrigin Origin);

        private enum CommandDockSide
        {
            Top,
            Right,
            Bottom
        }

        private enum SplitterDragMode
        {
            None,
            Column,
            Sidebar
        }

        private enum ColumnSplitterKind
        {
            Name = 1,
            Type = 2,
            Size = 3,
            Modified = 4
        }

        private readonly record struct ColumnResizeState(
            double Name,
            double Type,
            double Size,
            double Modified,
            double ContentWidth);

        private static int s_navigationPerfSequence;
        private static readonly object s_navigationPerfLogLock = new();
        private static readonly string s_navigationPerfLogPath = Path.Combine(
            AppContext.BaseDirectory,
            "navigation-perf.log");
        private static readonly object s_windowSizeLogLock = new();
        private static readonly string s_windowSizeLogPath = Path.Combine(
            AppContext.BaseDirectory,
            "window-size.log");
        private static int s_detailsViewportPerfSequence;
        private const int SettingsShellMinWindowWidth = 1024;

        private const int GWL_WNDPROC = -4;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCRBUTTONDOWN = 0x00A4;
        private const int WM_CLOSE = 0x0010;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_APP = 0x8000;
        private const int WM_SETCURSOR = 0x0020;
        private const int IDC_ARROW = 32512;
        private const int IDI_APPLICATION = 32512;
        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;
        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;
        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint MF_STRING = 0x00000000;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_NONOTIFY = 0x0080;
        private const uint TrayCallbackMessage = WM_APP + 1;
        private const uint TrayOpenCommandId = 1001;
        private const uint TrayExitCommandId = 1002;
        private const string AutoStartRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AutoStartRegistryValueName = "NorthFile";
        private const int MinPersistedWindowWidth = 800;
        private const int MinPersistedWindowHeight = 600;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private static readonly Brush FolderIconBrush = CreateBrush(0xC4, 0x93, 0x2A);
        private static readonly Brush FolderLinkIconBrush = CreateBrush(0xA7, 0x79, 0x1F);
        private static readonly Brush FileIconBrush = CreateBrush(0x6C, 0x72, 0x7D);
        private static readonly Brush FileLinkIconBrush = CreateBrush(0x5E, 0x79, 0xB9);
        private static readonly Brush TextIconBrush = CreateBrush(0x5B, 0x7F, 0xA3);
        private static readonly Brush ArchiveIconBrush = CreateBrush(0x8B, 0x6A, 0x3F);
        private static readonly Brush ImageIconBrush = CreateBrush(0xA4, 0x62, 0xB8);
        private static readonly Brush VideoIconBrush = CreateBrush(0xC6, 0x5C, 0x5C);
        private static readonly Brush AudioIconBrush = CreateBrush(0x3F, 0x93, 0x8D);
        private static readonly Brush PdfIconBrush = CreateBrush(0xC2, 0x4F, 0x4A);
        private static readonly Brush WordIconBrush = CreateBrush(0x4C, 0x74, 0xC9);
        private static readonly Brush ExcelIconBrush = CreateBrush(0x3E, 0x8A, 0x63);
        private static readonly Brush PowerPointIconBrush = CreateBrush(0xD0, 0x72, 0x44);
        private static readonly Brush CodeIconBrush = CreateBrush(0x6A, 0x66, 0xC7);
        private static readonly Brush ExecutableIconBrush = CreateBrush(0xB0, 0x5A, 0x7A);
        private static readonly Brush ShortcutIconBrush = CreateBrush(0x5E, 0x79, 0xB9);
        private static readonly Brush DiskImageIconBrush = CreateBrush(0x6B, 0x73, 0x88);
        private const double BreadcrumbOverflowButtonWidth = 34;
        private const double BreadcrumbItemSpacing = 2;
        private const double BreadcrumbWidthReserve = 4;
        private const string BreadcrumbMyComputerGlyph = "\uE7F4";

        private static string S(string key) => LocalizedStrings.Instance.Get(key);

        private static string SF(string key, params object[] args)
        {
            string? format = S(key);
            if (string.IsNullOrEmpty(format))
            {
                return key;
            }

            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;
            }
        }

        private void UpdateStatusKey(string key, params object[] args)
        {
            string message = SF(key, args);
            switch (GetStatusFeedbackKind(key))
            {
                case StatusFeedbackKind.DialogWarning:
                    ShowStatusDialog(message, warning: true);
                    return;
                case StatusFeedbackKind.DialogError:
                    ShowStatusDialog(message, warning: false);
                    return;
                case StatusFeedbackKind.None:
                    return;
                default:
                    UpdateStatus(message);
                    return;
            }
        }

        private static StatusFeedbackKind GetStatusFeedbackKind(string key) => key switch
        {
            "StatusCopyFailedSelectLoaded" => StatusFeedbackKind.DialogError,
            "StatusCopyFailedDriveRootsUnsupported" => StatusFeedbackKind.DialogError,
            "StatusCutFailedSelectLoaded" => StatusFeedbackKind.DialogError,
            "StatusCutFailedDriveRootsUnsupported" => StatusFeedbackKind.DialogError,
            "StatusDeleteCanceled" => StatusFeedbackKind.None,
            "StatusDeleteFailedSelectLoaded" => StatusFeedbackKind.DialogError,
            "StatusDeleteFailedInvalidIndex" => StatusFeedbackKind.DialogError,
            "StatusNewFailedOpenFolderFirst" => StatusFeedbackKind.DialogError,
            "StatusNoMoreEntries" => StatusFeedbackKind.DialogWarning,
            "StatusPasteFailedClipboardEmpty" => StatusFeedbackKind.DialogError,
            "StatusPasteFailedOpenFolderFirst" => StatusFeedbackKind.DialogError,
            "StatusPasteSkippedConflicts" => StatusFeedbackKind.DialogWarning,
            "StatusPasteSkippedNothingApplied" => StatusFeedbackKind.DialogWarning,
            "StatusPasteSkippedSamePath" => StatusFeedbackKind.DialogWarning,
            "StatusPathAccessDenied" => StatusFeedbackKind.DialogError,
            "StatusPathAccessDeniedSkip" => StatusFeedbackKind.DialogError,
            "StatusPathInvalidateWarning" => StatusFeedbackKind.DialogWarning,
            "StatusPathError" => StatusFeedbackKind.DialogError,
            "StatusPathRustError" => StatusFeedbackKind.DialogError,
            "StatusRenameFailedCouldNotStartInlineEditor" => StatusFeedbackKind.DialogError,
            "StatusRenameFailedInvalidIndex" => StatusFeedbackKind.DialogError,
            "StatusRenameFailedSelectLoaded" => StatusFeedbackKind.DialogError,
            "StatusRenameFailedSelectTreeNode" => StatusFeedbackKind.DialogError,
            "StatusRenameFailedTreeItemUnavailable" => StatusFeedbackKind.DialogError,
            "StatusRenameFailedTreeOverlayUnavailable" => StatusFeedbackKind.DialogError,
            "StatusRenameFailedTreeTextAnchorUnavailable" => StatusFeedbackKind.DialogError,
            "StatusCopyPathFailed" => StatusFeedbackKind.DialogError,
            "StatusCreateShortcutFailed" => StatusFeedbackKind.DialogError,
            "StatusOpenFailed" => StatusFeedbackKind.DialogError,
            "StatusLoadFailedWithReason" => StatusFeedbackKind.DialogError,
            "StatusOpenInNewWindowFailed" => StatusFeedbackKind.DialogError,
            "StatusOpenTargetFailed" => StatusFeedbackKind.DialogError,
            "StatusOpenTerminalFailed" => StatusFeedbackKind.DialogError,
            "StatusOpenWithFailed" => StatusFeedbackKind.DialogError,
            "StatusPropertiesFailed" => StatusFeedbackKind.DialogError,
            "StatusRunAsAdministratorFailed" => StatusFeedbackKind.DialogError,
            "StatusSidebarNavFailed" => StatusFeedbackKind.DialogError,
            "StatusSidebarNavIgnoredLoading" => StatusFeedbackKind.DialogWarning,
            "StatusSidebarTreeExpandFailed" => StatusFeedbackKind.DialogError,
            "StatusSidebarTreeNavFailed" => StatusFeedbackKind.DialogError,
            "StatusSidebarTreeNavIgnoredLoading" => StatusFeedbackKind.DialogWarning,
            "StatusAlreadyAtRoot" => StatusFeedbackKind.Info,
            "StatusCompressZipSuccess" => StatusFeedbackKind.Info,
            "StatusCopyPathReady" => StatusFeedbackKind.Info,
            "StatusCopyReady" => StatusFeedbackKind.Info,
            "StatusCreateShortcutSuccess" => StatusFeedbackKind.Info,
            "StatusCreateSuccess" => StatusFeedbackKind.Info,
            "StatusCutReady" => StatusFeedbackKind.Info,
            "StatusDeleteSuccess" => StatusFeedbackKind.Info,
            "StatusExtractZipSuccess" => StatusFeedbackKind.Info,
            "StatusOpened" => StatusFeedbackKind.Info,
            "StatusOpenedInNewWindow" => StatusFeedbackKind.Info,
            "StatusOpenTerminalSuccess" => StatusFeedbackKind.Info,
            "StatusOpenWithOpened" => StatusFeedbackKind.Info,
            "StatusPropertiesOpened" => StatusFeedbackKind.Info,
            "StatusRenameSuccess" => StatusFeedbackKind.Info,
            "StatusRunAsAdministratorStarted" => StatusFeedbackKind.Info,
            "StatusSettingsExported" => StatusFeedbackKind.Info,
            "StatusSettingsImported" => StatusFeedbackKind.Info,
            "StatusShareOpened" => StatusFeedbackKind.Info,
            "StatusTransferSuccess" => StatusFeedbackKind.Info,
            _ when key.Contains("Skipped", StringComparison.OrdinalIgnoreCase) => StatusFeedbackKind.DialogWarning,
            _ when key.Contains("Warning", StringComparison.OrdinalIgnoreCase) => StatusFeedbackKind.DialogWarning,
            _ when key.Contains("Failed", StringComparison.OrdinalIgnoreCase) => StatusFeedbackKind.DialogError,
            _ when key.Contains("Error", StringComparison.OrdinalIgnoreCase) => StatusFeedbackKind.DialogError,
            _ => StatusFeedbackKind.Info
        };

        private enum StatusFeedbackKind
        {
            None,
            Info,
            DialogWarning,
            DialogError
        }

        private static string CreateKindLabel(bool isDirectory) => S(isDirectory ? "CreateKindFolder" : "CreateKindFile");
    }
}
