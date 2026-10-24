using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Baseera.Core;

internal static class StreamingScanChecks
{
    private sealed class Progress(Action<ScanProgress> action) : IProgress<ScanProgress>
    { public void Report(ScanProgress value) => action(value); }

    internal static void Run(Action<bool, string> check, string root)
    {
        string path = Path.Combine(root, "large-content.bin");
        const long size = 72L * 1024 * 1024;
        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            file.SetLength(size);
            void Put(long offset, byte[] data) { file.Position = offset; file.Write(data); }
            Put(512 + 256 * 1024 - 12, Encoding.UTF8.GetBytes("powershell -EncodedCommand QUJDREVGR0hJSktMTU5PUA=="));
            Put(17 * 1024 * 1024 + 1, Encoding.Unicode.GetBytes("Set-MpPreference -DisableRealtimeMonitoring"));
            Put(33 * 1024 * 1024 - 9, Encoding.BigEndianUnicode.GetBytes("CreateRemoteThread"));
            Put(size - 100, Encoding.UTF8.GetBytes("Invoke-WebRequest example.invalid; Start-Process payload"));
        }
        long lastRead = 0; int events = 0; bool bounded = true;
        var report = new ModScanner().Scan(path, progress: new Progress(p => { bounded &= p.ReadBytes >= lastRead && p.ReadBytes <= size; lastRead = p.ReadBytes; events++; }));
        check(bounded, "Streaming progress is monotonic and bounded");
        check(!report.Incomplete && report.Files.Single().Size == size && lastRead == size && events > 0, "Default scan reads a file larger than the old 64 MiB limit");
        using (var file = File.OpenRead(path))
            check(report.Files.Single().Sha256 == Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(), "Large file SHA-256 covers every byte");
        check(report.Findings.Any(f => f.Code == "encoded-powershell") && report.Findings.Any(f => f.Code == "defender-setting-change") &&
            report.Findings.Any(f => f.Code == "process-injection-api") && report.Findings.Any(f => f.Code == "download-and-execute"),
            "Streaming finds boundary-spanning, middle, UTF-16 and final-byte evidence");
        using (var canceled = new CancellationTokenSource())
        {
            bool stopped = false;
            try { new ModScanner().Scan(path, canceled.Token, new Progress(p => { if (p.ReadBytes > 1024 * 1024) canceled.Cancel(); })); }
            catch (OperationCanceledException) { stopped = true; }
            check(stopped, "Large scan can be canceled during reading");
        }
        check(new ModScanner(new ScanLimits(MaxTotalBytes: 1024)).Scan(path).Incomplete, "Streaming retains the total-byte budget");

        var outer = Path.Combine(root, "nested-budget.zip");
        using (var file = File.Create(outer))
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
        {
            using var stream = zip.CreateEntry("inside.zip", CompressionLevel.NoCompression).Open();
            using var inner = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
            using var data = inner.CreateEntry("plain.bin", CompressionLevel.NoCompression).Open(); data.Write(new byte[2048]);
        }
        var nested = new ModScanner(new ScanLimits(MaxNestedArchiveBytes: 1024)).Scan(outer);
        check(nested.Incomplete && nested.Findings.Any(f => f.Evidence.Contains("indexing limit")), "Nested archive indexing remains memory-bounded");
    }
}
