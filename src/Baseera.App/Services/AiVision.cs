using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Baseera.App.Services;

internal static class AiVision
{
    internal static async Task<string> CompleteOpenRouter(string prompt, byte[]? image, string mimeType, string key, string model,
        CancellationToken ct, HttpClient? suppliedClient = null)
    {
        if (string.IsNullOrWhiteSpace(key) || !AiCatalog.ValidModel("openrouter", model))
            throw new InvalidOperationException(I18n.T("needsKey"));
        using var owned = suppliedClient is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(90) } : null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        request.Headers.Add("HTTP-Referer", "https://github.com/yabs117/baseera");
        request.Headers.Add("X-Title", "Baseera");
        object content = image is null ? prompt : new object[]
        {
            new { type = "text", text = prompt },
            new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{Convert.ToBase64String(image)}" } }
        };
        request.Content = new StringContent(JsonSerializer.Serialize(new { model, messages = new[] { new { role = "user", content } }, max_tokens = 2200 }), Encoding.UTF8, "application/json");
        using var response = await (suppliedClient ?? owned!).SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"OpenRouter HTTP {(int)response.StatusCode}. {I18n.T("errorApiFailed")}");
        if (response.Content.Headers.ContentLength is > 2_000_000) throw new InvalidDataException(I18n.T("errorApiFailed"));
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        { if (buffer.Length + read > 2_000_000) throw new InvalidDataException(I18n.T("errorApiFailed")); buffer.Write(chunk, 0, read); }
        using var document = JsonDocument.Parse(buffer.ToArray());
        var message = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content");
        string text = message.ValueKind == JsonValueKind.String ? message.GetString() ?? "" :
            string.Concat(message.EnumerateArray().Where(part => part.TryGetProperty("text", out _)).Select(part => part.GetProperty("text").GetString()));
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLine = text.IndexOf('\n'); int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine > 0 && lastFence > firstLine) text = text[(firstLine + 1)..lastFence].Trim();
        }
        if (text.Length == 0) throw new InvalidDataException(I18n.T("errorApiFailed"));
        return text;
    }
}
