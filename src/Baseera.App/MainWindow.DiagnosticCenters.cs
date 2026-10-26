using System.Globalization;
using System.Windows.Automation;
using System.Windows.Shapes;
using Baseera.App.Services;
using Baseera.Core;
using Microsoft.Win32;

namespace Baseera.App;

internal sealed record DetectiveHistory(int Format, IReadOnlyList<PerformancePoint> Points, IReadOnlyList<DateTimeOffset> Marks);

public sealed partial class MainWindow
{
    private bool doctorEvents, doctorServices, doctorDrivers;
    private CaseEvidence? doctorEvidence;
    private readonly List<PerformancePoint> detectivePoints = [];
    private readonly List<DateTimeOffset> detectiveMarks = [];
    private bool detectiveRunning, networkRunning;
    private Action? drawDetective, drawNetwork;
    private InvestigationReport? securityEvidence, signatureEvidence, reputationEvidence;
    private StorageFile? signatureFile;
    private bool reputationConsent, networkConsent, networkRepeat;
    private string networkHost = "example.com", reputationKeyDraft = "";
    private readonly List<NetworkReading> networkReadings = [];
    internal Func<DiagnosticOptions, CancellationToken, Task<CaseEvidence>>? DoctorCaptureOverride { get; set; }
    internal Func<CancellationToken, Task<PerformancePoint>>? DetectiveSampleOverride { get; set; }
    internal Func<CancellationToken, Task<InvestigationReport>>? SecurityProbeOverride { get; set; }
    internal Func<string, CancellationToken, Task<InvestigationReport>>? SignatureProbeOverride { get; set; }
    internal Func<string, string, CancellationToken, Task<InvestigationReport>>? ReputationProbeOverride { get; set; }
    internal Func<string, CancellationToken, Task<NetworkReading>>? NetworkProbeOverride { get; set; }
    internal int DetectiveSampleLimitForSmoke { get; set; } = 120;
    internal TimeSpan? DetectiveDelayForSmoke { get; set; }

    private void DoctorPage(StackPanel body)
    {
        if (!Guard(body, "doctor")) return;
        Help(body, "hubDoctorScope");
        var choices = Stack(); choices.Children.Add(Toggle("hubIncludeEvents", doctorEvents, value => doctorEvents = value));
        choices.Children.Add(Toggle("smartServiceStatus", doctorServices, value => doctorServices = value));
        choices.Children.Add(Toggle("hubDrivers", doctorDrivers, value => doctorDrivers = value));
        choices.Children.Add(Text(I18n.T("hubDoctorDefaultSources"), 12, true));
        choices.Children.Add(HubButton("hubCollect", async ct =>
        {
            var options = new DiagnosticOptions(SystemInfo: true, ApplicationEvents: doctorEvents, SystemEvents: doctorEvents, DeviceHealth: true, ServiceStatus: doctorServices, Drivers: doctorDrivers);
            var sample = await (DoctorCaptureOverride?.Invoke(options, ct) ?? Services.Diagnostics.Capture(options, ct)); ct.ThrowIfCancellationRequested();
            if (sample.Text is null || sample.Text.Length > DiagnosticPrivacy.MaxCharacters || sample.Sources != options) throw new InvalidDataException(I18n.T("hubInvalidEvidence"));
            doctorEvidence = sample;
            if (page == "doctor") Render(true);
        }, true)); body.Children.Add(Card(choices));
        if (doctorEvidence is not { } evidence) { EmptyState(body, "doctor", "hubReady", "hubNothingCollected"); return; }
        var evidenceIndex = DiagnosticEvidence.Read(evidence.Text);
        var facts = new InvestigationReport("doctor", evidence.CollectedUtc,
            [new("hubCollectionState", "state:" + (evidence.Complete ? "hubEvidenceCollected" : "hubPartial")),
             new("hubEventCount", evidenceIndex.EventCount.ToString(CultureInfo.InvariantCulture)),
             new("hubErrorCodeCount", evidenceIndex.ErrorCodes.Count.ToString(CultureInfo.InvariantCulture))], ["hubDoctorLimits"]);
        HubReport(body, facts);
        var preview = HubTextBox("HubDoctorPreview", evidence.Text, true, 280); AutomationProperties.SetName(preview, I18n.T("hubDiagnosticPreview")); body.Children.Add(preview);
        body.Children.Add(Text(I18n.T("hubDoctorConfidence"), 12, true));
        HubShare(body, evidence.Text, evidence);
    }

