using System.Text;
using System.Text.Json;

namespace Baseera.App.Services;

internal static class AiVision
{
    internal static async Task<string> Complete(string providerId, string prompt, byte[]? image, string mimeType, string key, string model,
        CancellationToken ct, HttpClient? suppliedClient = null, bool useChatGpt = false)
    {
        if (useChatGpt)
        {
            if (providerId != "openai") throw new InvalidOperationException(I18n.T("chatGptInvalidResponse"));
            return CleanText(await ChatGptAuth.Complete(Settings.Current.ChatGptAccount, model, prompt, image, mimeType, ct, suppliedClient));
        }
        var provider = AiProviders.Get(providerId);
        if (string.IsNullOrWhiteSpace(key) || !AiCatalog.ValidModel(providerId, model)) throw new InvalidOperationException(I18n.T("needsKey"));
        using var owned = suppliedClient is null ? AiCatalog.Client(TimeSpan.FromSeconds(120)) : null;
        string data = image is null ? "" : Convert.ToBase64String(image);
        string endpoint;
        object payload;
        switch (provider.Protocol)
        {
            case "responses":
                endpoint = "responses";
                var input = new List<object> { new { type = "input_text", text = prompt } };
                if (image is not null) input.Add(new { type = "input_image", image_url = $"data:{mimeType};base64,{data}" });
                payload = new { model, input = new[] { new { role = "user", content = input } }, max_output_tokens = 8192, store = false };
                break;
            case "anthropic":
                endpoint = "messages";
                var blocks = new List<object> { new { type = "text", text = prompt } };
                if (image is not null) blocks.Add(new { type = "image", source = new { type = "base64", media_type = mimeType, data } });
                payload = new { model, max_tokens = 4096, messages = new[] { new { role = "user", content = blocks } } };
                break;
            default:
                endpoint = provider.Protocol == "cohere" ? "chat" : "chat/completions";
                var content = new List<object> { new { type = "text", text = prompt } };
                if (image is not null) content.Add(new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{data}" } });
                payload = new { model, messages = new[] { new { role = "user", content } }, max_tokens = 8192, stream = false };
                break;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, provider.BaseUrl + endpoint);
        AiCatalog.Authenticate(request, provider, key);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await (suppliedClient ?? owned!).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{provider.Name} HTTP {(int)response.StatusCode}. {I18n.T("errorApiFailed")}");
        using var document = await AiCatalog.ReadJson(response, 2_000_000, ct);
        var root = document.RootElement;
        string answer = provider.Protocol switch
        {
            "responses" => string.Concat(root.GetProperty("output").EnumerateArray()
                .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "message")
                .SelectMany(item => item.GetProperty("content").EnumerateArray())
                .Where(part => part.TryGetProperty("type", out var type) && type.GetString() == "output_text")
                .Select(part => part.GetProperty("text").GetString())),
            "anthropic" => Text(root.GetProperty("content")),
            "cohere" => Text(root.GetProperty("message").GetProperty("content")),
            _ => Text(root.GetProperty("choices")[0].GetProperty("message").GetProperty("content"))
        };
        return CleanText(answer);
    }
    private static string CleanText(string answer)
    {
        answer = answer.Trim();
        if (answer.StartsWith("```", StringComparison.Ordinal))
        {
            int firstLine = answer.IndexOf('\n'), lastFence = answer.LastIndexOf("```", StringComparison.Ordinal);
            if (firstLine > 0 && lastFence > firstLine) answer = answer[(firstLine + 1)..lastFence].Trim();
        }
        if (answer.Length == 0) throw new InvalidDataException(I18n.T("errorApiFailed"));
        return answer;
    }
    private static string Text(JsonElement content) => content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" :
        string.Concat(content.EnumerateArray().Where(part => part.TryGetProperty("text", out _)).Select(part => part.GetProperty("text").GetString()));
}
