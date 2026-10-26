using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Input;
using Baseera.App.Services;
using Baseera.App.ViewModels;
using Baseera.Core;

namespace Baseera.App;

internal static class WorkflowSmokeChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Nodes(child)) yield return next; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(n => AutomationProperties.GetAutomationId(n) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Settle(Window window) { await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(70); window.UpdateLayout(); }
    private static async Task Until(Func<bool> condition) { for (int n = 0; n < 100; n++) { if (condition()) return; await Task.Delay(50); } throw new Exception("Workflow action timed out."); }
    private sealed class Source : IWindowsDiagnosticSource
    {
        internal int Calls;
        public string SystemSummary() => throw new Exception("Unselected source");
        public string Services(CancellationToken token) { token.ThrowIfCancellationRequested(); Calls++; return "BITS: stopped"; }
        public IEnumerable<string> Events(string channel, int hours, string filter, CancellationToken token) => throw new Exception("Unselected events");
    }
    private sealed class Collector(string scope) : IDiagnosticCollector
    {
        internal int Calls;
        public Task<CaseEvidence> Capture(DiagnosticOptions options, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(Sample(scope, Calls, options)); }
    }
    private static CaseEvidence Sample(string scope, int n, DiagnosticOptions? options = null)
    {
        var device = new DeviceSnapshot(DateTimeOffset.UtcNow, .9, n == 1 ? 90 : 20, new(16000, 9000), [new("C:\\", 256000, 120000, true)], [new(1, "private-hidden-process", 1, 1)], [new("private-hidden-startup", "user")], true, []);
        return new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(n), scope, options ?? new(DeviceHealth: true, ServiceStatus: true), "Fixture device snapshot for review", device, [new("BITS", n == 1 ? "stopped" : "running")], true);
    }
    internal static async Task Run(MainWindow originalOwner, string output)
    {
        string folder = Path.Combine(Settings.Folder, "workflow-fixture-" + Guid.NewGuid().ToString("N"));
        string original = JsonSerializer.Serialize(Settings.Current);
        var repository = new CaseRepository(folder); var collector = new Collector(repository.HostScope);
        var services = new ApplicationServices(repository, collector);
        var vm = new RepairCaseViewModel(repository);
        var first = Sample(repository.HostScope, 1); vm.Capture(first); vm.Title = "Fixture case";
        Require(!await vm.Save("not-a-date", first.Text, CancellationToken.None), "Invalid onset date accepted.");
        Require(await vm.Save("2026-01-02 10:00", first.Text, CancellationToken.None), "Case could not be saved with DPAPI.");
        var saved = (await repository.List(CancellationToken.None)).Single();
        Require(saved.FirstObservedUtc is not null && saved.Evidence[0].Device is null && saved.Evidence[0].Text == "", "Default storage lost onset or retained excluded raw evidence.");
        byte[] protectedBytes = await File.ReadAllBytesAsync(Directory.GetFiles(folder, "*.case").Single());
        Require(!System.Text.Encoding.UTF8.GetString(protectedBytes).Contains("Fixture case"), "Case stored as plaintext.");
        var stale = saved; vm.StoreEvidence = true; Require(await vm.Save("", first.Text, CancellationToken.None), "Reviewed evidence storage failed.");
        var stored = (await repository.List(CancellationToken.None)).Single();
        Require(stored.Evidence[0].Device is { Processes.Count: 0, Startup.Count: 0 } && stored.Evidence[0].Text == first.Text, "Reviewed evidence retained hidden data or lost visible preview.");
        bool conflict = false; try { await repository.Save(stale, stale.Revision, CancellationToken.None); } catch (IOException ex) { conflict = ex.Message == "caseConflict"; }
        Require(conflict, "Stale case overwrote a newer revision.");
        await File.WriteAllTextAsync(Path.Combine(folder, Guid.NewGuid().ToString("N") + ".case"), "corrupt fixture");
        Require((await repository.List(CancellationToken.None)).Count == 1 && repository.UnreadableCount == 1, "One damaged file hid valid cases or was silently discarded.");
        var action = vm.BeginAction("flush-dns"); await vm.SaveActionMetadata(CancellationToken.None);
        Require((await repository.List(CancellationToken.None)).Single().Actions.Single().Status == "completion-unknown", "Reopened pending native action implied success.");
        vm.FinishAction(action, "completed", 0); var second = Sample(repository.HostScope, 2); var result = vm.Retest(second);
        Require(result.Status == "changed" && result.Limits.Contains("caseNotVerifiedFix"), "Retest claimed a verified symptom fix.");
        Require(await vm.Save("", second.Text, CancellationToken.None), "Comparison persistence failed.");
        var history = (await repository.List(CancellationToken.None)).Single(); var reopened = new RepairCaseViewModel(repository); reopened.Load(history);
        Require(reopened.Current!.Retests.Single().Comparison.Measurements.Any(m => m.Key == "deviceCpu" && m.Before == 90 && m.After == 20) && !reopened.Dirty, "Reopened case lost historical comparison.");
        var source = new Source(); await WindowsDiagnostics.Collect(new(ServiceStatus: true), CancellationToken.None, source);
        Require(source.Calls == 1, "Opted-in services not read."); source = new Source();
        bool canceled = false; try { await WindowsDiagnostics.Collect(new(ServiceStatus: true), new CancellationToken(true), source); } catch (OperationCanceledException) { canceled = true; }
        Require(canceled && source.Calls == 0, "Canceled diagnostic read a service.");
        var native = WindowsServiceStatus.Read(CancellationToken.None);
        Require(native.Count == WindowsServiceNames.All.Count && native.All(s => WindowsServiceNames.All.Contains(s.Name) && WindowsServiceNames.States.Contains(s.State)), "Native read-only fixed service query failed.");
        MainWindow? owner = null;
        try
        {
            Settings.Current.Onboarded = true;
            owner = new MainWindow(null, true, false, services); owner.Show(); await Settle(owner);
            foreach (string language in new[] { "ar", "en", "fr", "es" })
            {
                history = (await repository.List(CancellationToken.None)).Single();
                Settings.Current.Language = language; Settings.Current.Dark = language == "ar"; owner.ApplyTheme(); owner.Navigate("cases"); await Settle(owner);
                Require(Find<Button>(owner, "Navigate-cases").IsVisible && Find<Button>(owner, "Navigate-activity").IsVisible, "Case/activity navigation unavailable.");
                Click(owner, "CaseArchiveRefresh"); await Settle(owner); var list = Find<ComboBox>(owner, "CaseArchivePicker");
                Require(list.Items.Count == 1, "Archive list failed."); list.SelectedIndex = 0; await Settle(owner);
                Require(Nodes(owner).OfType<TextBlock>().Any(t => t.Text.Contains("BITS:")), "Archived service comparison is missing.");
                var dialog = new ErrorAssistantWindow(owner, analyze: (_, _, _, _) => throw new Exception("No AI requests authorized in archive test"));
                try
                {
                    dialog.Show(); await Settle(dialog); Require(dialog.PrepareSavedCase(history), "Saved case did not open."); await Settle(dialog);
                    Require(Find<TextBox>(dialog, "CaseTitle").Text == "Fixture case" && AutomationProperties.GetName(Find<TextBox>(dialog, "CaseTitle")) == I18n.T("caseTitle"), "MVVM title binding or accessible name failed.");
                    Require(!Nodes(dialog).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b).StartsWith("SmartRepair-")), "Opening archived actions enabled an executable control.");
                    Require(Find<TextBlock>(dialog, "CaseStatus").Text == I18n.T("caseLoadedReadOnly") && AutomationProperties.GetLiveSetting(Find<TextBlock>(dialog, "CaseStatus")) == AutomationLiveSetting.Polite, "Case status is not localized or announced.");
                    Require(Find<TextBox>(dialog, "CaseTitle").Focus(), "Case title cannot receive keyboard focus.");
                    Find<TextBox>(dialog, "CaseTitle").MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    Require(Keyboard.FocusedElement is not null, "Keyboard traversal lost focus.");
                    await Task.Delay(220); App.Snapshot(dialog, Path.Combine(output, "case-workflow-" + language + ".png"));
                    File.WriteAllText(Path.Combine(output, "workflow-phase.txt"), language + " retest");
                    Click(dialog, "CaseRetest"); await Until(() => Find<Button>(dialog, "CaseRetest").IsEnabled); await Settle(dialog);
                    Find<Expander>(dialog, "ConversationTools").IsExpanded = true; await Settle(dialog);
                    Find<Expander>(dialog, "SmartDiagnostics").IsExpanded = true; await Settle(dialog);
                    Require(collector.Calls > 0 && Find<CheckBox>(dialog, "SmartDiagnosticConsent").IsChecked != true, "Retest bypassed DI or enabled upload consent.");
                    File.WriteAllText(Path.Combine(output, "workflow-phase.txt"), language + " save");
                    Click(dialog, "CaseSave"); await Until(() => Find<Button>(dialog, "CaseSave").IsEnabled); await Settle(dialog);
                    Require(Find<TextBlock>(dialog, "CaseStatus").Text == I18n.T("caseSaved"), "UI case save failed: " + Find<TextBlock>(dialog, "CaseStatus").Text);
                    // Reload only the fixture to avoid a save prompt on automated close.
                    dialog.PrepareSavedCase(history);
                }
                finally { dialog.PrepareSavedCase(history); dialog.Close(); }
                File.WriteAllText(Path.Combine(output, "workflow-phase.txt"), language + " task view");
                using var work = services.Tasks.Begin("scan", true); owner.Navigate("activity"); await Settle(owner);
                Click(owner, "TaskCancel-" + work.Item.Id); Require(work.Token.IsCancellationRequested, "Activity cancellation not connected."); work.Cancelled();
                owner.HighContrastOverride = true; owner.ApplyTheme(); owner.Navigate("cases"); await Settle(owner);
                Click(owner, "CaseArchiveRefresh"); await Until(() => Find<ComboBox>(owner, "CaseArchivePicker").Items.Count > 0); await Settle(owner);
                Require(((SolidColorBrush)Application.Current.FindResource("Ink")).Color == System.Windows.SystemColors.WindowTextColor, "High contrast palette ignores system colors.");
                Require(Find<Button>(owner, "Navigate-cases").FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), "Workflow RTL direction failed.");
                await Task.Delay(220); App.Snapshot(owner, Path.Combine(output, "case-archive-highcontrast-" + language + ".png")); owner.HighContrastOverride = null;
            }
            File.WriteAllText(Path.Combine(output, "workflow-result.txt"), "PASS: protected local case persistence, default exclusion, explicit reviewed storage, hidden names excluded, stale revisions, corrupt-file isolation, onset validation, crash-unknown history, comparison reopen, fixed read-only native service query, cancellation, constructor DI/MVVM, four-language workflow/archive, archived actions inert, live status, keyboard focus traversal, high-contrast palette and RTL. All AI/native repair execution mocked; no diagnostic upload.");
        }
        finally { owner?.Close(); File.WriteAllText(Path.Combine(Settings.Folder, "settings.json"), original); Settings.Load(); originalOwner.ApplyTheme(); }
    }
}
