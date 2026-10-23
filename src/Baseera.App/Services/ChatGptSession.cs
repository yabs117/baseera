using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Baseera.App.Services;

internal static partial class ChatGptAuth
{
    internal static async Task<string> AccessToken(string clientId, CancellationToken ct, HttpClient? suppliedClient = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var fileLock = await LockStore(ct); var store = ReadStore();
            var account = store.Accounts.SingleOrDefault(account => account.ClientId == clientId);
            if (account is null || !account.HasPermission) throw new ChatGptException("chatGptConsentNeeded");
            if (account.AccessToken.Length > 0 && account.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return account.AccessToken;
            if (account.EarliestRefreshAt > DateTimeOffset.UtcNow && account.ExpiresAt > DateTimeOffset.UtcNow) return account.AccessToken;
            if (account.RefreshToken.Length == 0 || account.EarliestRefreshAt > DateTimeOffset.UtcNow) throw new ChatGptException("chatGptReconnect");
            using var owned = suppliedClient is null ? AiCatalog.Client(TimeSpan.FromSeconds(35)) : null;
            var http = suppliedClient ?? owned!; var endpoints = await Discover(http, ct);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.Token)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token", ["client_id"] = account.ClientId,
                    ["refresh_token"] = account.RefreshToken, ["resource"] = Resource
                })
            };
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var failure = await HttpFailure(response, ct);
                if (failure.ErrorCode is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused")
                { ClearTokens(account); WriteStore(store); StopSession(clientId); }
                throw failure.ErrorCode is "invalid_grant" or "invalid_refresh_token" or "token_expired" or "refresh_token_expired" or "refresh_token_invalidated" or "refresh_token_reused"
                    ? new ChatGptException("chatGptReconnect", (int)response.StatusCode, failure.ErrorCode, failure.RequestId, failure.BodyShape) : failure;
            }
            using var json = await AiCatalog.ReadJson(response, 300_000, ct);
            string newIdToken = Optional(json.RootElement, "id_token");
            if (newIdToken.Length > 0)
            {
                using var keysResponse = await http.GetAsync(endpoints.Jwks, HttpCompletionOption.ResponseHeadersRead, ct);
                keysResponse.EnsureSuccessStatusCode(); using var keys = await AiCatalog.ReadJson(keysResponse, 200_000, ct);
                var identity = ValidateIdToken(newIdToken, keys.RootElement, account.ClientId, null);
                if (identity.Subject != account.Subject) throw new InvalidDataException(I18n.T("chatGptIdentityMismatch"));
            }
            ApplyTokens(account, json.RootElement, initial: false); WriteStore(store);
            if (!account.HasPermission) throw new ChatGptException("chatGptConsentNeeded");
            return account.AccessToken;
        }
        finally { gate.Release(); }
    }
    private static void ClearTokens(ChatGptAccount account)
    { account.AccessToken = account.RefreshToken = account.IdToken = ""; account.Scopes = []; account.ExpiresAt = DateTimeOffset.MinValue; }
    internal static async Task<bool> SignOut(string clientId, CancellationToken ct, HttpClient? suppliedClient = null)
    {
        StopSession(clientId); string refreshToken = "";
        await gate.WaitAsync(ct);
        try
        {
            await using var fileLock = await LockStore(ct); var store = ReadStore();
            var account = store.Accounts.SingleOrDefault(account => account.ClientId == clientId);
            if (account is not null)
            {
                refreshToken = account.RefreshToken;
                ClearTokens(account); WriteStore(store);
            }
        }
        finally { gate.Release(); }
        // Disconnect every tool before contacting the server. API-mode settings and keys are independent.
        if (Settings.Current.ChatGptAccount == clientId)
        {
            Settings.Current.ChatGptAccount = ""; Settings.Current.ChatGptModel = ""; Settings.Save();
        }
        SessionSignedOut?.Invoke(clientId);
        if (refreshToken.Length == 0) return true;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct); bounded.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var owned = suppliedClient is null ? AiCatalog.Client(TimeSpan.FromSeconds(20)) : null;
            var http = suppliedClient ?? owned!; var endpoints = await Discover(http, bounded.Token);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.Revoke)
                    { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken, ["token_type_hint"] = "refresh_token", ["client_id"] = clientId }) };
                    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token);
                    if (response.StatusCode == HttpStatusCode.OK) return true;
                    if ((int)response.StatusCode < 500) break;
                }
                catch (HttpRequestException) { }
                if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), bounded.Token);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException) { }
        return false;
    }
    internal static async Task<IReadOnlyList<AiModelOption>> Models(string clientId, CancellationToken ct, HttpClient? suppliedClient = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, SessionToken(clientId));
        string token = await AccessToken(clientId, linked.Token, suppliedClient);
        using var owned = suppliedClient is null ? AiCatalog.Client(TimeSpan.FromSeconds(35)) : null;
        using var request = new HttpRequestMessage(HttpMethod.Get, Resource + "/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await (suppliedClient ?? owned!).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
        if (!response.IsSuccessStatusCode) throw await HttpFailure(response, linked.Token);
        using var json = await AiCatalog.ReadJson(response, 2_000_000, linked.Token);
        var result = new List<AiModelOption>();
        foreach (var model in json.RootElement.GetProperty("models").EnumerateArray())
        {
            string slug = Optional(model, "slug");
            if (Optional(model, "visibility") != "list" || !AiCatalog.ValidModel("openai", slug) || !AiCatalog.SupportsVision("openai", slug, model)) continue;
            result.Add(new(slug, Optional(model, "display_name") is { Length: > 0 } name ? name : slug, "OpenAI", "chatGptPlan"));
        }
        return result.DistinctBy(model => model.Id).ToArray(); // Preserve the account catalog's order.
    }
    internal static async Task<string> Complete(string clientId, string model, string prompt, byte[]? image, string mimeType, CancellationToken ct, HttpClient? suppliedClient = null, string? instructions = null)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, SessionToken(clientId));
        string token = await AccessToken(clientId, linked.Token, suppliedClient);
        var content = new List<object> { new { type = "input_text", text = prompt } };
        if (image is not null) content.Add(new { type = "input_image", image_url = $"data:{mimeType};base64,{Convert.ToBase64String(image)}" });
        using var owned = suppliedClient is null ? AiCatalog.Client(TimeSpan.FromMinutes(3)) : null;
        using var request = new HttpRequestMessage(HttpMethod.Post, Resource + "/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("text/event-stream");
        // ChatGPT-plan requests intentionally omit API-only fields such as max_output_tokens and temperature.
        var body = JsonSerializer.SerializeToNode(new { model, input = new[] { new { role = "user", content } }, store = false, stream = true })!.AsObject();
        if (!string.IsNullOrWhiteSpace(instructions)) body["instructions"] = instructions;
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await (suppliedClient ?? owned!).SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw await HttpFailure(response, linked.Token);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
        return await ReadResponseStream(stream, linked.Token, RequestId(response));
    }
    internal static async Task<string> ReadResponseStream(Stream stream, CancellationToken ct, string requestId = "")
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var pending = new StringBuilder(); var data = new StringBuilder(); var deltas = new StringBuilder();
        var buffer = new char[4096]; int total = 0;
        while (true)
        {
            int count = await reader.ReadAsync(buffer, ct); if (count == 0) break;
            total += count; if (total > 4_000_000) throw new InvalidDataException(I18n.T("chatGptStreamFailed"));
            pending.Append(buffer, 0, count);
            while (true)
            {
                int end = -1; for (int i = 0; i < pending.Length; i++) if (pending[i] == '\n') { end = i; break; }
                if (end < 0) break;
                string line = pending.ToString(0, end).TrimEnd('\r'); pending.Remove(0, end + 1);
                if (line.StartsWith("data:", StringComparison.Ordinal)) data.Append(line[5..].TrimStart()).Append('\n');
                else if (line.Length == 0 && data.Length > 0)
                {
                    string value = data.ToString().Trim(); data.Clear();
                    if (value == "[DONE]") throw new InvalidDataException(I18n.T("chatGptStreamFailed"));
                    using var document = JsonDocument.Parse(value); var root = document.RootElement; string type = Optional(root, "type");
                    if (type == "response.output_text.delta") deltas.Append(Optional(root, "delta"));
                    else if (type is "response.failed" or "response.incomplete" or "error")
                    {
                        string code = root.TryGetProperty("response", out var failed) && failed.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object ? Optional(error, "code") : Optional(root, "code");
                        throw ResponseFailure(code, 200, requestId, "stream-error");
                    }
                    else if (type == "response.completed")
                    {
                        if (root.TryGetProperty("response", out var response) && response.TryGetProperty("status", out var status) && status.GetString() != "completed")
                            throw new InvalidDataException(I18n.T("chatGptStreamFailed"));
                        string answer = deltas.ToString().Trim();
                        if (response.ValueKind == JsonValueKind.Object && response.TryGetProperty("output", out var output))
                        {
                            string final = string.Concat(output.EnumerateArray().Where(item => Optional(item, "type") == "message")
                                .SelectMany(item => item.GetProperty("content").EnumerateArray()).Where(part => Optional(part, "type") == "output_text").Select(part => Optional(part, "text"))).Trim();
                            if (final.Length > 0) answer = final;
                        }
                        if (answer.Length == 0) throw new InvalidDataException(I18n.T("chatGptStreamFailed"));
                        return answer;
                    }
                }
            }
        }
        throw new InvalidDataException(I18n.T("chatGptStreamFailed"));
    }
}
