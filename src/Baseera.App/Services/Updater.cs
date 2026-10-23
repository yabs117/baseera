using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Baseera.Core;

namespace Baseera.App.Services;

public sealed record InstalledPackage(string Id, string Version, string Source);
public sealed record ReleaseAsset(string Name, Uri Download, long Size);
public sealed record ReleaseInfo(string Version, Uri Page, List<ReleaseAsset> Assets);
public static class Updater
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static async Task<(int Exit, string Output)> Run(string[] args, CancellationToken ct)
    {
        var start = new ProcessStartInfo("winget.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("WinGet unavailable.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
        try { await process.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        return (process.ExitCode, (await stdout) + "\n" + (await stderr));
    }
    public static async Task<List<InstalledPackage>> List(CancellationToken ct)
    {
        string export = Path.Combine(Path.GetTempPath(), "Baseera-winget-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var result = await Run(["export", "--source", "winget", "--output", export, "--include-versions", "--disable-interactivity"], ct);
            if (result.Exit != 0 || !File.Exists(export)) throw new InvalidOperationException(result.Output);
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(export, ct));
            var packages = new List<InstalledPackage>();
            foreach (var source in doc.RootElement.GetProperty("Sources").EnumerateArray())
            {
                string name = source.GetProperty("SourceDetails").GetProperty("Name").GetString() ?? "";
                // Keep identity tied to the known WinGet source. Other package sources need separate support.
                if (name != "winget") continue;
                foreach (var package in source.GetProperty("Packages").EnumerateArray())
                    packages.Add(new(package.GetProperty("PackageIdentifier").GetString()!,
                        package.TryGetProperty("Version", out var version) ? version.GetString() ?? "" : "", name));
            }
            return packages.OrderBy(p => p.Id).ToList();
        }
        finally { if (File.Exists(export)) File.Delete(export); }
    }
    public static async Task<string> UpdateSelected(CancellationToken ct)
    {
        if (!Settings.Current.UpdatesEnabled) return "";
        if (!await Gate.WaitAsync(0, ct)) return I18n.T("working");
        try
        {
            var log = new StringBuilder();
            foreach (string id in Settings.Current.AutoUpdateIds.ToArray())
            {
                if (!Settings.Current.UpdatesEnabled) break;
                if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[A-Za-z0-9_.+-]+$")) continue;
                if (Settings.Current.ManualSources.TryGetValue(id, out var manual))
                {
                    try
                    {
                        var release = await CheckRelease(manual, ct);
                        log.AppendLine(id + " · " + release.Version + " · Manual source requires review: " + release.Page);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or ArgumentException or InvalidOperationException)
                    { log.AppendLine(id + " · " + ex.Message); }
                    continue;
                }
                var result = await Run(["upgrade", "--id", id, "--exact", "--source", "winget", "--silent", "--disable-interactivity"], ct);
                log.AppendLine(id + " · exit " + result.Exit).AppendLine(result.Output);
            }
            Directory.CreateDirectory(Settings.Folder);
            await File.WriteAllTextAsync(Path.Combine(Settings.Folder, "last-updates.log"), log.ToString(), ct);
            return log.Length == 0 ? I18n.T("empty") : log.ToString();
        }
        finally { Gate.Release(); }
    }
    public static async Task<ReleaseInfo> CheckRelease(string url, CancellationToken ct)
    {
        string repo = UpdateSources.GitHubRepository(url) ?? throw new InvalidOperationException(I18n.T("unsupportedSource"));
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Baseera/0.1");
        using var response = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var assets = root.GetProperty("assets").EnumerateArray().Take(100).Select(a => new ReleaseAsset(
            a.GetProperty("name").GetString()!, UpdateSources.ValidateHttps(a.GetProperty("browser_download_url").GetString()!),
            a.GetProperty("size").GetInt64())).ToList();
        return new(root.GetProperty("tag_name").GetString()!, UpdateSources.ValidateHttps(root.GetProperty("html_url").GetString()!), assets);
    }
}
