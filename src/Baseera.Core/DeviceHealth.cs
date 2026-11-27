namespace Baseera.Core;

public sealed record CpuTimes(ulong Idle, ulong Kernel, ulong User);
public sealed record MemoryReading(long Total, long Available)
{
    public double? UsedPercent => Total > 0 && Available >= 0 && Available <= Total ? 100d * (Total - Available) / Total : null;
}
public sealed record VolumeReading(string Name, long Total, long Free, bool System)
{
    public double? FreePercent => Total > 0 && Free >= 0 && Free <= Total ? 100d * Free / Total : null;
}
public sealed record ProcessReading(int Id, string Name, double? CpuPercent, long WorkingSet);
public sealed record StartupReading(string Name, string Source);
public sealed record HealthFinding(string Code, string Target = "");
public sealed record DeviceSnapshot(DateTimeOffset CollectedUtc, double SampleSeconds, double? CpuPercent, MemoryReading? Memory,
    IReadOnlyList<VolumeReading> Volumes, IReadOnlyList<ProcessReading> Processes, IReadOnlyList<StartupReading> Startup,
    bool PerformanceIncluded, IReadOnlyList<string> Notices)
{
    public IReadOnlyList<HealthFinding> Findings => DeviceHealth.Findings(this);
}

public static class DeviceHealth
{
    public static double? CpuPercent(CpuTimes before, CpuTimes after)
    {
        if (after.Idle < before.Idle || after.Kernel < before.Kernel || after.User < before.User) return null;
        double idle = after.Idle - before.Idle, total = (double)(after.Kernel - before.Kernel) + (after.User - before.User);
        return total <= 0 || idle > total ? null : Math.Clamp(100 * (total - idle) / total, 0, 100);
    }
    public static double? ProcessPercent(TimeSpan before, TimeSpan after, TimeSpan elapsed, int processors)
    {
        if (processors < 1 || elapsed.TotalSeconds <= 0 || after < before || before < TimeSpan.Zero) return null;
        return Math.Clamp(100 * (after - before).TotalSeconds / elapsed.TotalSeconds / processors, 0, 100);
    }
    public static IReadOnlyList<HealthFinding> Findings(DeviceSnapshot value)
    {
        var findings = new List<HealthFinding>();
        if (value.CpuPercent is >= 85 and <= 100) findings.Add(new("deviceCpuHigh"));
        if (value.Memory?.UsedPercent is >= 90) findings.Add(new("deviceMemoryHigh"));
        foreach (var disk in value.Volumes.Where(v => v.FreePercent is not null))
            if (disk.FreePercent < 10 || disk.System && disk.Free < 5L * 1024 * 1024 * 1024) findings.Add(new("deviceStorageLow", disk.Name));
        return findings;
    }
    // Labels/paths obtained from the OS remain inert bounded display data.
    public static string Label(string value) => new(value.Where(c => !char.IsControl(c) && c is not (>= '\u202a' and <= '\u202e') and not (>= '\u2066' and <= '\u2069')).Take(120).ToArray());
}
