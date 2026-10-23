using System.Net;
using System.Text.Json;
using Baseera.App.Services;

namespace Baseera.App;

internal static class ErrorAssistantSmokeChecks
{
    private sealed class Reply(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => answer(request, ct);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    internal static async Task Run(MainWindow owner, string output)
    {
        using var sample = new System.Drawing.Bitmap(480, 180);
        using (var graphics = System.Drawing.Graphics.FromImage(sample))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.DrawString("Error 0x80070005", new System.Drawing.Font("Arial", 20), System.Drawing.Brushes.Black, 12, 60);
        }
        using var png = new MemoryStream(); sample.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var attachment = ErrorAssistant.Prepare(png.ToArray(), "sample-error.png");
        if (attachment.Bytes.Length < 100 || attachment.Bytes.Length > 8_000_000) throw new Exception("Error image normalization failed.");
        int calls = 0;
        using var client = new HttpClient(new Reply(async (request, ct) =>
        {
            calls++;
            if (request.Method != HttpMethod.Post || request.RequestUri?.Host != "generativelanguage.googleapis.com" ||
                !request.RequestUri.AbsolutePath.EndsWith("/models/gemini-3.8-flash:generateContent", StringComparison.Ordinal) ||
                request.Headers.GetValues("x-goog-api-key").Single() != "sample-key")
                throw new Exception("Gemini request target or key header incorrect.");
            string body = await request.Content!.ReadAsStringAsync(ct);
            if (body.Contains("sample-key", StringComparison.Ordinal)) throw new Exception("API key leaked into request body.");
            using var document = JsonDocument.Parse(body);
            var parts = document.RootElement.GetProperty("contents")[0].GetProperty("parts");
            if (!parts[0].GetProperty("text").GetString()!.Contains("0x80070005") ||
                parts[1].GetProperty("inline_data").GetProperty("mime_type").GetString() != "image/jpeg" ||
                document.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString() != "application/json")
                throw new Exception("Error text, image or structured response was not requested.");
            string advice = JsonSerializer.Serialize(new { summary = "Access denied", cause = "The requested operation may lack permission.",
                steps = new[] { "Check the file permissions.", "Try the official app repair flow." },
                checks = new[] { "Record the app name and operation." }, confidence = "Moderate" });
            string envelope = JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = advice } } } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope) };
        }));
        var response = await ErrorAssistant.Analyze("Error 0x80070005", attachment, "English", CancellationToken.None, client, "sample-key", "gemini-3.8-flash");
        if (calls != 1 || response.Steps.Count != 2 || response.Summary != "Access denied") throw new Exception("Gemini response parsing failed.");
        bool rejectedEmpty = false;
        try { await ErrorAssistant.Analyze("", null, "English", CancellationToken.None, client, "sample-key", "gemini-3.8-flash"); }
        catch (InvalidOperationException) { rejectedEmpty = true; }
        if (!rejectedEmpty || calls != 1 || ErrorAssistant.ValidModel("bad/model")) throw new Exception("Invalid input was sent to Gemini.");

        using var catalog = new HttpClient(new Reply((request, _) =>
        {
            if (request.RequestUri!.Host == "generativelanguage.googleapis.com")
            {
                if (request.Headers.GetValues("x-goog-api-key").Single() != "catalog-key") throw new Exception("Gemini catalog key missing.");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { models = new object[]
                {
                    new { name = "models/gemini-3.8-flash", displayName = "Gemini Flash", supportedGenerationMethods = new[] { "generateContent" } },
                    new { name = "models/gemini-tts", displayName = "Speech", supportedGenerationMethods = new[] { "generateContent" } },
                    new { name = "models/text-embedding", displayName = "Embedding", supportedGenerationMethods = new[] { "embedContent" } }
                } })) });
            }
            if (request.RequestUri.AbsolutePath != "/api/v1/models/user" || request.Headers.Authorization?.Parameter != "catalog-key")
                throw new Exception("Account-scoped OpenRouter catalog was not used.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { data = new object[]
            {
                new { id = "openai/vision-free:free", name = "Vision Free", architecture = new { input_modalities = new[] { "text", "image" }, output_modalities = new[] { "text" } }, pricing = new { prompt = "0", completion = "0" } },
                new { id = "anthropic/vision-paid", name = "Vision Paid", architecture = new { input_modalities = new[] { "text", "image" }, output_modalities = new[] { "text" } }, pricing = new { prompt = "0.000001", completion = "0.000002" } },
                new { id = "other/text-only", name = "Text only", architecture = new { input_modalities = new[] { "text" }, output_modalities = new[] { "text" } }, pricing = new { prompt = "0", completion = "0" } }
            } })) });
        }));
        var geminiModels = await AiCatalog.ListVisionModels("gemini", "catalog-key", CancellationToken.None, catalog);
        var routedModels = await AiCatalog.ListVisionModels("openrouter", "catalog-key", CancellationToken.None, catalog);
        if (geminiModels.Count != 1 || routedModels.Count != 2 || routedModels.Single(m => m.Author == "openai").Tier != "aiFree" ||
            routedModels.Single(m => m.Author == "anthropic").Tier != "aiPaid")
            throw new Exception("Vision model filtering or free/paid labels are incorrect.");
        using var routedClient = new HttpClient(new Reply(async (request, ct) =>
        {
            if (request.RequestUri?.AbsolutePath != "/api/v1/chat/completions" || request.Headers.Authorization?.Parameter != "sample-key")
                throw new Exception("OpenRouter completion target or key incorrect.");
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var content = payload.RootElement.GetProperty("messages")[0].GetProperty("content");
            if (content[1].GetProperty("type").GetString() != "image_url" || !content[1].GetProperty("image_url").GetProperty("url").GetString()!.StartsWith("data:image/jpeg;base64,"))
                throw new Exception("OpenRouter vision request omitted image input.");
            string adviceJson = JsonSerializer.Serialize(new { summary = "Access denied", cause = "Permission is missing.", steps = new[] { "Check permissions." }, checks = Array.Empty<string>(), confidence = "Moderate" });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = adviceJson } } } })) };
        }));
        var routedAdvice = await ErrorAssistant.Analyze("Access denied", attachment, "English", CancellationToken.None,
            routedClient, "sample-key", "openai/vision-free:free", "openrouter");
        if (routedAdvice.Steps.Count != 1) throw new Exception("OpenRouter advice response was not parsed.");

        string originalLanguage = Settings.Current.Language;
        try
        {
            bool hasKey;
            try { hasKey = !string.IsNullOrWhiteSpace(Settings.GetKey()); } catch { hasKey = false; }
            if (!hasKey)
            {
                var setup = new ErrorAssistantWindow(owner);
                setup.Show(); setup.UpdateLayout();
                if (!Descendants(setup).OfType<PasswordBox>().Any(box => box.IsVisible &&
                    System.Windows.Automation.AutomationProperties.GetAutomationId(box) == "ErrorAssistantApiKey") ||
                    !Descendants(setup).OfType<ComboBox>().Any(box => box.IsVisible &&
                    System.Windows.Automation.AutomationProperties.GetAutomationId(box) == "ErrorAssistantModel"))
                    throw new Exception("First-run Gemini setup is unavailable.");
                WindowAppearance.SavePreview(setup, Path.Combine(output, "error-assistant-setup.png"));
                setup.Close();
            }
            foreach (string language in new[] { "ar", "en", "fr", "es" })
            {
                Settings.Current.Language = language; owner.ApplyTheme(); owner.Render();
                var dialog = new ErrorAssistantWindow(owner, (_, _, _, _) => Task.FromResult(response));
                dialog.Show(); dialog.UpdateLayout();
                if (!WindowAppearance.HasNativeCaption(dialog)) throw new Exception("Native dialog frame missing.");
                var input = Descendants(dialog).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "ErrorDescription");
                input.Text = "Error 0x80070005";
                if (language == "en")
                {
                    var setter = typeof(ErrorAssistantWindow).GetMethod("SetImage", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                    setter.Invoke(dialog, [attachment]); dialog.UpdateLayout();
                    if (!Descendants(dialog).OfType<System.Windows.Controls.Image>().Any(view => view.IsVisible && view.Source is not null))
                        throw new Exception("A pasted, chosen or captured image is not visible after attachment.");
                }
                var analyze = Descendants(dialog).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "errorAnalyze");
                analyze.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (!Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "Access denied") ||
                    !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == I18n.T("errorPrivacy")))
                    throw new Exception("Error guidance or privacy notice missing in " + language);
                if (language is "ar" or "en")
                {
                    dialog.Activate(); dialog.UpdateLayout(); await Task.Delay(150);
                    WindowAppearance.SavePreview(dialog, Path.Combine(output, "error-assistant-" + language + ".png"));
                }
                dialog.Close();
            }
        }
        finally { Settings.Current.Language = originalLanguage; owner.ApplyTheme(); owner.Render(); }
        File.WriteAllText(Path.Combine(output, "error-assistant-result.txt"),
            "PASS: bounded screenshot preparation; text/image request and structured advice via fake HTTP; empty input blocked; native dialog, privacy notice and result in all four languages; no live API, credential changes or system changes.");
    }
}
