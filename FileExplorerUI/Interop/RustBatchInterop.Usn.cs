using System;
using System.Runtime.InteropServices;

namespace FileExplorerUI.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RustUsnCapability
{
    public byte is_ntfs_local;
    public byte can_open_volume;
    public byte access_denied;
    public byte available;
    public int error_code;
}

public static partial class RustBatchInterop
{
    [LibraryImport("rust_engine.dll", EntryPoint = "fe_usn_probe_volume", StringMarshalling = StringMarshalling.Utf8)]
    private static partial RustUsnCapability UsnProbeVolume(string path);

    [LibraryImport("rust_engine.dll", EntryPoint = "fe_usn_mark_path_changed", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int UsnMarkPathChangedNative(string path);

    public static RustUsnCapability ProbeUsnCapability(string path)
    {
        lock (NativeCallGate)
        {
            return UsnProbeVolume(path);
        }
    }

    public static void MarkPathChanged(string path)
    {
        int code;
        lock (NativeCallGate)
        {
            code = UsnMarkPathChangedNative(path);
        }
        if (code != 0)
        {
            throw new InvalidOperationException($"USN mark changed failed: {code}");
        }
    }
}
