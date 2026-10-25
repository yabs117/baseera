using Baseera.Core;

internal static class DeviceHealthChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(Math.Abs(DeviceHealth.CpuPercent(new(100, 200, 100), new(200, 400, 200))!.Value - 66.6666667) < .0001, "CPU delta includes kernel idle time once, across all measured processors");
        check(DeviceHealth.CpuPercent(new(0, 0, 0), new(100, 100, 0)) == 0 && DeviceHealth.CpuPercent(new(0, 0, 0), new(0, 100, 100)) == 100, "Fully idle and fully busy CPU samples are distinct");
        check(DeviceHealth.CpuPercent(new(100, 200, 100), new(99, 300, 200)) is null && DeviceHealth.CpuPercent(new(1, 1, 1), new(1, 1, 1)) is null && DeviceHealth.CpuPercent(new(0, 0, 0), new(10, 1, 1)) is null, "Zero, reset and inconsistent CPU counters are unavailable rather than healthy");
        check(DeviceHealth.CpuPercent(new(0, 0, 0), new(0, ulong.MaxValue, ulong.MaxValue)) == 100, "CPU accumulation does not overflow native counters");
        check(DeviceHealth.ProcessPercent(TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), 4) == 50 && DeviceHealth.ProcessPercent(TimeSpan.Zero, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1), 4) == 100, "Process usage is normalized and bounded to total CPU capacity");
        check(DeviceHealth.ProcessPercent(TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.FromSeconds(1), 4) is null && DeviceHealth.ProcessPercent(TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.Zero, 0) is null, "Invalid process timing is not reported as zero CPU");
        check(new MemoryReading(100, 25).UsedPercent == 75 && new MemoryReading(0, 0).UsedPercent is null && new MemoryReading(100, 101).UsedPercent is null && new MemoryReading(100, -1).UsedPercent is null, "Memory ratios validate capacity and available bytes");
        const long gib = 1024L * 1024 * 1024;
        var snapshot = new DeviceSnapshot(DateTimeOffset.UtcNow, 1, 90, new(10 * gib, gib / 2), [new("C:\\", 100 * gib, 2 * gib, true)], [], [], false, []);
        check(snapshot.Findings.Select(f => f.Code).SequenceEqual(new[] { "deviceCpuHigh", "deviceMemoryHigh", "deviceStorageLow" }), "CPU, memory and storage threshold findings are evidence-specific");
        check((snapshot with { CpuPercent = 0, Memory = new(10 * gib, 5 * gib), Volumes = [new("D:\\", 100 * gib, 50 * gib, false)] }).Findings.Count == 0, "Ordinary samples do not invent system issues or event counts");
        check((snapshot with { CpuPercent = null, Memory = null, Volumes = [new("X", 0, 0, true)] }).Findings.Count == 0 && new VolumeReading("X", 10, 20, false).FreePercent is null, "Missing metrics and invalid volume sizes cannot create false low-space findings");
        check(DeviceHealth.Label("safe\u202ename\n") == "safename" && DeviceHealth.Label(new string('x', 1000)).Length == 120, "OS labels are bounded and strip controls and bidirectional overrides");
        new DiagnosticOptions(DeviceHealth: true).Validate();
        check(!new DiagnosticOptions().DeviceHealth, "Device diagnostics are opt-in and can be selected independently");
    }
}
