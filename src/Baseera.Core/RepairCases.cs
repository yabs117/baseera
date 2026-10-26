using System.Text.Json;

namespace Baseera.Core;

public sealed record ServiceReading(string Name, string State);
public sealed record CaseEvidence(Guid Id, DateTimeOffset CollectedUtc, string HostScope, DiagnosticOptions Sources,
    string Text, DeviceSnapshot? Device, IReadOnlyList<ServiceReading> Services, bool Complete, bool Edited = false, bool Reviewed = false);
public sealed record CaseAnalysis(DateTimeOffset Time, string Description, string Summary, string Cause, IReadOnlyList<string> Steps, string Confidence, Guid? SharedEvidence);
public sealed record CaseAction(Guid Id, DateTimeOffset Time, string ActionId, string Status, int? ExitCode, Guid? BeforeEvidence);
public sealed record MeasurementChange(string Key, string Target, double Before, double After);
public sealed record ServiceChange(string Name, string Before, string After);
public sealed record CaseComparison(string Status, IReadOnlyList<MeasurementChange> Measurements, IReadOnlyList<ServiceChange> Services, IReadOnlyList<string> Limits);
public sealed record CaseRetest(DateTimeOffset Time, Guid? Before, Guid After, Guid? Action, CaseComparison Comparison);
public sealed record RepairCaseDocument(int Format, Guid Id, int Revision, string Title, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc,
    DateTimeOffset? FirstObservedUtc, IReadOnlyList<CaseEvidence> Evidence, IReadOnlyList<CaseAnalysis> Analyses,
    IReadOnlyList<CaseAction> Actions, IReadOnlyList<CaseRetest> Retests, bool StoreEvidence)
{
    public static RepairCaseDocument Create(string title) => new(1, Guid.NewGuid(), 0, title, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, [], [], [], [], false);
}

public static class CaseVerification
{
    public static CaseComparison Compare(CaseEvidence? before, CaseEvidence after)
    {
        var limits = new List<string>(); var metrics = new List<MeasurementChange>(); var services = new List<ServiceChange>();
        if (before is null) return new("inconclusive", [], [], ["caseNoBaseline", "caseNotVerifiedFix"]);
        if (before.HostScope != after.HostScope || before.Sources != after.Sources || before.Edited || after.Edited || after.CollectedUtc <= before.CollectedUtc)
            return new("inconclusive", [], [], ["caseScopeMismatch", "caseNotVerifiedFix"]);
        if (!before.Complete || !after.Complete) limits.Add("casePartialEvidence");
        if (before.Sources.DeviceHealth && before.Device is { } left && after.Device is { } right)
        {
            void Metric(string key, double? a, double? b, string target = "")
            { if (a is { } x && b is { } y && double.IsFinite(x) && double.IsFinite(y)) metrics.Add(new(key, target, x, y)); }
            Metric("deviceCpu", left.CpuPercent, right.CpuPercent); Metric("deviceMemory", left.Memory?.UsedPercent, right.Memory?.UsedPercent);
            foreach (var volume in left.Volumes.Where(v => v.FreePercent is not null))
                if (right.Volumes.FirstOrDefault(v => v.Name == volume.Name && v.Total == volume.Total && v.FreePercent is not null) is { } other)
                    Metric("caseFreeGiB", volume.Free / 1073741824d, other.Free / 1073741824d, volume.Name);
        }
        if (before.Sources.ServiceStatus)
            foreach (var service in before.Services)
                if (after.Services.FirstOrDefault(s => s.Name == service.Name) is { } current && service.State != "unknown" && current.State != "unknown")
                    services.Add(new(service.Name, service.State, current.State));
        if (before.Sources.ApplicationEvents || before.Sources.SystemEvents)
            limits.Add(after.CollectedUtc - before.CollectedUtc < TimeSpan.FromHours(before.Sources.Hours) ? "caseOverlappingEvents" : "caseSampledEvents");
        // Numeric/service changes are observations, never evidence that a symptom is fixed.
        string status = metrics.Count == 0 && services.Count == 0 ? "inconclusive" :
            metrics.Any(m => Math.Abs(m.After - m.Before) > .01) || services.Any(s => s.Before != s.After) ? "changed" : "unchanged";
        limits.Add("caseNotVerifiedFix"); return new(status, metrics, services, limits.Distinct().ToArray());
    }
}

