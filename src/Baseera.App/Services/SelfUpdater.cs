using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Baseera.Core;

namespace Baseera.App.Services;

internal sealed record UpdatePlan(string Target, string OldHash, SelfRelease Release, int ParentId, long ParentStarted);

internal static class SelfUpdater
{
    internal static string CurrentVersion => (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.4.0-preview").Split('+')[0];
    private static HttpClient Client()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Baseera/" + CurrentVersion);
        return client;
    }

    internal static async Task<SelfRelease?> Check(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var client = Client();
        using var response = await client.GetAsync($"https://api.github.com/repos/{SelfRelease.Repository}/releases?per_page=30", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream(); var chunk = new byte[32768]; int count;
        while ((count = await stream.ReadAsync(chunk, timeout.Token)) > 0)
        {
            if (buffer.Length + count > 5 * 1024 * 1024) throw new InvalidDataException("Release response is too large.");
            buffer.Write(chunk, 0, count);
        }
        return SelfRelease.Select(System.Text.Encoding.UTF8.GetString(buffer.ToArray()), CurrentVersion);
    }

    internal static async Task Download(SelfRelease release, string file, IProgress<long> progress, CancellationToken token, HttpClient? transport = null)
    {
        // Reconstruct the expected official URL instead of accepting arbitrary download sources.
        string expected = $"https://github.com/{SelfRelease.Repository}/releases/download/{release.Tag}/Baseera-v{release.Version}-Windows-x64.exe";
        if (release.Download.AbsoluteUri != expected || release.Size <= 0 || release.Size > SelfRelease.MaxBytes) throw new InvalidDataException("Invalid release.");
        using var client = transport ?? Client(); Uri url = release.Download;
        try
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                if (!SelfRelease.AllowedDownload(url)) throw new InvalidDataException("Unexpected update download host.");
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    url = response.Headers.Location is { } location ? new Uri(url, location) : throw new InvalidDataException("Missing redirect.");
                    continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new InvalidDataException("Update length changed.");
                await using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                {
                    var bytes = new byte[65536]; long received = 0; int count;
                    while ((count = await input.ReadAsync(bytes, token)) > 0)
                    {
                        received += count; if (received > release.Size) throw new InvalidDataException("Update exceeds expected length.");
                        await output.WriteAsync(bytes.AsMemory(0, count), token); progress.Report(received);
                    }
                }
                await Task.Run(() => UpdateTransaction.Verify(file, release.Sha256, release.Size), token);
                VerifyVersion(file, release); return;
            }
            throw new HttpRequestException("Too many update redirects.");
        }
        catch { await DeleteDownload(file); throw; }
    }

    internal static async Task DeleteDownload(string file)
    {
        for (int attempt = 0; ; attempt++)
            try { File.Delete(file); return; }
            catch (IOException) when (attempt < 49) { await Task.Delay(100); }
            catch (UnauthorizedAccessException) when (attempt < 49) { await Task.Delay(100); }
    }

    private static void VerifyVersion(string file, SelfRelease release)
    {
        var value = FileVersionInfo.GetVersionInfo(file); var expected = release.Version;
        if (value.FileMajorPart != expected.Major || value.FileMinorPart != expected.Minor || value.FileBuildPart != expected.Build)
            throw new InvalidDataException("The downloaded application version does not match its release.");
        if (value.ProductVersion?.Split('+')[0] != release.Tag.TrimStart('v'))
            throw new InvalidDataException("The downloaded release channel does not match its tag.");
    }

    internal static async Task<string> Prepare(SelfRelease release, IProgress<long> progress, CancellationToken token)
    {
#pragma warning disable IL3000
        if (!string.IsNullOrEmpty(Assembly.GetExecutingAssembly().Location)) throw new InvalidOperationException(I18n.T("selfPackagedOnly"));
#pragma warning restore IL3000
        string target = Environment.ProcessPath ?? throw new IOException("Application path unavailable.");
        // Same-volume staging also checks write permission before downloading. No elevation is requested.
        string folder = Path.Combine(Path.GetDirectoryName(target)!, ".baseera-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            await Download(release, Path.Combine(folder, "payload.exe"), progress, token);
            await Task.Run(() => File.Copy(target, Path.Combine(folder, "helper.exe")), token);
            using var process = Process.GetCurrentProcess();
            var plan = new UpdatePlan(target, UpdateTransaction.Hash(target), release, process.Id, process.StartTime.ToUniversalTime().Ticks);
            File.WriteAllText(Path.Combine(folder, "plan.json"), JsonSerializer.Serialize(plan));
            return folder;
        }
        catch { Directory.Delete(folder, true); throw; }
    }

    internal static async Task StartHelper(string folder)
    {
        var start = new ProcessStartInfo(Path.Combine(folder, "helper.exe")) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(Path.Combine(folder, "plan.json"));
        using var helper = Process.Start(start) ?? throw new IOException("Could not start update helper.");
        for (int i = 0; i < 150; i++)
        {
            if (File.Exists(Path.Combine(folder, "ready"))) return;
            if (helper.HasExited) throw new IOException("Update helper could not prepare installation.");
            await Task.Delay(100);
        }
        helper.Kill(true); throw new TimeoutException("Update helper did not respond. The current app remains open.");
    }

    private static UpdatePlan ReadPlan(string planFile, bool helper)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(planFile))!;
        if (Path.GetFileName(planFile) != "plan.json" || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(folder), @"^\.baseera-update-[a-f0-9]{32}$")) throw new IOException("Invalid update transaction.");
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planFile)) ?? throw new IOException("Missing update plan.");
        string target = Path.GetFullPath(plan.Target);
        string process = Path.GetFullPath(Environment.ProcessPath!);
        if (!string.Equals(Path.GetDirectoryName(target), Directory.GetParent(folder)!.FullName, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(process, helper ? Path.Combine(folder, "helper.exe") : target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update target does not match this application.");
        return plan;
    }

    internal static async Task<int> Apply(string planFile)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(planFile))!; UpdatePlan? plan = null; bool replaced = false; bool parentExited = false;
        string backup = Path.Combine(folder, "previous.exe");
        try
        {
            plan = ReadPlan(planFile, true);
            if (UpdateTransaction.Hash(Environment.ProcessPath!) != plan.OldHash) throw new IOException("Update helper integrity check failed.");
            using var parent = Process.GetProcessById(plan.ParentId);
            if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStarted || !string.Equals(parent.MainModule?.FileName, plan.Target, StringComparison.OrdinalIgnoreCase)) throw new IOException("Update parent process does not match.");
            UpdateTransaction.Verify(Path.Combine(folder, "payload.exe"), plan.Release.Sha256, plan.Release.Size);
            VerifyVersion(Path.Combine(folder, "payload.exe"), plan.Release);
            File.WriteAllText(Path.Combine(folder, "ready"), "ready");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90)); await parent.WaitForExitAsync(timeout.Token); parentExited = true;
            for (int attempt = 0; ; attempt++)
            {
                try { UpdateTransaction.Replace(Path.Combine(folder, "payload.exe"), plan.Target, backup, plan.OldHash, plan.Release.Sha256, plan.Release.Size); replaced = true; break; }
                catch (IOException) when (attempt < 39) { await Task.Delay(500); }
            }
            using var next = Launch(plan.Target, planFile);
            for (int i = 0; i < 600; i++)
            {
                if (File.Exists(Path.Combine(folder, "started")))
                {
                    // Cleanup failure is not an installation failure: the new app is already running.
                    try { File.Delete(backup); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    File.WriteAllText(Path.Combine(folder, "done"), "updated"); return 0;
                }
                if (next.HasExited) throw new IOException("Updated application exited before its window loaded.");
                await Task.Delay(100);
            }
            // Release the new image lock before restoring the backup.
            next.Kill(true); await next.WaitForExitAsync(); throw new TimeoutException("Updated application did not finish starting.");
        }
        catch (Exception ex)
        {
            if (plan is not null && parentExited)
            {
                try
                {
                    if (replaced)
                    {
                        if (UpdateTransaction.Hash(backup) != plan.OldHash) throw new IOException("Backup integrity check failed; recovery files retained.");
                        for (int attempt = 0; ; attempt++)
                            try { UpdateTransaction.Rollback(plan.Target, backup, plan.Release.Sha256); break; }
                            catch (IOException) when (attempt < 19) { await Task.Delay(500); }
                    }
                    if (UpdateTransaction.Hash(plan.Target) != plan.OldHash) throw new IOException("Previous application no longer matches its backup.");
                    File.WriteAllText(Path.Combine(folder, "error.txt"), ex.Message);
                    using var restored = Launch(plan.Target, planFile);
                }
                catch (Exception recovery) { File.WriteAllText(Path.Combine(folder, "error.txt"), ex.Message + "\n" + recovery.Message + "\nBackup: " + backup); }
            }
            File.WriteAllText(Path.Combine(folder, "done"), "failed"); return 1;
        }
    }

    private static Process Launch(string target, string plan)
    {
        var start = new ProcessStartInfo(target) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(target)! };
        start.ArgumentList.Add("--update-result"); start.ArgumentList.Add(plan);
        return Process.Start(start) ?? throw new IOException("Application could not restart.");
    }

    internal static string? Acknowledge(string planFile)
    {
        var plan = ReadPlan(planFile, false); string folder = Path.GetDirectoryName(planFile)!;
        string? error = File.Exists(Path.Combine(folder, "error.txt")) ? File.ReadAllText(Path.Combine(folder, "error.txt")) : null;
        if (error is null) { UpdateTransaction.Verify(plan.Target, plan.Release.Sha256, plan.Release.Size); File.WriteAllText(Path.Combine(folder, "started"), "started"); }
        // Cleanup only this validated transaction, after the helper has released its own executable.
        _ = Task.Run(async () =>
        {
            bool finished = false;
            for (int i = 0; i < 300; i++)
            {
                await Task.Delay(500);
                finished |= File.Exists(Path.Combine(folder, "done"));
                if (!finished) continue;
                // Preserve backup/error evidence when recovery failed.
                if (File.Exists(Path.Combine(folder, "previous.exe"))) return;
                try { Directory.Delete(folder, true); return; } catch (DirectoryNotFoundException) { return; } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
        return error;
    }
}
