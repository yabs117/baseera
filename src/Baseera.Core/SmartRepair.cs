using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Baseera.Core;

public sealed record DiagnosticOptions(bool SystemInfo = false, bool ApplicationEvents = false, bool SystemEvents = false, int Hours = 24, string MatchText = "", bool DeviceHealth = false, bool ServiceStatus = false)
{
    public void Validate()
    {
        if ((!SystemInfo && !ApplicationEvents && !SystemEvents && !DeviceHealth && !ServiceStatus) || Hours is < 1 or > 72)
            throw new ArgumentException("Select a diagnostic source and a time range between 1 and 72 hours.");
        if (MatchText is null || MatchText.Length > 100 || MatchText.Any(char.IsControl)) throw new ArgumentException("Use a literal event filter of at most 100 characters without control characters.");
    }
}

public static class DiagnosticPrivacy
{
    public const int MaxCharacters = 24000;
    public static string Sanitize(string value, string? userName = null, string? machineName = null)
    {
        string Limit(string text, int max) => text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];
        value = Limit(value, MaxCharacters);
        string Replace(string pattern, string replacement) => Regex.Replace(value, pattern, replacement,
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(150));
        try
        {
            value = Replace(@"\b[A-Z]:\\Users\\[^\\\r\n""<>]+", @"C:\Users\[user]");
            value = Replace(@"(https?://)[^/\s:@]+:[^/\s@]+@", "$1[credentials]@");
            value = Replace(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[email]");
            value = Replace(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[IP address]");
            value = Replace(@"(?<![\w:])(?:(?:[0-9a-f]{1,4}:){7}[0-9a-f]{1,4}|(?:[0-9a-f]{0,4}:){1,7}:[0-9a-f:]{0,28})(?![\w:])", "[IPv6 address]");
            value = Replace(@"\bsk-[A-Z0-9_-]{10,}", "[API key]");
            value = Replace(@"\bBearer\s+[A-Z0-9._~+/-]+=*", "Bearer [redacted]");
            value = Replace(@"([?&](?:key|token|secret|password|sig|code)=)[^&#\s]+", "$1[redacted]");
            value = Replace(@"\b(api[_ -]?key|access[_ -]?token|refresh[_ -]?token|client[_ -]?secret|password|authorization)[""']?\s*[:=]\s*[^\r\n,;]+", "$1=[redacted]");
            foreach (string? identity in new[] { userName, machineName })
                if (!string.IsNullOrWhiteSpace(identity)) value = Replace(@"(?<!\w)" + Regex.Escape(identity) + @"(?!\w)", "[local identity]");
            value = new string(value.Where(c => (!char.IsControl(c) || c is '\r' or '\n' or '\t') && c is not (>= '\u202a' and <= '\u202e') and not (>= '\u2066' and <= '\u2069')).ToArray());
            return Limit(value, MaxCharacters);
        }
        catch (RegexMatchTimeoutException) { return "[Diagnostic text omitted because automatic redaction could not finish. Collect a smaller time range.]"; }
    }
}

public sealed record RepairAction(string Id, string Executable, IReadOnlyList<string> Arguments, bool Administrator, bool Network, bool LongRunning)
{
    public string Command => Executable + " " + string.Join(" ", Arguments);
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Id + "\n" + Command)));
}

public static class RepairCatalog
{
    // Executable names and arguments are application-owned. Model text is never executable input.
    public static IReadOnlyList<RepairAction> All { get; } = Array.AsReadOnly(new[]
    {
        new RepairAction("flush-dns", "ipconfig.exe", Array.AsReadOnly(new[] { "/flushdns" }), false, false, false),
        new RepairAction("sfc-repair", "sfc.exe", Array.AsReadOnly(new[] { "/scannow" }), true, false, true),
        new RepairAction("dism-repair", "Dism.exe", Array.AsReadOnly(new[] { "/Online", "/Cleanup-Image", "/RestoreHealth", "/NoRestart" }), true, true, true)
    });
    public static RepairAction Get(string id) => All.SingleOrDefault(x => x.Id == id) ?? throw new ArgumentException("Unknown repair action.");
    public static IReadOnlyList<string> ValidateRecommendations(IEnumerable<string> ids)
    {
        var values = ids.Take(4).ToArray();
        if (values.Length > 3 || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
            throw new InvalidDataException("Invalid repair recommendations.");
        foreach (string id in values) if (!All.Any(action => action.Id == id)) throw new InvalidDataException("Unknown repair recommendation.");
        return Array.AsReadOnly(values);
    }
}

public sealed class RepairApproval
{
    private readonly string id, fingerprint;
    private readonly DateTimeOffset issued;
    private int consumed;
    private RepairApproval(RepairAction action, DateTimeOffset issued)
    { id = action.Id; fingerprint = action.Fingerprint; this.issued = issued; }
    public static RepairApproval Confirm(string actionId, bool acknowledged, DateTimeOffset? now = null)
    {
        if (!acknowledged) throw new InvalidOperationException("Explicit approval is required.");
        return new(RepairCatalog.Get(actionId), now ?? DateTimeOffset.UtcNow);
    }
    public RepairAction Consume(DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        if (Interlocked.Exchange(ref consumed, 1) != 0 || current < issued || current - issued > TimeSpan.FromMinutes(5))
            throw new InvalidOperationException("Approval expired or was already used.");
        var action = RepairCatalog.Get(id);
        if (action.Fingerprint != fingerprint) throw new InvalidOperationException("The approved command changed.");
        return action;
    }
}
