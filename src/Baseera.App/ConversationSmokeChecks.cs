using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Baseera.Core;
using Baseera.App.Services;

namespace Baseera.App;

internal static class ConversationSmokeChecks
{
    private sealed class Reply(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => reply(request); }
    private sealed class Recorder : IVoiceRecorder
    {
        public bool Recording { get; private set; }
        public void Start() => Recording = true;
        public byte[] Stop() { Recording = false; return VoiceRecording.WrapPcm(new byte[32000]); }
        public void Dispose() => Recording = false;
    }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Settle(Window window) { await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(30); window.UpdateLayout(); }
    internal static async Task Run(MainWindow owner, string output)
    {
        var wave = VoiceRecording.WrapPcm(new byte[32000]);
        Require(Encoding.ASCII.GetString(wave, 0, 4) == "RIFF" && BitConverter.ToInt32(wave, 24) == 16000 && BitConverter.ToInt32(wave, 40) == 32000 && wave.Length == 32044, "Recorded WAV header/PCM bounds are wrong.");
        Require(!VoiceTranscription.Supported("openai", true) && !VoiceTranscription.Supported("anthropic", false) && VoiceTranscription.Supported("groq", false), "Voice capability gating accepts unsupported authentication/provider.");
        string videoPath = Path.Combine(Path.GetFullPath(output), "generated-fixture.avi"); VideoFixture.Write(videoPath);
        var clip = await ConversationMedia.Video(videoPath, CancellationToken.None);
        Require(clip.Bytes.Length > 100 && clip.Seconds is > 1 and < 3 && clip.Frames.Bytes.Length > 1000 && clip.Mime == "video/avi", "Generated video was not sampled into a bounded preview.");
        using (var frameStream = new MemoryStream(clip.Frames.Bytes))
        using (var pixels = new System.Drawing.Bitmap(frameStream))
        { Require(pixels.GetPixel(100, 100).B > 60, "Video extraction produced blank frames."); }
        foreach (var provider in AiProviders.All.Where(x => x.Id != "gemini"))
        {
            bool denied = false;
            try { await ErrorAssistant.Analyze("Native video must not leak", null, "English", CancellationToken.None, suppliedKey: "fixture-key", suppliedModel: "fixture-model", suppliedProvider: provider.Id, video: clip); }
            catch (InvalidDataException) { denied = true; }
            Require(denied, "Native video reached an unsupported provider instead of the sampled image path.");
        }
        using (var videoHttp = new HttpClient(new Reply(async request =>
        {
            string body = await request.Content!.ReadAsStringAsync();
            Require(body.Contains("video/avi") && !body.Contains("audio/wav"), "Native video payload is missing or replaced with an unrelated recording.");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = ErrorLanguageSmokeChecks.Fixture("en") } } } } } }), Encoding.UTF8, "application/json") };
        })))
            await ErrorAssistant.Analyze("Video fixture", null, "English", CancellationToken.None, videoHttp, "fixture-key", "gemini-fixture", "gemini", video: clip);
        foreach (string provider in new[] { "openai", "groq", "gemini" })
        {
            int requests = 0;
            using var http = new HttpClient(new Reply(async request =>
            {
                requests++; string body = await request.Content!.ReadAsStringAsync();
                Require(request.RequestUri!.Host == new Uri(AiProviders.Get(provider).BaseUrl).Host && !body.Contains("different-provider-key"), "Transcription changed service/key scope.");
                if (provider == "gemini") Require(body.Contains("audio/wav") && body.Contains("systemInstruction"), "Gemini audio payload or transcription instructions missing.");
                else Require(body.Contains("recording.wav") && body.Contains(VoiceTranscription.Model(provider, "gemini-fixture")) && request.Content.Headers.ContentType!.MediaType == "multipart/form-data", "Audio upload model or multipart payload missing.");
                string reply = provider == "gemini" ? JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = "النص المفرغ للاختبار" } } } } } }) : "{\"text\":\"النص المفرغ للاختبار\"}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
            }));
            string transcript = await VoiceTranscription.Transcribe(wave, provider, "fixture-own-key", "gemini-fixture", CancellationToken.None, http);
            Require(requests == 1 && transcript == "النص المفرغ للاختبار", "Audio transcript was not decoded correctly.");
        }
        var chatFixture = JsonNode.Parse(ErrorLanguageSmokeChecks.Fixture("en"))!.AsObject();
        chatFixture["response_mode"] = "conversation"; chatFixture["message"] = "Yes. **Tell me what happened next.**"; chatFixture["steps"] = new JsonArray();
        var advice = ErrorAssistant.ParseAdvice(chatFixture.ToJsonString()); Require(advice.Message is not null && advice.Steps.Count == 0, "Conversational answer was forced into a diagnosis worksheet.");
        chatFixture["repair_actions"] = new JsonArray("flush-dns"); bool rejected = false;
        try { ErrorAssistant.ParseAdvice(chatFixture.ToJsonString()); } catch (InvalidDataException) { rejected = true; }
        Require(rejected, "Conversational answer bypassed structured repair safety.");
        var history = new[] { new ConversationExchange("Earlier question", "Earlier answer", "Prior image not resent", DateTimeOffset.UtcNow) };
        string prompt = ErrorAssistantPrompt.Input("follow-up", false, "English", conversation: history);
        Require(prompt.Contains("Earlier question") && prompt.Contains("Earlier answer") && !ErrorAssistantPrompt.Instructions.Contains("Earlier question"), "Conversation context entered the instruction channel or was lost.");

        string language = Settings.Current.Language, providerBefore = Settings.Current.AiProvider, modelBefore = Settings.Current.GeminiModel, keyBefore = Settings.GetKey("gemini"); bool dark = Settings.Current.Dark;
        try
        {
            Settings.Current.AiProvider = "gemini"; Settings.Current.GeminiModel = "gemini-fixture"; Settings.SetKey("gemini", "fixture-conversation-key");
            foreach (string locale in new[] { "en", "ar", "fr", "es" })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = locale == "ar"; owner.ApplyTheme();
                var inputs = new List<ErrorChatRequest>(); bool continueAllowed = false; int confirmations = 0;
                var localized = advice with { Locale = AdviceLocale.Interface, Message = locale switch { "ar" => "نعم، يمكننا المتابعة. **ما الذي حدث بعد الخطوة السابقة؟** يمكنك إضافة صورة أو فيديو أو تسجيل صوتك ثم مراجعة النص قبل إرساله.", "fr" => "Oui, continuons. **Que s’est-il passé ensuite ?**", "es" => "Sí, continuemos. **¿Qué pasó después?**", _ => advice.Message } };
                var dialog = new ErrorAssistantWindow(owner, analyzeConversation: (request, _) => { inputs.Add(request); return Task.FromResult(localized); },
                    confirmRequests: _ => { confirmations++; return continueAllowed; });
                dialog.Show(); await Settle(dialog);
                var composer = Find<TextBox>(dialog, "ErrorDescription");
                for (int i = 0; i < 10; i++)
                {
                    composer.Text = "Question " + i; Click(dialog, "errorAnalyze"); await Settle(dialog); Nodes(dialog).OfType<AdvicePresenter>().Last().Finish(); await Settle(dialog);
                    Require(composer.Text.Length == 0, "Sent draft was not cleared from the bottom composer.");
                }
                Require(inputs.Count == 10 && inputs[1].History.Count == 1 && inputs[1].History[0].User == "Question 0" && inputs[^1].History.Count <= 8,
                    "Follow-ups did not receive bounded chronological context.");
                composer.Text = "Do not send without continuation"; Click(dialog, "errorAnalyze"); await Settle(dialog);
                Require(inputs.Count == 10 && confirmations == 1 && composer.Text.Contains("Do not send"), "Declined usage checkpoint sent a request or discarded the draft.");
                Require(Find<Border>(dialog, "ChatCheckpoint").IsVisible, "Ten-request checkpoint is not visible.");
                Click(dialog, "chatContinue"); Require(inputs.Count == 10, "Allow continuation automatically sent a message.");
                continueAllowed = true;
                for (int i = 10; i < 23; i++) { composer.Text = "Question " + i; Click(dialog, "errorAnalyze"); await Settle(dialog); Nodes(dialog).OfType<AdvicePresenter>().Last().Finish(); }
                Require(inputs.Count == 23 && Nodes(dialog).OfType<AdvicePresenter>().Count() == 20 && inputs[^1].History.Count <= 8, "Continuing chat leaked unbounded controls/context or stopped after an arbitrary turn limit.");
                var last = Nodes(dialog).OfType<AdvicePresenter>().Last(); Require(!Nodes(last).OfType<AdviceTextView>().Any(x => AutomationProperties.GetAutomationId(x) == "AdviceGreeting"), "Follow-up repeated the first-turn welcome.");
                if (locale is "ar" or "en")
                {
                    await Settle(dialog);
                    App.Snapshot(dialog, Path.Combine(output, "continuous-chat-" + locale + ".png"));
                    composer.Text = locale == "ar" ? "هل يمكن أن تشرح لي الخطوة التالية؟" : "Could you explain the next step?";
                    App.Snapshot(dialog, Path.Combine(output, "continuous-composer-" + locale + ".png"));
                }
                dialog.Close();
            }
            Settings.Current.Language = "en"; Settings.Current.Dark = false; owner.ApplyTheme();
            bool uploadConsent = false; int uploads = 0, analyses = 0; var fakeRecorder = new Recorder();
            var voice = new ErrorAssistantWindow(owner, analyzeConversation: (_, _) => { analyses++; return Task.FromResult(advice); }, recorderFactory: () => fakeRecorder,
                approveAudioUpload: () => uploadConsent, transcribeAudio: (_, _, _, _, _) => { uploads++; return Task.FromResult("Editable speech transcript"); });
            voice.Show(); await Settle(voice);
            Click(voice, "chatRecord"); Require(fakeRecorder.Recording && uploads == 0, "Recording uploaded audio or did not start locally.");
            Click(voice, "chatRecord"); Require(!fakeRecorder.Recording && uploads == 0, "Stopping recording automatically uploaded audio.");
            Click(voice, "chatTranscribe"); Require(uploads == 0, "Declined upload transmitted audio.");
            uploadConsent = true; Click(voice, "chatTranscribe"); await Settle(voice);
            Require(uploads == 1 && analyses == 0 && Find<TextBox>(voice, "ErrorDescription").Text == "Editable speech transcript", "Speech transcript auto-sent or did not remain editable.");
            var ledger = (ConversationLedger)typeof(ErrorAssistantWindow).GetField("conversation", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(voice)!;
            Require(ledger.Requests == 1, "Transcription was not included in the credit checkpoint.");
            App.Snapshot(voice, Path.Combine(output, "voice-review-en.png"));
            Click(voice, "errorAnalyze"); await Settle(voice); Require(analyses == 1 && ledger.Requests == 2, "Reviewed voice transcript did not send on explicit request or bypassed usage accounting.");
            voice.Close(); Require(!fakeRecorder.Recording, "Closing left a recorder active.");
            int failedAttempts = 0;
            var failed = new ErrorAssistantWindow(owner, analyzeConversation: (_, _) => { failedAttempts++; throw new InvalidDataException("Fixture request failure"); });
            failed.Show(); await Settle(failed); Find<TextBox>(failed, "ErrorDescription").Text = "Keep this question for retry";
            Click(failed, "errorAnalyze"); await Settle(failed);
            Require(failedAttempts == 1 && Find<TextBox>(failed, "ErrorDescription").Text == "Keep this question for retry", "Failed request retried automatically or discarded its draft.");
            failed.Close();
            int scopedRequests = 0;
            var scoped = new ErrorAssistantWindow(owner, analyzeConversation: (_, _) => { scopedRequests++; return Task.FromResult(advice); });
            scoped.Show(); await Settle(scoped); Find<TextBox>(scoped, "ErrorDescription").Text = "Do not send after an external credential change";
            Settings.SetKey("gemini", "different-fixture-account-key"); Click(scoped, "errorAnalyze"); await Settle(scoped);
            Require(scopedRequests == 0 && Find<TextBox>(scoped, "ErrorDescription").Text.StartsWith("Do not send"), "External account change silently submitted a draft before review.");
            scoped.Close(); Settings.SetKey("gemini", "fixture-conversation-key");
            File.WriteAllText(Path.Combine(output, "conversation-result.txt"), "PASS: bounded continuous text history, 23-turn continuation in four locales, immutable past turns, no repeated follow-up welcome, ten-request refusal/approval, no auto-send, native bottom composer, 20-render retention, conversational/diagnostic safety, real decoding and six nonblank samples from a generated silent AVI, native Gemini video HTTP payload and rejection at the other 19 native-video paths, fixture WAV framing, three direct transcription HTTP payloads, local fake recording/stop/discard, explicit upload/text review and transcription usage accounting. No real microphone, OS dictation, paid API or repair command was used.");
        }
        finally { Settings.Current.Language = language; Settings.Current.Dark = dark; Settings.Current.AiProvider = providerBefore; Settings.Current.GeminiModel = modelBefore; Settings.SetKey("gemini", keyBefore); owner.ApplyTheme(); owner.Render(); }
    }
}
