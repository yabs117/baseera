using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using Baseera.Core;

namespace Baseera.App.Services;

internal interface IDeviceSource
{
    CpuTimes? Cpu();
    MemoryReading? Memory();
    IReadOnlyList<VolumeReading> Volumes(List<string> notices);
    IReadOnlyList<ProcessSample> Processes(List<string> notices);
    IReadOnlyList<StartupReading> Startup(List<string> notices);
    int Processors { get; }
}
internal sealed record ProcessSample(int Id, string Name, long StartedUtc, TimeSpan Cpu, long WorkingSet, long Timestamp);

internal sealed class WindowsDeviceSource : IDeviceSource
{
    [StructLayout(LayoutKind.Sequential)] private struct FileTime { public uint Low, High; public readonly ulong Value => ((ulong)High << 32) | Low; }
    [StructLayout(LayoutKind.Sequential)] private struct MemoryStatus
    { public uint Length, Load; public ulong TotalPhys, AvailablePhys, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
    [DllImport("kernel32.dll")] private static extern uint GetActiveProcessorCount(ushort group);
    public int Processors => checked((int)GetActiveProcessorCount(0xffff));
    public CpuTimes? Cpu() => Processors > 64 || !GetSystemTimes(out var idle, out var kernel, out var user) ? null : new(idle.Value, kernel.Value, user.Value);
    public MemoryReading? Memory()
    {
        var state = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        return GlobalMemoryStatusEx(ref state) && state.TotalPhys <= long.MaxValue ? new((long)state.TotalPhys, (long)state.AvailablePhys) : null;
    }
    public IReadOnlyList<VolumeReading> Volumes(List<string> notices)
    {
        var result = new List<VolumeReading>();
        foreach (var drive in DriveInfo.GetDrives().Take(32))
            try
            {
                if (drive.DriveType != DriveType.Fixed) continue;
                if (!drive.IsReady) { notices.Add("deviceDiskUnavailable"); continue; }
                result.Add(new(drive.Name, drive.TotalSize, drive.AvailableFreeSpace,
                    string.Equals(drive.Name, Path.GetPathRoot(Environment.SystemDirectory), StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notices.Add("deviceDiskUnavailable"); }
        return result;
    }
    public IReadOnlyList<ProcessSample> Processes(List<string> notices)
    {
        var result = new List<ProcessSample>(); int skipped = 0;
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes.Take(512))
                try
                {
                    if (process.Id == 0) continue;
                    result.Add(new(process.Id, DeviceHealth.Label(process.ProcessName), process.StartTime.ToUniversalTime().Ticks,
                        process.TotalProcessorTime, process.WorkingSet64, Stopwatch.GetTimestamp()));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { skipped++; }
            if (skipped > 0 || processes.Length > 512) notices.Add("deviceProcessPartial");
        }
        finally { foreach (var process in processes) process.Dispose(); }
        return result;
    }
    public IReadOnlyList<StartupReading> Startup(List<string> notices)
    {
        var result = new List<StartupReading>();
        void Registry(RegistryHive hive, RegistryView view, string source)
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
                if (key is null) return;
                var names = key.GetValueNames(); if (names.Length > 100) notices.Add("deviceStartupPartial");
                foreach (var name in names.Take(100)) result.Add(new(DeviceHealth.Label(name), source));
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { notices.Add("deviceStartupPartial"); }
        }
        Registry(RegistryHive.CurrentUser, RegistryView.Registry64, "deviceStartupUser");
        Registry(RegistryHive.LocalMachine, RegistryView.Registry64, "deviceStartupMachine64");
        Registry(RegistryHive.LocalMachine, RegistryView.Registry32, "deviceStartupMachine32");
        foreach (var location in new[] { (Environment.SpecialFolder.Startup, "deviceStartupFolderUser"), (Environment.SpecialFolder.CommonStartup, "deviceStartupFolderAll") })
            try
            {
                string folder = Environment.GetFolderPath(location.Item1);
                if (folder.Length == 0) continue;
                if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\") || new DriveInfo(Path.GetPathRoot(folder)!).DriveType != DriveType.Fixed) { notices.Add("deviceStartupPartial"); continue; }
                if (!Directory.Exists(folder)) continue;
                var names = Directory.EnumerateFiles(folder).Where(p => !Path.GetFileName(p).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)).Take(101).ToArray();
                if (names.Length > 100) notices.Add("deviceStartupPartial");
                foreach (string name in names.Take(100)) result.Add(new(DeviceHealth.Label(Path.GetFileName(name)), location.Item2));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { notices.Add("deviceStartupPartial"); }
        if (result.Count > 100) notices.Add("deviceStartupPartial");
        return result.Distinct().OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
    }
}

internal static class DeviceMonitor
{
    internal static async Task<DeviceSnapshot> Collect(bool performance, CancellationToken token, IDeviceSource? supplied = null, TimeSpan? interval = null)
    {
        var source = supplied ?? new WindowsDeviceSource(); var notices = new List<string>();
        return await Task.Run(async () =>
        {
            token.ThrowIfCancellationRequested(); var start = Stopwatch.StartNew(); var first = source.Cpu();
            var processes = performance ? source.Processes(notices) : [];
            await Task.Delay(interval ?? TimeSpan.FromMilliseconds(850), token).ConfigureAwait(false);
            var second = source.Cpu(); double? cpu = first is null || second is null ? null : DeviceHealth.CpuPercent(first, second);
            double sampledSeconds = start.Elapsed.TotalSeconds;
            var memory = source.Memory(); var volumes = source.Volumes(notices);
            if (volumes.Count == 0) notices.Add("deviceDiskUnavailable");
            if (cpu is null) notices.Add("deviceCpuUnavailable");
            if (memory?.UsedPercent is null) notices.Add("deviceMemoryUnavailable");
            var readings = new List<ProcessReading>();
            if (performance)
            {
                var old = processes.ToDictionary(x => (x.Id, x.StartedUtc));
                foreach (var current in source.Processes(notices))
                {
                    token.ThrowIfCancellationRequested();
                    double? percent = old.TryGetValue((current.Id, current.StartedUtc), out var before)
                        ? DeviceHealth.ProcessPercent(before.Cpu, current.Cpu, Stopwatch.GetElapsedTime(before.Timestamp, current.Timestamp), source.Processors) : null;
                    readings.Add(new(current.Id, current.Name, percent, current.WorkingSet));
                }
            }
            var startup = performance ? source.Startup(notices) : [];
            token.ThrowIfCancellationRequested();
            return new DeviceSnapshot(DateTimeOffset.UtcNow, sampledSeconds, cpu, memory, volumes,
                readings.OrderByDescending(x => x.CpuPercent ?? -1).ThenByDescending(x => x.WorkingSet).Take(512).ToArray(), startup, performance, notices.Distinct().ToArray());
        }, token).ConfigureAwait(false);
    }
    internal static string DiagnosticText(DeviceSnapshot snapshot)
    {
        string Percent(double? value) => value?.ToString("0.0", CultureInfo.InvariantCulture) + (value is null ? "unavailable" : "%");
        var text = new StringBuilder("Baseera device snapshot — read only; not a hardware-health certificate\n");
        text.AppendLine($"Collected UTC: {snapshot.CollectedUtc:O}; sample seconds: {snapshot.SampleSeconds.ToString("0.00", CultureInfo.InvariantCulture)}");
        text.AppendLine("CPU sampled utilization: " + Percent(snapshot.CpuPercent));
        text.AppendLine("Physical RAM usage: " + Percent(snapshot.Memory?.UsedPercent));
        if (snapshot.Memory is { } memory) text.AppendLine($"RAM bytes total/available: {memory.Total}/{memory.Available}");
        foreach (var volume in snapshot.Volumes) text.AppendLine($"Local volume {volume.Name}: free/total bytes {volume.Free}/{volume.Total}; system={volume.System}");
        foreach (var process in snapshot.Processes.Take(4).Concat(snapshot.Processes.OrderByDescending(p => p.WorkingSet).Take(4)).DistinctBy(p => p.Id).Take(8)) text.AppendLine($"Process {process.Name}; PID {process.Id}; sampled CPU {Percent(process.CpuPercent)}; working-set bytes {process.WorkingSet}");
        if (snapshot.PerformanceIncluded) text.AppendLine("Startup registrations found: " + snapshot.Startup.Count + "; enabled state/impact unknown, excludes scheduled tasks/services/packaged startup tasks.");
        text.AppendLine("Threshold observations: " + string.Join(", ", snapshot.Findings.Select(f => f.Code + " " + f.Target)));
        text.AppendLine("Coverage notices: " + string.Join(", ", snapshot.Notices));
        text.AppendLine("Limits: snapshot, not sustained load or a cause. Process names may reveal app usage. No command lines, file contents, SMART/temperature, disk I/O, GPU, battery or event logs were read here. Event log timestamps may differ: correlate cautiously, do not infer causation from coexistence. Working sets can share memory and must not be summed.");
        return text.ToString();
    }
}
