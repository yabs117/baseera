using System.Diagnostics;
using System.Runtime.InteropServices;
using Baseera.Core;
using Microsoft.Win32.SafeHandles;

namespace Baseera.App.Services;

internal static class PerformanceDetective
{
    [StructLayout(LayoutKind.Sequential)] private struct Io { public ulong Reads, Writes, Others, ReadBytes, WriteBytes, OtherBytes; }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int id);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetProcessIoCounters(SafeProcessHandle process, out Io counters);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string name, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
    [StructLayout(LayoutKind.Explicit)] private struct CounterValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
    [DllImport("pdh.dll")] private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
    private static ulong? Bytes(int id)
    { using var handle = OpenProcess(0x1000, false, id); return !handle.IsInvalid && GetProcessIoCounters(handle, out var io) && ulong.MaxValue - io.ReadBytes >= io.WriteBytes ? io.ReadBytes + io.WriteBytes : null; }
    internal static async Task<PerformancePoint> Sample(CancellationToken token)
    {
        return await Task.Run(async () =>
        {
            IntPtr query = IntPtr.Zero, bytes = IntPtr.Zero, latency = IntPtr.Zero; var source = new WindowsDeviceSource(); var notices = new List<string>();
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out query) == 0)
                { PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Disk Bytes/sec", IntPtr.Zero, out bytes); PdhAddEnglishCounter(query, @"\PhysicalDisk(_Total)\Avg. Disk sec/Transfer", IntPtr.Zero, out latency); PdhCollectQueryData(query); }
                var cpu = source.Cpu(); var first = source.Processes(notices).ToDictionary(p => (p.Id, p.StartedUtc), p => (Process: p, Bytes: Bytes(p.Id))); var clock = Stopwatch.StartNew();
                await Task.Delay(1000, token).ConfigureAwait(false); var second = source.Cpu(); var readings = new List<ProcessActivity>();
                var serviceMap = WindowsServiceStatus.ProcessMap(token);
                foreach (var p in source.Processes(notices))
                {
                    token.ThrowIfCancellationRequested(); double? pct = null, io = null;
                    if (first.TryGetValue((p.Id, p.StartedUtc), out var prior))
                    {
                        pct = DeviceHealth.ProcessPercent(prior.Process.Cpu, p.Cpu, Stopwatch.GetElapsedTime(prior.Process.Timestamp, p.Timestamp), source.Processors);
                        if (prior.Bytes is { } a && Bytes(p.Id) is { } b && b >= a) io = (b - a) / clock.Elapsed.TotalSeconds;
                    }
                    string name = serviceMap.TryGetValue(p.Id, out var services) ? DeviceHealth.Label(p.Name + " [" + string.Join(", ", services) + "]") : p.Name;
                    readings.Add(new(p.Id, p.StartedUtc, name, pct, p.WorkingSet, io));
                }
                double? Counter(IntPtr c) => c != IntPtr.Zero && PdhGetFormattedCounterValue(c, 0x200, out _, out var value) == 0 && value.Status is 0 or 1 && double.IsFinite(value.Value) && value.Value >= 0 ? value.Value : null;
                if (query != IntPtr.Zero) PdhCollectQueryData(query);
                var leaders = readings.OrderByDescending(p => p.Cpu ?? -1).Take(8).Concat(readings.OrderByDescending(p => p.IoBytes ?? -1).Take(8)).DistinctBy(p => (p.Id, p.Started)).Take(16).ToArray();
                return new PerformancePoint(DateTimeOffset.UtcNow, cpu is not null && second is not null ? DeviceHealth.CpuPercent(cpu, second) : null, source.Memory()?.UsedPercent, Counter(bytes), Counter(latency) * 1000, leaders);
            }
            finally { if (query != IntPtr.Zero) PdhCloseQuery(query); }
        }, token);
    }
}
