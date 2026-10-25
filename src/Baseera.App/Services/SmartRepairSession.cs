using System.Text.Json;
using Baseera.Core;

namespace Baseera.App.Services;

internal sealed record RepairActivity(DateTimeOffset Time, string ActionId, string Command, string Status, int? ExitCode);
internal sealed record RepairObservation(DateTimeOffset Time, string Outcome);

// Window-scoped, memory-only. No settings, account IDs, keys or image bytes.
internal sealed class SmartRepairSession
{
    internal DateTimeOffset? AnalyzedAt { get; private set; }
    internal ErrorAdvice? Advice { get; private set; }
    private string description = "", diagnostics = "", provider = "", model = "";
    private bool hasImage;
    private readonly List<RepairActivity> activity = [];
    private readonly List<RepairObservation> observations = [];
    internal IReadOnlyList<RepairActivity> Activity => activity.AsReadOnly();
    internal IReadOnlyList<RepairObservation> Observations => observations.AsReadOnly();
    internal void Clear()
    { Advice = null; AnalyzedAt = null; description = diagnostics = provider = model = ""; hasImage = false; activity.Clear(); observations.Clear(); }

    internal void SetAnalysis(string text, bool image, string approvedDiagnostics, string service, string selectedModel, ErrorAdvice advice)
    {
        AnalyzedAt = DateTimeOffset.UtcNow; Advice = advice;
        description = text; hasImage = image; diagnostics = approvedDiagnostics; provider = service; model = selectedModel;
        // A new result starts a new case. Never attach a prior case's commands to it.
        activity.Clear(); observations.Clear();
    }
    internal void Record(string actionId, string status, int? exitCode = null)
    {
        if (status is not ("completed" or "declined" or "execution-error")) throw new ArgumentException("Unknown repair status.");
        if ((status == "completed") != exitCode.HasValue) throw new ArgumentException("Exit status does not match launch state.");
        var action = RepairCatalog.Get(actionId);
        if (activity.Count == 20) activity.RemoveAt(0);
        activity.Add(new(DateTimeOffset.UtcNow, action.Id, action.Command, status, exitCode));
    }
    internal void Observe(string outcome)
    {
        if (Advice is null || outcome is not ("resolved" or "still-present")) throw new InvalidOperationException("No case or invalid outcome.");
        if (observations.Count == 20) observations.RemoveAt(0);
        observations.Add(new(DateTimeOffset.UtcNow, outcome));
    }
    internal string Export(bool includeDiagnostics)
    {
        if (Advice is null) throw new InvalidOperationException("Analyze a case before exporting it.");
        string Clean(string value) => DiagnosticPrivacy.Sanitize(value, Environment.UserName, Environment.MachineName);
        return JsonSerializer.Serialize(new
        {
            format = "baseera-smart-repair-report-v1", exported_utc = DateTimeOffset.UtcNow, analyzed_utc = AnalyzedAt,
            notice = "Local report. AI advice and user-reported outcomes are not independent verification of a repair. Redaction is best effort; review before sharing. Screenshots and stored connection credentials/account identifiers are excluded.",
            provider, model, description = Clean(description), image_was_attached = hasImage,
            approved_diagnostics_included = includeDiagnostics && diagnostics.Length > 0,
            approved_diagnostics = includeDiagnostics ? Clean(diagnostics) : "",
            advice = new { summary = Clean(Advice.Summary), cause = Clean(Advice.Cause),
                steps = Advice.Steps.Select(Clean), checks = Advice.Checks.Select(Clean), confidence = Clean(Advice.Confidence) },
            repairs = activity, user_reported_outcomes = observations
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