    private static void ValidateDetective(DetectiveHistory history)
    {
        if (history is null || history.Format != 1 || history.Points is null || history.Marks is null || history.Points.Count > 120 || history.Marks.Count > 20) throw new InvalidDataException("Invalid performance history.");
        PerformanceEvidence.ValidateHistory(history.Points);
        if (history.Marks.Any(m => m > DateTimeOffset.UtcNow.AddMinutes(2))) throw new InvalidDataException("Invalid symptom time.");
        DateTimeOffset? previous = null;
        foreach (var p in history.Points)
        {
            static bool Percent(double? n) => n is null || double.IsFinite(n.Value) && n is >= 0 and <= 100;
            static bool NonNegative(double? n) => n is null || double.IsFinite(n.Value) && n is >= 0 and <= 1e15;
            if (p is null || p.Processes is null || p.Processes.Count > 32 || !Percent(p.Cpu) || !Percent(p.Memory) || !NonNegative(p.DiskBytes) || !NonNegative(p.DiskLatencyMs) || p.Time > DateTimeOffset.UtcNow.AddMinutes(2) || previous is { } time && p.Time <= time)
                throw new InvalidDataException("Invalid performance sample.");
            foreach (var process in p.Processes)
                if (process is null || process.Name is null || process.Name.Length > 120 || process.Id < 0 || process.Started < 0 || process.Memory < 0 || !Percent(process.Cpu) || !NonNegative(process.IoBytes)) throw new InvalidDataException("Invalid process sample.");
            previous = p.Time;
        }
    }
    private PerformancePoint[] DetectiveWindow()
    {
        var all = detectivePoints.TakeLast(120).ToArray(); int start = 0;
        for (int n = 1; n < all.Length; n++) if (all[n].Time - all[n - 1].Time > TimeSpan.FromSeconds(30)) start = n;
        var current = all.Skip(start).ToArray();
        return detectiveMarks.LastOrDefault() is { } mark && mark != default ? current.Where(p => Math.Abs((p.Time - mark).TotalSeconds) <= 30).ToArray() : current.TakeLast(30).ToArray();
    }
    private InvestigationReport DetectiveReport()
    {
        var facts = new List<InvestigationFact> { new("hubSampleCount", detectivePoints.Count.ToString(CultureInfo.InvariantCulture)) };
        if (detectivePoints.LastOrDefault() is { } latest)
        {
            facts.Add(new("deviceCpu", HubPercent(latest.Cpu))); facts.Add(new("deviceMemory", HubPercent(latest.Memory)));
            facts.Add(new("hubDiskRate", HubRate(latest.DiskBytes))); facts.Add(new("hubDiskLatency", HubMilliseconds(latest.DiskLatencyMs)));
        }
        foreach (var mark in detectiveMarks.TakeLast(5)) facts.Add(new("hubSymptomMark", HubTime(mark)));
        var coincidences = PerformanceEvidence.Analyze(DetectiveWindow());
        foreach (var finding in coincidences)
            facts.Add(new(finding.Kind, finding.Process, string.Format(CultureInfo.CurrentCulture, I18n.T("hubCoincidenceSamples"), finding.Samples) + (finding.Correlation is { } r ? " · r=" + r.ToString("0.00", CultureInfo.InvariantCulture) : "")));
        if (coincidences.Count == 0 && detectivePoints.Count > 0) facts.Add(new("hubObservation", "state:hubNoCoincidence"));
        return new("detective", detectivePoints.LastOrDefault()?.Time ?? DateTimeOffset.UtcNow, facts, ["hubPerformanceLimits", "hubCoincidenceLimits", "hubHistoryPrivacy"]);
    }
    private string HubPercent(double? value) => value is { } n ? n.ToString("0.0", CultureInfo.CurrentCulture) + "%" : I18n.T("hubUnavailable");
    private string HubRate(double? value) => value is { } n ? (n / 1048576d).ToString("0.00", CultureInfo.CurrentCulture) + " MiB/s" : I18n.T("hubUnavailable");
    private string HubMilliseconds(double? value) => value is { } n ? n.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : I18n.T("hubUnavailable");

