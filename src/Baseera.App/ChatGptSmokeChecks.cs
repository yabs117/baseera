using Baseera.App.Services;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Baseera.App;

internal static class ChatGptSmokeChecks
{
    private sealed class Reply(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => reply(request, ct); }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var node in Nodes(child)) yield return node; }
    }
    internal static async Task Run(MainWindow owner, string output)
    {
        Require(Environment.GetEnvironmentVariable("BASEERA_DATA_DIR") is { Length: > 0 }, "OAuth checks require isolated app storage.");
        Settings.Save(); string settingsPath = Path.Combine(Settings.Folder, "settings.json"), storePath = Path.Combine(Settings.Folder, "chatgpt-accounts.bin");
        byte[] originalSettings = File.ReadAllBytes(settingsPath); byte[]? originalStore = File.Exists(storePath) ? File.ReadAllBytes(storePath) : null;
        string oldKey = Settings.GetKey("openai");
        using var rsa = RSA.Create(2048); var publicKey = rsa.ExportParameters(false);
        object jwks = new { keys = new[] { new { kty = "RSA", alg = "RS256", use = "sig", kid = "baseera-test", n = ChatGptAuth.Base64Url(publicKey.Modulus!), e = ChatGptAuth.Base64Url(publicKey.Exponent!) } } };
        Dictionary<string, string> authorization = []; string nonce = ""; string clientId = "oaiapp_baseera_test_one";
        string idToken = "", previousNonce = "", hostId = ""; int exchanges = 0, refreshes = 0, inference = 0, revokes = 0, authorizations = 0;
        bool simulateInvalidGrant = true;
        Task callback = Task.CompletedTask;
        string Token(string audience, string tokenNonce, string subject = "fixture-subject", long? expires = null)
        {
            string header = ChatGptAuth.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "baseera-test" }));
            string body = ChatGptAuth.Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iss = ChatGptAuth.Issuer, aud = audience, sub = subject,
                exp = expires ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds(), iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), nonce = tokenNonce, email = "sample@example.invalid", name = "Sample account" }));
            return header + "." + body + "." + ChatGptAuth.Base64Url(rsa.SignData(Encoding.ASCII.GetBytes(header + "." + body), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        using var http = new HttpClient(new Reply(async (request, ct) =>
        {
            if (request.RequestUri!.Host == "auth.openai.com")
            {
                switch (request.RequestUri.AbsolutePath)
                {
                    case "/.well-known/openid-configuration": return Json(new { issuer = ChatGptAuth.Issuer,
                        authorization_endpoint = ChatGptAuth.Issuer + "/api/accounts/authorize", token_endpoint = ChatGptAuth.Issuer + "/api/accounts/oauth/token",
                        jwks_uri = ChatGptAuth.Issuer + "/.well-known/jwks.json", revocation_endpoint = ChatGptAuth.Issuer + "/api/accounts/oauth/revoke" });
                    case "/.well-known/jwks.json": return Json(jwks);
                    case "/api/accounts/oauth/revoke":
                        var revoke = ChatGptAuth.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
                        Require(revoke["client_id"] == clientId && revoke["token_type_hint"] == "refresh_token", "Revocation session mapping incorrect.");
                        revokes++; return new HttpResponseMessage(HttpStatusCode.OK);
                    case "/api/accounts/oauth/token":
                        var form = ChatGptAuth.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
                        Require(form["resource"] == ChatGptAuth.Resource && form["client_id"] == clientId && !form.ContainsKey("client_secret"), "OAuth resource/client/secret incorrect.");
                        if (form["grant_type"] == "authorization_code")
                        {
                            Require(ChatGptAuth.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))) == authorization["code_challenge"], "PKCE failed.");
                            Require(form["redirect_uri"] == authorization["redirect_uri"] && form["code"] == "fixture-code", "Callback URI or authorization code changed.");
                            if (simulateInvalidGrant) { simulateInvalidGrant = false; return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") }; }
                            exchanges++; idToken = Token(clientId, nonce);
                        }
                        else
                        {
                            Require(form["grant_type"] == "refresh_token" && form["refresh_token"] == "fixture-refresh-one" && !form.ContainsKey("scope"), "Refresh contract incorrect.");
                            refreshes++;
                            idToken = Token(clientId, nonce);
                        }
                        return Json(new { access_token = refreshes > 0 ? "fixture-access-two" : "fixture-access-one", refresh_token = refreshes > 0 ? "fixture-refresh-two" : "fixture-refresh-one",
                            id_token = idToken, token_type = "Bearer", expires_in = 3600, scope = ChatGptAuth.Scope, earliest_refresh_at = (long?)null });
                }
            }
            Require(request.RequestUri.Host == "api.openai.com", "OAuth token sent to an unexpected host.");
            Require(request.Headers.Authorization?.Parameter is "fixture-access-one" or "fixture-access-two", "OAuth bearer token missing or API key substituted.");
            if (request.RequestUri.AbsolutePath == "/v1/models") return Json(new { models = new[] {
                new { slug = "gpt-6.1-sol", display_name = "GPT Sol", visibility = "list", input_modalities = new[] { "text", "image" }, output_modalities = new[] { "text" } },
                new { slug = "gpt-hidden", display_name = "Hidden", visibility = "hidden", input_modalities = new[] { "text", "image" }, output_modalities = new[] { "text" } },
                new { slug = "gpt-text-only", display_name = "Text", visibility = "list", input_modalities = new[] { "text" }, output_modalities = new[] { "text" } }
            } });
            Require(request.RequestUri.AbsolutePath == "/v1/responses", "OAuth inference must use the public Responses API.");
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var root = payload.RootElement;
            Require(!root.GetProperty("store").GetBoolean() && root.GetProperty("stream").GetBoolean() && root.GetProperty("input").ValueKind == JsonValueKind.Array &&
                !root.TryGetProperty("max_output_tokens", out _) && !root.TryGetProperty("temperature", out _) && !root.TryGetProperty("previous_response_id", out _), "ChatGPT plan request used unsupported fields.");
            Require(root.GetProperty("input")[0].GetProperty("content")[1].GetProperty("type").GetString() == "input_image", "OAuth image input missing.");
            inference++;
            string answer = "{\"summary\":\"Test error\",\"cause\":\"Fixture cause\",\"steps\":[\"Review the error.\"],\"checks\":[],\"confidence\":\"Uncertain\"}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "data: " + JsonSerializer.Serialize(new { type = "response.output_text.delta", delta = answer }) + "\n\n" +
                "data: " + JsonSerializer.Serialize(new { type = "response.completed", response = new { status = "completed" } }) + "\n\n", Encoding.UTF8, "text/event-stream") };
        }));
        try
        {
            if (File.Exists(storePath)) File.Delete(storePath);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var profile = await ChatGptAuth.SignIn(null, deadline.Token, http, uri =>
            {
                authorization = ChatGptAuth.ParseQuery(uri.Query); nonce = authorization["nonce"];
                if (authorizations == 0)
                {
                    Require(authorization["client_id"] == "dynamic_agent_client" && authorization["agent_name_hint"] == "Baseera", "Initial registration parameters incorrect.");
                    hostId = authorization["ext_agent_host_id"];
                }
                else Require(authorization["client_id"] == clientId && !authorization.ContainsKey("agent_name_hint") && authorization["ext_agent_host_id"] == hostId, "invalid_grant retry lost the issued client or stable host.");
                Require(authorization["code_challenge_method"] == "S256" && hostId.StartsWith("urn:uuid:") && nonce != previousNonce, "PKCE, host or fresh nonce missing.");
                previousNonce = nonce; authorizations++;
                var redirect = new Uri(authorization["redirect_uri"]);
                Require(redirect.Host == "127.0.0.1" && redirect.AbsolutePath == "/auth/callback", "Listener is not the documented loopback callback.");
                callback = Task.Run(async () =>
                {
                    using var browser = new HttpClient();
                    var rejected = await browser.GetAsync(redirect.AbsoluteUri + "?state=wrong&code=fixture-code&client_id=" + clientId, deadline.Token);
                    Require(rejected.StatusCode == HttpStatusCode.BadRequest, "Wrong state was accepted.");
                    var accepted = await browser.GetAsync(redirect.AbsoluteUri + "?state=" + Uri.EscapeDataString(authorization["state"]) + "&code=fixture-code&client_id=" + clientId, deadline.Token);
                    Require(accepted.StatusCode == HttpStatusCode.OK, "Valid callback rejected.");
                });
                return Task.CompletedTask;
            });
            await callback;
            Require(profile.Connected && exchanges == 1 && authorizations == 2 && ChatGptAuth.Profiles.Count == 1 && ChatGptAuth.NeedsWelcome(clientId), "Validated account, grant recovery or first-sign-in flag missing.");
            clientId = "oaiapp_baseera_test_two";
            var second = await ChatGptAuth.SignIn(null, deadline.Token, http, uri =>
            {
                authorization = ChatGptAuth.ParseQuery(uri.Query); nonce = authorization["nonce"];
                Require(authorization["client_id"] == "dynamic_agent_client" && authorization["ext_agent_host_id"] == hostId, "New account did not keep this host and create its own registration.");
                callback = Task.Run(async () =>
                {
                    using var browser = new HttpClient();
                    using var response = await browser.GetAsync(authorization["redirect_uri"] + "?state=" + Uri.EscapeDataString(authorization["state"]) + "&code=fixture-code&client_id=" + clientId, deadline.Token);
                    string page = await response.Content.ReadAsStringAsync(deadline.Token);
                    Require(response.IsSuccessStatusCode && page.Contains("Baseera") && !page.Contains("fixture-code"), "Callback page leaked the authorization code.");
                }); return Task.CompletedTask;
            });
            await callback; clientId = profile.Id; idToken = Token(clientId, nonce);
            Require(second.Connected && ChatGptAuth.Profiles.Count == 2 && ChatGptAuth.Profiles.Select(account => account.ToString()).Distinct().Count() == 2, "Accounts with the same email were merged or mislabeled.");
            string encrypted = Encoding.UTF8.GetString(File.ReadAllBytes(storePath));
            Require(!encrypted.Contains("fixture-access") && !encrypted.Contains("sample@example.invalid"), "OAuth credentials or identity saved in plaintext.");
            using var keys = JsonDocument.Parse(JsonSerializer.Serialize(jwks));
            foreach (var invalid in new[] { Token("wrong-audience", nonce), Token(clientId, "wrong-nonce"), Token(clientId, nonce, expires: DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()), idToken[..^4] + "AAAA" })
            {
                bool rejected = false; try { ChatGptAuth.ValidateIdToken(invalid, keys.RootElement, clientId, nonce); } catch { rejected = true; }
                Require(rejected, "JWT signature, audience, nonce or expiry validation missing.");
            }
            byte[] retained = File.ReadAllBytes(storePath), plain = Settings.ReadProtected("chatgpt-accounts.bin");
            var noConsent = JsonSerializer.Deserialize<ChatGptStore>(plain)!; CryptographicOperations.ZeroMemory(plain);
            noConsent.Accounts.Single(account => account.ClientId == clientId).Scopes = ["openid", "profile", "email"];
            Settings.WriteProtected("chatgpt-accounts.bin", JsonSerializer.SerializeToUtf8Bytes(noConsent));
            bool denied = false; try { await ChatGptAuth.AccessToken(clientId, deadline.Token, http); } catch (ChatGptException ex) { denied = ex.Key == "chatGptConsentNeeded"; }
            Require(denied, "Identity-only sign-in authorized inference.");
            var endpoints = new ChatGptDiscovery(new(ChatGptAuth.Issuer + "/api/accounts/authorize"), new(ChatGptAuth.Issuer + "/api/accounts/oauth/token"), new(ChatGptAuth.Issuer + "/.well-known/jwks.json"), new(ChatGptAuth.Issuer + "/api/accounts/oauth/revoke"));
            var consentUrl = ChatGptAuth.AuthorizationUrl(endpoints, hostId, "http://127.0.0.1:12345/auth/callback", "state", "nonce", "verifier", noConsent.Accounts.Single(account => account.ClientId == clientId));
            Require(ChatGptAuth.ParseQuery(consentUrl.Query).GetValueOrDefault("prompt") == "consent", "Enabling plan use after a decline did not request consent.");
            File.WriteAllBytes(storePath, retained);
            Require(ChatGptAuth.ResponseFailure("subscription_sharing_user_not_eligible", 403).Key == "chatGptNotEligible" &&
                ChatGptAuth.ResponseFailure("subscription_sharing_usage_unavailable", 503).Key == "chatGptTemporarilyUnavailable", "Admission failures would cause a sign-in/usage-limit loop.");
            var models = await ChatGptAuth.Models(clientId, deadline.Token, http);
            Require(models.Count == 1 && models[0].Id == "gpt-6.1-sol" && models[0].Tier == "chatGptPlan", "Account model catalog leaked hidden/text-only models.");
            var protectedBytes = Settings.ReadProtected("chatgpt-accounts.bin");
            var store = JsonSerializer.Deserialize<ChatGptStore>(protectedBytes)!; CryptographicOperations.ZeroMemory(protectedBytes);
            store.Accounts.Single(account => account.ClientId == clientId).ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1); Settings.WriteProtected("chatgpt-accounts.bin", JsonSerializer.SerializeToUtf8Bytes(store));
            var tokens = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ChatGptAuth.AccessToken(clientId, deadline.Token, http)));
            Require(refreshes == 1 && tokens.All(token => token == "fixture-access-two"), "Rotating refresh tokens raced or were not persisted.");

            Settings.SetKey("openai", "fixture-api-key-kept-separate");
            Settings.Current.AiProvider = "openai"; Settings.Current.OpenAiAuth = "chatgpt"; Settings.Current.ChatGptAccount = clientId; Settings.Current.ChatGptModel = "gpt-6.1-sol";
            var advice = await ErrorAssistant.Analyze("Fixture error", new ErrorImage([1, 2, 3], "fixture"), "English", deadline.Token, http);
            Require(advice.Summary == "Test error" && inference == 1 && Settings.GetKey("openai") == "fixture-api-key-kept-separate", "OAuth analysis used or overwrote the API key.");
            foreach (string failingStream in new[] {
                "data: {\"type\":\"response.output_text.delta\",\"delta\":\"partial\"}\n\n",
                "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"code\":\"subscription_sharing_usage_limit_exceeded\"}}}\n\n",
                "data: {\"type\":\"response.incomplete\"}\n\n"
            })
            {
                bool rejected = false; using var stream = new MemoryStream(Encoding.UTF8.GetBytes(failingStream));
                try { await ChatGptAuth.ReadResponseStream(stream, deadline.Token); } catch { rejected = true; }
                Require(rejected, "Incomplete/failed stream treated as success.");
            }
            foreach (string language in new[] { "en", "ar", "fr", "es" })
            {
                Settings.Current.Language = language; owner.ApplyTheme();
                var draft = new AiConnectionDraft { Provider = "openai", OpenAiMode = "chatgpt", ChatGptAccount = clientId };
                draft.ChatGpt.Models = models; draft.ChatGpt.Model = models[0].Id; draft.ChatGpt.Fingerprint = ChatGptAuth.Fingerprint(clientId);
                var panel = new AiConnectionPanel(draft, () => { });
                var surface = new Border { Padding = new Thickness(24), Child = panel, FlowDirection = language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
                surface.SetResourceReference(Border.BackgroundProperty, "Bg");
                var host = new Window { Owner = owner, Content = new ScrollViewer { Content = surface, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Width = 760, Height = 790,
                    Title = "Baseera · ChatGPT", Icon = Brand.WindowIcon, FontFamily = Brand.InterfaceFont, FlowDirection = FlowDirection.LeftToRight, ShowInTaskbar = false };
                host.Show(); host.UpdateLayout();
                try
                {
                    Require(!panel.KeyInput.IsVisible && Nodes(panel).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "chatGptContinue").IsVisible, "Account sign-in mode still requires an API key.");
                    panel.Children.OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(Settings.Current.OpenAiAuth == "chatgpt" && Settings.Current.ChatGptAccount == clientId, "Account choice was not saved.");
                    if (language is "en" or "ar")
                    {
                        await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        await Task.Delay(80); WindowAppearance.SavePreview(host, Path.Combine(output, "chatgpt-account-" + language + ".png"));
                        var welcome = ChatGptUi.CreateDialog(owner, "chatGptWelcomeTitle", "chatGptWelcomeBody", false);
                        welcome.Show(); welcome.UpdateLayout();
                        Require(WindowAppearance.HasNativeCaption(welcome, requireMinMax: false), "ChatGPT welcome lost its native frame.");
                        await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        await Task.Delay(80); WindowAppearance.SavePreview(welcome, Path.Combine(output, "chatgpt-welcome-" + language + ".png")); welcome.Close();
                    }
                    panel.Service.SelectedItem = AiProviders.Get("anthropic"); host.UpdateLayout();
                    Require(panel.KeyInput.IsVisible && !Nodes(panel).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "chatGptContinue").IsVisible, "Unsupported provider exposed ChatGPT sign-in.");
                }
                finally { host.Close(); }
            }
            await owner.CheckChatGptSettings(clientId, models, output);
            await ChatGptAuth.MarkWelcomeSeen(clientId, deadline.Token); Require(!ChatGptAuth.NeedsWelcome(clientId), "Welcome confirmation did not persist.");
            bool signedOut = await ChatGptAuth.SignOut(clientId, deadline.Token, http);
            Require(signedOut && revokes == 1 && !ChatGptAuth.Connected(clientId) && ChatGptAuth.Connected(second.Id) && ChatGptAuth.Profiles.Count == 2, "Sign-out affected another account or failed to retain the registration.");
            using var revocationFailure = new HttpClient(new Reply((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.Contains("openid-configuration")
                ? Json(new { issuer = ChatGptAuth.Issuer, authorization_endpoint = ChatGptAuth.Issuer + "/api/accounts/authorize", token_endpoint = ChatGptAuth.Issuer + "/api/accounts/oauth/token", jwks_uri = ChatGptAuth.Issuer + "/.well-known/jwks.json", revocation_endpoint = ChatGptAuth.Issuer + "/api/accounts/oauth/revoke" })
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));
            Require(!await ChatGptAuth.SignOut(second.Id, deadline.Token, revocationFailure) && !ChatGptAuth.Connected(second.Id), "Unconfirmed remote revocation did not clear local credentials.");
            File.WriteAllText(Path.Combine(output, "chatgpt-auth-result.txt"), "PASS: real loopback callback with fake OpenAI HTTP; PKCE/state; signed JWT, audience, nonce and expiry checks; protected identity/token storage; account-scoped model filtering; serialized rotating refresh; image analysis over public Responses/SSE; partial/error streams rejected; separate API key; four-language account UI; unsupported providers keep API mode; welcome persistence; revocation. No browser login, real token or paid inference.");
        }
        finally
        {
            if (originalStore is null) { if (File.Exists(storePath)) File.Delete(storePath); } else File.WriteAllBytes(storePath, originalStore);
            Settings.SetKey("openai", oldKey); File.WriteAllBytes(settingsPath, originalSettings); Settings.Load(); owner.ApplyTheme(); owner.Render();
        }
    }
}

