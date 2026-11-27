using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

// Explicit, local integration checks. Never part of the fixture-only smoke run.
// Results contain counts/availability only, not window titles or event payloads.
internal static class ReadOnlySystemSmokeChecks
{
    internal static async Task Run(string output, bool network)
    {
        var lines = new List<string>(); int failures = 0;
        async Task Check(string name, Func<CancellationToken, Task<string>> test)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { lines.Add("PASS: " + name + " — " + await test(deadline.Token)); }
            catch (Exception ex) { failures++; lines.Add("FAILED: " + name + " — " + ex.GetType().Name); }
        }
        static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool value) { if (!value) throw new InvalidDataException("Read-only integration result invalid."); }
        await Check("Actual visible windows", _ =>
        {
            var windows = WindowTranslator.ListWindows(true, true); Require(windows.Count > 0);
            return Task.FromResult(windows.Count + " selectable windows; titles not retained");
        });
        await Check("Device health and process/startup collection", async ct =>
        {
            var device = await DeviceMonitor.Collect(true, ct);
            Require(device.Memory?.UsedPercent is >= 0 and <= 100 && device.Volumes.Count > 0);
            return device.Processes.Count + " process samples; " + device.Notices.Count + " source limitations; values/names not retained";
        });
        await Check("Native performance counters", async ct =>
        {
            var point = await PerformanceDetective.Sample(ct); PerformanceEvidence.ValidateHistory([point]);
            Require(point.Memory is not null && point.Processes.Count > 0);
            return "CPU=" + (point.Cpu is null ? "unavailable" : "available") + "; disk=" + (point.DiskBytes is null ? "unavailable" : "available") + "; process I/O=" + (point.Processes.Any(p => p.IoBytes is not null) ? "available" : "unavailable");
        });
        foreach (string kind in new[] { "security", "drivers", "usb" })
            await Check("Native " + kind + " probe", async ct =>
            {
                var report = await ProbeRunner.Read<InvestigationReport>(kind, ct);
                Require(report.Facts is not null && report.Limits is not null && report.Facts.Count > 0);
                return report.Facts.Count + " facts; " + report.Limits.Count + " limits; partial/unavailable=" + report.Limits.Any(l => l is "hubPartial" or "hubUnavailable");
            });
        await Check("Native event and change timeline", async ct =>
        {
            var timeline = await ProbeRunner.Read<TimelineSnapshot>("timeline", ct, days: 1);
            Require(timeline.Entries is not null && timeline.Notices is not null && timeline.Entries.Count <= 400);
            return timeline.Entries.Count + " records; " + timeline.Notices.Count + " limits; event payloads not retained";
        });
        await Check("Real Smart Repair selected-source capture", async ct =>
        {
            var text = await WindowsDiagnostics.Collect(new DiagnosticOptions(SystemInfo: true, DeviceHealth: true, ServiceStatus: true, ApplicationEvents: true, SystemEvents: true), ct);
            Require(text.Contains("Device health and performance snapshot") && text.Contains("CPU sampled utilization") && text.Contains("Selected Windows service states"));
            return "selected device/services/events included; payload not retained";
        });
        await Check("Existing local/OneDrive source scope", async ct =>
        {
            string root = Path.GetFullPath("src/Baseera.Core");
            if (!Directory.Exists(root)) return "SKIPPED: run from the repository for this check";
            string file = Path.Combine(root, "LocalPathSafety.cs"); LocalFileScope.RequireFile(root, file);
            var scan = await Task.Run(() => new ModScanner().Scan(root, ct), ct);
            Require(scan.Files.Count > 0 && !scan.Findings.Any(f => f.Code == "not-inspected" && f.Evidence.Contains("link skipped")));
            var extracted = await ProbeRunner.Read<ExtractedText>("extract", ct, Path.GetFullPath("README.md"), Path.GetFullPath("."));
            Require(extracted.Text.Contains("Baseera"));
            return scan.Files.Count + " source files inspected; existing Markdown extracted; cloud=" + ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0);
        });
        await Check("Native signature inspection", async ct =>
        {
            string signed = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var report = await ProbeRunner.Read<InvestigationReport>("signature", ct, signed);
            Require(report.Facts.Any(f => f.Label == "hubSha256" && f.Value.Length == 64) && report.Facts.Any(f => f.Label == "hubAuthenticode"));
            return "offline trust and SHA-256 returned; trust limits preserved";
        });
        if (network)
        {
            await Check("Live DNS/ICMP network measurement", async ct =>
            {
                var reading = await NetworkDoctor.Measure("github.com", ct); NetworkEvidence.Validate(reading);
                Require(reading.DnsMs is not null && reading.Sent == 6);
                return "DNS available; ICMP replies=" + reading.Replies.Count + "/" + reading.Sent + "; blocked ICMP remains inconclusive";
            });
        }
        else lines.Add("SKIPPED: external network check (requires --allow-network-check)");
        lines.Add("No repair, cleanup, application upgrade, AI request or audio recording was performed. This run does not certify Windows 10/11 device compatibility.");
        await File.WriteAllLinesAsync(Path.Combine(output, "read-only-system-result.txt"), lines);
        if (failures > 0) throw new InvalidOperationException(failures + " read-only integration check(s) failed; see summary.");
    }
}
