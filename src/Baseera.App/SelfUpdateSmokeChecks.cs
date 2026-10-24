using System.Net;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal static class SelfUpdateSmokeChecks
{
    private sealed class Reply(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(respond(request, token));
    }
    private sealed class Progress(Action<long> receive) : IProgress<long> { public void Report(long value) => receive(value); }

    internal static async Task Run(string output)
    {
        string folder = Path.Combine(output, "update-checks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        byte[] image = File.ReadAllBytes(Environment.ProcessPath!);
        string hash = UpdateTransaction.Hash(Environment.ProcessPath!);
        string tag = "v" + SelfUpdater.CurrentVersion; var version = SelfRelease.ParseVersion(tag)!;
        var release = new SelfRelease(tag, version, "", new Uri($"https://github.com/yabs117/baseera/releases/download/{tag}/Baseera.exe"), image.Length, hash);
        string file = Path.Combine(folder, "download.exe"); long reported = 0;
        HttpClient Response(byte[] bytes) => new(new Reply((_, _) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
        await SelfUpdater.Download(release, file, new Progress(x => reported = x), CancellationToken.None, Response(image));
        if (reported != image.Length || UpdateTransaction.Hash(file) != hash) throw new Exception("Verified update download failed."); await SelfUpdater.DeleteDownload(file);
        await SelfUpdater.Download(release with { Download = new Uri($"https://github.com/yabs117/baseera/releases/download/{tag}/Baseera-v{version}-Windows-x64.exe") }, file, new Progress(_ => { }), CancellationToken.None, Response(image));
        if (UpdateTransaction.Hash(file) != hash) throw new Exception("Legacy filename download compatibility failed."); await SelfUpdater.DeleteDownload(file);
        bool failed = false;
        try { await SelfUpdater.Download(release with { Sha256 = new string('0', 64) }, file, new Progress(_ => { }), CancellationToken.None, Response(image)); }
        catch (InvalidDataException) { failed = true; }
        if (!failed || File.Exists(file)) throw new Exception("Corrupt update was retained.");
        failed = false;
        using var truncated = new HttpClient(new Reply((_, _) => { var content = new ByteArrayContent(image[..(image.Length / 2)]); content.Headers.ContentLength = image.Length; return new(HttpStatusCode.OK) { Content = content }; }));
        try { await SelfUpdater.Download(release, file, new Progress(_ => { }), CancellationToken.None, truncated); }
        catch (InvalidDataException) { failed = true; }
        if (!failed || File.Exists(file)) throw new Exception("Partial update was retained.");
        int calls = 0; failed = false;
        using var redirect = new HttpClient(new Reply((_, _) => { calls++; var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://evil.invalid/app.exe"); return response; }));
        try { await SelfUpdater.Download(release, file, new Progress(_ => { }), CancellationToken.None, redirect); }
        catch (InvalidDataException) { failed = true; }
        if (!failed || calls != 1 || File.Exists(file)) throw new Exception("Unsafe update redirect was followed.");
        failed = false; using var cancel = new CancellationTokenSource();
        try { await SelfUpdater.Download(release, file, new Progress(_ => cancel.Cancel()), cancel.Token, Response(image)); }
        catch (OperationCanceledException) { failed = true; }
        if (!failed || File.Exists(file)) throw new Exception("Canceled update was retained.");
        Directory.Delete(folder, true);
    }
}