    private void DetectivePage(StackPanel body)
    {
        if (!Guard(body, "detective")) return;
        Help(body, "hubPerformanceScope");
        var actions = new WrapPanel(); var start = HubButton("hubStartObserve", ObservePerformance, true);
        var stop = Action("hubStopObserve", () => operation?.Cancel()); DeviceId(stop, "Hub-detective-stop");
        var mark = Action("hubMarkSymptom", () => { detectiveMarks.Add(DateTimeOffset.UtcNow); if (detectiveMarks.Count > 20) detectiveMarks.RemoveAt(0); drawDetective?.Invoke(); }); DeviceId(mark, "Hub-detective-mark");
        var clearMarks = Action("hubClearMarks", () => { detectiveMarks.Clear(); drawDetective?.Invoke(); }); DeviceId(clearMarks, "Hub-detective-clear-marks");
        var save = HubButton("hubSaveHistory", async ct => { ValidateDetective(new(1, detectivePoints.ToArray(), detectiveMarks.ToArray())); await InvestigationStore.Save("performance", new DetectiveHistory(1, detectivePoints.ToArray(), detectiveMarks.ToArray()), ct); });
        var load = HubButton("hubLoadHistory", async ct =>
        {
            var saved = await InvestigationStore.Load<DetectiveHistory>("performance", ct); if (saved is null) { SetOperationStatus("hubNoHistory"); return; } ValidateDetective(saved);
            detectivePoints.Clear(); detectivePoints.AddRange(saved.Points); detectiveMarks.Clear(); detectiveMarks.AddRange(saved.Marks); if (page == "detective") drawDetective?.Invoke();
        });
        foreach (var action in new[] { start, stop, mark, clearMarks, save, load }) actions.Children.Add(action); body.Children.Add(actions);
        var state = Text("", 12, true); DeviceId(state, "HubDetectiveState"); AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite); body.Children.Add(state);
        var charts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1 }; body.Children.Add(charts);
        Canvas Chart(string key, string scale, Func<PerformancePoint, double?> value, double maximum)
        {
            var box = Stack(); box.Children.Add(Text(I18n.T(key) + " · " + scale, 12, true)); var canvas = new Canvas { Height = 90, ClipToBounds = true }; box.Children.Add(canvas); charts.Children.Add(Card(box, 14));
            canvas.SizeChanged += (_, _) => DrawDetectiveChart(canvas, value, maximum); return canvas;
        }
        var cpu = Chart("deviceCpu", "0–100%", p => p.Cpu, 100); var memory = Chart("deviceMemory", "0–100%", p => p.Memory, 100); var disk = Chart("hubDiskLatency", "0–200 ms", p => p.DiskLatencyMs, 200);
        var table = HubTextBox("HubDetectiveSampleTable", "", true, 220); AutomationProperties.SetName(table, I18n.T("hubSampleTable")); body.Children.Add(new Expander { Header = I18n.T("hubSampleTable"), Content = table, Margin = new Thickness(0, 4, 0, 16) });
        var observations = Stack(); var processes = Stack(); body.Children.Add(observations); body.Children.Add(processes);
        drawDetective = () =>
        {
            if (page != "detective") return;
            start.IsEnabled = !detectiveRunning; stop.IsEnabled = detectiveRunning; mark.IsEnabled = detectiveRunning;
            clearMarks.IsEnabled = detectiveMarks.Count > 0;
            save.IsEnabled = !detectiveRunning && detectivePoints.Count > 0; load.IsEnabled = !detectiveRunning;
            state.Text = I18n.T(detectiveRunning ? "hubObserving" : "hubObservationStopped") + " · " + detectivePoints.Count + "/120";
            if (detectivePoints.Count > 0) state.Text += "\n" + HubTime(detectivePoints[0].Time) + " → " + HubTime(detectivePoints[^1].Time);
            table.Text = string.Join("\t", I18n.T("hubTime"), "CPU %", "RAM %", I18n.T("hubDiskRate"), I18n.T("hubDiskLatency")) + "\n" + string.Join("\n", detectivePoints.Select(p => string.Join("\t", HubTime(p.Time), HubPercent(p.Cpu), HubPercent(p.Memory), HubRate(p.DiskBytes), HubMilliseconds(p.DiskLatencyMs))));
            DrawDetectiveChart(cpu, p => p.Cpu, 100); DrawDetectiveChart(memory, p => p.Memory, 100); DrawDetectiveChart(disk, p => p.DiskLatencyMs, 200);
            observations.Children.Clear(); processes.Children.Clear();
            if (detectivePoints.Count == 0) { observations.Children.Add(Text(I18n.T("hubNothingCollected"), 12, true)); return; }
            var report = DetectiveReport(); HubReport(observations, report); HubShare(observations, HubReportText(report));
            SectionHeading(processes, "hubSampledProcesses"); processes.Children.Add(Text(I18n.T("hubProcessIoLimits"), 12, true));
            foreach (var p in detectivePoints[^1].Processes)
            {
                var row = Stack(); row.Children.Add(Text(p.Name + " · PID " + p.Id, 14)); row.Children.Add(Text("CPU " + HubPercent(p.Cpu) + " · " + (p.Memory / 1048576d).ToString("0.0") + " MiB · I/O " + HubRate(p.IoBytes), 12, true)); processes.Children.Add(Card(row, 12));
            }
        }; drawDetective();
    }
    private async Task ObservePerformance(CancellationToken token)
    {
        if (detectiveRunning) return;
        if (detectivePoints.Count > 0 && DateTimeOffset.UtcNow - detectivePoints[^1].Time > TimeSpan.FromSeconds(30)) { detectivePoints.Clear(); detectiveMarks.Clear(); }
        detectiveRunning = true; if (page == "detective") drawDetective?.Invoke();
        try
        {
            int limit = smokeMode ? Math.Clamp(DetectiveSampleLimitForSmoke, 1, 120) : 120;
            for (int n = 0; n < limit; n++)
            {
                token.ThrowIfCancellationRequested(); if (page != "detective") break;
                var point = await (DetectiveSampleOverride?.Invoke(token) ?? PerformanceDetective.Sample(token)); token.ThrowIfCancellationRequested();
                ValidateDetective(new(1, [point], [])); if (page != "detective") break;
                if (detectivePoints.Count > 0 && point.Time <= detectivePoints[^1].Time) throw new InvalidDataException(I18n.T("hubInvalidEvidence"));
                detectivePoints.Add(point); if (detectivePoints.Count > 120) detectivePoints.RemoveAt(0); drawDetective?.Invoke();
                if (n + 1 < limit) await Task.Delay(smokeMode ? DetectiveDelayForSmoke ?? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(3), token);
            }
        }
        finally { detectiveRunning = false; if (page == "detective") drawDetective?.Invoke(); }
    }
    private void DrawDetectiveChart(Canvas canvas, Func<PerformancePoint, double?> select, double maximum)
    {
        canvas.Children.Clear(); double width = canvas.ActualWidth, height = canvas.Height; if (width < 20) return;
        foreach (double fraction in new[] { 0d, .5, 1d }) canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = fraction * (height - 4) + 2, Y2 = fraction * (height - 4) + 2, Stroke = Resource("Line"), StrokeThickness = 1 });
        if (detectivePoints.Count == 0) return; var first = detectivePoints[0].Time; double span = Math.Max(1, (detectivePoints[^1].Time - first).TotalSeconds);
        Polyline CreateTrace() => new() { Stroke = Resource("Accent"), StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
        var line = CreateTrace();
        foreach (var point in detectivePoints)
        {
            if (select(point) is not { } value || !double.IsFinite(value)) { if (line.Points.Count > 0) canvas.Children.Add(line); line = CreateTrace(); continue; }
            line.Points.Add(new System.Windows.Point(Math.Clamp((point.Time - first).TotalSeconds / span, 0, 1) * width, 2 + (1 - Math.Clamp(value / maximum, 0, 1)) * (height - 4)));
        }
        if (line.Points.Count > 0) canvas.Children.Add(line);
    }

    private void SecurityPage(StackPanel body)
    {
        if (!Guard(body, "security")) return;
        Help(body, "hubSecurityScope"); body.Children.Add(HubButton("hubReadSecurity", async ct =>
        { var report = await (SecurityProbeOverride?.Invoke(ct) ?? ProbeRunner.Read<InvestigationReport>("security", ct)); ValidateHubReport(report); securityEvidence = report; if (page == "security") Render(true); }, true));
        if (securityEvidence is { } security) { HubReport(body, security); HubShare(body, HubReportText(security)); }
        SectionHeading(body, "hubFileTrust"); var fileBox = Stack(); fileBox.Children.Add(Text(signatureFile?.Path ?? I18n.T("hubChooseLocalFile"), 12, true));
        var choose = Action("hubChooseLocalFile", () =>
        {
            if (operation is not null) return; var picker = new OpenFileDialog { CheckFileExists = true, Multiselect = false };
            if (picker.ShowDialog(this) != true) return;
            try
            {
                string full = System.IO.Path.GetFullPath(picker.FileName); LocalFileScope.RequireFile(System.IO.Path.GetDirectoryName(full)!, full);
                var info = new FileInfo(full); signatureFile = new(info.FullName, info.Length, info.LastWriteTimeUtc); signatureEvidence = reputationEvidence = null; reputationConsent = false; Render(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { SetOperationStatus("hubUnavailable"); }
        }); DeviceId(choose, "Hub-security-file"); fileBox.Children.Add(choose);
        var inspect = HubButton("hubInspectSignature", async ct =>
        {
            if (signatureFile is not { } file) return; if (file.Bytes > 1073741824 || !LocalFileScope.Unchanged(file)) throw new IOException(I18n.T("hubChangedFile"));
            var report = await (SignatureProbeOverride?.Invoke(file.Path, ct) ?? ProbeRunner.Read<InvestigationReport>("signature", ct, file.Path)); ValidateHubReport(report);
            if (!LocalFileScope.Unchanged(file)) throw new IOException(I18n.T("hubChangedFile")); signatureEvidence = report; reputationEvidence = null; reputationConsent = false; if (page == "security") Render(true);
        }); inspect.IsEnabled = signatureFile is not null; fileBox.Children.Add(inspect); fileBox.Children.Add(Text(I18n.T("hubSignatureLimits"), 12, true)); body.Children.Add(Card(fileBox));
        if (signatureEvidence is { } signature) HubReport(body, signature);
        SectionHeading(body, "hubReputationLookup"); var reputation = Stack(); reputation.Children.Add(Text(I18n.T("hubReputationPrivacy"), 12, true));
        var consent = Toggle("hubReputationConsent", reputationConsent, value => reputationConsent = value); DeviceId(consent, "HubReputationConsent");
        var key = new PasswordBox { MaxLength = 500, Margin = new Thickness(0, 6, 0, 6), Password = reputationKeyDraft }; DeviceId(key, "HubVirusTotalKey"); AutomationProperties.SetName(key, I18n.T("hubReputationKey"));
        key.PasswordChanged += (_, _) => { reputationKeyDraft = key.Password; reputationConsent = false; consent.IsChecked = false; };
        reputation.Children.Add(Text(I18n.T("hubReputationKey"), 12)); reputation.Children.Add(key);
        reputation.Children.Add(HubButton("hubSaveReputationKey", _ => { if (key.Password.Length > 0) { Settings.SetReputationKey(key.Password); reputationKeyDraft = ""; key.Clear(); } reputationConsent = false; consent.IsChecked = false; return Task.CompletedTask; }));
        reputation.Children.Add(consent);
        reputation.Children.Add(HubButton("hubCheckReputation", async ct =>
        {
            if (reputationKeyDraft.Length > 0) throw new InvalidOperationException(I18n.T("hubSaveReputationKey"));
            if (!reputationConsent || signatureFile is not { } file || signatureEvidence is null) throw new InvalidOperationException(I18n.T("hubReputationNeedsConsent"));
            if (!LocalFileScope.Unchanged(file)) { reputationConsent = false; consent.IsChecked = false; throw new IOException(I18n.T("hubChangedFile")); }
            string? hash = signatureEvidence.Facts.FirstOrDefault(f => f.Label == "hubSha256")?.Value;
            if (hash is null || hash.Length != 64) throw new InvalidDataException(I18n.T("hubInvalidEvidence"));
            string apiKey = Settings.GetReputationKey(); if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException(I18n.T("hubReputationNeedsKey"));
            reputationConsent = false; consent.IsChecked = false;
            string selectedRoot = System.IO.Path.GetDirectoryName(file.Path)!;
            LocalFileScope.RequireFile(selectedRoot, file.Path);
            if (!string.Equals(await LocalFileScope.Hash(file, ct), hash, StringComparison.OrdinalIgnoreCase))
            { signatureEvidence = reputationEvidence = null; throw new IOException(I18n.T("hubChangedFile")); }
            var report = await (ReputationProbeOverride?.Invoke(hash, apiKey, ct) ?? FileTrust.Reputation(hash, apiKey, ct)); ValidateHubReport(report);
            LocalFileScope.RequireFile(selectedRoot, file.Path);
            if (!string.Equals(await LocalFileScope.Hash(file, ct), hash, StringComparison.OrdinalIgnoreCase))
            { signatureEvidence = reputationEvidence = null; throw new IOException(I18n.T("hubChangedFile")); }
            reputationEvidence = report; if (page == "security") Render(true);
        })); body.Children.Add(Card(reputation));
        if (reputationEvidence is { } result) { HubReport(body, result); HubShare(body, HubReportText(result)); }
    }
    internal void SetSignatureFixture(string path, InvestigationReport report)
    {
        if (!smokeMode) throw new InvalidOperationException("Fixture setup is limited to smoke mode."); ValidateHubReport(report);
        var info = new FileInfo(path); signatureFile = new(info.FullName, info.Length, info.LastWriteTimeUtc); signatureEvidence = report; reputationEvidence = null; reputationConsent = false;
    }

    private InvestigationReport NetworkReport(NetworkReading reading)
    {
        NetworkEvidence.Validate(reading); var facts = new List<InvestigationFact>
        {
            new("hubHost", reading.Host), new("hubDnsDuration", HubMilliseconds(reading.DnsMs)),
            new("hubPingReplies", reading.Replies.Count + " / " + reading.Sent), new("hubPacketLossMetric", HubPercent(NetworkEvidence.Loss(reading))),
            new("hubMeanLatency", reading.Replies.Count == 0 ? I18n.T("hubUnavailable") : HubMilliseconds(reading.Replies.Average())),
            new("hubLatencySpread", reading.Replies.Count < 2 ? I18n.T("hubUnavailable") : (reading.Replies.Max() - reading.Replies.Min()).ToString(CultureInfo.InvariantCulture) + " ms"),
            new("hubAdapters", reading.AdapterState), new("hubWifiQuality", HubPercent(reading.WifiQuality)),
            new("hubWifiLink", reading.WifiLinkMbps is { } rate ? rate.ToString("0.0", CultureInfo.CurrentCulture) + " Mbps" : I18n.T("hubUnavailable"))
        };
        var previous = networkReadings.Where(r => r.Host == reading.Host && r.Time < reading.Time).LastOrDefault();
        if (previous is not null && previous.Replies.Count > 0 && reading.Replies.Count > 0)
            facts.Add(new("hubLatencyChange", (reading.Replies.Average() - previous.Replies.Average()).ToString("+0.0;-0.0;0.0", CultureInfo.CurrentCulture) + " ms", HubTime(previous.Time)));
        return new("network", reading.Time, facts, NetworkEvidence.Interpret(reading).Concat(["hubWifiLimits"]).Distinct().ToArray());
    }
    private void NetworkPage(StackPanel body)
    {
        if (!Guard(body, "network")) return;
        Help(body, "hubNetworkScope"); var input = Stack(); input.Children.Add(Text(I18n.T("hubHost"), 12));
        var host = HubTextBox("HubNetworkHost", networkHost); host.MaxLength = 253; host.IsEnabled = !networkRunning; AutomationProperties.SetName(host, I18n.T("hubHost")); input.Children.Add(host);
        var consent = Toggle("hubNetworkConsent", networkConsent, value => networkConsent = value); DeviceId(consent, "HubNetworkConsent"); consent.IsEnabled = !networkRunning;
        host.TextChanged += (_, _) => { networkHost = host.Text; networkConsent = false; consent.IsChecked = false; };
        input.Children.Add(consent); var repeat = Toggle("hubNetworkRepeat", networkRepeat, value => networkRepeat = value); repeat.IsEnabled = !networkRunning; DeviceId(repeat, "HubNetworkRepeat"); input.Children.Add(repeat);
        var actions = new WrapPanel(); var run = HubButton("hubMeasureNetwork", async ct =>
        {
            if (!networkConsent) throw new InvalidOperationException(I18n.T("hubNetworkNeedsConsent"));
            string target = NetworkEvidence.Host(networkHost); bool repeating = networkRepeat; networkConsent = false; networkRunning = true; if (page == "network") drawNetwork?.Invoke();
            try
            {
                for (int n = 0; n < (repeating ? 5 : 1); n++)
                {
                    ct.ThrowIfCancellationRequested(); if (page != "network") break;
                    var measured = await (NetworkProbeOverride?.Invoke(target, ct) ?? NetworkDoctor.Measure(target, ct)); NetworkEvidence.Validate(measured); ct.ThrowIfCancellationRequested();
                    if (!string.Equals(measured.Host, target, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(I18n.T("hubInvalidEvidence"));
                    networkReadings.Add(measured); if (networkReadings.Count > 100) networkReadings.RemoveAt(0); if (page == "network") drawNetwork?.Invoke();
                    if (repeating && n < 4) await Task.Delay(TimeSpan.FromSeconds(10), ct);
                }
            }
            finally { networkRunning = false; if (page == "network") drawNetwork?.Invoke(); }
        }, true);
        var save = HubButton("hubSaveHistory", async ct => { foreach (var row in networkReadings) NetworkEvidence.Validate(row); await InvestigationStore.Save("network", networkReadings.ToArray(), ct); });
        var load = HubButton("hubLoadHistory", async ct =>
        {
            var saved = await InvestigationStore.Load<NetworkReading[]>("network", ct); if (saved is null) { SetOperationStatus("hubNoHistory"); return; }
            if (saved.Length > 100) throw new InvalidDataException(I18n.T("hubInvalidEvidence")); foreach (var row in saved) NetworkEvidence.Validate(row);
            networkReadings.Clear(); networkReadings.AddRange(saved.OrderBy(r => r.Time)); if (page == "network") drawNetwork?.Invoke();
        }); actions.Children.Add(run); actions.Children.Add(save); actions.Children.Add(load); input.Children.Add(actions); input.Children.Add(Text(I18n.T("hubNetworkHistoryPrivacy"), 12, true)); body.Children.Add(Card(input));
        var state = Text("", 12, true); DeviceId(state, "HubNetworkState"); AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite); body.Children.Add(state);
        var result = Stack(); var history = Stack(); body.Children.Add(result); body.Children.Add(history);
        drawNetwork = () =>
        {
            if (page != "network") return;
            host.IsEnabled = consent.IsEnabled = repeat.IsEnabled = run.IsEnabled = !networkRunning; consent.IsChecked = networkConsent;
            save.IsEnabled = !networkRunning && networkReadings.Count > 0; load.IsEnabled = !networkRunning;
            state.Text = I18n.T(networkRunning ? "hubMeasuringNetwork" : "hubNetworkIdle"); result.Children.Clear(); history.Children.Clear();
            if (networkReadings.Count == 0) { result.Children.Add(Text(I18n.T("hubNothingCollected"), 12, true)); return; }
            var report = NetworkReport(networkReadings[^1]); HubReport(result, report); HubShare(result, HubReportText(report));
            SectionHeading(history, "hubMeasurementHistory");
            foreach (var row in networkReadings.TakeLast(20).Reverse())
                history.Children.Add(Card(Text(HubTime(row.Time) + " · " + row.Host + "\n" + I18n.T("hubMeanLatency") + ": " + (row.Replies.Count == 0 ? I18n.T("hubUnavailable") : HubMilliseconds(row.Replies.Average())) + " · " + I18n.T("hubPacketLossMetric") + ": " + HubPercent(NetworkEvidence.Loss(row)), 12), 12));
        }; drawNetwork();
    }
}
