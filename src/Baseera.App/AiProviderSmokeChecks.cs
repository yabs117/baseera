using Baseera.App.Services;
using System.Net;
using System.Text.Json;

namespace Baseera.App;

internal static class AiProviderSmokeChecks
{
    private sealed class Reply(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply(request, ct); }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static async Task Run(MainWindow owner, string output)
    {
        Require(AiProviders.All.Count == 20 && AiProviders.All.DistinctBy(p => p.Id).Count() == 20 && !AiProviders.Contains("openrouter"), "Expected 20 direct services without OpenRouter.");
        int catalogs = 0, completions = 0;
        foreach (var provider in AiProviders.All)
        {
            string key = "fixture-key-" + provider.Id;
            using var http = new HttpClient(new Reply(async (request, ct) =>
            {
                string expectedKey = provider.Protocol switch
                { "gemini" => request.Headers.GetValues("x-goog-api-key").Single(), "anthropic" => request.Headers.GetValues("x-api-key").Single(), _ => request.Headers.Authorization?.Parameter ?? "" };
                Require(expectedKey == key, "Key crossed service boundary: " + provider.Id);
                Require(request.RequestUri!.Host == new Uri(provider.BaseUrl).Host, "Wrong official host: " + provider.Id);
                if (provider.Protocol == "anthropic") Require(request.Headers.GetValues("anthropic-version").Single() == "2023-06-01", "Anthropic API version missing.");
                if (request.Method == HttpMethod.Get)
                {
                    catalogs++;
                    string expectedUrl = provider.ModelsUrl ?? provider.BaseUrl + "models";
                    Require(request.RequestUri.GetLeftPart(UriPartial.Path) == new Uri(expectedUrl).GetLeftPart(UriPartial.Path), "Wrong catalog route: " + provider.Id);
                    object[] models = [
                        new { id = "vision-model", name = "vision-model", input_modalities = new[] { "text", "image" }, output_modalities = new[] { "text" }, pricing = new { prompt = "0", completion = "0" } },
                        new { id = "paid-vision", name = "paid-vision", capabilities = new { vision = true }, pricing = new { prompt = "0.1", completion = "0.2" } },
                        new { id = "gemini-text-only", name = "gemini-text-only", input_modalities = new[] { "text" }, output_modalities = new[] { "text" } },
                        new { id = "image-generator", name = "image-generator", input_modalities = new[] { "text", "image" }, output_modalities = new[] { "image" } }
                    ];
                    return provider.Id is "gemini" or "cohere" or "xai" ? Json(new { models }) : provider.Id == "together" ? Json(models) : Json(new { data = models });
                }
                completions++;
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Require(!body.RootElement.GetRawText().Contains(key), "Key leaked into body.");
                string expectedPath = provider.Protocol switch { "responses" => "responses", "anthropic" => "messages", "cohere" => "chat", _ => "chat/completions" };
                Require(request.RequestUri.AbsoluteUri == provider.BaseUrl + expectedPath, "Wrong generation route: " + provider.Id);
                var root = body.RootElement;
                Require(root.GetProperty("model").GetString() == "vision-model", "Selected model changed.");
                string advice = ErrorLanguageSmokeChecks.Fixture("en");
                string instructions = provider.Protocol switch
                {
                    "responses" => root.GetProperty("instructions").GetString()!,
                    "anthropic" => root.GetProperty("system").GetString()!,
                    _ => root.GetProperty("messages")[0].GetProperty("content").GetString()!
                };
                Require(instructions == ErrorAssistantPrompt.Instructions, "Baseera instructions missing: " + provider.Id);
                if (provider.Protocol == "responses")
                {
                    var parts = root.GetProperty("input")[0].GetProperty("content");
                    Require(parts[1].GetProperty("type").GetString() == "input_image" && parts[1].GetProperty("image_url").GetString()!.StartsWith("data:image/jpeg;base64,"), "Responses image missing.");
                    return Json(new { output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = advice } } } } });
                }
                var content = root.GetProperty("messages")[provider.Protocol == "anthropic" ? 0 : 1].GetProperty("content");
                if (provider.Protocol == "anthropic")
                {
                    Require(content[1].GetProperty("source").GetProperty("media_type").GetString() == "image/jpeg", "Claude image missing.");
                    return Json(new { content = new[] { new { type = "text", text = advice } } });
                }
                Require(content[1].GetProperty("image_url").GetProperty("url").GetString()!.StartsWith("data:image/jpeg;base64,"), "Chat image missing: " + provider.Id);
                return provider.Protocol == "cohere" ? Json(new { message = new { content = new[] { new { type = "text", text = advice } } } }) :
                    Json(new { choices = new[] { new { message = new { content = advice } } } });
            }));
            var options = await AiCatalog.ListVisionModels(provider.Id, key, CancellationToken.None, http);
            Require(options.Count == 2 && options.Single(m => m.Id == "vision-model").Tier == "aiFree" && options.Single(m => m.Id == "paid-vision").Tier == "aiPaid", "Vision filtering/pricing failed: " + provider.Id);
            if (provider.Id != "gemini")
            {
                var advice = await ErrorAssistant.Analyze("Error", new ErrorImage([1, 2, 3], "fixture"), "English", CancellationToken.None, http, key, "vision-model", provider.Id);
                Require(advice.Steps.Count == 1, "Advice parsing failed: " + provider.Id);
            }
        }
        // Metadata must override family names. Missing metadata uses only documented vision families.
        using var unknown = JsonDocument.Parse("{}");
        using var textOnly = JsonDocument.Parse("{\"capabilities\":{\"vision\":false}}");
        Require(!AiCatalog.SupportsVision("mistral", "pixtral-latest", textOnly.RootElement), "Explicit text-only metadata ignored.");
        Require(!AiCatalog.SupportsVision("groq", "llama-3.3-70b-versatile", unknown.RootElement), "Text-only Llama was accepted.");
        Require(AiCatalog.SupportsVision("sambanova", "gemma-4-31B-it", unknown.RootElement), "Documented SambaNova vision family missing.");
        Require(!AiCatalog.SupportsVision("openai", "gpt-image-1", unknown.RootElement), "Image generator accepted as image understanding.");

        // All storage is in the isolated smoke directory. Check no provider can read another provider's secret.
        foreach (var provider in AiProviders.All) Settings.SetKey(provider.Id, "fixture-" + provider.Id);
        foreach (var provider in AiProviders.All)
        { Require(Settings.GetKey(provider.Id) == "fixture-" + provider.Id, "DPAPI secret collision: " + provider.Id); Settings.SetKey(provider.Id, ""); }

        var draft = new AiConnectionDraft();
        var pending = new TaskCompletionSource<IReadOnlyList<AiModelOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var panel = new AiConnectionPanel(draft, () => { }, list: (_, _, _) => pending.Task);
        var host = new Window { Owner = owner, Content = panel, Width = 740, Height = 700, ShowInTaskbar = false };
        host.Show();
        try
        {
            foreach (var provider in AiProviders.All.Concat(AiProviders.All.Reverse()))
            {
                panel.Service.SelectedItem = provider; panel.KeyInput.Password = "draft-" + provider.Id;
                host.UpdateLayout(); Require(panel.Service.Items.Count == 20 && panel.KeyInput.IsVisible, "Provider switch hid the key input.");
            }
            panel.Service.SelectedItem = AiProviders.Get("gemini");
            Require(panel.KeyInput.Password == "draft-gemini", "Switching services lost the key draft.");
            var request = panel.LoadModels();
            panel.Service.SelectedItem = AiProviders.Get("openai");
            pending.SetResult([new("stale-model", "Stale", "Google", "aiFree")]); await request;
            Require(panel.ModelInput.Items.Count == 0, "Old provider response appeared under another service.");
            Require(panel.KeyInput.Password == "draft-openai", "Key from another service displayed.");
        }
        finally { host.Close(); }

        var failedPanel = new AiConnectionPanel(new(), () => { }, list: (_, _, _) => throw new HttpRequestException("Simulated connection failure"));
        await failedPanel.LoadModels(); // Must render an error rather than escape the UI event handler.

        string originalProvider = Settings.Current.AiProvider, originalGemini = Settings.Current.GeminiModel;
        var originalModels = new Dictionary<string, string>(Settings.Current.AiModels);
        int saves = 0;
        var successDraft = new AiConnectionDraft();
        var successful = new AiConnectionPanel(successDraft, () => saves++, list: (provider, key, _) =>
        {
            Require(key == "save-fixture-" + provider, "Wrong key used by model-loading UI.");
            return Task.FromResult<IReadOnlyList<AiModelOption>>([new("vision-model", "Vision model", provider, "aiTierVaries")]);
        });
        try
        {
            foreach (var provider in AiProviders.All)
            {
                successful.Service.SelectedItem = provider; successful.KeyInput.Password = "save-fixture-" + provider.Id;
                await successful.LoadModels(); successful.ModelInput.SelectedIndex = 0;
                successful.Children.OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "save")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(Settings.Current.AiProvider == provider.Id && Settings.GetModel(provider.Id) == "vision-model" &&
                    Settings.GetKey(provider.Id) == "save-fixture-" + provider.Id, "Service/model/key saving failed: " + provider.Id);
            }
            Require(saves == 20, "Successful-save callback missing.");
        }
        finally
        {
            foreach (var provider in AiProviders.All) Settings.SetKey(provider.Id, "");
            Settings.Current.AiProvider = originalProvider; Settings.Current.GeminiModel = originalGemini; Settings.Current.AiModels = originalModels; Settings.Save();
        }
        File.WriteAllText(Path.Combine(output, "ai-providers-result.txt"), $"PASS: {catalogs} direct catalogs, {completions} direct image requests via fake HTTP; 20 isolated DPAPI secrets; 40 provider switches; obsolete response discarded; connection failure handled. No live keys or paid requests.");
    }
}

public sealed partial class MainWindow
{
    internal void CheckAiServices()
    {
        Navigate("settings"); UpdateLayout();
        static IEnumerable<DependencyObject> Nodes(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var node in Nodes(child)) yield return node; }
        }
        var panel = Nodes(frame).OfType<AiConnectionPanel>().Single();
        foreach (var provider in AiProviders.All.Concat(AiProviders.All.Reverse()))
        {
            panel.Service.SelectedItem = provider; UpdateLayout();
            if (!ReferenceEquals(panel, Nodes(frame).OfType<AiConnectionPanel>().Single()) || !IsVisible)
                throw new InvalidOperationException("Settings provider switch rebuilt or closed the app.");
        }
        aiConnectionDraft.Services.Clear(); aiConnectionDraft.Provider = AiCatalog.Provider;
        Navigate("home");
    }
}
