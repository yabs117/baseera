using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal static class InvestigationSmokeChecks
{
    private const long GiB = 1073741824;
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        var matches = Nodes(root).OfType<T>().Where(e => AutomationProperties.GetAutomationId(e) == id).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("Expected exactly one " + typeof(T).Name + " / " + id + ", found " + matches.Length + ". Visible IDs: " +
            string.Join(",", Nodes(root).OfType<T>().Select(AutomationProperties.GetAutomationId).Where(s => !string.IsNullOrEmpty(s)).Take(40)));
        return matches[0];
    }
    private static void Click(DependencyObject root, string id) { var button = Find<Button>(root, id); Require(button.IsEnabled, "Fixture action is unexpectedly disabled: " + id); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static async Task Settle(Window window) { await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(50); window.UpdateLayout(); }
    private static async Task Until(Func<bool> condition, string detail)
    { for (int n = 0; n < 200; n++) { if (condition()) return; await Task.Delay(25); } throw new Exception("Investigation fixture timed out: " + detail); }
    private static async Task WorkFinished(MainWindow owner)
    { await Until(() => owner.Services.Tasks.Items.All(t => t.State != "running"), "tracked operation"); await Settle(owner); }
    private static CheckBox Check(MainWindow owner, string key) => Nodes(owner).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == I18n.T(key));
    private static bool HasText(Window window, string marker) => Nodes(window).OfType<TextBlock>().Any(t => t.Text.Contains(marker, StringComparison.Ordinal));
    private static void RedactFixturePaths(Window owner, string fixture)
    {
        foreach (var box in Nodes(owner).OfType<TextBox>()) if (box.IsReadOnly && box.Text.Contains(fixture, StringComparison.OrdinalIgnoreCase)) box.Text = box.Text.Replace(fixture, @"C:\Baseera-Demo", StringComparison.OrdinalIgnoreCase);
        foreach (var block in Nodes(owner).OfType<TextBlock>()) if (block.Text.Contains(fixture, StringComparison.OrdinalIgnoreCase)) block.Text = block.Text.Replace(fixture, @"C:\Baseera-Demo", StringComparison.OrdinalIgnoreCase);
    }
    internal static async Task Run(MainWindow originalOwner, string output)
    {
        Require(Environment.GetEnvironmentVariable("BASEERA_DATA_DIR") is { Length: > 0 }, "Investigation smoke checks require an explicit isolated data folder.");
        string settings = JsonSerializer.Serialize(Settings.Current), originalKey = Settings.GetKey("gemini"), originalReputation = Settings.GetReputationKey();
        string fixture = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "baseera-investigation-ui-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(fixture);
        string payload = Path.Combine(fixture, "sample.bin"), candidate = Path.Combine(fixture, "old.tmp"); byte[] payloadBytes = Encoding.UTF8.GetBytes("Synthetic fixture payload"); await File.WriteAllBytesAsync(payload, payloadBytes); await File.WriteAllBytesAsync(Path.Combine(fixture, "duplicate.bin"), payloadBytes);
        await File.WriteAllTextAsync(candidate, "temporary fixture"); File.SetLastWriteTimeUtc(candidate, DateTime.UtcNow.AddDays(-10));
        var candidateInfo = new FileInfo(candidate); var clean = new StorageFile(candidate, candidateInfo.Length, candidateInfo.LastWriteTimeUtc);
        string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(payload))).ToLowerInvariant();
        int doctorCalls = 0, performanceCalls = 0, securityCalls = 0, signatureCalls = 0, reputationCalls = 0, networkCalls = 0, storageCalls = 0, cleanupCalls = 0, recycleCalls = 0, searchCalls = 0, timelineCalls = 0, usbCalls = 0, readCalls = 0;
        bool searchReadContents = false, allowRecycle = false; CancellationToken canceledPerformance = default;
        MainWindow? owner = null;
        try
        {
            Settings.Current.Onboarded = true; Settings.Current.ErrorAssistantEnabled = true; Settings.Current.DisabledCenters.Clear(); Settings.Current.AiProvider = "gemini"; Settings.Current.GeminiModel = "gemini-fixture"; Settings.SetKey("gemini", "fixture-only-not-live"); Settings.SetReputationKey("fixture-only-not-live");
            owner = new MainWindow(null, true, false); owner.Width = 1160; owner.Height = 820; owner.Show(); await Settle(owner);
            var capturedAt = DateTimeOffset.UtcNow.AddSeconds(-10);
            var device = new DeviceSnapshot(capturedAt, 1, 91, new(16 * GiB, 2 * GiB), [new("C:\\", 256 * GiB, 16 * GiB, true)], [], [], false, []);
            owner.DoctorCaptureOverride = (options, token) =>
            { token.ThrowIfCancellationRequested(); doctorCalls++; Require(options.SystemInfo && options.DeviceHealth && !options.ApplicationEvents && !options.SystemEvents && !options.Drivers && !options.ServiceStatus, "Doctor read an unselected source."); return Task.FromResult(new CaseEvidence(Guid.NewGuid(), DateTimeOffset.UtcNow, "fixture-scope", options, "Synthetic diagnostic evidence 0x80070005", device, [], true)); };
            owner.DetectiveSampleLimitForSmoke = 4; owner.DetectiveDelayForSmoke = TimeSpan.Zero;
            owner.DetectiveSampleOverride = token =>
            { token.ThrowIfCancellationRequested(); int n = ++performanceCalls; return Task.FromResult(new PerformancePoint(capturedAt.AddMilliseconds(n * 10), 90, 92, n * 10000000d, 25, [new(42, 100, "Fixture editor", 35, GiB, n * 2000000d)])); };
            owner.SecurityProbeOverride = token => { token.ThrowIfCancellationRequested(); securityCalls++; return Task.FromResult(new InvestigationReport("security", DateTimeOffset.UtcNow, [new("hubDefender", "state:hubOn"), new("hubPublicFirewall", "state:hubOn", "hubActiveProfile")], ["hubSecurityLimits"])); };
            owner.SignatureProbeOverride = (file, token) => { token.ThrowIfCancellationRequested(); signatureCalls++; Require(file == payload, "Signature fixture received a different file."); return Task.FromResult(new InvestigationReport("security", DateTimeOffset.UtcNow, [new("hubSha256", hash), new("hubAuthenticode", "state:hubNotSigned")], ["hubSignatureLimits"])); };
            owner.ReputationProbeOverride = (digest, key, token) => { token.ThrowIfCancellationRequested(); reputationCalls++; Require(digest == hash && key == "fixture-only-not-live", "Reputation request changed the approved hash or key."); return Task.FromResult(new InvestigationReport("security", DateTimeOffset.UtcNow, [new("hubReputation", "0 / 70"), new("hubSha256", hash)], ["hubReputationLimits"])); };
            owner.NetworkProbeOverride = (host, token) => { token.ThrowIfCancellationRequested(); networkCalls++; return Task.FromResult(new NetworkReading(DateTimeOffset.UtcNow, host, 12, 6, [14, 15, 16], "Fixture Ethernet=Up", "", 75, 300)); };
            owner.ExplorerFolderPickerForSmoke = () => fixture; owner.ExplorerFilePickerForSmoke = () => payload;
            owner.StorageInspectionForSmoke = (root, duplicates, token) =>
            { token.ThrowIfCancellationRequested(); storageCalls++; Require(root == fixture, "Storage inspection left the selected fixture folder."); long total = payloadBytes.Length * 2 + clean.Bytes; return Task.FromResult(new StorageReport(root, DateTimeOffset.UtcNow, total, 3, [new(root, total)], [new(payload, payloadBytes.Length, File.GetLastWriteTimeUtc(payload))], duplicates ? [new(hash, payloadBytes.Length, [new(payload, payloadBytes.Length, File.GetLastWriteTimeUtc(payload)), new(Path.Combine(root, "duplicate.bin"), payloadBytes.Length, File.GetLastWriteTimeUtc(payload))])] : [], [])); };
            owner.CleanupPreviewForSmoke = token => { token.ThrowIfCancellationRequested(); cleanupCalls++; return Task.FromResult<IReadOnlyList<StorageFile>>([clean]); };
            owner.CleanupConfirmationForSmoke = selected => { Require(selected.Count == 1 && selected[0].Path == candidate, "Cleanup review included an unselected file."); return allowRecycle; };
            owner.CleanupRecycleForSmoke = approval => { recycleCalls++; var selected = approval.Consume(fixture); Require(selected.Count == 1 && selected[0].Path == candidate, "Recycler received a forged selection."); return (0, true); };
            owner.SearchBuildForSmoke = (root, contents, token) =>
            { token.ThrowIfCancellationRequested(); searchCalls++; searchReadContents = contents; var now = DateTime.UtcNow; return Task.FromResult(new SearchIndex(DateTimeOffset.UtcNow, root, [new(Path.Combine(root, "driver.pdf"), "driver.pdf", ".pdf", 24, now, contents ? "driver أخطاء تعريف" : "", contents ? "hubTextLimited" : "hubNameOnly")], [])); };
            owner.TimelineSourceForSmoke = (days, token) => { token.ThrowIfCancellationRequested(); timelineCalls++; Require(days == 7, "Timeline changed the selected period."); return Task.FromResult(new TimelineSnapshot(DateTimeOffset.UtcNow, [new(DateTimeOffset.UtcNow.AddHours(-2), "hubProgramDate", "Fixture program date", "hubRegistryDateLimits"), new(DateTimeOffset.UtcNow.AddHours(-1), "hubCrashEvent", "Fixture application event", "Example event 1000"), new(DateTimeOffset.UtcNow.AddDays(-3), "hubDriverEvent", "Older fixture event", "Example configuration event")], ["hubTimelineLimits"])); };
            owner.UsbSourceForSmoke = token => { token.ThrowIfCancellationRequested(); usbCalls++; return Task.FromResult(new InvestigationReport("usb", DateTimeOffset.UtcNow, [new("hubUsbDevice", "Fixture USB storage", "PnP 0"), new("hubRemovableVolume", "E:\\", "8 GiB / 16 GiB")], ["hubUsbLimits"])); };
            owner.UsbReadForSmoke = (file, token) => { token.ThrowIfCancellationRequested(); readCalls++; Require(file == payload, "Read fixture used an unexpected file."); return Task.FromResult(new InvestigationReport("usb", DateTimeOffset.UtcNow, [new("hubReadBytes", "48"), new("hubRepeatedRead", "state:hubSamplesMatch")], ["hubReadTestLimits"])); };

            foreach (string language in new[] { "en", "ar" })
            foreach (bool dark in new[] { false, true })
            {
                Settings.Current.Language = language; Settings.Current.Dark = dark; owner.ApplyTheme();
                foreach (string center in MainWindow.InvestigationCenters)
                {
                    int before = doctorCalls + performanceCalls + securityCalls + signatureCalls + reputationCalls + networkCalls + storageCalls + cleanupCalls + searchCalls + timelineCalls + usbCalls + readCalls;
                    owner.Navigate(center); await Settle(owner);
                    Require(Find<Button>(owner, "Navigate-" + center).FlowDirection == (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight), "Investigation navigation lost RTL/LTR direction.");
                    Require(before == doctorCalls + performanceCalls + securityCalls + signatureCalls + reputationCalls + networkCalls + storageCalls + cleanupCalls + searchCalls + timelineCalls + usbCalls + readCalls, "Opening a center collected or transmitted data automatically.");
                    switch (center)
                    {
                        case "doctor":
                            Click(owner, "Hub-doctor-hubCollect"); await WorkFinished(owner); Require(Find<TextBox>(owner, "HubDoctorPreview").Text.Contains("Synthetic diagnostic evidence"), "Doctor did not present the collected evidence.");
                            break;
                        case "detective":
                            Click(owner, "Hub-detective-hubStartObserve"); await WorkFinished(owner);
                            var sampleTable = Nodes(owner).OfType<Expander>().Single(e => Equals(e.Header, I18n.T("hubSampleTable"))); sampleTable.IsExpanded = true; await Settle(owner);
                            Require(sampleTable.Content is TextBox sampleValues && sampleValues.Text.Contains("90") && HasText(owner, "Fixture editor"), "Performance history/chart fallback lost fixture values or process observations.");
                            Click(owner, "Hub-detective-hubSaveHistory"); await WorkFinished(owner); Click(owner, "Hub-detective-hubLoadHistory"); await WorkFinished(owner);
                            sampleTable.IsExpanded = false; await Settle(owner);
                            break;
                        case "security":
                            Click(owner, "Hub-security-hubReadSecurity"); await WorkFinished(owner);
                            owner.SetSignatureFixture(payload, new("security", DateTimeOffset.UtcNow, [new("hubSha256", hash), new("hubAuthenticode", "state:hubNotSigned")], ["hubSignatureLimits"])); owner.Render(true); await Settle(owner);
                            Click(owner, "Hub-security-hubInspectSignature"); await WorkFinished(owner);
                            Require(!Find<CheckBox>(owner, "HubReputationConsent").IsChecked!.Value, "Signature inspection silently granted reputation consent.");
                            Find<CheckBox>(owner, "HubReputationConsent").IsChecked = true; Click(owner, "Hub-security-hubCheckReputation"); await WorkFinished(owner);
                            Require(!Find<CheckBox>(owner, "HubReputationConsent").IsChecked!.Value && HasText(owner, string.Format(I18n.T("hubFlaggedOf"), 0, 70)), "Hash reputation consent was not one-request only or its localized result was lost.");
                            break;
                        case "network":
                            var host = Find<TextBox>(owner, "HubNetworkHost"); Find<CheckBox>(owner, "HubNetworkConsent").IsChecked = true; host.Text = "example.net";
                            Require(Find<CheckBox>(owner, "HubNetworkConsent").IsChecked == false, "Network target edits retained consent.");
                            host.Text = "example.com"; Find<CheckBox>(owner, "HubNetworkConsent").IsChecked = true; Click(owner, "Hub-network-hubMeasureNetwork"); await WorkFinished(owner);
                            Require(Find<CheckBox>(owner, "HubNetworkConsent").IsChecked == false && HasText(owner, "example.com") && HasText(owner, string.Format(I18n.T("hubCountOf"), 3, 6)), "Network run lost its target, localized reply count or reused consent.");
                            Click(owner, "Hub-network-hubSaveHistory"); await WorkFinished(owner); Click(owner, "Hub-network-hubLoadHistory"); await WorkFinished(owner);
                            break;
                        case "storage":
                            Click(owner, "Hub-storage-hubChooseFolder"); await WorkFinished(owner); Check(owner, "hubFindDuplicates").IsChecked = true; Click(owner, "Hub-storage-hubInspectStorage"); await WorkFinished(owner);
                            Require(HasText(owner, I18n.T("hubDuplicateSets")) && Nodes(owner).OfType<TextBox>().Any(t => t.Text == "SHA-256  " + hash), "Storage duplicate results are unavailable.");
                            Click(owner, "Hub-storage-hubPreviewCleanup"); await WorkFinished(owner); Require(Find<CheckBox>(owner, "HubCleanupCandidate-0").IsChecked == false && Find<CheckBox>(owner, "HubCleanupAcknowledge").IsChecked == false, "Cleanup preview silently selected or approved removal.");
                            int priorRecycle = recycleCalls; Click(owner, "Hub-storage-hubRecycleSelected"); Require(recycleCalls == priorRecycle, "Cleanup proceeded without selection and acknowledgement.");
                            Find<CheckBox>(owner, "HubCleanupCandidate-0").IsChecked = true; Find<CheckBox>(owner, "HubCleanupAcknowledge").IsChecked = true; allowRecycle = false; Click(owner, "Hub-storage-hubRecycleSelected"); Require(recycleCalls == priorRecycle, "Declined cleanup review reached the recycler.");
                            allowRecycle = true; Click(owner, "Hub-storage-hubRecycleSelected"); await Settle(owner); Require(recycleCalls == priorRecycle + 1 && File.Exists(candidate), "Mock cleanup invoked a real deletion or lost explicit approval.");
                            break;
                        case "search":
                            Click(owner, "Hub-search-hubChooseFolder"); await WorkFinished(owner); Require(Find<CheckBox>(owner, "HubSearchContentConsent").IsChecked == false, "Selecting a folder silently opted into content indexing.");
                            Click(owner, "Hub-search-hubBuildIndex"); await WorkFinished(owner); Require(!searchReadContents, "Name-only indexing read content without consent.");
                            Find<CheckBox>(owner, "HubSearchContentConsent").IsChecked = true; Click(owner, "Hub-search-hubBuildIndex"); await WorkFinished(owner); Require(searchReadContents, "Approved content indexing was ignored.");
                            Find<TextBox>(owner, "HubSearchQuery").Text = "ابحث عن ملفات PDF عن أخطاء الأسبوع الماضي";
                            await Until(() => Nodes(Find<StackPanel>(owner, "HubSearchResults")).OfType<Button>().Any(), "Arabic local search result");
                            Click(owner, "HubSearchResult-0"); Require(Find<TextBox>(owner, "HubSearchPreview").Text.Contains("أخطاء"), "Indexed Arabic text was not previewed.");
                            break;
                        case "timeline":
                            Click(owner, "Hub-timeline-hubCollectTimeline"); await WorkFinished(owner); Require(HasText(owner, "Older fixture event"), "Timeline dropped an in-range fixture before filtering.");
                            Find<TextBox>(owner, "HubTimelineOnset").Text = DateTime.Now.AddMinutes(-30).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); await Settle(owner);
                            Require(!HasText(owner, "Older fixture event") && HasText(owner, "Fixture application event") && HasText(owner, I18n.T("hubChronologyOnly")), "Timeline onset filtering treated chronology incorrectly.");
                            Click(owner, "Hub-timeline-hubSaveTimeline"); await WorkFinished(owner); Click(owner, "Hub-timeline-hubLoadTimeline"); await WorkFinished(owner);
                            Find<TextBox>(owner, "HubTimelineOnset").Clear(); await Settle(owner);
                            break;
                        case "usb":
                            Click(owner, "Hub-usb-hubRefreshUsb"); await WorkFinished(owner); Click(owner, "Hub-usb-hubSelectExternalFile"); await WorkFinished(owner);
                            Require(!Find<Button>(owner, "Hub-usb-hubRunReadTest").IsEnabled && Find<CheckBox>(owner, "HubUsbReadConsent").IsChecked == false, "USB file selection silently approved reading.");
                            Find<CheckBox>(owner, "HubUsbReadConsent").IsChecked = true; Click(owner, "Hub-usb-hubRunReadTest"); await WorkFinished(owner); Require(HasText(owner, I18n.T("hubSamplesMatch")), "USB read-only fixture result is missing.");
                            break;
                    }
                    RedactFixturePaths(owner, fixture); await Task.Delay(220); App.Snapshot(owner, Path.Combine(output, "investigation-" + center + "-" + language + (dark ? "-dark" : "") + ".png"));
                }
            }

            // A same-size replacement with a restored timestamp must not reuse prior reputation.
            owner.Navigate("security"); owner.SetSignatureFixture(payload, new("security", DateTimeOffset.UtcNow, [new("hubSha256", hash), new("hubAuthenticode", "state:hubNotSigned")], ["hubSignatureLimits"])); owner.Render(true); await Settle(owner);
            DateTime originalStamp = File.GetLastWriteTimeUtc(payload); Exception? rejectedReputation = null; int priorReputation = reputationCalls;
            try
            {
                byte[] replacement = payloadBytes.ToArray(); replacement[0] ^= 1; await File.WriteAllBytesAsync(payload, replacement); File.SetLastWriteTimeUtc(payload, originalStamp);
                Require(new FileInfo(payload).Length == payloadBytes.Length && File.GetLastWriteTimeUtc(payload) == originalStamp, "Replacement fixture did not preserve metadata.");
                owner.OperationErrorForSmoke = error => rejectedReputation = error; Find<CheckBox>(owner, "HubReputationConsent").IsChecked = true; Click(owner, "Hub-security-hubCheckReputation"); await WorkFinished(owner);
                Require(rejectedReputation is IOException && reputationCalls == priorReputation && Find<CheckBox>(owner, "HubReputationConsent").IsChecked == false, "Same-metadata replacement reused a stale hash or outbound consent.");
                owner.Render(true); await Settle(owner); Require(!Nodes(owner).OfType<TextBlock>().Any(t => t.Text == hash), "Stale signature/reputation evidence remained visible after file replacement.");
            }
            finally { owner.OperationErrorForSmoke = null; await File.WriteAllBytesAsync(payload, payloadBytes); File.SetLastWriteTimeUtc(payload, originalStamp); }
            Settings.Current.DisabledCenters.Add("network"); owner.Navigate("network"); await Settle(owner);
            Require(!Nodes(owner).OfType<TextBox>().Any(t => AutomationProperties.GetAutomationId(t) == "HubNetworkHost"), "A disabled center retained a live diagnostic control."); Settings.Current.DisabledCenters.Remove("network");
            owner.Navigate("doctor"); await Settle(owner); Click(owner, "Hub-doctor-share"); await Settle(owner);
            var assistant = owner.CurrentErrorAssistant ?? throw new Exception("Doctor handoff did not open the existing assistant.");
            try
            {
                var tools = Find<Expander>(assistant, "ConversationTools"); tools.IsExpanded = true; await Settle(assistant); Find<Expander>(assistant, "SmartDiagnostics").IsExpanded = true; await Settle(assistant);
                Require(Find<TextBox>(assistant, "SmartDiagnosticPreview").Text.Contains("Synthetic diagnostic evidence") && Find<CheckBox>(assistant, "SmartDiagnosticConsent").IsChecked == false && !Nodes(assistant).OfType<AdvicePresenter>().Any(), "Doctor handoff auto-approved or sent diagnostic data.");
            }
            finally { assistant.Close(); }
            owner.Navigate("detective"); owner.DetectiveSampleOverride = async token => { canceledPerformance = token; await Task.Delay(Timeout.InfiniteTimeSpan, token); throw new InvalidOperationException("Canceled fixture must not complete."); }; await Settle(owner);
            Click(owner, "Hub-detective-hubStartObserve"); await Until(() => canceledPerformance.CanBeCanceled, "performance cancellation token"); owner.Navigate("network"); await Until(() => canceledPerformance.IsCancellationRequested, "departing performance page cancellation"); await WorkFinished(owner);
            Require(File.Exists(candidate) && reputationCalls == 4 && networkCalls == 4 && readCalls == 4, "Fixture-only calls escaped consent boundaries or touched cleanup files.");
            File.WriteAllText(Path.Combine(output, "investigations-result.txt"), "PASS: eight centers in English/Arabic and both themes (32 fixture screenshots), no collection on page open, source opt-in, real source-result rendering, performance table/history/cancellation on navigation, one-request hash reputation, same-size/mtime file replacement rejects stale reputation without a request, network consent reset/history, storage duplicate display, cleanup unselected/ack/declined/approved mocked boundaries without deletion, content-index opt-in/Arabic preview, timeline chronology/history, USB read consent, disabled centers and Smart Repair reviewed handoff without transmission. All AI/network/WMI/signature/recycler/read-test calls injected fixtures; no native repair, file deletion, external request or microphone use.");
        }
        finally
        {
            if (owner?.CurrentErrorAssistant is { } assistant) assistant.Close(); owner?.Close();
            Settings.SetKey("gemini", originalKey); Settings.SetReputationKey(originalReputation); File.WriteAllText(Path.Combine(Settings.Folder, "settings.json"), settings); Settings.Load(); originalOwner.ApplyTheme(); originalOwner.Navigate("home");
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath());
            if (LocalFileScope.Contains(temporaryRoot, fixture) && Path.GetFileName(fixture).StartsWith("baseera-investigation-ui-", StringComparison.Ordinal) && Directory.Exists(fixture) && (File.GetAttributes(fixture) & FileAttributes.ReparsePoint) == 0) Directory.Delete(fixture, true);
        }
    }
}
