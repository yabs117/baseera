using System.Security.Cryptography;
using System.Text.Json;
using Baseera.Core;

internal static class SelfUpdateChecks
{
    internal static void Run(Action<bool, string> check, string root)
    {
        string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var hash = new string('a', 64);
        object Release(string tag, string? digest = null, string? url = null, string state = "uploaded", bool draft = false, long size = 123) => new
        {
            tag_name = tag, draft, body = "Release notes", assets = new[] { new { name = $"Baseera-v{SelfRelease.ParseVersion(tag)}-Windows-x64.exe", state, size, digest = digest ?? "sha256:" + hash,
                browser_download_url = url ?? $"https://github.com/yabs117/baseera/releases/download/{tag}/Baseera-v{SelfRelease.ParseVersion(tag)}-Windows-x64.exe" } }
        };
        SelfRelease? Select(params object[] releases) => SelfRelease.Select(JsonSerializer.Serialize(releases), "0.4.0-preview");
        check(Select(Release("v0.3.1-preview")) is null, "Self update never downgrades");
        check(Select(Release("v0.5.0-preview"), Release("v0.4.1-preview"))?.Tag == "v0.5.0-preview", "Self update selects highest numeric version, including previews");
        check(Select(Release("v0.4.0"))?.Tag == "v0.4.0", "Stable release supersedes same-version preview");
        check(SelfRelease.IsNewer("0.4.0-preview.2", "v0.4.0-preview.10") && !SelfRelease.IsNewer("0.4.0", "v0.4.0-preview"), "Preview counters and stable ordering");
        check(SelfRelease.ParseVersion("v0.5.0-preview/evil") is null && SelfRelease.ParseVersion("v0.5.0+payload") is null, "Unexpected tags are rejected");
        check(Select(Release("v0.5.0-preview", digest: "sha256:bad")) is null, "Missing or malformed digest prevents update offer");
        check(Select(Release("v0.5.0-preview", state: "new")) is null && Select(Release("v0.5.0-preview", draft: true)) is null, "Partial uploads and drafts are not offered");
        check(Select(Release("v0.5.0-preview", url: "https://github.com/other/repo/payload.exe")) is null, "Update asset must belong to the exact official repository");
        check(Select(Release("v0.5.0-preview", size: SelfRelease.MaxBytes + 1)) is null, "Update size is bounded before download");
        check(!SelfRelease.AllowedDownload(new Uri("https://github.com.evil.invalid/payload")) && !SelfRelease.AllowedDownload(new Uri("http://github.com/payload")) && !SelfRelease.AllowedDownload(new Uri("https://user@github.com/payload")) &&
            SelfRelease.AllowedDownload(new Uri("https://release-assets.githubusercontent.com/asset?token=example")), "Redirect policy rejects lookalikes, HTTP and credentials");
        string target = Path.Combine(root, "current.exe"), payload = Path.Combine(root, "payload.exe"), backup = Path.Combine(root, "previous.exe");
        byte[] oldBytes = [1, 2, 3], newBytes = [4, 5, 6, 7]; File.WriteAllBytes(target, oldBytes); File.WriteAllBytes(payload, newBytes);
        bool rejected = false;
        try { UpdateTransaction.Replace(payload, target, backup, Digest(oldBytes), hash, 4); } catch (InvalidDataException) { rejected = true; }
        check(rejected && File.ReadAllBytes(target).SequenceEqual(oldBytes) && !File.Exists(backup), "Bad checksum leaves current app untouched");
        rejected = false;
        try { UpdateTransaction.Replace(payload, target, backup, hash, Digest(newBytes), 4); } catch (IOException) { rejected = true; }
        check(rejected && File.Exists(payload), "Changed current executable aborts installation");
        UpdateTransaction.Replace(payload, target, backup, Digest(oldBytes), Digest(newBytes), 4);
        check(File.ReadAllBytes(target).SequenceEqual(newBytes) && File.ReadAllBytes(backup).SequenceEqual(oldBytes), "Atomic replacement retains the exact previous executable");
        UpdateTransaction.Rollback(target, backup, Digest(newBytes));
        check(File.ReadAllBytes(target).SequenceEqual(oldBytes) && !File.Exists(backup), "Rollback restores the same file path and original bytes");
        File.WriteAllBytes(payload, newBytes); UpdateTransaction.Replace(payload, target, backup, Digest(oldBytes), Digest(newBytes), 4); File.WriteAllBytes(target, [9]); rejected = false;
        try { UpdateTransaction.Rollback(target, backup, Digest(newBytes)); } catch (IOException) { rejected = true; }
        check(rejected && File.Exists(backup) && File.ReadAllBytes(target).SequenceEqual(new byte[] { 9 }), "Rollback preserves backup if another process changed the target");
    }
}
