using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Baseera.App.Services;

internal sealed record ChatGptIdentity(string Subject, string Email, string Name);
internal sealed record ChatGptDiscovery(Uri Authorize, Uri Token, Uri Jwks, Uri Revoke);
internal static partial class ChatGptAuth
{
    internal static async Task<ChatGptDiscovery> Discover(HttpClient http, CancellationToken ct)
    {
        using var response = await http.GetAsync(Issuer + "/.well-known/openid-configuration", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode(); using var document = await AiCatalog.ReadJson(response, 200_000, ct);
        var root = document.RootElement;
        if (Required(root, "issuer") != Issuer) throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        static Uri Endpoint(JsonElement root, string field)
        {
            var uri = new Uri(Required(root, field));
            if (uri.Scheme != "https" || uri.Host != "auth.openai.com" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
                throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
            return uri;
        }
        return new(Endpoint(root, "authorization_endpoint"), Endpoint(root, "token_endpoint"), Endpoint(root, "jwks_uri"), Endpoint(root, "revocation_endpoint"));
    }
    internal static Uri AuthorizationUrl(ChatGptDiscovery endpoints, string hostId, string callback, string state, string nonce,
        string verifier, ChatGptAccount? account)
    {
        var values = new Dictionary<string, string>
        {
            ["client_id"] = account?.ClientId ?? "dynamic_agent_client", ["ext_agent_host_id"] = hostId,
            ["response_type"] = "code", ["redirect_uri"] = callback, ["scope"] = Scope, ["resource"] = Resource,
            ["state"] = state, ["nonce"] = nonce, ["code_challenge_method"] = "S256",
            ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
        };
        if (account is null) values["agent_name_hint"] = "Baseera";
        else
        {
            if (account.IdToken.Length > 0) values["id_token_hint"] = account.IdToken;
            if (account.Email.Length > 0) values["login_hint"] = account.Email;
            if (account.Scopes.Length > 0 && !account.HasPermission) values["prompt"] = "consent";
        }
        return new Uri(endpoints.Authorize.AbsoluteUri + "?" + string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
    }
    internal static async Task<ChatGptProfile> SignIn(string? existingClientId, CancellationToken ct, HttpClient? suppliedClient = null,
        Func<Uri, Task>? launchBrowser = null)
        => await SignInCore(existingClientId, ct, suppliedClient, launchBrowser, null, 0);
    private static async Task<ChatGptProfile> SignInCore(string? existingClientId, CancellationToken ct, HttpClient? suppliedClient,
        Func<Uri, Task>? launchBrowser, ChatGptAccount? pendingRegistration, int attempt)
    {
        ChatGptAccount? existing; string host;
        await gate.WaitAsync(ct);
        try
        {
            await using var fileLock = await LockStore(ct); var store = ReadStore();
            if (store.HostId.Length == 0) { store.HostId = "urn:uuid:" + Guid.NewGuid().ToString("D"); WriteStore(store); }
            host = store.HostId; existing = existingClientId is null ? pendingRegistration : store.Accounts.Single(account => account.ClientId == existingClientId);
        }
        finally { gate.Release(); }
        using var owned = suppliedClient is null ? AiCatalog.Client(TimeSpan.FromSeconds(40)) : null;
        var http = suppliedClient ?? owned!; var endpoints = await Discover(http, ct);
        string state = Base64Url(RandomNumberGenerator.GetBytes(32)), nonce = Base64Url(RandomNumberGenerator.GetBytes(32)), verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(4);
        string callback = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/auth/callback";
        // The listener is already bound before the system browser receives the authorization URL.
        Uri url = AuthorizationUrl(endpoints, host, callback, state, nonce, verifier, existing);
        if (launchBrowser is null) Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        else await launchBrowser(url);
        var result = await ReceiveCallback(listener, callback, state, existing?.ClientId, ct);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, endpoints.Token)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = result.ClientId, ["code"] = result.Code,
                ["code_verifier"] = verifier, ["redirect_uri"] = callback, ["resource"] = Resource
            })
        };
        using var tokenResponse = await http.SendAsync(tokenRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            using var failure = await AiCatalog.ReadJson(tokenResponse, 100_000, ct);
            if (attempt == 0 && Optional(failure.RootElement, "error") == "invalid_grant")
                return await SignInCore(existingClientId, ct, http, launchBrowser,
                    existing ?? new ChatGptAccount { ClientId = result.ClientId }, attempt + 1);
            throw new ChatGptException("chatGptSignInFailed");
        }
        using var tokenJson = await AiCatalog.ReadJson(tokenResponse, 300_000, ct);
        var tokens = tokenJson.RootElement;
        using var jwksResponse = await http.GetAsync(endpoints.Jwks, HttpCompletionOption.ResponseHeadersRead, ct);
        jwksResponse.EnsureSuccessStatusCode(); using var jwks = await AiCatalog.ReadJson(jwksResponse, 200_000, ct);
        var identity = ValidateIdToken(Required(tokens, "id_token"), jwks.RootElement, result.ClientId, nonce);
        if (existing is { Subject.Length: > 0 } && identity.Subject != existing.Subject) throw new InvalidDataException(I18n.T("chatGptIdentityMismatch"));
        var account = new ChatGptAccount { ClientId = result.ClientId, Subject = identity.Subject, Email = identity.Email, Name = identity.Name };
        ApplyTokens(account, tokens, initial: true);
        ct.ThrowIfCancellationRequested();
        await gate.WaitAsync(ct);
        try
        {
            await using var fileLock = await LockStore(ct); var store = ReadStore();
            var previous = store.Accounts.SingleOrDefault(item => item.ClientId == account.ClientId);
            if (previous is not null && previous.Subject != account.Subject) throw new InvalidDataException(I18n.T("chatGptIdentityMismatch"));
            account.WelcomeSeen = previous?.WelcomeSeen ?? false;
            store.Accounts.RemoveAll(item => item.ClientId == account.ClientId); store.Accounts.Add(account); WriteStore(store);
            StopSession(account.ClientId, restart: true);
        }
        finally { gate.Release(); }
        return account.Profile;
    }
    internal static ChatGptIdentity ValidateIdToken(string token, JsonElement jwks, string clientId, string? nonce)
    {
        string[] parts = token.Split('.');
        if (parts.Length != 3 || token.Length > 100_000) throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        using var header = JsonDocument.Parse(FromBase64Url(parts[0])); using var payload = JsonDocument.Parse(FromBase64Url(parts[1]));
        if (Required(header.RootElement, "alg") != "RS256") throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        string kid = Required(header.RootElement, "kid");
        var key = jwks.GetProperty("keys").EnumerateArray().Single(item => Optional(item, "kid") == kid);
        if (Required(key, "kty") != "RSA" || Optional(key, "use") is not ("" or "sig") || Optional(key, "alg") is not ("" or "RS256"))
            throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        byte[] modulus = FromBase64Url(Required(key, "n")), exponent = FromBase64Url(Required(key, "e"));
        if (modulus.Length < 256 || exponent.Length is < 1 or > 8) throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        using var rsa = RSA.Create(); rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), FromBase64Url(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        var root = payload.RootElement; long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var audience = root.GetProperty("aud");
        bool validAudience = audience.ValueKind == JsonValueKind.String ? audience.GetString() == clientId :
            audience.ValueKind == JsonValueKind.Array && audience.EnumerateArray().Any(value => value.GetString() == clientId);
        if (!validAudience || Required(root, "iss") != Issuer || root.GetProperty("exp").GetInt64() <= now - 30 ||
            root.TryGetProperty("iat", out var issued) && issued.GetInt64() > now + 60 ||
            root.TryGetProperty("nbf", out var notBefore) && notBefore.GetInt64() > now + 60 ||
            nonce is not null && !Equal(Optional(root, "nonce"), nonce) ||
            audience.ValueKind == JsonValueKind.Array && audience.GetArrayLength() > 1 && Optional(root, "azp") != clientId ||
            Optional(root, "azp") is { Length: > 0 } party && party != clientId)
            throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        return new(Required(root, "sub"), Optional(root, "email")[..Math.Min(254, Optional(root, "email").Length)], Optional(root, "name")[..Math.Min(200, Optional(root, "name").Length)]);
    }
    private static void ApplyTokens(ChatGptAccount account, JsonElement tokens, bool initial)
    {
        if (!string.Equals(Required(tokens, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        long lifetime = tokens.GetProperty("expires_in").GetInt64();
        if (lifetime is < 1 or > 86400) throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        account.AccessToken = Required(tokens, "access_token");
        string refresh = Optional(tokens, "refresh_token");
        if (refresh.Length > 0) account.RefreshToken = refresh;
        string idToken = Optional(tokens, "id_token"); if (idToken.Length > 0) account.IdToken = idToken;
        string granted = Optional(tokens, "scope");
        if (initial || granted.Length > 0) account.Scopes = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        account.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(lifetime);
        account.EarliestRefreshAt = tokens.TryGetProperty("earliest_refresh_at", out var earliest) && earliest.ValueKind == JsonValueKind.Number && earliest.TryGetInt64(out long seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : DateTimeOffset.MinValue;
    }
    private sealed class CallbackResult { internal string ClientId = "", Code = ""; }
    internal static Dictionary<string, string> ParseQuery(string query)
    {
        if (query.Length > 16_000) throw new InvalidDataException();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string field in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = field.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' ')), value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (!values.TryAdd(key, value)) throw new InvalidDataException();
        }
        return values;
    }
    private static async Task<CallbackResult> ReceiveCallback(TcpListener listener, string callback, string state, string? existingClient, CancellationToken ct)
    {
        var expected = new Uri(callback);
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var stream = client.GetStream();
            try
            {
                var bytes = new byte[16_384]; int length = 0;
                while (length < bytes.Length)
                {
                    int count = await stream.ReadAsync(bytes.AsMemory(length), timeout.Token); if (count == 0) break; length += count;
                    if (Encoding.ASCII.GetString(bytes, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
                }
                string header = Encoding.ASCII.GetString(bytes, 0, length); string[] lines = header.Split("\r\n");
                string[] request = lines[0].Split(' ');
                if (length >= bytes.Length || request.Length != 3 || request[0] != "GET" || !request[1].StartsWith("/auth/callback?", StringComparison.Ordinal) ||
                    !lines.Skip(1).Any(line => line.Equals("Host: " + expected.Authority, StringComparison.OrdinalIgnoreCase)))
                { await CallbackPage(stream, false, timeout.Token); continue; }
                var query = ParseQuery(request[1][request[1].IndexOf('?')..]);
                if (!Equal(query.GetValueOrDefault("state", ""), state)) { await CallbackPage(stream, false, timeout.Token); continue; }
                await CallbackPage(stream, true, timeout.Token);
                if (query.ContainsKey("error")) throw new ChatGptException("chatGptDeclined");
                string issued = query.GetValueOrDefault("client_id", existingClient ?? "");
                if (issued.Length is < 3 or > 200 || issued == "dynamic_agent_client" ||
                    issued.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.') ||
                    existingClient is not null && issued != existingClient || !query.TryGetValue("code", out var code) || code.Length == 0)
                    throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
                return new() { ClientId = issued, Code = code };
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (IOException) { }
        }
    }
    private static async Task CallbackPage(Stream stream, bool accepted, CancellationToken ct)
    {
        string title = WebUtility.HtmlEncode(I18n.T(accepted ? "chatGptReturn" : "chatGptInvalidCallback"));
        string background = Settings.Current.Dark ? "#272727" : "#fafafa", ink = Settings.Current.Dark ? "#f4f4f4" : "#1b1b1b", line = Settings.Current.Dark ? "#414141" : "#e4e4e4";
        byte[] body = Encoding.UTF8.GetBytes("<!doctype html><html dir='" + (Settings.Current.Language == "ar" ? "rtl" : "ltr") + "'><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Baseera</title>" +
            "<style>body{margin:0;padding:24px;min-height:90vh;display:grid;place-items:center;font:16px/1.8 system-ui;background:" + background + ";color:" + ink + "}main{max-width:540px;padding:36px;border:1px solid " + line + ";border-radius:8px}h1{margin:0 0 16px;font-size:26px}p{margin:0}</style><main><h1>Baseera</h1><p>" + title + "</p></main></html>");
        string headers = "HTTP/1.1 " + (accepted ? "200 OK" : "400 Bad Request") + "\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), ct); await stream.WriteAsync(body, ct);
    }
}
