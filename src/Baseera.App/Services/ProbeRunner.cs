using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Baseera.Core;

namespace Baseera.App.Services;

internal sealed record ProbeInput(string Nonce, string Path = "", string Root = "", int Days = 7);
internal sealed record ExtractedText(string Text, string Coverage);
internal sealed record TimelineEntry(DateTimeOffset? Time, string Kind, string Name, string Detail);
internal sealed record TimelineSnapshot(DateTimeOffset Collected, IReadOnlyList<TimelineEntry> Entries, IReadOnlyList<string> Notices);

internal static class ProbeRunner
{
    private static readonly SemaphoreSlim Slots = new(2, 2);
    private sealed class Job : SafeHandleZeroOrMinusOneIsInvalid
    { public Job() : base(true) { } protected override bool ReleaseHandle() => CloseHandle(handle); }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit
    { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorking, MaxWorking; public uint Processes; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong Read, Write, Other, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern Job CreateJobObject(IntPtr security, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(Job job, int kind, ref ExtendedLimit limit, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(Job job, IntPtr process);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    internal static async Task<T> Read<T>(string kind, CancellationToken token, string path = "", string root = "", int days = 7)
    {
        if (!new[] { "security", "drivers", "timeline", "usb", "signature", "extract", "fixture" }.Contains(kind)) throw new ArgumentException("Unsupported local probe.");
        await Slots.WaitAsync(token);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(kind == "extract" ? 8 : 20));
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            start.ArgumentList.Add("--local-probe"); start.ArgumentList.Add(kind);
            using var job = CreateJobObject(IntPtr.Zero, null);
            var limit = new ExtendedLimit { Basic = new() { Flags = 0x2000 | 0x100 | 8 | 2, Processes = 1, ProcessTime = TimeSpan.FromSeconds(6).Ticks }, ProcessMemory = (UIntPtr)(384UL * 1024 * 1024) };
            if (job.IsInvalid || !SetInformationJobObject(job, 9, ref limit, (uint)Marshal.SizeOf<ExtendedLimit>())) throw new IOException("Local probe isolation unavailable.");
            using var process = Process.Start(start) ?? throw new IOException("Local probe could not start.");
            try
            {
                // The child waits for this bounded request before it reads any data.
                if (!AssignProcessToJobObject(job, process.Handle)) throw new IOException("Local probe resource limits unavailable.");
                var read = ReadBounded(process.StandardOutput, timeout.Token);
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new ProbeInput(Guid.NewGuid().ToString("N"), path, root, days))); process.StandardInput.Close();
                await process.WaitForExitAsync(timeout.Token); string output = await read;
                if (process.ExitCode != 0) throw new IOException("Local probe unavailable or exceeded its limits.");
                return JsonSerializer.Deserialize<T>(output, new JsonSerializerOptions { MaxDepth = 32 }) ?? throw new InvalidDataException("Empty local probe result.");
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); } // Only this newly created read-only child; never native repairs.
        }
        finally { Slots.Release(); }
    }
    private static async Task<string> ReadBounded(StreamReader stream, CancellationToken token)
    {
        var text = new StringBuilder(); var buffer = new char[4096];
        while (true) { int read = await stream.ReadAsync(buffer.AsMemory(), token); if (read == 0) break; if (text.Length + read > 1048576) throw new InvalidDataException("Probe output limit."); text.Append(buffer, 0, read); }
        return text.ToString();
    }
}
