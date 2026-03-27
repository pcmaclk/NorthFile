using System;
using System.Globalization;
using System.IO;
using Microsoft.UI.Xaml.Media;

namespace FileExplorerUI
{
    public sealed partial class MainWindow
    {
        private static string GetEntryTypeText(string name, bool isDirectory, bool isLink)
        {
            if (isDirectory)
            {
                return S(isLink ? "FileTypeFolderLink" : "FileTypeFolder");
            }

            if (isLink)
            {
                return S("FileTypeFileLink");
            }

            return GetFileTypeText(name);
        }

        private static string GetEntryIconGlyph(bool isDirectory, bool isLink, string? name = null)
        {
            EntryIconKind kind = GetEntryIconKind(name, isDirectory, isLink);
            return kind switch
            {
                EntryIconKind.Folder => "\uE8B7",
                EntryIconKind.FolderLink => "\uE8F0",
                EntryIconKind.File => "\uE8A5",
                EntryIconKind.FileLink => "\uE71B",
                EntryIconKind.Text => "\uF000",
                EntryIconKind.Archive => "\uF012",
                EntryIconKind.Image => "\uEB9F",
                EntryIconKind.Video => "\uEC0D",
                EntryIconKind.Audio => "\uEC4F",
                EntryIconKind.Pdf => "\uEA90",
                EntryIconKind.Word => "\uF1C2",
                EntryIconKind.Excel => "\uF1C3",
                EntryIconKind.PowerPoint => "\uF1C4",
                EntryIconKind.Code => "\uE943",
                EntryIconKind.Executable => "\uE756",
                EntryIconKind.Shortcut => "\uE71B",
                EntryIconKind.DiskImage => "\uE7F8",
                _ => "\uE8A5"
            };
        }

        private static Brush GetEntryIconBrush(bool isDirectory, bool isLink, string? name = null)
        {
            EntryIconKind kind = GetEntryIconKind(name, isDirectory, isLink);
            return kind switch
            {
                EntryIconKind.Folder => FolderIconBrush,
                EntryIconKind.FolderLink => FolderLinkIconBrush,
                EntryIconKind.File => FileIconBrush,
                EntryIconKind.FileLink => FileLinkIconBrush,
                EntryIconKind.Text => TextIconBrush,
                EntryIconKind.Archive => ArchiveIconBrush,
                EntryIconKind.Image => ImageIconBrush,
                EntryIconKind.Video => VideoIconBrush,
                EntryIconKind.Audio => AudioIconBrush,
                EntryIconKind.Pdf => PdfIconBrush,
                EntryIconKind.Word => WordIconBrush,
                EntryIconKind.Excel => ExcelIconBrush,
                EntryIconKind.PowerPoint => PowerPointIconBrush,
                EntryIconKind.Code => CodeIconBrush,
                EntryIconKind.Executable => ExecutableIconBrush,
                EntryIconKind.Shortcut => ShortcutIconBrush,
                EntryIconKind.DiskImage => DiskImageIconBrush,
                _ => FileIconBrush
            };
        }

