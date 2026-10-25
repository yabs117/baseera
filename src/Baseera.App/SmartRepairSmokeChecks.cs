using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal static class SmartRepairSmokeChecks
{
    private sealed class Source : IWindowsDiagnosticSource
    {
        internal readonly List<string> Calls = [];
        internal int Hours; internal string MatchText = "";
        public string SystemSummary() { Calls.Add("info"); return "Windows fixture build; no identifiers"; }
        public IEnumerable<string> Events(string channel, int hours, string matchText, CancellationToken token)
        {
            Hours = hours; MatchText = matchText;
            Calls.Add(channel); token.ThrowIfCancellationRequested();
            if (channel == "System") throw new UnauthorizedAccessException();
            return Enumerable.Repeat(@"Application crash 1000 C:\Users\FixtureUser\secret.txt fixture@example.com 192.168.1.2 password=secret;", 100);
        }
    }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; }
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().SingleOrDefault(x => AutomationProperties.GetAutomationId(x) == id) ?? throw new Exception("Missing Smart Repair control: " + id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Settle(Window window)
    { await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(80); window.UpdateLayout(); }
    private static async Task Until(Func<bool> ready)
    { for (int i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(30); } throw new Exception("Smart Repair check timed out."); }

    internal static async Task Run(MainWindow owner, string output)
    {
        var source = new Source();
        string evidence = await WindowsDiagnostics.Collect(new DiagnosticOptions(ApplicationEvents: true, SystemEvents: true), CancellationToken.None, source);
        Require(source.Calls.SequenceEqual(new[] { "Application", "System" }) && !evidence.Contains("FixtureUser") && !evidence.Contains("fixture@example.com") && evidence.Contains("Source unavailable") && evidence.Length <= DiagnosticPrivacy.MaxCharacters,
            "Selected read-only sources, redaction or unavailable-source coverage failed.");
        Require(WindowsDiagnosticSource.Matches("Example App ERROR 0x80070005", " error 0X80070005 ") && !WindowsDiagnosticSource.Matches("Example App", "App.*") && WindowsDiagnosticSource.Matches("anything", ""),
            "Literal filtering was case-sensitive, regex-based or rejected empty filters.");
        await WindowsDiagnostics.Collect(new DiagnosticOptions(ApplicationEvents: true, Hours: 6, MatchText: "0x80070005"), CancellationToken.None, source);
        Require(source.Hours == 6 && source.MatchText == "0x80070005", "Selected time range/filter did not reach diagnostic source.");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); bool canceled = false;
            try { await WindowsDiagnostics.Collect(new DiagnosticOptions(true), cancel.Token, source); } catch (OperationCanceledException) { canceled = true; }
            Require(canceled && !source.Calls.Contains("info"), "Canceled collection read a source.");
        }
        var fixture = JsonNode.Parse(ErrorLanguageSmokeChecks.Fixture("en"))!.AsObject();
        fixture["repair_actions"] = new JsonArray("flush-dns");
        var advice = ErrorAssistant.ParseAdvice(fixture.ToJsonString());
        Require(advice.RepairActions!.SequenceEqual(new[] { "flush-dns" }), "Catalog recommendations were not parsed.");
        var report = new SmartRepairSession();
        report.SetAnalysis("Example problem user@example.com", true, "Reviewed diagnostics private-marker password=private-secret;", "gemini", "fixture-model", advice);
        report.Record("flush-dns", "declined"); report.Record("sfc-repair", "completed", 3010); report.Observe("still-present");
        var defaultReport = JsonNode.Parse(report.Export(false))!;
        Require(defaultReport["approved_diagnostics"]!.GetValue<string>() == "" && !report.Export(false).Contains("private-marker") &&
            !report.Export(false).Contains("user@example.com") && defaultReport["repairs"]!.AsArray().Count == 2 &&
            defaultReport["user_reported_outcomes"]![0]!["Outcome"]!.GetValue<string>() == "still-present", "Local report leaked excluded diagnostics or lost actual activity/outcome.");
        Require(report.Export(true).Contains("private-marker") && !report.Export(true).Contains("private-secret"), "Explicit diagnostic export lost the snapshot or skipped redaction.");
        for (int i = 0; i < 25; i++) { report.Record("flush-dns", "completed", 0); report.Observe("resolved"); }
        Require(report.Activity.Count == 20 && report.Observations.Count == 20, "Window-scoped case history is unbounded.");
        report.SetAnalysis("New case", false, "", "gemini", "fixture-model", advice);
        Require(report.Activity.Count == 0 && report.Observations.Count == 0 && !report.Export(true).Contains("private-marker"), "A new case retained old observations or diagnostic evidence.");
        report.Clear(); Require(report.Advice is null && report.AnalyzedAt is null, "Clearing a case retained its report.");
        fixture["repair_actions"] = new JsonArray("cmd.exe /c malicious");
        bool rejected = false;
        try { ErrorAssistant.ParseAdvice(fixture.ToJsonString()); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Untrusted model command was accepted.");
        var forged = RepairCatalog.Get("flush-dns") with { Executable = "cmd.exe", Arguments = new[] { "/c", "malicious" } };
        var canonical = RepairRunner.StartInfo(forged);
        Require(canonical.FileName == Path.Combine(Environment.SystemDirectory, "ipconfig.exe") && canonical.ArgumentList.SequenceEqual(new[] { "/flushdns" }), "Runner trusted non-canonical paths/arguments.");
        Require(RepairRunner.StartInfo(RepairCatalog.Get("sfc-repair")).Verb == "runas" && RepairRunner.StartInfo(RepairCatalog.Get("dism-repair")).ArgumentList.Contains("/NoRestart"), "Repair launch lost elevation or restart protection.");
        int launches = 0; var waiting = new TaskCompletionSource<int>();
        var runner = new RepairRunner(info => { launches++; Require(!info.ArgumentList.Any(x => x.Contains("malicious")), "Untrusted arguments reached launcher."); return waiting.Task; });
        var running = runner.Execute(RepairApproval.Confirm("sfc-repair", true), CancellationToken.None);
        bool busy = false; try { await runner.Execute(RepairApproval.Confirm("flush-dns", true), CancellationToken.None); } catch (InvalidOperationException) { busy = true; }
        Require(busy && launches == 1, "Concurrent repair was launched."); waiting.SetResult(3010);
        Require((await running).ExitCode == 3010, "Restart-required exit code was lost.");
        var declined = await new RepairRunner(_ => throw new Win32Exception(1223)).Execute(RepairApproval.Confirm("sfc-repair", true), CancellationToken.None);
        Require(!declined.Started && declined.ExitCode is null, "UAC rejection was reported as repair success.");

        string language = Settings.Current.Language; bool dark = Settings.Current.Dark;
        try
        {
            foreach (string locale in new[] { "ar", "en", "fr", "es" })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = locale == "ar"; owner.ApplyTheme();
                string summary = locale switch { "ar" => "مثال تجريبي: تعذر فتح التطبيق", "fr" => "Exemple : l’application ne démarre pas", "es" => "Ejemplo: la aplicación no se inicia", _ => "Example: the app did not start" };
                var uiAdvice = ErrorAssistant.ParseAdvice(ErrorLanguageSmokeChecks.Fixture(locale, summary)) with { RepairActions = advice.RepairActions };
                int collections = 0, commands = 0, reviews = 0; bool approve = false; var inputs = new List<string>();
                var fakeRunner = new RepairRunner(_ => { commands++; return Task.FromResult(0); });
                var dialog = new ErrorAssistantWindow(owner,
                    collectDiagnostics: (options, ct) => { collections++; Require(options.ApplicationEvents && !options.SystemEvents && !options.SystemInfo, "Unselected diagnostic source collected."); return Task.FromResult(evidence); },
                    analyzeDiagnostics: (text, image, lang, diagnostics, ct) => { inputs.Add(diagnostics); return Task.FromResult(uiAdvice); },
                    approveRepair: id => { reviews++; return approve ? RepairApproval.Confirm(id, true) : null; }, repairRunner: fakeRunner);
                try
                {
                    dialog.Show(); await Settle(owner);
                    Click(dialog, "chatTools"); await Settle(owner);
                    var expander = Find<Expander>(dialog, "SmartDiagnostics"); expander.IsExpanded = true; await Settle(dialog);
                    var preview = Find<TextBox>(dialog, "SmartDiagnosticPreview"); var share = Find<CheckBox>(dialog, "SmartDiagnosticConsent");
                    Require(collections == 0 && string.IsNullOrEmpty(preview.Text) && share.IsChecked != true, "Diagnostics were collected/shared at startup.");
                    Click(dialog, "smartCollect"); await Settle(owner); Require(collections == 0, "No-source collection was permitted.");
                    Find<CheckBox>(dialog, "smartApplicationEvents").IsChecked = true; Click(dialog, "smartCollect"); await Until(() => preview.Text.Length > 0);
                    Require(collections == 1 && share.IsChecked != true, "Collecting silently approved sharing.");
                    Find<TextBox>(dialog, "ErrorDescription").Text = "App error fixture";
                    Click(dialog, "errorAnalyze"); await Until(() => inputs.Count == 1);
                    Require(inputs[0] == "", "Unapproved diagnostics reached AI.");
                    var presentation = Nodes(dialog).OfType<AdvicePresenter>().Single();
                    if (presentation.IsTyping) { Click(dialog, "SmartRepair-flush-dns"); Require(reviews == 0, "Repair review opened before guidance completed."); }
                    presentation.Finish(); await Settle(owner);
                    share.IsChecked = true; string provider = Settings.Current.AiProvider;
                    try
                    {
                        Settings.Current.AiProvider = provider == "gemini" ? "openai" : "gemini";
                        Click(dialog, "errorAnalyze"); Require(inputs.Count == 1 && share.IsChecked != true, "Sharing approval survived a provider change.");
                    }
                    finally { Settings.Current.AiProvider = provider; }
                    Click(dialog, "errorAnalyze"); // Review the externally restored connection without transmitting a draft.
                    share.IsChecked = true; preview.Text += "\nEdited preview"; Require(share.IsChecked != true, "Editing did not invalidate sharing approval.");
                    share.IsChecked = true; string reviewed = preview.Text;
                    Click(dialog, "errorAnalyze"); await Until(() => inputs.Count == 2);
                    Require(inputs[1] == reviewed && share.IsChecked != true, "Reviewed diagnostics were changed or approval was reused.");
                    presentation = Nodes(dialog).OfType<AdvicePresenter>().Last(); presentation.Finish(); await Settle(owner);
                    Find<Expander>(dialog, "SmartSessionDetails").IsExpanded = true; await Settle(owner);
                    Require(Find<Button>(dialog, "smartSaveReport").IsEnabled && Find<CheckBox>(dialog, "SmartReportDiagnostics").IsChecked != true,
                        "Report was unavailable after analysis or diagnostics were included by default.");
                    var description = Find<TextBox>(dialog, "ErrorDescription"); string original = description.Text; description.Text += " changed";
                    Click(dialog, "SmartRepair-flush-dns"); Click(dialog, "smartResolved");
                    Require(reviews == 0 && !Find<Button>(dialog, "smartResolved").IsEnabled, "Changed case executed stale repair or accepted an unrelated outcome.");
                    description.Text = original;
                    Click(dialog, "SmartRepair-flush-dns"); await Settle(owner); Require(commands == 0, "Declined repair executed.");
                    approve = true; Click(dialog, "SmartRepair-flush-dns"); await Until(() => commands == 1);
                    share.IsChecked = true; Click(dialog, "smartUnresolved"); Require(share.IsChecked != true, "Unresolved follow-up reused sharing consent.");
                    var localCase = (SmartRepairSession)typeof(ErrorAssistantWindow).GetField("repairSession", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(dialog)!;
                    Require(localCase.Activity.Count == 2 && localCase.Activity[0].Status == "declined" && localCase.Activity[1].ExitCode == 0 && localCase.Observations.Last().Outcome == "still-present",
                        "UI did not record the declined/completed commands or user outcome.");
                    var range = Find<ComboBox>(dialog, "SmartDiagnosticRange"); range.SelectedIndex = 3;
                    Require(preview.Text == "" && share.IsChecked != true && collections == 1, "Changing the time range retained stale evidence or collected silently.");
                    range.SelectedIndex = 2;
                    Click(dialog, "smartClear"); Require(preview.Text == "" && share.IsChecked != true, "Clear retained diagnostics or consent.");
                    // Render only fixture diagnostics; no real machine data is collected or published.
                    preview.Text = "2026-10-25T10:30:00.0000000Z | Application | Example App | ID 1000 | Level 2\nExample app crash. Error code 0x80070005\n[Source unavailable: permission denied]";
                    await Settle(owner);
                    if (locale is "ar" or "en")
                    {
                        WindowAppearance.SavePreview(dialog, Path.Combine(output, "smart-repair-actions-" + locale + ".png"));
                        Find<Border>(dialog, "SmartRepairSession").BringIntoView(); await Settle(dialog);
                        WindowAppearance.SavePreview(dialog, Path.Combine(output, "smart-repair-session-" + locale + ".png"));
                        expander.BringIntoView(); await Settle(dialog);
                        WindowAppearance.SavePreview(dialog, Path.Combine(output, "smart-repair-" + locale + ".png"));
                    }
                    var confirmation = new RepairApprovalWindow(dialog, "dism-repair"); confirmation.Show(); await Settle(owner);
                    Require(!Find<Button>(confirmation, "SmartConfirmRepair").IsEnabled && confirmation.Approval is null, "Repair approval defaults to enabled.");
                    Click(confirmation, "SmartConfirmRepair"); Require(confirmation.Approval is null && confirmation.IsVisible, "Approval handler trusted a click without acknowledgement.");
                    Require(confirmation.FlowDirection == FlowDirection.LeftToRight && ((FrameworkElement)confirmation.Content).FlowDirection == (locale == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), "Native repair frame/content direction is inconsistent.");
                    if (locale is "ar" or "en") WindowAppearance.SavePreview(confirmation, Path.Combine(output, "smart-repair-approval-" + locale + ".png"));
                    Find<CheckBox>(confirmation, "SmartAcknowledgeRepair").IsChecked = true;
                    Require(Find<Button>(confirmation, "SmartConfirmRepair").IsEnabled, "Explicit acknowledgement did not enable approval.");
                    confirmation.Close(); Require(confirmation.Approval is null, "Closing confirmation granted approval.");
                    var approvedDialog = new RepairApprovalWindow(dialog, "flush-dns"); Exception? approvalFailure = null;
                    approvedDialog.Loaded += (_, _) =>
                    {
                        try { Find<CheckBox>(approvedDialog, "SmartAcknowledgeRepair").IsChecked = true; Click(approvedDialog, "SmartConfirmRepair"); }
                        catch (Exception ex) { approvalFailure = ex; approvedDialog.Close(); }
                    };
                    Require(approvedDialog.ShowDialog() == true && approvalFailure is null && approvedDialog.Approval is not null, "Explicit modal approval did not grant a command-bound consent.");
                    await fakeRunner.Execute(approvedDialog.Approval!, CancellationToken.None);
                    Require(commands == 2, "Approved modal consent did not reach the fake native launcher.");
                }
                finally { dialog.Close(); }
            }
            var waitingForCancel = new TaskCompletionSource();
            var canceledDialog = new ErrorAssistantWindow(owner, analyzeDiagnostics: (_, _, _, _, _) => throw new Exception("Unexpected AI request"),
                collectDiagnostics: async (_, ct) => { waitingForCancel.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return "Never shared"; });
            canceledDialog.Show(); await Settle(canceledDialog);
            Click(canceledDialog, "chatTools"); await Settle(canceledDialog);
            Find<Expander>(canceledDialog, "SmartDiagnostics").IsExpanded = true; await Settle(canceledDialog);
            Find<CheckBox>(canceledDialog, "smartApplicationEvents").IsChecked = true;
            Click(canceledDialog, "smartCollect"); await waitingForCancel.Task;
            Click(canceledDialog, "smartClear"); await Until(() => Find<Button>(canceledDialog, "smartCollect").IsEnabled);
            Require(Find<TextBox>(canceledDialog, "SmartDiagnosticPreview").Text == "" && Find<CheckBox>(canceledDialog, "SmartDiagnosticConsent").IsChecked != true, "Clearing did not cancel an in-flight collection.");
            canceledDialog.Close();
            File.WriteAllText(Path.Combine(output, "smart-repair-result.txt"), "PASS: fixture-only selected diagnostic sources, bounded/redacted preview, literal filters/time ranges, denied-source coverage, cancellation, no startup collection, no unapproved transmission, edit invalidation, one-request sharing, stale-input repair rejection, no execution on declined approval, fixed native command paths/arguments, serialized repairs, UAC rejection, exit/restart status, explicit unchecked approval dialogs, local case reports excluding diagnostics by default, bounded activity/user observations, fresh-case reset and four-language/RTL UI. No real event logs, live API, UAC prompt or Windows repair command was used.");
        }
        finally { Settings.Current.Language = language; Settings.Current.Dark = dark; owner.ApplyTheme(); owner.Render(); }
    }
}