public static class RepairCaseCodec
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 32 };
    public static byte[] Write(RepairCaseDocument value) { Validate(value); var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options); if (bytes.Length > MaxBytes) throw new InvalidDataException("Case exceeds its size limit."); return bytes; }
    public static RepairCaseDocument Read(byte[] bytes)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("Case exceeds its size limit.");
        var value = JsonSerializer.Deserialize<RepairCaseDocument>(bytes, Options) ?? throw new InvalidDataException("Missing case."); Validate(value); return value;
    }
    public static void Validate(RepairCaseDocument v)
    {
        static void Text(string? value, int max) { if (value is null || value.Length > max) throw new InvalidDataException("Invalid case text."); }
        static void Percent(double? p) { if (p is { } n && (!double.IsFinite(n) || n < 0 || n > 100)) throw new InvalidDataException("Invalid percentage."); }
        if (v is null || v.Format != 1 || v.Id == Guid.Empty || v.Revision < 0 || v.CreatedUtc > v.UpdatedUtc || v.FirstObservedUtc > DateTimeOffset.UtcNow.AddDays(1) ||
            v.Evidence is null || v.Analyses is null || v.Actions is null || v.Retests is null || v.Evidence.Count > 20 || v.Analyses.Count > 20 || v.Actions.Count > 40 || v.Retests.Count > 20)
            throw new InvalidDataException("Invalid case structure.");
        Text(v.Title, 160);
        if (v.Evidence.Any(e => e is null) || v.Evidence.Select(e => e.Id).Distinct().Count() != v.Evidence.Count) throw new InvalidDataException("Duplicate evidence.");
        foreach (var e in v.Evidence)
        {
            if (e.Id == Guid.Empty || e.Sources is null || e.Services is null || e.Services.Count > 8) throw new InvalidDataException("Invalid evidence.");
            e.Sources.Validate(); Text(e.HostScope, 64); Text(e.Text, DiagnosticPrivacy.MaxCharacters);
            foreach (var service in e.Services) if (service is null || !WindowsServiceNames.All.Contains(service.Name) || !WindowsServiceNames.States.Contains(service.State)) throw new InvalidDataException("Unknown service evidence.");
            if (e.Services.Select(s => s.Name).Distinct().Count() != e.Services.Count) throw new InvalidDataException("Duplicate services.");
            if (e.Device is { } d)
            {
                if (d.Volumes is null || d.Processes is null || d.Startup is null || d.Notices is null || d.Volumes.Count > 32 || d.Processes.Count > 512 || d.Startup.Count > 100 || d.Notices.Count > 20 || !double.IsFinite(d.SampleSeconds) || d.SampleSeconds < 0) throw new InvalidDataException("Unbounded device data.");
                Percent(d.CpuPercent);
                if (d.Memory is { } memory && (memory.Total < 0 || memory.Available < 0 || memory.Available > memory.Total)) throw new InvalidDataException("Invalid memory.");
                foreach (var disk in d.Volumes) { if (disk is null || disk.Total < 0 || disk.Free < 0 || disk.Free > disk.Total) throw new InvalidDataException("Invalid volume."); Text(disk.Name, 120); }
                foreach (var process in d.Processes) { if (process is null || process.Id < 0 || process.WorkingSet < 0) throw new InvalidDataException("Invalid process."); Text(process.Name, 120); Percent(process.CpuPercent); }
                foreach (var startup in d.Startup) { if (startup is null) throw new InvalidDataException("Invalid startup."); Text(startup.Name, 120); Text(startup.Source, 120); }
                foreach (string notice in d.Notices) Text(notice, 120);
            }
        }
        foreach (var a in v.Analyses) { if (a is null) throw new InvalidDataException("Invalid analysis."); Text(a.Description, 12000); Text(a.Summary, 6000); Text(a.Cause, 1200); Text(a.Confidence, 150); if (a.Steps is null || a.Steps.Count > 8) throw new InvalidDataException("Invalid steps."); foreach (string step in a.Steps) Text(step, 1000); }
        foreach (var a in v.Actions)
            if (a is null || a.Id == Guid.Empty || !RepairCatalog.All.Any(r => r.Id == a.ActionId) || a.Status is not ("pending" or "completed" or "failed" or "declined" or "execution-error" or "completion-unknown")) throw new InvalidDataException("Invalid action history.");
        if (v.Actions.Select(a => a.Id).Distinct().Count() != v.Actions.Count) throw new InvalidDataException("Duplicate actions.");
        foreach (var r in v.Retests)
        {
            if (r is null || !v.Evidence.Any(e => e.Id == r.After) || r.Comparison is null || r.Comparison.Measurements is null || r.Comparison.Services is null || r.Comparison.Limits is null || r.Comparison.Measurements.Count > 34 || r.Comparison.Services.Count > 8 || r.Comparison.Limits.Count > 8 || r.Comparison.Status is not ("changed" or "unchanged" or "inconclusive")) throw new InvalidDataException("Invalid retest.");
            foreach (var m in r.Comparison.Measurements)
            { if (m is null || m.Key is not ("deviceCpu" or "deviceMemory" or "caseFreeGiB") || !double.IsFinite(m.Before) || !double.IsFinite(m.After) || m.Before < 0 || m.After < 0) throw new InvalidDataException("Invalid measurement."); Text(m.Target, 120); }
            foreach (var s in r.Comparison.Services) if (s is null || !WindowsServiceNames.All.Contains(s.Name) || !WindowsServiceNames.States.Contains(s.Before) || !WindowsServiceNames.States.Contains(s.After)) throw new InvalidDataException("Invalid service change.");
            foreach (string limit in r.Comparison.Limits) if (!new[] { "caseNoBaseline", "caseScopeMismatch", "casePartialEvidence", "caseOverlappingEvents", "caseSampledEvents", "caseNotVerifiedFix" }.Contains(limit)) throw new InvalidDataException("Invalid comparison limit.");
        }
    }
    public static RepairCaseDocument ForStorage(RepairCaseDocument value, bool includeEvidence, string user, string machine)
    {
        string Clean(string s, int max = DiagnosticPrivacy.MaxCharacters)
        { string clean = DiagnosticPrivacy.Sanitize(s, user, machine); int end = Math.Min(max, clean.Length); if (end > 0 && char.IsHighSurrogate(clean[end - 1])) end--; return clean[..end]; }
        return value with
        {
            Title = Clean(value.Title, 160), StoreEvidence = includeEvidence,
            Evidence = value.Evidence.Select(e => e with { Text = includeEvidence && e.Reviewed ? Clean(e.Text) : "",
                Sources = e.Sources with { MatchText = includeEvidence && e.Reviewed ? Clean(e.Sources.MatchText, 100) : "" },
                Edited = e.Edited || e.Sources.MatchText != (includeEvidence && e.Reviewed ? Clean(e.Sources.MatchText, 100) : ""),
                // Preview includes only a small process sample. Never persist hidden process/startup names.
                Device = includeEvidence && e.Reviewed && e.Device is { } device ? device with { Processes = [], Startup = [], Volumes = device.Volumes.Select(d => d with { Name = Clean(d.Name, 120) }).ToArray() } : null,
                Services = includeEvidence && e.Reviewed ? e.Services : [] }).ToArray(),
            Retests = value.Retests.Select(r => r with { Comparison = r.Comparison with { Measurements = r.Comparison.Measurements.Select(m => m with { Target = Clean(m.Target, 120) }).ToArray() } }).ToArray(),
            Analyses = value.Analyses.Select(a => a with { Description = Clean(a.Description, 12000), Summary = Clean(a.Summary, 6000), Cause = Clean(a.Cause, 1200), Confidence = Clean(a.Confidence, 150), Steps = a.Steps.Select(s => Clean(s, 1000)).ToArray() }).ToArray()
        };
    }
}

public static class WindowsServiceNames
{
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[] { "Dnscache", "Dhcp", "wuauserv", "BITS", "EventLog", "WinDefend" });
    public static IReadOnlyList<string> States { get; } = Array.AsReadOnly(new[] { "stopped", "start-pending", "stop-pending", "running", "continue-pending", "pause-pending", "paused", "unknown" });
}