        private static Brush CreateBrush(byte r, byte g, byte b) =>
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, r, g, b));

        private static EntryIconKind GetEntryIconKind(string? name, bool isDirectory, bool isLink)
        {
            if (isDirectory)
            {
                return isLink ? EntryIconKind.FolderLink : EntryIconKind.Folder;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return isLink ? EntryIconKind.FileLink : EntryIconKind.File;
            }

            string ext = Path.GetExtension(name).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                return isLink ? EntryIconKind.FileLink : EntryIconKind.File;
            }

            if (ext is ".lnk" or ".url")
            {
                return EntryIconKind.Shortcut;
            }

            if (ext is ".txt" or ".log" or ".md" or ".ini" or ".cfg" or ".conf" or ".nfo")
            {
                return EntryIconKind.Text;
            }

            if (ext is ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" or ".xz" or ".cab")
            {
                return EntryIconKind.Archive;
            }

            if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" or ".ico" or ".heic")
            {
                return EntryIconKind.Image;
            }

            if (ext is ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" or ".m4v")
            {
                return EntryIconKind.Video;
            }

            if (ext is ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" or ".wma")
            {
                return EntryIconKind.Audio;
            }

            if (ext == ".pdf")
            {
                return EntryIconKind.Pdf;
            }

            if (ext is ".doc" or ".docx" or ".rtf" or ".odt")
            {
                return EntryIconKind.Word;
            }

            if (ext is ".xls" or ".xlsx" or ".csv" or ".ods")
            {
                return EntryIconKind.Excel;
            }

            if (ext is ".ppt" or ".pptx" or ".odp")
            {
                return EntryIconKind.PowerPoint;
            }

            if (ext is ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" or ".com")
            {
                return EntryIconKind.Executable;
            }

            if (ext is ".iso" or ".img" or ".vhd" or ".vhdx")
            {
                return EntryIconKind.DiskImage;
            }

            if (ext is ".rs" or ".cs" or ".cpp" or ".c" or ".h" or ".hpp" or ".py" or ".js" or ".ts" or ".tsx"
                or ".jsx" or ".java" or ".kt" or ".go" or ".php" or ".swift" or ".json" or ".xml" or ".yaml"
                or ".yml" or ".toml" or ".md" or ".sql" or ".html" or ".css" or ".scss" or ".sh")
            {
                return EntryIconKind.Code;
            }

            return isLink ? EntryIconKind.FileLink : EntryIconKind.File;
        }

        private static string GetFileTypeText(string name)
        {
            string ext = Path.GetExtension(name);
            if (string.IsNullOrWhiteSpace(ext))
            {
                return S("FileTypeGeneric");
            }

            return SF("FileTypeWithExtension", ext.TrimStart('.').ToUpperInvariant());
        }

        private bool ShouldIncludeEntry(string fullPath, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (!_appSettings.ShowDotEntries && name.StartsWith(".", StringComparison.Ordinal))
            {
                return false;
            }

            try
            {
                FileAttributes attributes = File.GetAttributes(fullPath);
                bool isHidden = (attributes & FileAttributes.Hidden) != 0;
                bool isSystem = (attributes & FileAttributes.System) != 0;

                if (!_appSettings.ShowHiddenEntries && isHidden)
                {
                    return false;
                }

                if (!_appSettings.ShowProtectedSystemEntries && isSystem)
                {
                    return false;
                }
            }
            catch
            {
            }

            return true;
        }

        private string GetEntryDisplayName(string name, bool isDirectory)
        {
            if (isDirectory || _appSettings.ShowFileExtensions)
            {
                return name;
            }

            string trimmedName = name.TrimEnd('.');
            if (string.IsNullOrEmpty(trimmedName))
            {
                return name;
            }

            string withoutExtension = Path.GetFileNameWithoutExtension(name);
            return string.IsNullOrWhiteSpace(withoutExtension) ? name : withoutExtension;
        }

        private static void ApplyEntryVisibilityStyling(EntryViewModel entry)
        {
            bool isHidden = false;
            bool isSystem = false;

            try
            {
                FileAttributes attributes = File.GetAttributes(entry.FullPath);
                isHidden = (attributes & FileAttributes.Hidden) != 0;
                isSystem = (attributes & FileAttributes.System) != 0;
            }
            catch
            {
            }

            entry.IsHiddenEntry = isHidden;
            entry.IsSystemEntry = isSystem;
            entry.IconOpacity = isSystem
                ? 0.55
                : isHidden
                    ? 0.68
                    : 1.0;
        }

        private static string GetFileSizeText(string fullPath)
        {
            return TryGetFileSize(fullPath).SizeText;
        }

        private static (long? SizeBytes, string SizeText) TryGetFileSize(string fullPath)
        {
            try
            {
                var fi = new FileInfo(fullPath);
                if (!fi.Exists)
                {
                    return (null, "-");
                }

                return (fi.Length, FormatBytes(fi.Length));
            }
            catch
            {
                return (null, "-");
            }
        }

        private static string GetModifiedTimeText(string fullPath, bool isDirectory)
        {
            return FormatModifiedTime(TryGetModifiedTime(fullPath, isDirectory));
        }

        private static DateTime? TryGetModifiedTime(string fullPath, bool isDirectory)
        {
            try
            {
                DateTime dt = isDirectory
                    ? new DirectoryInfo(fullPath).LastWriteTime
                    : new FileInfo(fullPath).LastWriteTime;
                if (dt == DateTime.MinValue)
                {
                    return null;
                }

                return dt;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatModifiedTime(DateTime? modifiedAt)
        {
            return modifiedAt?.ToString("g", CultureInfo.CurrentCulture) ?? "-";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0)
            {
                return "-";
            }

            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} {units[unit]}" : $"{size:F1} {units[unit]}";
        }
    }
}
