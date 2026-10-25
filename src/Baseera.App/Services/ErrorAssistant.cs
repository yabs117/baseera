using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Baseera.Core;

namespace Baseera.App.Services;

internal sealed record ErrorAdvice(string Summary, string Cause, IReadOnlyList<string> Steps, IReadOnlyList<string> Checks, string Confidence, AdviceLocale? Locale = null, IReadOnlyList<string>? RepairActions = null, string? Message = null);
internal sealed record ErrorImage(byte[] Bytes, string Label);

internal static class ErrorAssistant
{
    internal const string DefaultModel = "gemini-3.8-flash";
    internal static bool ValidModel(string model) => Regex.IsMatch(model.Trim(), "^[A-Za-z0-9._-]{3,100}$");

    internal static ErrorImage PrepareFile(string path)
    {
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(I18n.T("errorImageType"));
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is < 1 or > 15_000_000) throw new InvalidDataException(I18n.T("errorImageSize"));
        return Prepare(File.ReadAllBytes(path), file.Name);
    }

    internal static ErrorImage Prepare(byte[] bytes, string label)
    {
        if (bytes.Length is < 1 or > 15_000_000) throw new InvalidDataException(I18n.T("errorImageSize"));
        using var source = new MemoryStream(bytes, false);
        using var image = System.Drawing.Image.FromStream(source, false, true);
        if (image.Width < 1 || image.Height < 1 || (long)image.Width * image.Height > 40_000_000)
            throw new InvalidDataException(I18n.T("errorImageSize"));
        // Normalize all inputs to a bounded JPEG before sending; EXIF and file metadata are not copied.
        double scale = Math.Min(1, Math.Min(2000d / image.Width, 2000d / image.Height));
        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        using var output = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(output))
        {
            graphics.Clear(System.Drawing.Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, width, height);
        }
        using var stream = new MemoryStream();
        var encoder = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");
        using (var parameters = new EncoderParameters(1))
        {
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
            output.Save(stream, encoder, parameters);
        }
        if (stream.Length > 8_000_000) throw new InvalidDataException(I18n.T("errorImageSize"));
        return new(stream.ToArray(), label);
    }

    internal static async Task<ErrorAdvice> Analyze(string description, ErrorImage? image, string language, CancellationToken ct,
        HttpClient? suppliedClient = null, string? suppliedKey = null, string? suppliedModel = null, string? suppliedProvider = null, string approvedDiagnostics = "",
        IReadOnlyList<ConversationExchange>? conversation = null, ErrorVideo? video = null, string attachmentContext = "", string localRepairs = "")
    {
        description = description.Trim();
        if (description.Length > 12_000) throw new InvalidDataException(I18n.T("errorTextSize"));
        if (description.Length == 0 && image is null && video is null && string.IsNullOrWhiteSpace(approvedDiagnostics)) throw new InvalidOperationException(I18n.T("errorNeedInput"));
        string provider = suppliedProvider ?? AiCatalog.Provider;
        bool chatGpt = suppliedProvider is null && suppliedKey is null && ChatGptAuth.Selected;
        string key = suppliedKey ?? (chatGpt ? "" : Settings.GetKey(provider));
        string model = (suppliedModel ?? (chatGpt ? AiCatalog.Model : Settings.GetModel(provider))).Trim();
        if ((!chatGpt && string.IsNullOrWhiteSpace(key)) || !AiCatalog.ValidModel(provider, model)) throw new InvalidOperationException(I18n.T("needsKey"));
        if (image is { Bytes.Length: > 8_000_000 }) throw new InvalidDataException(I18n.T("errorImageSize"));
        if (approvedDiagnostics.Length > DiagnosticPrivacy.MaxCharacters) throw new InvalidDataException(I18n.T("errorTextSize"));

        if (video is not null && (provider != "gemini" || video.Bytes.Length is < 1 or > 12000000 || video.Seconds is <= 0 or > 60 ||
            video.Mime is not ("video/mp4" or "video/webm" or "video/wmv" or "video/avi" or "video/mov"))) throw new InvalidDataException(I18n.T("chatVideoUnsupported"));
        string prompt = ErrorAssistantPrompt.Input(description, image is not null, language, approvedDiagnostics, conversation, attachmentContext, localRepairs);
        if (provider != "gemini")
        {
            string answer = await AiVision.Complete(provider, prompt, image?.Bytes, "image/jpeg", key, model, ct, suppliedClient, useChatGpt: chatGpt,
                instructions: ErrorAssistantPrompt.Instructions);
            return ParseAdvice(answer);
        }
        var parts = new List<object> { new { text = prompt } };
        if (image is not null) parts.Add(new { inline_data = new { mime_type = "image/jpeg", data = Convert.ToBase64String(image.Bytes) } });
        if (video is not null) parts.Add(new { inline_data = new { mime_type = video.Mime, data = Convert.ToBase64String(video.Bytes) } });
        bool ownClient = suppliedClient is null;
        using var client = ownClient ? AiCatalog.Client(TimeSpan.FromSeconds(90)) : null;
        var http = suppliedClient ?? client!;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts } },
            systemInstruction = new { parts = new[] { new { text = ErrorAssistantPrompt.Instructions } } },
            generationConfig = new { responseMimeType = "application/json", responseSchema = ErrorAssistantPrompt.Schema(), temperature = 0.2 }
        }), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Gemini HTTP {(int)response.StatusCode}. " + I18n.T("errorApiFailed"));
        if (response.Content.Headers.ContentLength is > 2_000_000) throw new InvalidDataException(I18n.T("errorApiFailed"));
        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int count;
        while ((count = await responseStream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + count > 2_000_000) throw new InvalidDataException(I18n.T("errorApiFailed"));
            buffer.Write(chunk, 0, count);
        }
        string raw = Encoding.UTF8.GetString(buffer.ToArray());
        try
        {
            using var envelope = JsonDocument.Parse(raw);
            string text = string.Concat(envelope.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")
                .EnumerateArray().Where(p => p.TryGetProperty("text", out _)).Select(p => p.GetProperty("text").GetString()));
            return ParseAdvice(text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or InvalidDataException or KeyNotFoundException or IndexOutOfRangeException)
        { throw new InvalidDataException(I18n.T("errorApiFailed"), ex); }
    }

    internal static ErrorAdvice ParseAdvice(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        static string Value(JsonElement root, string key, int max)
        {
            string value = root.GetProperty(key).GetString()?.Trim() ?? "";
            if (value.Length is 0 || value.Length > max) throw new InvalidDataException("Invalid response field: " + key);
            return value;
        }
        static IReadOnlyList<string> Lines(JsonElement root, string key, int max)
        {
            var values = root.GetProperty(key).EnumerateArray().Take(max + 1).Select(x => x.GetString()?.Trim() ?? "").ToArray();
            if (values.Length > max || values.Any(x => x.Length is 0 or > 1000)) throw new InvalidDataException("Invalid response field: " + key);
            return values;
        }
        var locale = root.GetProperty("locale"); string tag = Value(locale, "language", 64), direction = Value(locale, "direction", 3);
        if (!Regex.IsMatch(tag, "^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*$") || direction is not ("ltr" or "rtl"))
            throw new InvalidDataException("Invalid response language.");
        var localization = new AdviceLocale(tag, direction == "rtl", AdviceLocale.GreetingFor(tag, Value(locale, "greeting", 300)), Value(locale, "cause_title", 100),
            Value(locale, "steps_title", 100), Value(locale, "checks_title", 100), Value(locale, "confidence_title", 100));
        IReadOnlyList<string> repairs = root.TryGetProperty("repair_actions", out _) ? RepairCatalog.ValidateRecommendations(Lines(root, "repair_actions", 3)) : Array.Empty<string>();
        string mode = root.TryGetProperty("response_mode", out var style) ? style.GetString() ?? "diagnosis" : "diagnosis";
        if (mode is not ("diagnosis" or "conversation")) throw new InvalidDataException("Invalid response mode.");
        string? message = mode == "conversation" ? Value(root, "message", 6000) : null;
        if (message is not null && repairs.Count > 0) throw new InvalidDataException("Repair actions require a structured diagnosis.");
        var result = new ErrorAdvice(Value(root, "summary", 400), Value(root, "cause", 1200), Lines(root, "steps", 8),
            Lines(root, "checks", 5), Value(root, "confidence", 150), localization, repairs, message);
        if (result.Steps.Count == 0 && message is null) throw new InvalidDataException("No troubleshooting steps returned.");
        return result;
    }
}
