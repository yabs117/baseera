using System.Text.RegularExpressions;

namespace Baseera.Core;

public static partial class UpdateSources
{
    public static Uri ValidateHttps(string input)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || uri.IsLoopback)
            throw new ArgumentException("Use a public HTTPS URL without credentials.");
        return uri;
    }
    public static string? GitHubRepository(string input)
    {
        var uri = ValidateHttps(input);
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 3 || parts[2] != "releases" ||
            !parts.Take(2).All(p => RepoPart().IsMatch(p))) return null;
        return parts[0] + "/" + parts[1];
    }
    public static bool IsNewer(string installed, string available)
    {
        static Version? Parse(string s) => Version.TryParse(s.Trim().TrimStart('v', 'V'), out var v) ? v : null;
        var a = Parse(installed); var b = Parse(available);
        return a is not null && b is not null && b > a;
    }
    [GeneratedRegex("^[a-zA-Z0-9_.-]+$")]
    private static partial Regex RepoPart();
}
