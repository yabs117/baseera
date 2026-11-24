using System.Windows.Automation;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal static class DiagnosticDeliverySmokeChecks
{
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Settle(Window window) { await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(60); window.UpdateLayout(); }
    private static async Task Until(Func<bool> ready) { for (int i = 0; i < 150; i++) { if (ready()) return; await Task.Delay(20); } throw new Exception("Diagnostic delivery fixture timed out."); }
    internal static async Task Run(MainWindow owner, string output)
    {
        string language = Settings.Current.Language, provider = Settings.Current.AiProvider; bool dark = Settings.Current.Dark;
        string key = Settings.GetKey("gemini"), model = Settings.Current.GeminiModel;
        const string marker = "private-device-fixture";
        try
        {
            Settings.Current.AiProvider = "gemini";
            Settings.Current.GeminiModel = "gemini-fixture"; Settings.SetKey("gemini", "diagnostic-fixture-key-not-live");
            foreach (string locale in new[] { "ar", "en", "fr", "es" })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = locale == "ar"; owner.ApplyTheme();
                int collections = 0; var requests = new List<ErrorChatRequest>();
                var pending = new TaskCompletionSource<ErrorAdvice>(TaskCreationOptions.RunContinuationsAsynchronously); bool delay = false;
                var advice = ErrorAssistant.ParseAdvice(ErrorLanguageSmokeChecks.Fixture(locale));
                var dialog = new ErrorAssistantWindow(owner,
                    collectDiagnostics: (options, ct) =>
                    {
                        Require(options.DeviceHealth && !options.SystemInfo && !options.ApplicationEvents && !options.SystemEvents, "Unselected source was read.");
                        ct.ThrowIfCancellationRequested(); collections++; return Task.FromResult("CPU sampled utilization: 90%\nRAM: 92%\n" + marker);
                    },
                    analyzeConversation: (request, _) => { requests.Add(request); return delay ? pending.Task : Task.FromResult(advice); });
                dialog.Show(); await Settle(dialog);
                try
                {
                    var composer = Find<TextBox>(dialog, "ErrorDescription"); var delivery = Find<TextBlock>(dialog, "DiagnosticDeliveryStatus");
                    Require(delivery.Text == I18n.T("diagnosticNoneAttached") && collections == 0, "Initial diagnostics state hides automatic collection or claims an attachment.");
                    composer.Text = "An ordinary question"; Click(dialog, "errorAnalyze"); await Until(() => requests.Count == 1); await Settle(dialog); Nodes(dialog).OfType<AdvicePresenter>().Last().Finish();
                    Require(requests[0].Diagnostics.Length == 0, "Ordinary chat attached diagnostics automatically.");
                    Click(dialog, "chatTools"); await Settle(dialog); Find<Expander>(dialog, "SmartDiagnostics").IsExpanded = true; await Settle(dialog);
                    Find<CheckBox>(dialog, "deviceDiagnosticSource").IsChecked = true; composer.Text = "Review my enabled device readings";
                    Click(dialog, "errorAnalyze"); await Settle(dialog);
                    Require(requests.Count == 1 && collections == 0 && composer.Text == "Review my enabled device readings" && Find<Button>(dialog, "DiagnosticSendWithout").IsVisible && delivery.Text == I18n.T("diagnosticSourcesNotCollected"),
                        "Source selection silently sent a diagnosis without its readings or charged a request.");
                    if (locale is "ar" or "en") App.Snapshot(dialog, Path.Combine(output, "diagnostic-selected-" + locale + ".png"));
                    Click(dialog, "DiagnosticCollectForMessage"); await Until(() => collections == 1 && Find<TextBox>(dialog, "SmartDiagnosticPreview").Text.Contains(marker)); await Settle(dialog);
                    var share = Find<CheckBox>(dialog, "SmartDiagnosticConsent");
                    Require(share.IsChecked != true && requests.Count == 1 && delivery.Text == I18n.T("diagnosticPreviewNotAttached"), "Collect action granted sharing or started inference.");
                    Click(dialog, "errorAnalyze"); await Settle(dialog); Require(requests.Count == 1 && composer.Text.Length > 0, "Unapproved preview was silently omitted by Send.");
                    Click(dialog, "DiagnosticReviewForMessage"); await Settle(dialog); Require(share.IsChecked != true && share.IsKeyboardFocusWithin, "Review action silently approved sharing or failed to focus consent.");
                    share.IsChecked = true;
                    Require(delivery.Text == string.Format(I18n.T("diagnosticReadyForMessage"), AiProviderBadge.Name("gemini", false)), "Approved next-message attachment state is incorrect.");
                    Require(!Nodes(dialog).OfType<TextBlock>().Any(t => t.Text == I18n.T("diagnosticPreviewNotAttached")), "Ready status contradicts a stale not-attached warning.");
                    if (locale is "ar" or "en") App.Snapshot(dialog, Path.Combine(output, "diagnostic-approved-" + locale + ".png"));
                    delay = true; Click(dialog, "errorAnalyze"); await Until(() => requests.Count == 2);
                    Require(delivery.Text == string.Format(I18n.T("diagnosticInRequest"), AiProviderBadge.Name("gemini", false)) && requests[1].Diagnostics.Contains(marker) && share.IsChecked == false,
                        "Approved readings did not reach the request, were reapproved or lacked in-flight attachment status.");
                    pending.SetResult(advice); delay = false; await Until(() => Find<Button>(dialog, "errorAnalyze").IsEnabled); await Settle(dialog); Nodes(dialog).OfType<AdvicePresenter>().Last().Finish();
                    Require(delivery.Text == I18n.T("diagnosticApprovalConsumed"), "Consumed approval remained ready for another request.");
                    composer.Text = "A follow-up"; Click(dialog, "errorAnalyze"); await Settle(dialog); Require(requests.Count == 2, "Follow-up silently dropped the selected readings or reused one-request consent.");
                    Click(dialog, "DiagnosticSendWithout"); await Until(() => requests.Count == 3); await Settle(dialog); Nodes(dialog).OfType<AdvicePresenter>().Last().Finish();
                    Require(requests[2].Diagnostics.Length == 0 && !requests[2].History.Any(x => x.User.Contains(marker) || x.Assistant.Contains(marker)), "Explicit text-only send leaked private diagnostic context.");
                    Require(delivery.Text == I18n.T("diagnosticPreviewNotAttached") && share.IsChecked == false, "Text-only choice left misleading attachment status.");
                    // Editing and recipient changes still require a fresh approval; the gate itself never grants it.
                    share.IsChecked = true; Find<TextBox>(dialog, "SmartDiagnosticPreview").Text += "\nreviewed edit";
                    Require(share.IsChecked == false && delivery.Text == I18n.T("diagnosticPreviewNotAttached"), "Editing retained a ready attachment label.");
                    share.IsChecked = true; Settings.Current.AiProvider = "openai"; composer.Text = "Recipient changed"; Click(dialog, "errorAnalyze"); await Settle(dialog);
                    Require(requests.Count == 3 && share.IsChecked == false, "Changed recipient reused consent or sent a request."); Settings.Current.AiProvider = "gemini";
                    Click(dialog, "errorAnalyze"); // Acknowledge restoration of the connection; no automatic send.
                    Click(dialog, "errorAnalyze"); await Settle(dialog); Require(requests.Count == 3, "Recipient restoration bypassed review.");
                    composer.Clear(); Click(dialog, "DiagnosticSendWithout"); await Settle(dialog); Require(requests.Count == 3, "An empty text-only request was sent.");
                    // The existing diagnostic-only request path works once data is actually approved.
                    share.IsChecked = true; Click(dialog, "errorAnalyze"); await Until(() => requests.Count == 4); await Settle(dialog);
                    Require(requests[3].Text.Length == 0 && requests[3].Diagnostics.Contains(marker), "Approved diagnostic-only submission was blocked or lost its data.");
                }
                finally { dialog.Close(); Settings.Current.AiProvider = "gemini"; }
            }
            File.WriteAllText(Path.Combine(output, "diagnostic-delivery-result.txt"), "PASS: four-language per-message diagnostic status; selected-but-uncollected and unapproved/consumed previews block silent omission; explicit scoped collection/review without automatic approval; approved and diagnostic-only payloads reach the analyzer; in-flight status; one-request consent, edit/recipient invalidation; explicit text-only choice; empty input blocked and private context excluded. Fake diagnostics/AI only, no live logs or paid request.");
        }
        finally { Settings.SetKey("gemini", key); Settings.Current.GeminiModel = model; Settings.Current.Language = language; Settings.Current.Dark = dark; Settings.Current.AiProvider = provider; owner.ApplyTheme(); owner.Render(); }
    }
}
