using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;
using Baseera.Core;
using Baseera.App.Services;

namespace Baseera.App;

internal static class DeviceCenterSmokeChecks
{
    private const long GiB = 1024L * 1024 * 1024;
    private static readonly DeviceSnapshot Fixture = new(DateTimeOffset.UtcNow, .85, 92, new(16 * GiB, GiB),
        [new("C:\\", 256 * GiB, 4 * GiB, true), new("D:\\", 512 * GiB, 256 * GiB, false)],
        [new(42, "Browser", 35, GiB), new(84, "Editor", 5, 2 * GiB), new(96, "Protected fixture", null, GiB / 10)],
        [new("Baseera", "deviceStartupUser"), new("Example launcher", "deviceStartupFolderUser")], true, []);
    private sealed class Source : IDeviceSource
    {
        internal int ProcessCalls, StartupCalls, CpuCalls;
        public int Processors => 4;
        public CpuTimes? Cpu() => ++CpuCalls % 2 == 1 ? new(0, 0, 0) : new(10, 50, 50);
        public MemoryReading? Memory() => new(16 * GiB, GiB);
        public IReadOnlyList<VolumeReading> Volumes(List<string> notices) => Fixture.Volumes;
        public IReadOnlyList<StartupReading> Startup(List<string> notices) { StartupCalls++; return Fixture.Startup; }
        public IReadOnlyList<ProcessSample> Processes(List<string> notices)
        {
            int iteration = ++ProcessCalls;
            return [new(42, "Browser", 1, TimeSpan.FromSeconds(iteration), GiB, iteration * Stopwatch.Frequency),
                new(84, "Reused PID", iteration, TimeSpan.FromSeconds(iteration), GiB, iteration * Stopwatch.Frequency)];
        }
    }
    private sealed class Diagnostics : IWindowsDiagnosticSource
    {
        internal int EventCalls, HealthCalls;
        public string SystemSummary() => throw new Exception("Unselected system source read.");
        public string DeviceHealth(CancellationToken token) { token.ThrowIfCancellationRequested(); HealthCalls++; return DeviceMonitor.DiagnosticText(Fixture); }
        public IEnumerable<string> Events(string channel, int hours, string filter, CancellationToken token)
        {
            Require(channel == "Application", "Unselected event channel read."); EventCalls++;
            return [DateTimeOffset.UtcNow.ToString("O") + " | Application | Fixture application | ID 1000 | Level 2\nFixture failure 0x80070005"];
        }
    }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Settle(Window owner) { await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(50); owner.UpdateLayout(); }
    internal static async Task Run(MainWindow owner, string output)
    {
        var source = new Source();
        var basic = await DeviceMonitor.Collect(false, CancellationToken.None, source, TimeSpan.Zero);
        Require(source.ProcessCalls == 0 && source.StartupCalls == 0 && basic.CpuPercent == 90 && basic.Findings.Count == 3 && !basic.PerformanceIncluded, "Basic monitoring reads private process/startup sources or computes incorrect values.");
        source = new(); var detailed = await DeviceMonitor.Collect(true, CancellationToken.None, source, TimeSpan.Zero);
        Require(detailed.Processes.Single(p => p.Id == 42).CpuPercent == 25 && detailed.Processes.Single(p => p.Id == 84).CpuPercent is null && source.StartupCalls == 1, "Process normalization/PID reuse handling or startup sampling failed.");
        using (var cancel = new CancellationTokenSource())
        {
            source = new(); var pending = DeviceMonitor.Collect(false, cancel.Token, source, TimeSpan.FromSeconds(10)); cancel.Cancel();
            bool stopped = false; try { await pending; } catch (OperationCanceledException) { stopped = true; }
            Require(stopped, "Device sampling ignored cancellation.");
        }
        var native = await DeviceMonitor.Collect(false, CancellationToken.None);
        Require(native.Memory?.UsedPercent is >= 0 and <= 100 && native.Volumes.Count > 0 && native.Processes.Count == 0 && native.Startup.Count == 0 && (native.CpuPercent is >= 0 and <= 100 || native.Notices.Contains("deviceCpuUnavailable")), "Native Windows read-only CPU/memory/volume sample failed.");
        var nativePerformance = await DeviceMonitor.Collect(true, CancellationToken.None);
        Require(nativePerformance.Processes.Count is > 0 and <= 512 && nativePerformance.Startup.Count <= 100 && nativePerformance.Processes.All(p => p.CpuPercent is null or >= 0 and <= 100), "Native bounded process/startup read failed.");
        // The native values above are neither exported nor used in preview images.
        string original = JsonSerializer.Serialize(Settings.Current), key = Settings.GetKey("gemini");
        try
        {
            Settings.Current.Onboarded = true; Settings.Current.ErrorAssistantEnabled = true;
            owner.DeviceSourceOverride = (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(Fixture); };
            foreach (string language in new[] { "ar", "en", "fr", "es" })
            foreach (bool dark in new[] { false, true })
            foreach (string page in new[] { "home", "health", "performance" })
            {
                Settings.Current.Language = language; Settings.Current.Dark = dark; owner.ApplyTheme(); owner.Navigate(page); await owner.RefreshDevice(); await Settle(owner);
                Require(Nodes(owner).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Navigate-health") && Nodes(owner).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Navigate-performance"), "New centers are absent from navigation.");
                Require(Find<Button>(owner, "DeviceCpuCard").IsVisible && Find<Button>(owner, "DeviceMemoryCard").IsVisible && Find<TextBlock>(owner, "DeviceTimestamp").Text.StartsWith(I18n.T("deviceLastRead").Split('{')[0]), "Metric/timestamp controls are unavailable.");
                if (page == "home") Require(Find<TextBlock>(owner, "DeviceObservationCount").Text == string.Format(I18n.T("deviceObservations"), 3) && Find<Button>(owner, "Quick-updates").IsVisible, "Home invented issues/update counts or lacks quick access.");
                if (page == "performance")
                {
                    var sort = Find<ComboBox>(owner, "DeviceProcessSort"); sort.SelectedIndex = 1;
                    var rows = Find<StackPanel>(owner, "DeviceProcesses"); Require(Nodes(rows).OfType<TextBlock>().First().Text.StartsWith("Editor"), "Memory sort is incorrect.");
                    var filter = Find<TextBox>(owner, "DeviceProcessFilter"); filter.Text = "Browser";
                    Require(Nodes(rows).OfType<TextBlock>().Count() == 1 && Nodes(rows).OfType<TextBlock>().Single().Text.StartsWith("Browser"), "Process filtering does not match current readings.");
                    filter.Clear(); sort.SelectedIndex = 0;
                }
                if (language is "ar" or "en") { await Task.Delay(220); App.Snapshot(owner, Path.Combine(output, "device-" + page + "-" + language + (dark ? "-dark" : "") + ".png")); }
            }
            var completion = new TaskCompletionSource<DeviceSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken requestToken = default;
            owner.Navigate("home"); owner.DeviceSourceOverride = (_, ct) => { requestToken = ct; return completion.Task; };
            var waiting = owner.RefreshDevice(); owner.Navigate("settings"); completion.SetResult(Fixture with { CpuPercent = 1 }); await waiting;
            Require(requestToken.IsCancellationRequested && owner.DeviceSnapshotForSmoke?.CpuPercent == 92, "A sample from a departed page overwrote the current cache.");
            owner.DeviceSourceOverride = (_, _) => throw new IOException("No private path may be echoed"); owner.Navigate("home"); await owner.RefreshDevice();
            Require(Find<TextBlock>(owner, "DeviceTimestamp").Text == I18n.T("deviceReadFailed"), "Failed refresh disguised old readings as current.");
            owner.DeviceSourceOverride = (_, _) => Task.FromResult(Fixture with { CpuPercent = null, Memory = null, Volumes = [], Notices = ["deviceCpuUnavailable"] });
            await owner.RefreshDevice(); Require(AutomationProperties.GetName(Find<Button>(owner, "DeviceHealthCard")).Contains(I18n.T("devicePartial")), "Missing readings falsely labeled healthy.");
            owner.DeviceSourceOverride = (_, _) => Task.FromResult(Fixture with { PerformanceIncluded = false, Processes = [], Startup = [] });
            owner.Navigate("performance"); await owner.RefreshDevice(); await Settle(owner);
            Require(Nodes(Find<StackPanel>(owner, "DeviceProcesses")).OfType<TextBlock>().Single().Text == I18n.T("deviceNoData"), "An uncollected process source was shown as an empty measured result.");

            // Dashboard -> setup -> same assistant, without automatically submitting the reviewed context.
            Settings.Current.AiProvider = "gemini"; Settings.Current.GeminiModel = ""; Settings.SetKey("gemini", ""); owner.ResetConnectionDraftForSmoke();
            owner.DeviceSourceOverride = (_, _) => Task.FromResult(Fixture); owner.Navigate("home"); await owner.RefreshDevice(); await Settle(owner); Click(owner, "Quick-errors"); await Settle(owner);
            Require(owner.CurrentErrorAssistant is null && Nodes(owner).OfType<AiConnectionPanel>().Count() == 1, "Missing AI setup did not route to Settings.");
            Settings.Current.GeminiModel = "gemini-fixture"; Settings.SetKey("gemini", "fixture-not-live"); owner.ResetConnectionDraftForSmoke(); owner.ShowErrorAssistant(); await Settle(owner);
            var realWindow = owner.CurrentErrorAssistant!;
            Require(Find<TextBox>(realWindow, "SmartDiagnosticPreview").Text.Contains("CPU sampled utilization") && !Find<CheckBox>(realWindow, "SmartDiagnosticConsent").IsChecked!.Value && Find<TextBox>(realWindow, "ErrorDescription").Text == I18n.T("deviceDiagnosticQuestion"), "Setup round trip lost device review or granted sharing automatically."); realWindow.Close();

            var diagnostics = new Diagnostics(); var requests = new List<ErrorChatRequest>();
            var advice = ErrorAssistant.ParseAdvice(ErrorLanguageSmokeChecks.Fixture("en"));
            var assistant = new ErrorAssistantWindow(owner, collectDiagnostics: (options, ct) => WindowsDiagnostics.Collect(options, ct, diagnostics), analyzeConversation: (request, _) => { requests.Add(request); return Task.FromResult(advice); });
            assistant.Show(); await Settle(owner);
            try
            {
                Find<TextBox>(assistant, "ErrorDescription").Text = "Keep my existing draft";
                Require(assistant.ReviewDeviceSnapshot(Fixture) && requests.Count == 0 && Find<TextBox>(assistant, "ErrorDescription").Text == "Keep my existing draft", "Review sent data automatically or overwrote an existing draft.");
                await Settle(owner);
                Find<CheckBox>(assistant, "smartApplicationEvents").IsChecked = true; Click(assistant, "smartCollect"); await Settle(owner);
                Require(diagnostics.HealthCalls == 1 && diagnostics.EventCalls == 1 && Find<TextBox>(assistant, "SmartDiagnosticPreview").Text.Contains("CPU sampled utilization") && Find<TextBox>(assistant, "SmartDiagnosticPreview").Text.Contains("Fixture application"), "Explicit collection did not combine selected device readings and event evidence.");
                Require(Find<CheckBox>(assistant, "SmartDiagnosticConsent").IsChecked == false, "Collecting evidence implicitly approved transmission.");
                Find<CheckBox>(assistant, "SmartDiagnosticConsent").IsChecked = true; Click(assistant, "errorAnalyze"); await Settle(owner); Nodes(assistant).OfType<AdvicePresenter>().Last().Finish();
                Require(requests.Count == 1 && requests[0].Diagnostics.Contains("CPU sampled utilization") && requests[0].Diagnostics.Contains("Fixture application") && Find<CheckBox>(assistant, "SmartDiagnosticConsent").IsChecked == false, "Combined evidence bypassed explicit one-request sharing.");
                Find<TextBox>(assistant, "ErrorDescription").Text = "Follow-up without diagnostic sharing"; Click(assistant, "errorAnalyze"); await Settle(owner);
                Require(requests.Count == 2 && requests[1].Diagnostics.Length == 0 && requests[1].History.Count == 0, "Private device/log context escaped through follow-up history.");
            }
            finally { assistant.Close(); }
        }
        finally
        {
            owner.CurrentErrorAssistant?.Close(); owner.DeviceSourceOverride = null;
            Settings.SetKey("gemini", key); File.WriteAllText(Path.Combine(Settings.Folder, "settings.json"), original); Settings.Load(); owner.ResetConnectionDraftForSmoke(); owner.ApplyTheme(); owner.Navigate("home");
        }
        File.WriteAllText(Path.Combine(output, "device-center-result.txt"), "PASS: native read-only CPU/memory/fixed-volume sample without exporting real values; injected source arithmetic, PID reuse, cancellation and private-source gating; 24 localized/theme/page fixture renders; sorting/filtering, partial/unavailable/stale/error states; navigation cancellation; dashboard/setup/review round trip; combined device/event preview; explicit one-request AI consent and private-context exclusion. No process termination, registry write, real event read, paid AI or native repair.");
    }
}

public sealed partial class MainWindow
{
    internal DeviceSnapshot? DeviceSnapshotForSmoke => deviceSnapshot;
}
