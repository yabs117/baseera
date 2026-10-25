using Baseera.Core;

internal static class SmartRepairChecks
{
    internal static void Run(Action<bool, string> check)
    {
        static bool Reject(Action action) { try { action(); return false; } catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException) { return true; } }
        check(Reject(() => new DiagnosticOptions().Validate()) && Reject(() => new DiagnosticOptions(true, Hours: 73).Validate()), "Diagnostics require selected sources and a bounded time range");
        new DiagnosticOptions(SystemEvents: true).Validate();
        check(Reject(() => new DiagnosticOptions(true, MatchText: "invalid\nfilter").Validate()) && Reject(() => new DiagnosticOptions(true, MatchText: new string('x', 101)).Validate()), "Diagnostic filters reject oversized and multiline input instead of creating event queries");
        string eventHeader = "2026-10-25T10:30:00.0000000Z | Application | Example App | ID 1000 | Level 2";
        var indexed = DiagnosticEvidence.Read(eventHeader + "\n0x80070005 and 0X80070005\n" + eventHeader + "\n[Sample limit reached; additional matching events may exist.]\n[Source unavailable: permission denied]");
        check(indexed.EventCount == 2 && indexed.Groups.Single().Count == 2 && indexed.Groups[0].Latest.Offset == TimeSpan.Zero && indexed.ErrorCodes.Count == 1 && indexed.CoverageNotices == 2, "Evidence index groups exact event headers and repeated error codes without diagnosing a cause");
        check(DiagnosticEvidence.Read("No errors 0x8007000\nID 1000\nnot a timestamp | System | Provider | ID 1 | Level 2\n2026-10-25 | Security | Provider | ID 1 | Level 2").EventCount == 0, "Evidence ignores prose, invalid timestamps and unsupported log channels");
        check(DiagnosticEvidence.Read(new string('x', DiagnosticPrivacy.MaxCharacters) + "\n" + eventHeader).EventCount == 0 && DiagnosticEvidence.Read("").Groups.Count == 0, "Evidence indexing is bounded and does not invent events for empty data");
        string privateData = @"C:\Users\Alice\Documents\report.txt user@example.com 192.168.1.4 2001:db8::1 fe80:0000:0000:0000:0011:0022:0033:0044 password=secret; api_key=private; Bearer example.jwt.token; sk-secret0123456789 https://login:secret@example.invalid/?token=anothersecret HOSTNAME Alice";
        string sanitized = DiagnosticPrivacy.Sanitize(privateData, "Alice", "HOSTNAME");
        check(new[] { "Alice", "HOSTNAME", "user@example.com", "192.168.1.4", "2001:db8::1", "fe80:0000", "password=secret", "api_key=private", "example.jwt.token", "sk-secret0123456789", "login:secret", "anothersecret" }.All(x => !sanitized.Contains(x, StringComparison.OrdinalIgnoreCase)), "Diagnostic preview redacts identities, paths, addresses, passwords, bearer tokens and URL secrets");
        check(DiagnosticPrivacy.Sanitize("2026-10-24T12:43:12.000Z event 1000 0x80070005").Contains("12:43:12"), "Redaction preserves event timestamps and error codes");
        check(!DiagnosticPrivacy.Sanitize("{\"api_key\":\"private-value\"} \u202espoof").Contains("private-value") && !DiagnosticPrivacy.Sanitize("\u202espoof").Contains('\u202e'), "Quoted diagnostic secrets and bidirectional display overrides are removed");
        string bounded = DiagnosticPrivacy.Sanitize(new string('a', DiagnosticPrivacy.MaxCharacters - 1) + "😀tail");
        check(bounded.Length <= DiagnosticPrivacy.MaxCharacters && !char.IsHighSurrogate(bounded[^1]), "Diagnostic size bounds do not split a Unicode surrogate pair");
        check(RepairCatalog.All.Count == 3 && RepairCatalog.All.All(x => x.Executable.EndsWith(".exe") && !x.Arguments.Any(a => a.Contains(';') || a.Contains('&'))), "Repair catalog contains only three fixed native commands");
        check(Reject(() => RepairCatalog.Get("sfc-repair & calc.exe")) && Reject(() => RepairCatalog.ValidateRecommendations(["powershell.exe"])) && Reject(() => RepairCatalog.ValidateRecommendations(["flush-dns", "flush-dns"])), "Model-generated commands, unknown actions and duplicate recommendations are rejected");
        check(Reject(() => RepairApproval.Confirm("sfc-repair", false)), "Repair cannot be approved without acknowledgement");
        var approval = RepairApproval.Confirm("sfc-repair", true);
        check(approval.Consume().Command == "sfc.exe /scannow" && Reject(() => approval.Consume()), "Approval is bound to a canonical command and can only be consumed once");
        check(Reject(() => RepairApproval.Confirm("flush-dns", true, DateTimeOffset.UtcNow.AddMinutes(-6)).Consume()) && Reject(() => RepairApproval.Confirm("flush-dns", true, DateTimeOffset.UtcNow.AddMinutes(1)).Consume()), "Expired and future-dated repair approvals cannot execute");
        check(RepairCatalog.Get("sfc-repair").Administrator && RepairCatalog.Get("dism-repair").Network && !RepairCatalog.Get("flush-dns").Administrator, "Approval metadata distinguishes elevation, network access and limited DNS cache repair");
        check(RepairCatalog.Get("dism-repair").Arguments.Contains("/NoRestart"), "Native servicing explicitly suppresses restart");
    }
}
