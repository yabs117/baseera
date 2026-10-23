using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Baseera.App.Services;

internal sealed record AiModelOption(string Id, string Name, string Author, string Tier)
{
    public override string ToString() => $"{Name} · {Author} · {I18n.T(Tier)}";
}

internal static class AiCatalog
{
    internal static string Provider => Settings.Current.AiProvider == "openrouter" ? "openrouter" : "gemini";
    internal static string Model => Provider == "openrouter" ? Settings.Current.OpenRouterModel : Settings.Current.GeminiModel;
    internal static bool ValidModel(string provider, string model) => provider == "openrouter"
        ? System.Text.RegularExpressions.Regex.IsMatch(model, "^[A-Za-z0-9._:/-]{3,150}$") && model.Contains('/')
        : ErrorAssistant.ValidModel(model);
    internal static bool Configured()
    {
        try { return !string.IsNullOrWhiteSpace(Settings.GetKey(Provider)) && ValidModel(Provider, Model); }
        catch { return false; }
    }
    internal static string Label(string provider) => provider == "openrouter" ? "OpenRouter" : "Gemini";

    internal static async Task<IReadOnlyList<AiModelOption>> ListVisionModels(string provider, string key, CancellationToken ct,
        HttpClient? suppliedClient = null)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(I18n.T("aiNeedKey"));
        using var owned = suppliedClient is null ? new HttpClient { Timeout = TimeSpan.FromSeconds(30) } : null;
        var http = suppliedClient ?? owned!;
        var models = new List<AiModelOption>();
        string next = "";
        for (int page = 0; page < 6; page++)
        {
            string endpoint = provider == "openrouter"
                ? $"https://openrouter.ai/api/v1/models/user?limit=1000&offset={page * 1000}"
                : "https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000" + (next.Length == 0 ? "" : "&pageToken=" + Uri.EscapeDataString(next));
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (provider == "openrouter") request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            else request.Headers.Add("x-goog-api-key", key.Trim());
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{Label(provider)} HTTP {(int)response.StatusCode}. {I18n.T("aiCatalogFailed")}");
            if (response.Content.Headers.ContentLength is > 8_000_000) throw new InvalidDataException(I18n.T("aiCatalogFailed"));
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192]; int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            { if (buffer.Length + read > 8_000_000) throw new InvalidDataException(I18n.T("aiCatalogFailed")); buffer.Write(chunk, 0, read); }
            using var document = JsonDocument.Parse(buffer.ToArray());
            var root = document.RootElement;
            var entries = root.GetProperty(provider == "openrouter" ? "data" : "models");
            foreach (var entry in entries.EnumerateArray())
            {
                if (provider == "openrouter")
                {
                    if (!entry.TryGetProperty("architecture", out var architecture) ||
                        !HasModality(architecture, "input_modalities", "image") ||
                        !HasModality(architecture, "input_modalities", "text") ||
                        !HasModality(architecture, "output_modalities", "text")) continue;
                    string id = entry.GetProperty("id").GetString() ?? "";
                    if (!ValidModel(provider, id)) continue;
                    string author = id.Split('/')[0];
                    string name = entry.TryGetProperty("name", out var nameValue) ? nameValue.GetString() ?? id : id;
                    bool free = id.EndsWith(":free", StringComparison.OrdinalIgnoreCase);
                    if (!free && entry.TryGetProperty("pricing", out var pricing))
                        free = Zero(pricing, "prompt") && Zero(pricing, "completion") && Zero(pricing, "request") && Zero(pricing, "image");
                    models.Add(new(id, name, author, free ? "aiFree" : "aiPaid"));
                }
                else
                {
                    string id = entry.GetProperty("name").GetString()?.Removeprefix("models/") ?? "";
                    if (!id.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) || !ValidModel(provider, id) ||
                        id.Contains("-image", StringComparison.OrdinalIgnoreCase) || id.Contains("-tts", StringComparison.OrdinalIgnoreCase) ||
                        id.Contains("-audio", StringComparison.OrdinalIgnoreCase) || id.Contains("-live", StringComparison.OrdinalIgnoreCase) ||
                        !HasModality(entry, "supportedGenerationMethods", "generateContent")) continue;
                    string name = entry.TryGetProperty("displayName", out var display) ? display.GetString() ?? id : id;
                    models.Add(new(id, name, "Google", "aiTierVaries"));
                }
            }
            if (provider == "openrouter" && entries.GetArrayLength() < 1000) break;
            if (provider == "gemini")
            {
                next = root.TryGetProperty("nextPageToken", out var token) ? token.GetString() ?? "" : "";
                if (next.Length == 0) break;
            }
        }
        return models.DistinctBy(m => m.Id).OrderBy(m => m.Author, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasModality(JsonElement element, string property, string modality) =>
        element.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array &&
        values.EnumerateArray().Any(value => string.Equals(value.GetString(), modality, StringComparison.OrdinalIgnoreCase));
    private static bool Zero(JsonElement pricing, string property) => !pricing.TryGetProperty(property, out var value) ||
        (decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) && amount == 0);
    private static string Removeprefix(this string value, string prefix) => value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
}
