using System.ComponentModel;
using System.Runtime.CompilerServices;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App.ViewModels;

internal sealed class RepairCaseViewModel(ICaseRepository repository) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public RepairCaseDocument? Current { get; private set; }
    public bool Dirty { get; private set; }
    public bool Saving { get; private set; }
    public string Status { get; private set; } = "";
    public string Stage { get; private set; } = "caseDiscover";
    public CaseEvidence? LatestEvidence => Current?.Evidence.LastOrDefault();
    public string Title { get => Current?.Title ?? ""; set { Ensure(); if (Current!.Title == value) return; Change(Current with { Title = value[..Math.Min(160, value.Length)] }); } }
    public bool StoreEvidence { get => Current?.StoreEvidence == true; set { Ensure(); Change(Current! with { StoreEvidence = value }); } }
    private void Notify([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    private void Ensure() { Current ??= RepairCaseDocument.Create(I18n.T("caseUntitled")); }
    private void Change(RepairCaseDocument value)
    { Current = value; Dirty = true; Status = I18n.T("caseUnsaved"); foreach (string name in new[] { nameof(Current), nameof(Title), nameof(StoreEvidence), nameof(Status), nameof(Stage), nameof(Dirty) }) Notify(name); }
    public void Touch() { if (Current is not null && !Saving) Change(Current); }
    public void Reset() { Current = null; Dirty = false; Stage = "caseDiscover"; Status = ""; Notify(nameof(Current)); Notify(nameof(Title)); Notify(nameof(StoreEvidence)); Notify(nameof(Status)); Notify(nameof(Stage)); Notify(nameof(Dirty)); }
    public void Load(RepairCaseDocument value) { RepairCaseCodec.Validate(value); Current = value; Dirty = false; Stage = value.Evidence.Count == 0 && value.Analyses.Count == 0 ? "caseDiscover" : "caseVerify"; Status = I18n.T("caseLoadedReadOnly"); Notify(nameof(Current)); Notify(nameof(Title)); Notify(nameof(StoreEvidence)); Notify(nameof(Stage)); Notify(nameof(Status)); }
    public void Capture(CaseEvidence evidence)
    {
        Ensure(); var all = Current!.Evidence.Append(evidence).TakeLast(20).ToArray(); var ids = all.Select(e => e.Id).ToHashSet();
        Stage = "caseDiscover";
        Change(Current with { Evidence = all, Retests = Current.Retests.Where(r => ids.Contains(r.After) && (r.Before is null || ids.Contains(r.Before.Value))).ToArray() });
    }
    public void Analyze(string text, ErrorAdvice advice, string sharedDiagnostics)
    {
        Ensure(); Guid? shared = null; var evidence = Current!.Evidence.ToArray();
        if (sharedDiagnostics.Length > 0)
        {
            int index = Array.FindLastIndex(evidence, e => e.Text == sharedDiagnostics);
            if (index >= 0) { shared = evidence[index].Id; evidence[index] = evidence[index] with { Reviewed = true }; }
            else if (evidence.LastOrDefault() is { } latest)
            {
                var edited = latest with { Id = Guid.NewGuid(), CollectedUtc = DateTimeOffset.UtcNow, Text = sharedDiagnostics, Device = null, Services = [], Edited = true, Reviewed = true };
                evidence = evidence.Append(edited).TakeLast(20).ToArray(); shared = edited.Id;
            }
        }
        Stage = "caseAnalyze";
        Change(Current with { Title = Current.Title == I18n.T("caseUntitled") && text.Length > 0 ? DeviceHealth.Label(text) : Current.Title,
            Evidence = evidence, Analyses = Current.Analyses.Append(new(DateTimeOffset.UtcNow, DiagnosticPrivacy.Sanitize(text, Environment.UserName, Environment.MachineName), advice.Message ?? advice.Summary, advice.Cause, advice.Steps, advice.Confidence, shared)).TakeLast(20).ToArray() });
    }
    public Guid BeginAction(string id)
    {
        Ensure(); Stage = "caseRepair"; var action = new CaseAction(Guid.NewGuid(), DateTimeOffset.UtcNow, RepairCatalog.Get(id).Id, "pending", null, LatestEvidence?.Id);
        Change(Current! with { Actions = Current!.Actions.Append(action).TakeLast(40).ToArray() }); return action.Id;
    }
    public void FinishAction(Guid id, string state, int? exit)
    { if (Current is null) return; Change(Current with { Actions = Current.Actions.Select(a => a.Id == id ? a with { Status = state, ExitCode = exit } : a).ToArray() }); }
    public CaseComparison Retest(CaseEvidence after)
    {
        Ensure(); var before = LatestEvidence; var action = Current!.Actions.LastOrDefault();
        if (action?.BeforeEvidence is { } baseline) before = Current.Evidence.FirstOrDefault(e => e.Id == baseline);
        var comparison = CaseVerification.Compare(before, after); Capture(after); Stage = "caseVerify";
        Change(Current! with { Retests = Current!.Retests.Append(new(DateTimeOffset.UtcNow, before?.Id, after.Id, action?.Id, comparison)).TakeLast(20).ToArray() }); return comparison;
    }
    public async Task<bool> Save(string firstObserved, string preview, CancellationToken token)
    {
        if (Saving || Current is null) return false;
        try
        {
            DateTimeOffset? observed = null;
            if (firstObserved.Trim().Length > 0)
            {
                if (!DateTime.TryParseExact(firstObserved.Trim(), "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var parsed) || parsed > DateTime.Now.AddDays(1))
                    throw new InvalidDataException("caseDateInvalid");
                observed = new DateTimeOffset(parsed).ToUniversalTime();
            }
            var evidence = Current.Evidence.ToArray();
            if (Current.StoreEvidence && evidence.Length > 0)
            {
                var last = evidence[^1]; string clean = DiagnosticPrivacy.Sanitize(preview, Environment.UserName, Environment.MachineName);
                evidence[^1] = last.Text == clean ? last with { Reviewed = true } : last with { Text = clean, Device = null, Services = [], Edited = true, Reviewed = true };
            }
            Change(Current with { Title = string.IsNullOrWhiteSpace(Current.Title) ? I18n.T("caseUntitled") : Current.Title.Trim(), FirstObservedUtc = observed, Evidence = evidence });
            Saving = true; Notify(nameof(Saving)); var snapshot = Current;
            var saved = await repository.Save(snapshot, snapshot.Revision, token);
            if (Current?.Id == saved.Id)
            {
                bool unchanged = ReferenceEquals(Current, snapshot); Current = Current with { Revision = saved.Revision, UpdatedUtc = saved.UpdatedUtc }; Dirty = !unchanged;
                Status = I18n.T(Dirty ? "caseUnsaved" : "caseSaved"); Notify(nameof(Current)); Notify(nameof(Dirty)); Notify(nameof(Status));
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException or System.Security.Cryptography.CryptographicException or System.ComponentModel.Win32Exception)
        { Status = I18n.T(ex.Message is "caseConflict" or "caseDateInvalid" ? ex.Message : "caseSaveFailed"); Notify(nameof(Status)); return false; }
        finally { Saving = false; Notify(nameof(Saving)); }
    }
    public async Task SaveActionMetadata(CancellationToken token)
    {
        if (Current is not { Revision: > 0 } snapshot || Saving) return;
        try
        {
            Saving = true; Notify(nameof(Saving)); var saved = await repository.Save(snapshot, snapshot.Revision, token);
            if (Current?.Id == saved.Id) { bool unchanged = ReferenceEquals(Current, snapshot); Current = Current with { Revision = saved.Revision, UpdatedUtc = saved.UpdatedUtc }; Dirty = !unchanged; Status = I18n.T(Dirty ? "caseUnsaved" : "caseSaved"); Notify(nameof(Current)); Notify(nameof(Status)); Notify(nameof(Dirty)); }
        }
        catch (Exception) { Status = I18n.T("caseSaveFailed"); Notify(nameof(Status)); }
        finally { Saving = false; Notify(nameof(Saving)); }
    }
}
