using System.Text.RegularExpressions;

namespace Baseera.Core;

public sealed record InstalledAppEntry(string Name, string Version, string Publisher, string DisplayIcon, string InstallLocation, string PackageId = "", string RegistryKey = "", string AppUserModelId = "");

public static class InstalledAppIdentity
{
    private static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    private static string Name(InstalledAppEntry app)
    {
        string value = Regex.Replace(app.Name, @"\((?:x64|x86|arm64|32-bit|64-bit|user|machine|system)[^)]*\)", "", RegexOptions.IgnoreCase);
        if (!string.IsNullOrWhiteSpace(app.Version)) value = Regex.Replace(value, @"(?<![\p{L}\p{N}])" + Regex.Escape(app.Version) + @"(?![\p{L}\p{N}])", "", RegexOptions.IgnoreCase);
        return Normalize(value);
    }
    public static InstalledAppEntry? Match(string id, string version, IEnumerable<InstalledAppEntry> entries)
    {
        var all = entries.ToArray();
        var explicitMatches = all.Where(a => a.PackageId.Equals(id, StringComparison.OrdinalIgnoreCase) || a.RegistryKey.Equals(id, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (explicitMatches.Length > 0) return Unique(explicitMatches, version);
        string[] parts = id.Split('.');
        if (parts.Length > 2 && new[] { "LTS", "x64", "x86", "arm64", "User", "Machine" }.Contains(parts[^1], StringComparer.OrdinalIgnoreCase)) parts = parts[..^1];
        var names = new[] { Normalize(string.Join("", parts)), Normalize(string.Join("", parts.Skip(1))) }.Where(n => n.Length >= 3).ToHashSet();
        return Unique(all.Where(a => names.Contains(Name(a))).ToArray(), version);
    }
    private static InstalledAppEntry? Unique(InstalledAppEntry[] candidates, string version)
    {
        if (candidates.Length == 0) return null;
        var exactVersion = candidates.Where(a => a.Version.Equals(version, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exactVersion.Length > 0) candidates = exactVersion;
        // Never choose the first fuzzy/ambiguous registry entry and show another app's icon.
        var distinct = candidates.DistinctBy(a => (a.Name.ToLowerInvariant(), a.Version, a.Publisher.ToLowerInvariant(), a.DisplayIcon.ToLowerInvariant(), a.InstallLocation.ToLowerInvariant(), a.AppUserModelId)).ToArray();
        return distinct.Length == 1 ? distinct[0] : null;
    }
    public static (string Path, int Index)? ParseIconLocation(string value)
    {
        value = Environment.ExpandEnvironmentVariables(value.Trim());
        int index = 0; var suffix = Regex.Match(value, @",\s*(-?\d+)\s*$");
        if (suffix.Success)
        {
            if (!int.TryParse(suffix.Groups[1].Value, out index)) return null;
            value = value[..suffix.Index].Trim();
        }
        value = value.Trim('"');
        if (!Path.IsPathFullyQualified(value) || value.StartsWith(@"\\") || value.StartsWith("//") || value.StartsWith('@') || value.Contains('"')) return null;
        if (!new[] { ".exe", ".dll", ".ico", ".png", ".bmp" }.Contains(Path.GetExtension(value), StringComparer.OrdinalIgnoreCase)) return null;
        return (value, index);
    }
}
