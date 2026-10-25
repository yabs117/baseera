using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace Baseera.App.Services;

internal sealed record AiProbeConnection(string Provider, string Model, string Key, string Account = "")
{
    internal bool ChatGpt => Account.Length > 0;
    public override string ToString() => Provider + " / " + Model + (ChatGpt ? " (ChatGPT sign-in)" : " (API key)");
}
internal sealed record AiProbeResult(bool ImageAnswerMatched, TimeSpan Duration);

internal static class AiConnectionProbe
{
    private static readonly (string Name, System.Drawing.Color Color)[] Colors =
    [ ("RED", System.Drawing.Color.Red), ("GREEN", System.Drawing.Color.Lime), ("BLUE", System.Drawing.Color.Blue), ("YELLOW", System.Drawing.Color.Yellow) ];
    internal const string Prompt = "Inspect the supplied solid-color image. Reply with exactly one dominant color: RED, GREEN, BLUE, or YELLOW. No other text.";
    internal const string Instructions = "This is a minimal connection and image-input check. Follow the fixed test prompt. No conversation, private files or diagnostics are included.";
    internal static byte[] Image(int color)
    {
        using var bitmap = new System.Drawing.Bitmap(64, 64);
        using (var draw = System.Drawing.Graphics.FromImage(bitmap)) draw.Clear(Colors[color].Color);
        using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg); return stream.ToArray();
    }
    internal static async Task<AiProbeResult> Test(AiProbeConnection connection, CancellationToken token, HttpClient? supplied = null, int? color = null,
        Func<string, string, string, byte[], CancellationToken, Task<string>>? accountRequest = null)
    {
        if (!AiCatalog.ValidModel(connection.Provider, connection.Model) || connection.ChatGpt && connection.Provider != "openai" ||
            !connection.ChatGpt && string.IsNullOrWhiteSpace(connection.Key)) throw new InvalidOperationException(I18n.T("aiProbeNeedsSetup"));
        int selected = color ?? RandomNumberGenerator.GetInt32(Colors.Length);
        if (selected is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(color));
        byte[] image = Image(selected); var clock = Stopwatch.StartNew(); string answer;
        if (connection.ChatGpt)
            answer = accountRequest is not null ? await accountRequest(connection.Account, connection.Model, Prompt, image, token) :
                await ChatGptAuth.Complete(connection.Account, connection.Model, Prompt, image, "image/jpeg", token, supplied, Instructions);
        else if (connection.Provider != "gemini")
            answer = await AiVision.Complete(connection.Provider, Prompt, image, "image/jpeg", connection.Key, connection.Model, token, supplied, instructions: Instructions);
        else
        {
            using var owned = supplied is null ? AiCatalog.Client(TimeSpan.FromSeconds(35)) : null;
            using var request = new HttpRequestMessage(HttpMethod.Post, AiProviders.Get("gemini").BaseUrl + "models/" + connection.Model + ":generateContent");
            AiCatalog.Authenticate(request, AiProviders.Get("gemini"), connection.Key);
            request.Content = new StringContent(JsonSerializer.Serialize(new
            {
                systemInstruction = new { parts = new[] { new { text = Instructions } } },
                contents = new[] { new { parts = new object[] { new { text = Prompt }, new { inline_data = new { mime_type = "image/jpeg", data = Convert.ToBase64String(image) } } } } }
            }), Encoding.UTF8, "application/json");
            using var response = await (supplied ?? owned!).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Gemini HTTP " + (int)response.StatusCode);
            using var json = await AiCatalog.ReadJson(response, 200000, token);
            answer = string.Concat(json.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts").EnumerateArray().Where(x => x.TryGetProperty("text", out _)).Select(x => x.GetProperty("text").GetString()));
        }
        token.ThrowIfCancellationRequested(); clock.Stop();
        string normalized = answer.Trim().Trim('`', '"', '\'', '.', '!', ' ', '\r', '\n').ToUpperInvariant();
        return new(normalized == Colors[selected].Name, clock.Elapsed);
    }
}
