using System.Text.Json;
using System.Text.Json.Nodes;
using Baseera.Core;

internal static class RepairCaseChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var sources = new DiagnosticOptions(false, true, false, 1, "", true, true);
        DeviceSnapshot Device(double cpu) => new(now, .8, cpu, new(1000, 400), [new("C:\\", 1000, 500, true)], [new(123, "hidden-private-process", 1, 20)], [new("hidden-startup", "user")], true, []);
        var before = new CaseEvidence(Guid.NewGuid(), now, "same-install", sources, "reviewed text user@example.com", Device(90), [new("BITS", "stopped")], true, Reviewed: true);
        var after = before with { Id = Guid.NewGuid(), CollectedUtc = now.AddMinutes(3), Device = Device(12), Services = [new("BITS", "running")] };
        var comparison = CaseVerification.Compare(before, after);
        check(comparison.Status == "changed" && comparison.Measurements.Any(m => m.Key == "deviceCpu" && m.Before == 90 && m.After == 12) && comparison.Services.Count == 1, "Retest preserves real before/after measurements");
        check(comparison.Limits.Contains("caseNotVerifiedFix") && comparison.Limits.Contains("caseOverlappingEvents"), "Changed resources and overlapping events never prove a symptom fix");
        check(CaseVerification.Compare(before, after with { Device = before.Device, Services = before.Services }).Status == "unchanged", "Identical observations remain unchanged, not fixed");
        check(CaseVerification.Compare(null, after).Status == "inconclusive", "Missing baseline is inconclusive");
        check(CaseVerification.Compare(before, after with { HostScope = "other-install" }).Status == "inconclusive" && CaseVerification.Compare(before, after with { Edited = true }).Status == "inconclusive", "Different device and redacted typed evidence cannot be compared");
        check(CaseVerification.Compare(before, after with { Sources = sources with { Hours = 24 } }).Status == "inconclusive" && CaseVerification.Compare(after, before).Status == "inconclusive", "Scope and time ordering guard comparisons");
        check(CaseVerification.Compare(before, after with { Complete = false }).Limits.Contains("casePartialEvidence"), "Partial samples keep their evidence limitation");
        var value = RepairCaseDocument.Create("Example user@example.com") with { Evidence = [before, after], Retests = [new(now, before.Id, after.Id, null, comparison)] };
        var roundtrip = RepairCaseCodec.Read(RepairCaseCodec.Write(value));
        check(roundtrip.Id == value.Id && roundtrip.Retests[0].Comparison.Measurements.Count == comparison.Measurements.Count, "Case versioned bounded format round-trips comparisons");
        var privateDefault = RepairCaseCodec.ForStorage(value, false, "example-user", "example-host");
        check(privateDefault.Evidence.All(e => e.Text == "" && e.Device is null && e.Services.Count == 0), "Raw evidence excluded by default");
        var approved = RepairCaseCodec.ForStorage(value, true, "example-user", "example-host");
        string encoded = JsonSerializer.Serialize(approved);
        check(approved.Evidence[0].Device?.Processes.Count == 0 && !encoded.Contains("hidden-private-process") && !encoded.Contains("hidden-startup") && !encoded.Contains("user@example.com"), "Reviewed storage redacts identifiers and excludes hidden process/startup names");
        check(RepairCaseCodec.ForStorage(value with { Evidence = [before with { Reviewed = false }] }, true, "", "").Evidence[0].Text == "", "Storage opt-in cannot persist unreviewed raw evidence");
        var filtered = value with { Evidence = [before with { Sources = sources with { MatchText = "user@example.com" } }], Retests = [] };
        var defaultFilter = RepairCaseCodec.ForStorage(filtered, false, "", "").Evidence[0];
        var reviewedFilter = RepairCaseCodec.ForStorage(filtered, true, "", "").Evidence[0];
        check(defaultFilter.Sources.MatchText == "" && defaultFilter.Edited && !reviewedFilter.Sources.MatchText.Contains("user@example.com") && reviewedFilter.Edited, "Private literal filters excluded/redacted and scope changes remain inconclusive");
        bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException) { return true; } }
        void Malformed(Action<JsonObject> change, string name)
        { var node = JsonNode.Parse(RepairCaseCodec.Write(value))!.AsObject(); change(node); check(Reject(() => RepairCaseCodec.Read(JsonSerializer.SerializeToUtf8Bytes(node))), name); }
        Malformed(n => n["Format"] = 500, "Future case schema is refused safely");
        Malformed(n => n["Evidence"]![0] = null, "Null evidence rejected");
        Malformed(n => n["Analyses"] = new JsonArray((JsonNode?)null), "Null analyses rejected");
        Malformed(n => n["Actions"] = new JsonArray((JsonNode?)null), "Null action rejected");
        Malformed(n => n["Retests"]![0] = null, "Null retest rejected");
        Malformed(n => n["Evidence"]![0]!["Services"]![0] = null, "Null service evidence rejected");
        Malformed(n => n["Evidence"]![0]!["Device"]!["Volumes"]![0] = null, "Null nested device data rejected");
        Malformed(n => n["Retests"]![0]!["Comparison"]!["Measurements"]![0]!["Key"] = "execute-cmd", "Untrusted comparison keys rejected");
        Malformed(n => n["Retests"]![0]!["Comparison"]!["Measurements"]![0] = null, "Null comparison data rejected");
        Malformed(n => n["Evidence"]![0]!["Device"]!["Processes"]![0]!["Name"] = new string('x', 121), "Nested labels have independent bounds");
        check(Reject(() => RepairCaseCodec.Read(new byte[RepairCaseCodec.MaxBytes + 1])), "Oversize input refused before deserialization");
        check(Reject(() => RepairCaseCodec.Read(System.Text.Encoding.UTF8.GetBytes(new string('[', 100) + new string(']', 100)))), "Excessive JSON depth rejected");
        for (int i = 0; i < 100; i++)
        {
            byte[] bytes = RepairCaseCodec.Write(value); bytes[(i * 131) % bytes.Length] = 0;
            check(Reject(() => RepairCaseCodec.Read(bytes)), "Corrupted case bytes rejected " + i);
        }
        var hub = new TaskHub(); hub.Changed += () => throw new InvalidOperationException("Broken display");
        using (var job = hub.Begin("sample", true))
        {
            job.Report(.5); check(hub.Items[0].Progress == .5, "Task progress reports real values despite display subscriber failure");
            check(hub.Cancel(job.Item.Id) && job.Token.IsCancellationRequested, "Task cancellation reaches the worker token"); job.Cancelled();
        }
        using (var job = hub.Begin("native", false)) { check(!hub.Cancel(job.Item.Id) && !job.Token.IsCancellationRequested, "Native servicing cannot be force-canceled"); job.Complete(); }
        using (hub.Begin("unfinished", true)) { }
        check(hub.Items.Any(x => x.Kind == "unfinished" && x.State == "interrupted"), "Disposal without a result cannot imply success");
        for (int i = 0; i < 40; i++) { using var job = hub.Begin("bounded", true); job.Complete(); }
        check(hub.Items.Count == 32, "Task history stays bounded");
        var active = Enumerable.Range(0, 16).Select(_ => hub.Begin("active", true)).ToArray();
        bool capped = false; try { hub.Begin("extra", true); } catch (InvalidOperationException) { capped = true; }
        check(capped, "Task engine caps concurrent operations"); foreach (var job in active) job.Dispose();
    }
}
