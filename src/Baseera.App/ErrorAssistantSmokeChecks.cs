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
            string advice = JsonSerializer.Serialize(new { locale = ErrorLanguageSmokeChecks.LocaleObject("en"), summary = "Access denied", cause = "The requested operation may lack permission.",
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

        await AiProviderSmokeChecks.Run(owner, output);
        await ChatGptSmokeChecks.Run(owner, output);
        await ConnectionRegressionSmokeChecks.CheckReopen(owner, attachment, output);
        await ErrorLanguageSmokeChecks.Run(owner, output);
        await AdvicePresentationSmokeChecks.Run(owner, output);

        string originalLanguage = Settings.Current.Language;
        try
        {
            Settings.Current.Language = "en"; owner.ApplyTheme(); owner.Render();
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
                var connection = Descendants(setup).OfType<AiConnectionPanel>().Single();
                foreach (var provider in AiProviders.All.Concat(AiProviders.All.Reverse()))
                {
                    connection.Service.SelectedItem = provider; setup.UpdateLayout();
                    if (!connection.IsVisible || !ReferenceEquals(connection, Descendants(setup).OfType<AiConnectionPanel>().Single()))
                        throw new Exception("Service switch replaced or detached the connection panel.");
                }
                connection.Service.SelectedItem = AiProviders.Get("openai");
                setup.UpdateLayout();
                await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(150);
                WindowAppearance.SavePreview(setup, Path.Combine(output, "ai-services-dialog-en.png"));
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
                Descendants(dialog).OfType<AdvicePresenter>().Single().Finish();
                if (!Descendants(dialog).OfType<AdviceTextView>().Any(t => t.VisibleText == "Access denied") ||
                    !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == I18n.T("chatContextPrivacy")))
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
