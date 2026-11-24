using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Interop;
using Baseera.Core;
using Microsoft.Win32.SafeHandles;

namespace Baseera.App.Services;

internal static class StorageActions
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Operation
    { public IntPtr Window; public uint Function; public IntPtr From, To; public ushort Flags; [MarshalAs(UnmanagedType.Bool)] public bool Aborted; public IntPtr Mappings, Title; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref Operation operation);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputLength, byte[] output, int outputLength, out int returned, IntPtr overlapped);
    internal static string TempRoot => LocalFileScope.Root(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
    internal static (int Removed, bool Aborted) Recycle(Window owner, CleanupApproval approval)
    {
        var files = approval.Consume(TempRoot);
        if (files.Any(f => LocalFileScope.Contains(AppContext.BaseDirectory, f.Path) || LocalFileScope.Contains(Settings.Folder, f.Path))) throw new InvalidOperationException("Application and private data are excluded.");
        IntPtr names = Marshal.StringToHGlobalUni(string.Join('\0', files.Select(f => f.Path)) + "\0\0");
        try
        {
            // Retain Windows confirmation/error UI, including permanent-delete warnings.
            // Individual previewed files only; no directories, wildcards or silent fallback.
            var operation = new Operation { Window = new WindowInteropHelper(owner).Handle, Function = 3, From = names, Flags = 0x40 | 0x4000 | 0x1000 | 0x8000 | 0x2000 };
            int result = SHFileOperation(ref operation); return (files.Count(f => !File.Exists(f.Path)), operation.Aborted || result != 0);
        }
        finally { Marshal.FreeHGlobal(names); }
    }
    internal static bool ExternalVolume(string path)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(path))!; var drive = new DriveInfo(root);
        if (drive.DriveType == DriveType.Removable) return true;
        if (root.Length != 3) return false;
        using var handle = CreateFile(@"\\.\" + root[..2], 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        byte[] output = new byte[4096];
        return !handle.IsInvalid && DeviceIoControl(handle, 0x002D1400, new byte[12], 12, output, output.Length, out int count, IntPtr.Zero) && count >= 32 && BitConverter.ToUInt32(output, 28) == 7;
    }
    internal static async Task<InvestigationReport> ReadTest(string path, CancellationToken token, bool fixture = false)
    {
        if (!fixture && !ExternalVolume(path)) throw new InvalidDataException("Choose a file on removable or USB storage.");
        string root = Path.GetDirectoryName(Path.GetFullPath(path))!; LocalFileScope.RequireFile(root, path);
        var info = new FileInfo(path); var initial = new StorageFile(info.FullName, info.Length, info.LastWriteTimeUtc); long maximum = Math.Min(info.Length, 67108864), measured = 0; var clock = Stopwatch.StartNew();
        async Task<string> Pass()
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] bytes = new byte[65536]; long read = 0;
            while (read < maximum) { int n = await stream.ReadAsync(bytes.AsMemory(0, (int)Math.Min(bytes.Length, maximum - read)), token); if (n == 0) break; read += n; measured += n; hash.AppendData(bytes, 0, n); }
            if (read != maximum) throw new IOException("File size changed during read."); return Convert.ToHexString(hash.GetHashAndReset());
        }
        string a = await Pass(), b = await Pass(); bool same = a == b && LocalFileScope.Unchanged(initial);
        return new("usb", DateTimeOffset.UtcNow, [new("hubReadBytes", measured.ToString()), new("hubReadRate", (measured / 1048576d / Math.Max(.001, clock.Elapsed.TotalSeconds)).ToString("0.0") + " MiB/s"), new("hubRepeatedRead", same ? "state:hubSamplesMatch" : "state:hubSamplesDiffer")], ["hubReadTestLimits", maximum < info.Length ? "hubPartial" : "hubReadTestLimits"]);
    }
}
