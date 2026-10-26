using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Baseera.Core;

public static class LocalPathSafety
{
    [StructLayout(LayoutKind.Sequential)] private struct AttributeTag { public uint Attributes, Tag; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int kind, out AttributeTag info, uint size);

    // Cloud Files tags do not redirect names like junctions/symlinks. Inspect the
    // tag without following the reparse point or recalling its file contents.
    public static bool IsCloudTag(uint tag) => (tag & 0xFFFF0FFF) == 0x9000001A;
    public static bool RequiresDownload(FileAttributes attributes) => ((uint)attributes & (0x1000u | 0x40000u | 0x400000u)) != 0;
    public static bool IsLink(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0) return false;
        if (!OperatingSystem.IsWindows()) return true;
        using var handle = CreateFile(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        return handle.IsInvalid || !GetFileInformationByHandleEx(handle, 9, out var info, (uint)Marshal.SizeOf<AttributeTag>()) || !IsCloudTag(info.Tag);
    }
}
