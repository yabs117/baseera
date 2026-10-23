using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Baseera.App.Services;

internal sealed record AiModelOption(string Id, string Name, string Author, string Tier)
{
    public override string ToString() => $"{Name} · {I18n.T(Tier)}";
}

internal static class AiCatalog
{
    internal static string Provider => AiProviders.Contains(Settings.Current.AiProvider) ? Settings.Current.AiProvider : "gemini";
    internal static string Model => Settings.GetModel(Provider);
    internal static bool ValidModel(string provider, string model) => AiProviders.Contains(provider) &&
        Regex.IsMatch(model, "^[A-Za-z0-9][A-Za-z0-9._:/@+-]{1,249}$");
    internal static bool Configured()
    {
        try { return !string.IsNullOrWhiteSpace(Settings.GetKey(Provider)) && ValidModel(Provider, Model); }
        catch { return false; }
    }
    internal static string Label(string provider) => AiProviders.Get(provider).Name;
    internal static HttpClient Client(TimeSpan timeout) => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = timeout };
    internal static void Authenticate(HttpRequestMessage request, AiProvider provider, string key)
    {
        if (provider.Protocol == "gemini") request.Headers.Add("x-goog-api-key", key.Trim());
        else if (provider.Protocol == "anthropic")
        { request.Headers.Add("x-api-key", key.Trim()); request.Headers.Add("anthropic-version", "2023-06-01"); }
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
    }
    internal static async Task<JsonDocument> ReadJson(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException(I18n.T("errorApiFailed"));
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream(); var bytes = new byte[8192]; int count;
        while ((count = await input.ReadAsync(bytes, ct)) > 0)
        { if (buffer.Length + count > limit) throw new InvalidDataException(I18n.T("errorApiFailed")); buffer.Write(bytes, 0, count); }
        return JsonDocument.Parse(buffer.ToArray());
    }
    internal static async Task<IReadOnlyList<AiModelOption>> ListVisionModels(string providerId, string key, CancellationToken ct,
        HttpClient? suppliedClient = null)
    {
        var provider = AiProviders.Get(providerId);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(I18n.T("aiNeedKey"));
        using var owned = suppliedClient is null ? Client(TimeSpan.FromSeconds(35)) : null;
        var http = suppliedClient ?? owned!;
        var models = new List<AiModelOption>(); string next = "";
        for (int page = 0; page < 10; page++)
        {
            string endpoint = provider.ModelsUrl ?? provider.BaseUrl + "models";
            if (providerId == "gemini") endpoint += "?pageSize=1000" + (next.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(next));
            else if (providerId == "anthropic") endpoint += "?limit=1000" + (next.Length == 0 ? "" : "&after_id=" + Uri.EscapeDataString(next));
            else if (providerId == "cohere" && next.Length > 0) endpoint += "&page_token=" + Uri.EscapeDataString(next);
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            Authenticate(request, provider, key);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{provider.Name} HTTP {(int)response.StatusCode}. {I18n.T("aiCatalogFailed")}");
            using var document = await ReadJson(response, 8_000_000, ct);
            var root = document.RootElement; JsonElement entries = root;
            if (root.ValueKind != JsonValueKind.Array)
            {
                string field = providerId is "gemini" or "xai" or "cohere" ? "models" : "data";
                if (!root.TryGetProperty(field, out entries) || entries.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException(I18n.T("aiCatalogFailed"));
            }
            foreach (var entry in entries.EnumerateArray())
            {
                string id = String(entry, providerId is "gemini" or "cohere" ? "name" : "id");
                if (id.StartsWith("models/", StringComparison.Ordinal)) id = id[7..];
                if (!ValidModel(providerId, id) || !SupportsVision(providerId, id, entry)) continue;
                string name = String(entry, "displayName", "display_name", "name");
                if (name.Length == 0 || name.StartsWith("models/")) name = id;
                models.Add(new(id, name, provider.Name, Tier(entry)));
            }
            string previous = next;
            next = providerId switch
            {
                "gemini" => String(root, "nextPageToken"),
                "anthropic" when root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True => String(root, "last_id"),
                "cohere" => String(root, "next_page_token"),
                _ => ""
            };
            if (next.Length == 0 || next == previous) break;
        }
        return models.DistinctBy(m => m.Id).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    internal static bool SupportsVision(string provider, string id, JsonElement entry)
    {
        string lower = id.ToLowerInvariant();
        if (entry.TryGetProperty("active", out var active) && active.ValueKind == JsonValueKind.False ||
            entry.TryGetProperty("archived", out var archived) && archived.ValueKind == JsonValueKind.True) return false;
        // Explicit metadata wins, including an explicit text-only declaration.
        foreach (string property in new[] { "input_modalities", "modalities" })
            if (entry.TryGetProperty(property, out var modalities) && modalities.ValueKind == JsonValueKind.Array)
                return Has(modalities, "image") && (!entry.TryGetProperty("output_modalities", out var output) || Has(output, "text"));
        if (entry.TryGetProperty("architecture", out var architecture) && architecture.TryGetProperty("input_modalities", out var inputs))
            return Has(inputs, "image") && (!architecture.TryGetProperty("output_modalities", out var output) || Has(output, "text"));
        if (entry.TryGetProperty("capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Object)
            foreach (string property in new[] { "vision", "image_input" })
                if (capabilities.TryGetProperty(property, out var vision))
                    return vision.ValueKind == JsonValueKind.True || vision.ValueKind == JsonValueKind.Object &&
                        vision.TryGetProperty("supported", out var supported) && supported.ValueKind == JsonValueKind.True;
        if (entry.TryGetProperty("supports_image_input", out var imageInput)) return imageInput.ValueKind == JsonValueKind.True;
        if (Regex.IsMatch(lower, "embedding|rerank|moderation|guard|tts|transcrib|audio|realtime|image-gen|image_generation|flux|diffusion|dall-e|ocr|sora")) return false;
        // When modalities are absent, accept documented vision families, never every chat model.
        if (provider == "gemini") return lower.StartsWith("gemini-") && !Regex.IsMatch(lower, "-image|-live|embedding") &&
            entry.TryGetProperty("supportedGenerationMethods", out var methods) && Has(methods, "generateContent");
        if (provider == "openai") return Regex.IsMatch(lower, "^(gpt-(4o|4\\.1|4-turbo|5([.-]|$)|6([.-]|$))|chatgpt-(4o|5)|o3($|-)|o4-mini)") && !lower.Contains("codex") && !lower.Contains("mini-deep-research");
        if (provider == "anthropic") return lower.StartsWith("claude-") && !lower.StartsWith("claude-2") && !lower.Contains("instant");
        if (provider == "cohere") return lower.StartsWith("command-a-vision") || lower.StartsWith("command-a-plus");
        if (provider == "mistral") return lower.StartsWith("pixtral-");
        if (provider == "qwen") return lower.StartsWith("qwen-vl-") || Regex.IsMatch(lower, "^qwen[23]([.-].*)?-vl|^qwen3\\.[5678]");
        if (provider == "moonshot") return Regex.IsMatch(lower, "^kimi-k(2\\.[5-9]|3)") || lower.EndsWith("-vision-preview");
        if (provider == "stepfun") return Regex.IsMatch(lower, "^step-(1v|1\\.5v|3($|-)|3\\.7|5)");
        if (provider == "zai") return Regex.IsMatch(lower, "^glm-(4(v|\\.[156]v)|5\\.3-flash)");
        if (provider == "xai") return false;
        if (lower.Contains("gemma-3-1b")) return false;
        return Regex.IsMatch(lower, "(^|[/_-])(qwen[23]([.-].*)?-vl|qwen3\\.(5|6|7|8)|llama-?4|llama-?3[.-]2.*vision|pixtral|internvl|llava|gemma-?(3|4)(?!n)|kimi-k(2\\.[5-9]|3)|glm-4\\.[156]v|glm-5\\.3-flash|.*nemotron.*-vl|.*muse-spark)");
    }
    private static string Tier(JsonElement entry)
    {
        if (!entry.TryGetProperty("pricing", out var prices) || prices.ValueKind != JsonValueKind.Object) return "aiTierVaries";
        var fields = prices.EnumerateObject().Where(p => p.Name is "prompt" or "completion" or "input" or "output" or "image" or "request").ToArray();
        if (fields.Length == 0) return "aiTierVaries";
        var values = new List<decimal>();
        foreach (var field in fields)
        {
            var price = field.Value;
            if (price.ValueKind == JsonValueKind.Object && !price.TryGetProperty("price_per_m_decimal", out price)) return "aiTierVaries";
            if (!decimal.TryParse(price.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return "aiTierVaries";
            values.Add(value);
        }
        return values.All(value => value == 0) ? "aiFree" : "aiPaid";
    }
    private static bool Has(JsonElement values, string value) => values.ValueKind == JsonValueKind.Array &&
        values.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), value, StringComparison.OrdinalIgnoreCase));
    private static string String(JsonElement value, params string[] names)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (string name in names)
                if (value.TryGetProperty(name, out var text) && text.ValueKind == JsonValueKind.String) return text.GetString() ?? "";
        return "";
    }
}
