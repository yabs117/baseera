using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Baseera.App.Services;

internal sealed class ChatGptException : InvalidOperationException
{
    internal string Key { get; }
    internal int? HttpStatus { get; }
    internal string ErrorCode { get; }
    internal string RequestId { get; }
    internal string BodyShape { get; }
    internal string ErrorParameter { get; }
    internal ChatGptException(string key, int? status = null, string code = "", string requestId = "", string shape = "", string parameter = "")
        : base(I18n.T(key) + (status is null ? "" : $" HTTP {status}") + (Safe(code).Length == 0 ? "" : " · " + Safe(code)) + (Safe(requestId).Length == 0 ? "" : " · " + Safe(requestId)))
    { Key = key; HttpStatus = status; ErrorCode = Safe(code); RequestId = Safe(requestId); BodyShape = shape; ErrorParameter = Safe(parameter); }
    private static string Safe(string text) => text.Length <= 160 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? text : "";
}

internal sealed class ChatGptAccount
{
    public string ClientId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string IdToken { get; set; } = "";
    public string[] Scopes { get; set; } = [];
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset EarliestRefreshAt { get; set; }
    public bool WelcomeSeen { get; set; }
    internal bool HasPermission => Scopes.Contains("chatgpt.tokens.use.direct") && Scopes.Contains("resource.invoke");
    internal bool CanResume => HasPermission && (AccessToken.Length > 0 && ExpiresAt > DateTimeOffset.UtcNow || RefreshToken.Length > 0);
    internal ChatGptProfile Profile => new(ClientId, Email.Length > 0 ? Email : Name.Length > 0 ? Name : I18n.T("chatGptAccount"), CanResume);
    public override string ToString() => Profile.ToString();
}
internal sealed record ChatGptProfile(string Id, string Name, bool Connected)
{
    public override string ToString() => Name + " · " + Id[^Math.Min(6, Id.Length)..];
}
internal sealed class ChatGptStore
{
    public string HostId { get; set; } = "";
    public List<ChatGptAccount> Accounts { get; set; } = [];
}

internal static partial class ChatGptAuth
{
    internal const string Issuer = "https://auth.openai.com";
    internal const string Resource = "https://api.openai.com/v1";
    internal const string Scope = "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct";
    private const string StoreFile = "chatgpt-accounts.bin";
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly Dictionary<string, CancellationTokenSource> sessions = [];
    internal static bool Selected => AiCatalog.Provider == "openai" && Settings.Current.OpenAiAuth == "chatgpt";
    internal static IReadOnlyList<ChatGptProfile> Profiles => ReadStore().Accounts.Select(account => account.Profile).ToArray();
    internal static bool Connected(string clientId) => ReadStore().Accounts.Any(account => account.ClientId == clientId && account.CanResume);
    internal static string Fingerprint(string clientId) => "chatgpt:" + clientId;
    private static ChatGptStore ReadStore()
    {
        if (!File.Exists(Path.Combine(Settings.Folder, StoreFile))) return new();
        byte[] bytes = Settings.ReadProtected(StoreFile);
        try { return JsonSerializer.Deserialize<ChatGptStore>(bytes) ?? throw new InvalidDataException(I18n.T("chatGptStorageError")); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static void WriteStore(ChatGptStore store) => Settings.WriteProtected(StoreFile, JsonSerializer.SerializeToUtf8Bytes(store));
    private static async Task<FileStream> LockStore(CancellationToken ct)
    {
        Directory.CreateDirectory(Settings.Folder);
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(Settings.Folder, "chatgpt-session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 400) { await Task.Delay(100, ct); }
        }
    }
    private static CancellationToken SessionToken(string clientId)
    {
        lock (sessions)
        {
            if (!sessions.TryGetValue(clientId, out var source)) sessions[clientId] = source = new();
            return source.Token;
        }
    }
    private static void StopSession(string clientId, bool restart = false)
    {
        lock (sessions)
        {
            if (sessions.TryGetValue(clientId, out var source)) source.Cancel();
            if (restart) sessions[clientId] = new();
        }
    }
    internal static async Task MarkWelcomeSeen(string clientId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var fileLock = await LockStore(ct); var store = ReadStore();
            var account = store.Accounts.Single(account => account.ClientId == clientId);
            account.WelcomeSeen = true; WriteStore(store);
        }
        finally { gate.Release(); }
    }
    internal static bool NeedsWelcome(string clientId) => ReadStore().Accounts.Any(account => account.ClientId == clientId && account.HasPermission && !account.WelcomeSeen);
    private static string Required(JsonElement root, string name) => root.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
    private static string Optional(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static byte[] FromBase64Url(string text)
    {
        if (text.Length > 100_000 || text.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw new InvalidDataException(I18n.T("chatGptInvalidResponse"));
        return Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
    }
    internal static bool Equal(string left, string right) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(left)), SHA256.HashData(Encoding.UTF8.GetBytes(right)));
}
