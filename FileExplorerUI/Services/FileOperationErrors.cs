using System;
using System.IO;
using System.Runtime.InteropServices;

namespace FileExplorerUI.Services;

public enum FileOperationError
{
    Unknown,
    Canceled,
    NotFound,
    AccessDenied,
    AlreadyExists,
    InvalidName,
    PathTooLong,
    InUse,
    DiskFull,
    NotSupported,
    TargetIsSourceDescendant,
    InvalidArchive
}

public static class FileOperationErrors
{
    public static FileOperationError Classify(Exception ex)
    {
        if (ex is OperationCanceledException)
        {
            return FileOperationError.Canceled;
        }

        if (ex is UnauthorizedAccessException)
        {
            return FileOperationError.AccessDenied;
        }

        if (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return FileOperationError.NotFound;
        }

        if (ex is PathTooLongException)
        {
            return FileOperationError.PathTooLong;
        }

        if (ex is InvalidDataException)
        {
            return FileOperationError.InvalidArchive;
        }

        if (ex is DirectoryTargetIsSourceDescendantException)
        {
            return FileOperationError.TargetIsSourceDescendant;
        }

        int code = ex.HResult & 0xFFFF;
        return code switch
        {
            1223 => FileOperationError.Canceled,
            2 or 3 => FileOperationError.NotFound,
            5 => FileOperationError.AccessDenied,
            32 => FileOperationError.InUse,
            80 or 183 => FileOperationError.AlreadyExists,
            112 => FileOperationError.DiskFull,
            123 or 206 => FileOperationError.InvalidName,
            267 => FileOperationError.NotSupported,
            _ when ex is IOException ioEx && LooksLikeAlreadyExists(ioEx) => FileOperationError.AlreadyExists,
            _ when ex is IOException ioEx && LooksLikeInUse(ioEx) => FileOperationError.InUse,
            _ when ex is NotSupportedException => FileOperationError.NotSupported,
            _ when ex is COMException => FileOperationError.Unknown,
            _ => FileOperationError.Unknown
        };
    }

    public static string ToUserMessage(Exception ex)
    {
        return ToUserMessage(Classify(ex));
    }

    public static string ToUserMessage(FileOperationError error)
    {
        return LocalizedStrings.Instance.Get(error switch
        {
            FileOperationError.Canceled => "ErrorFileOperationCanceled",
            FileOperationError.NotFound => "ErrorFileOperationNotFound",
            FileOperationError.AccessDenied => "ErrorFileOperationAccessDenied",
            FileOperationError.AlreadyExists => "ErrorFileOperationAlreadyExists",
            FileOperationError.InvalidName => "ErrorFileOperationInvalidName",
            FileOperationError.PathTooLong => "ErrorFileOperationPathTooLong",
            FileOperationError.InUse => "ErrorFileOperationInUse",
            FileOperationError.DiskFull => "ErrorFileOperationDiskFull",
            FileOperationError.NotSupported => "ErrorFileOperationNotSupported",
            FileOperationError.TargetIsSourceDescendant => "ErrorPasteTargetIsSourceDescendant",
            FileOperationError.InvalidArchive => "ErrorFileOperationInvalidArchive",
            _ => "ErrorFileOperationUnknown"
        });
    }

    private static bool LooksLikeAlreadyExists(IOException ex)
    {
        return ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("已存在", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeInUse(IOException ex)
    {
        return ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("另一个程序正在使用", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("文件被", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class DirectoryTargetIsSourceDescendantException : IOException
{
    public DirectoryTargetIsSourceDescendantException()
        : base("The destination folder is a subfolder of the source folder.")
    {
    }
}

public sealed record FileOperationFailure(FileOperationError Error, string Message);

public sealed class FileOperationResult<T>
{
    private FileOperationResult(bool succeeded, T? value, FileOperationFailure? failure)
    {
        Succeeded = succeeded;
        Value = value;
        Failure = failure;
    }

    public bool Succeeded { get; }

    public T? Value { get; }

    public FileOperationFailure? Failure { get; }

    public static FileOperationResult<T> Success(T value) => new(true, value, failure: null);

    public static FileOperationResult<T> Fail(Exception ex) =>
        new(false, value: default, new FileOperationFailure(FileOperationErrors.Classify(ex), FileOperationErrors.ToUserMessage(ex)));
}
