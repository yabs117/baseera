using System.Net;
using System.Text.Json;
using System.Windows.Automation;
using Baseera.App.Services;

namespace Baseera.App;

// Isolated fixtures only: never send credentials, screenshots or paid inference to a live service.
internal static class AiSettingsSmokeChecks
{
    private sealed class Reply(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => answer(request, ct); }
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body)) };
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Settle(Window window) { await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(40); window.UpdateLayout(); }

    internal static async Task Run(MainWindow owner, string output)
    {
        int requests = 0;
        foreach (var provider in AiProviders.All)
        {
            string key = "probe-fixture-" + provider.Id;
            using var http = new HttpClient(new Reply(async (request, ct) =>
            {
                requests++;
                string auth = provider.Protocol switch { "gemini" => request.Headers.GetValues("x-goog-api-key").Single(), "anthropic" => request.Headers.GetValues("x-api-key").Single(), _ => request.Headers.Authorization?.Parameter ?? "" };
                Require(request.RequestUri!.Host == new Uri(provider.BaseUrl).Host && auth == key && request.Method == HttpMethod.Post, "Probe crossed provider/credential boundary.");
                string body = await request.Content!.ReadAsStringAsync(ct);
                Require(!body.Contains(key) && body.Contains(AiConnectionProbe.Prompt) && body.Contains(AiConnectionProbe.Instructions), "Probe leaked a key or omitted fixed instructions.");
                using var json = JsonDocument.Parse(body); var root = json.RootElement; string encoded;
                if (provider.Id == "gemini")
                {
                    Require(request.RequestUri.AbsoluteUri == provider.BaseUrl + "models/vision-fixture:generateContent", "Wrong Gemini test route.");
                    encoded = root.GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("inline_data").GetProperty("data").GetString()!;
                }
                else if (provider.Protocol == "responses") encoded = root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("image_url").GetString()!.Split(',')[1];
                else if (provider.Protocol == "anthropic") encoded = root.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("source").GetProperty("data").GetString()!;
                else encoded = root.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString()!.Split(',')[1];
                byte[] bytes = Convert.FromBase64String(encoded);
                Require(bytes.Length < 5000 && bytes.SequenceEqual(AiConnectionProbe.Image(2)), "Probe used an unexpected or user-provided image.");
                return provider.Protocol switch
                {
                    "gemini" => Json(new { candidates = new[] { new { content = new { parts = new[] { new { text = "BLUE" } } } } } }),
                    "responses" => Json(new { output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = "BLUE" } } } } }),
                    "anthropic" => Json(new { content = new[] { new { type = "text", text = "BLUE" } } }),
                    "cohere" => Json(new { message = new { content = new[] { new { type = "text", text = "BLUE" } } } }),
                    _ => Json(new { choices = new[] { new { message = new { content = "BLUE" } } } })
                };
            }));
            var connection = new AiProbeConnection(provider.Id, "vision-fixture", key);
            Require(!connection.ToString().Contains(key) && (await AiConnectionProbe.Test(connection, CancellationToken.None, http, 2)).ImageAnswerMatched, "Direct provider image test failed.");
        }
        Require(requests == 20, "Unexpected automatic/retried probe requests.");
        bool accountUsed = false;
        var accountResult = await AiConnectionProbe.Test(new("openai", "vision-fixture", "", "explicit-draft-account"), CancellationToken.None, color: 0,
            accountRequest: (account, model, prompt, image, _) =>
            {
                accountUsed = account == "explicit-draft-account" && model == "vision-fixture" && prompt == AiConnectionProbe.Prompt && image.SequenceEqual(AiConnectionProbe.Image(0));
                return Task.FromResult("RED");
            });
        Require(accountUsed && accountResult.ImageAnswerMatched, "ChatGPT probe ignored the explicitly selected draft account.");
        var partial = await AiConnectionProbe.Test(new("openai", "vision-fixture", "", "draft"), CancellationToken.None, color: 0, accountRequest: (_, _, _, _, _) => Task.FromResult("GREEN"));
        Require(!partial.ImageAnswerMatched, "Wrong image answer was claimed as successful vision.");
        using (var rejected = new HttpClient(new Reply((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))))
        {
            bool denied = false; try { await AiConnectionProbe.Test(new("gemini", "vision-fixture", "fake"), CancellationToken.None, rejected); } catch (InvalidOperationException) { denied = true; }
            Require(denied, "HTTP failure became success.");
        }
        using (var stop = new CancellationTokenSource())
        using (var waiting = new HttpClient(new Reply(async (_, ct) => { stop.Cancel(); await Task.Delay(Timeout.Infinite, ct); return Json(new { }); })))
        {
            bool canceled = false; try { await AiConnectionProbe.Test(new("gemini", "vision-fixture", "fake"), stop.Token, waiting); } catch (OperationCanceledException) { canceled = true; }
            Require(canceled, "Probe cancellation was ignored.");
        }

        string snapshot = JsonSerializer.Serialize(Settings.Current);
        var keys = AiProviders.All.ToDictionary(p => p.Id, p => Settings.GetKey(p.Id));
        try
        {
            Settings.Current.Onboarded = true; Settings.Current.ErrorAssistantEnabled = true;
            Settings.Current.AiProvider = "gemini"; Settings.Current.GeminiModel = ""; Settings.SetKey("gemini", "");
            owner.ResetConnectionDraftForSmoke(); owner.ShowErrorAssistant(); await Settle(owner);
            Require(owner.CurrentErrorAssistant is null && Nodes(owner).OfType<AiConnectionPanel>().Count() == 1, "Unconfigured assistant did not route to Settings.");
            int calls = 0, opened = 0; bool match = true, fail = false;
            TaskCompletionSource<AiProbeResult>? delayed = null; CancellationToken pendingToken = default;
            var draft = new AiConnectionDraft { Provider = "gemini" };
            var panel = new AiConnectionPanel(draft, () => { }, list: (_, _, _) => Task.FromResult<IReadOnlyList<AiModelOption>>([new("vision-fixture", "Fixture vision", "Google", "aiFree")]),
                probe: (connection, ct) =>
                {
                    calls++; Require(connection.Provider == draft.Provider && connection.Key == "synthetic-probe-key", "UI tested the wrong draft.");
                    if (fail) throw new HttpRequestException("synthetic-probe-key must never appear on screen");
                    if (delayed is not null) { pendingToken = ct; return delayed.Task; }
                    return Task.FromResult(new AiProbeResult(match, TimeSpan.FromSeconds(.8)));
                }, openAssistant: () => opened++);
            var surface = new Border { Padding = new Thickness(22), Child = panel }; surface.SetResourceReference(Border.BackgroundProperty, "Bg");
            var host = new Window { Owner = owner, Width = 700, Height = 790, Content = new ScrollViewer { Content = surface, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, FontFamily = Brand.InterfaceFont, ShowInTaskbar = false };
            host.Show(); await Settle(host);
            try
            {
                await panel.TestConnection(); Require(calls == 0 && !Find<Button>(panel, "AiOpenAssistant").IsEnabled, "Incomplete setup caused a request or enabled chat.");
                panel.KeyInput.Password = "synthetic-probe-key"; await panel.LoadModels(); panel.ModelInput.SelectedIndex = 0;
                Require(calls == 0, "Loading/switching models sent inference automatically.");
                await panel.TestConnection();
                Require(calls == 1 && Find<TextBlock>(panel, "AiTestStatus").Text.Contains(I18n.T("aiSaveBeforeChat")) && Settings.Current.GeminiModel.Length == 0, "Test silently saved settings or opened an unsaved connection.");
                Click(panel, "save"); Require(Find<Button>(panel, "AiOpenAssistant").IsEnabled && Find<TextBlock>(panel, "AiTestStatus").Text == I18n.T("aiSavedAndTested"), "Test/save did not mark the same saved connection ready.");
                Click(panel, "AiOpenAssistant"); Require(opened == 1, "Saved connection cannot open chat.");
                Settings.Current.ErrorAssistantEnabled = false; Click(panel, "AiOpenAssistant");
                Require(opened == 1 && Find<TextBlock>(panel, "AiTestStatus").Text == I18n.T("disabledDesc"), "Opening a disabled tool failed silently."); Settings.Current.ErrorAssistantEnabled = true;
                match = false; await panel.TestConnection(); Require(Find<TextBlock>(panel, "AiTestStatus").Text.StartsWith(string.Format(I18n.T("aiProbePartial"), "0.8")), "Partial vision result shown as complete success.");
                fail = true; await panel.TestConnection(); Require(Find<TextBlock>(panel, "AiTestStatus").Text == I18n.T("aiProbeFailed") && !Nodes(panel).OfType<TextBlock>().Any(t => t.Text.Contains("synthetic-probe-key")), "Network failure leaked credentials or escaped UI."); fail = false;
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously); var pending = panel.TestConnection();
                Require(!Find<Button>(panel, "AiTestConnection").IsEnabled && Find<Button>(panel, "AiCancelTest").IsVisible, "Pending test lacks cancel/disabled controls.");
                int beforeDuplicate = calls; await panel.TestConnection(); Require(calls == beforeDuplicate, "Duplicate test started a second request.");
                panel.Service.SelectedItem = AiProviders.Get("anthropic");
                delayed.SetResult(new(true, TimeSpan.Zero)); await pending;
                Require(pendingToken.IsCancellationRequested && Find<TextBlock>(panel, "AiTestStatus").Text.Length == 0, "Old provider result overwrote new provider state.");
                panel.Service.SelectedItem = AiProviders.Get("gemini"); delayed = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = panel.TestConnection();
                Click(panel, "AiCancelTest"); delayed.SetResult(new(true, TimeSpan.Zero)); await pending;
                Require(pendingToken.IsCancellationRequested && Find<TextBlock>(panel, "AiTestStatus").Text == I18n.T("aiProbeCancelled"), "Canceled test changed into success.");
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = panel.TestConnection();
                panel.ModelInput.SelectedIndex = -1; delayed.SetResult(new(true, TimeSpan.Zero)); await pending;
                Require(pendingToken.IsCancellationRequested && Find<TextBlock>(panel, "AiTestStatus").Text.Length == 0, "Model edit retained a stale result."); panel.ModelInput.SelectedIndex = 0;
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = panel.TestConnection();
                panel.KeyInput.Password = "changed-key"; delayed.SetResult(new(true, TimeSpan.Zero)); await pending;
                Require(pendingToken.IsCancellationRequested && Find<TextBlock>(panel, "AiTestStatus").Text.Length == 0 && !Find<Button>(panel, "AiOpenAssistant").IsEnabled, "Key edit retained a stale result or saved-ready state.");
                panel.KeyInput.Password = "synthetic-probe-key"; await panel.LoadModels(); panel.ModelInput.SelectedIndex = 0;
                delayed = new(TaskCreationOptions.RunContinuationsAsynchronously); pending = panel.TestConnection();
                host.Close(); delayed.SetResult(new(true, TimeSpan.Zero)); await pending;
                Require(pendingToken.IsCancellationRequested && Find<TextBlock>(panel, "AiTestStatus").Text.Length == 0, "Leaving Settings accepted a late probe result.");
            }
            finally { host.Close(); }

            Settings.Current.AiProvider = "gemini"; Settings.Current.GeminiModel = "vision-fixture"; Settings.SetKey("gemini", "synthetic-probe-key");
            foreach (string locale in new[] { "en", "ar", "fr", "es" })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = locale == "ar"; owner.ApplyTheme(); owner.ResetConnectionDraftForSmoke();
                owner.ConnectionProbeOverride = (_, _) => Task.FromResult(new AiProbeResult(true, TimeSpan.FromSeconds(.8)));
                owner.OpenAiSettings(); await Settle(owner);
                var settingsPanel = Nodes(owner).OfType<AiConnectionPanel>().Single(); await settingsPanel.TestConnection();
                Find<TextBlock>(settingsPanel, "AiTestStatus").BringIntoView(); await Settle(owner);
                Require(Find<Button>(settingsPanel, "AiOpenAssistant").IsEnabled && Find<TextBlock>(settingsPanel, "AiTestStatus").Text.Contains(I18n.T("aiChatReady")), "Localized saved probe/assistant controls unavailable.");
                if (locale is "en" or "ar") App.Snapshot(owner, Path.Combine(output, "ai-settings-test-" + locale + ".png"));
                owner.ShowErrorAssistant(); await Settle(owner);
                var assistant = owner.CurrentErrorAssistant!;
                Require(assistant.IsVisible && !Nodes(assistant).OfType<AiConnectionPanel>().Any() && !Nodes(assistant).OfType<PasswordBox>().Any() &&
                    !Nodes(assistant).OfType<ComboBox>().Any(c => AutomationProperties.GetAutomationId(c) is "ChatModelPicker" or "ErrorAssistantService"), "Assistant still contains editable AI configuration.");
                Require(Find<TextBox>(assistant, "ErrorDescription").IsKeyboardFocusWithin, "Chat did not start in composer.");
                if (locale is "en" or "ar") App.Snapshot(assistant, Path.Combine(output, "assistant-ready-" + locale + ".png"));
                assistant.Close();
            }
            // Follow the actual Settings menu path with a completed conversation and a new draft/image.
            Settings.Current.Language = "en"; owner.ApplyTheme(); owner.ResetConnectionDraftForSmoke();
            var advice = ErrorAssistant.ParseAdvice(ErrorLanguageSmokeChecks.Fixture("en"));
            var dialog = new ErrorAssistantWindow(owner, analyzeConversation: (_, _) => Task.FromResult(advice)); dialog.Show(); await Settle(dialog);
            var composer = Find<TextBox>(dialog, "ErrorDescription"); composer.Text = "First question"; Click(dialog, "errorAnalyze"); await Settle(dialog);
            var first = Nodes(dialog).OfType<AdvicePresenter>().Single(); first.Finish(); composer.Text = "Follow-up draft";
            typeof(ErrorAssistantWindow).GetMethod("SetImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, [new ErrorImage(AiConnectionProbe.Image(1), "synthetic.jpg")]);
            typeof(ErrorAssistantWindow).GetMethod("ShowConnectionSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, null); await Settle(owner);
            Require(!dialog.IsVisible, "Assistant did not give access to main Settings.");
            var actualPanel = Nodes(owner).OfType<AiConnectionPanel>().Single(); Click(actualPanel, "AiOpenAssistant"); await Settle(dialog);
            Require(ReferenceEquals(owner.CurrentErrorAssistant, dialog) && dialog.IsVisible && composer.Text == "Follow-up draft" && ReferenceEquals(Nodes(dialog).OfType<AdvicePresenter>().Single(), first) && Nodes(dialog).OfType<System.Windows.Controls.Image>().Any(i => i.Source is not null && i.IsVisible), "Settings round trip discarded history, attachment or draft.");
            dialog.Close();
        }
        finally
        {
            owner.CurrentErrorAssistant?.Close(); owner.ConnectionProbeOverride = null;
            foreach (var pair in keys) Settings.SetKey(pair.Key, pair.Value);
            File.WriteAllText(Path.Combine(Settings.Folder, "settings.json"), snapshot); Settings.Load(); owner.ResetConnectionDraftForSmoke(); owner.ApplyTheme(); owner.Navigate("home");
        }
        File.WriteAllText(Path.Combine(output, "ai-settings-result.txt"), "PASS: 20 direct provider probe payloads with a synthetic 64x64 image; explicit ChatGPT draft account; partial, HTTP failure and cancellation; setup/test/save/open separation; no automatic inference or credential disclosure; stale provider/key results and canceled probes ignored; four-language settings/chat UI; unconfigured routing; same conversation, draft and image on Settings round trip. Fake HTTP/probes only.");
    }
}

public sealed partial class MainWindow
{
    internal void ResetConnectionDraftForSmoke()
    {
        if (!smokeMode) throw new InvalidOperationException("Isolated smoke fixtures only.");
        aiConnectionDraft.Services.Clear(); aiConnectionDraft.Provider = AiCatalog.Provider; aiConnectionDraft.OpenAiMode = Settings.Current.OpenAiAuth;
        aiConnectionDraft.ChatGptAccount = Settings.Current.ChatGptAccount; aiConnectionDraft.ChatGpt = new() { Model = Settings.Current.ChatGptModel };
    }
}
