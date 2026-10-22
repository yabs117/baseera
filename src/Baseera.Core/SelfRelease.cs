using System.Text.Json;
using System.Text.RegularExpressions;

namespace Baseera.Core;

public sealed record SelfRelease(string Tag, Version Version, string Notes, Uri Download, long Size, string Sha256)
{
    public const long MaxBytes = 512L * 1024 * 1024;
    public const string Repository = "yabs117/baseera";
    public static Version? ParseVersion(string tag) => Regex.Match(tag, @"^v?(\d+\.\d+\.\d+)(?:-preview(?:\.\d+)?)?$") is { Success: true } match &&
        Version.TryParse(match.Groups[1].Value, out var value) ? value : null;

    // Numeric preview versions are the release channel used by this project. A stable
    // release of the same numeric version also supersedes its preview.
    public static bool IsNewer(string current, string candidate)
    {
        var left = ParseVersion(current); var right = ParseVersion(candidate);
        if (left is null || right is null) return false;
        if (right != left) return right > left;
        bool before = current.Contains("-preview"), after = candidate.Contains("-preview");
        if (before != after) return before;
        static int Counter(string value) => int.TryParse(value.Split("-preview.").Last(), out int n) ? n : 0;
        return before && Counter(candidate) > Counter(current);
    }

    public static SelfRelease? Select(string json, string current)
    {
        using var document = JsonDocument.Parse(json);
        SelfRelease? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            var version = ParseVersion(tag);
            if (version is null || !IsNewer(current, tag) || (best is not null && !IsNewer(best.Tag, tag))) continue;
            string name = $"Baseera-v{version}-Windows-x64.exe";
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != name || asset.GetProperty("state").GetString() != "uploaded") continue;
                long size = asset.GetProperty("size").GetInt64();
                string digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
                string expected = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
                if (size <= 0 || size > MaxBytes || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$") ||
                    asset.GetProperty("browser_download_url").GetString() != expected) continue;
                string notes = release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
                best = new(tag, version, notes[..Math.Min(notes.Length, 20000)], new Uri(expected), size, digest[7..].ToLowerInvariant());
            }
        }
        return best;
    }

    public static bool AllowedDownload(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");
}