public sealed partial class MainWindow
{
    internal async Task CheckChatGptSettings(string account, IReadOnlyList<AiModelOption> models, string output)
    {
        string provider = aiConnectionDraft.Provider, mode = aiConnectionDraft.OpenAiMode, previousAccount = aiConnectionDraft.ChatGptAccount;
        var previousState = aiConnectionDraft.ChatGpt; string language = Settings.Current.Language;
        try
        {
            aiConnectionDraft.Provider = "openai"; aiConnectionDraft.OpenAiMode = "chatgpt"; aiConnectionDraft.ChatGptAccount = account;
            aiConnectionDraft.ChatGpt = new() { Models = models, Model = models[0].Id, Fingerprint = ChatGptAuth.Fingerprint(account) };
            foreach (string locale in new[] { "en", "ar" })
            {
                Settings.Current.Language = locale; ApplyTheme(); OpenTranslationSettings();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); UpdateLayout();
                if (translationModelInput is null || !translationModelInput.IsKeyboardFocusWithin) throw new InvalidOperationException("OAuth model shortcut did not receive focus.");
                var bounds = translationModelInput.TransformToAncestor(scroller).TransformBounds(new Rect(0, 0, translationModelInput.ActualWidth, translationModelInput.ActualHeight));
                if (bounds.Top < 0 || bounds.Bottom > scroller.ActualHeight) throw new InvalidOperationException("OAuth model shortcut is outside the visible settings viewport.");
                await Task.Delay(100); WindowAppearance.SavePreview(this, Path.Combine(output, "chatgpt-settings-" + locale + ".png"));
            }
        }
        finally
        {
            aiConnectionDraft.Provider = provider; aiConnectionDraft.OpenAiMode = mode; aiConnectionDraft.ChatGptAccount = previousAccount; aiConnectionDraft.ChatGpt = previousState;
            Settings.Current.Language = language; ApplyTheme(); Navigate("home");
        }
    }
}
