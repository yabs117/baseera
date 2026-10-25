using System.IO.Compression;
using System.Text;
using Baseera.Core;

var root = Path.Combine(Path.GetTempPath(), "baseera-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
byte[] Zip(params (string Name, byte[] Bytes)[] files)
{
    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        foreach (var (name, bytes) in files) { using var entry = archive.CreateEntry(name).Open(); entry.Write(bytes); }
    return stream.ToArray();
}
ScanReport Scan(byte[] bytes, string name = "input.bin", ScanLimits? limits = null)
{
    string file = Path.Combine(root, name); File.WriteAllBytes(file, bytes); return new ModScanner(limits).Scan(file);
}
try
{
    var clean = Scan(Encoding.UTF8.GetBytes("A harmless mod configuration: speed=2"));
    Check(clean.Verdict == "no-indicators" && clean.Files.Count == 1, "Benign content has no rule matches");
    Check(clean.Files[0].Sha256.Length == 64, "SHA-256 available");
    var renamed = Scan(Encoding.UTF8.GetBytes("powershell -EncodedCommand QUJDREVGR0hJSktMTU5PUA=="), "picture.png");
    Check(renamed.Findings.Any(f => f.Code == "encoded-powershell"), "Inspect content regardless of extension");
    var nested = Scan(Zip(("mod/inside.zip", Zip(("script.ps1", Encoding.UTF8.GetBytes("Set-MpPreference -DisableRealtimeMonitoring $true"))))));
    Check(nested.Files.Count == 3 && nested.Findings.Any(f => f.Code == "defender-setting-change"), "Inspect nested ZIP and inner script");
    var utf16 = Scan(Encoding.Unicode.GetBytes("Invoke-WebRequest example.invalid; Start-Process payload"));
    Check(utf16.Findings.Any(f => f.Code == "download-and-execute"), "Inspect UTF-16 script strings");
    var traversal = Scan(Zip(("../../escape.txt", Encoding.UTF8.GetBytes("plain"))));
    Check(traversal.Findings.Any(f => f.Code == "archive-path") && !File.Exists(Path.Combine(root, "escape.txt")), "Traversal entry is reported, never extracted");
    var bomb = Scan(Zip(("huge.txt", new byte[2000000])));
    Check(bomb.Incomplete && bomb.Files.Count == 1, "Compression ratio budget blocks expansion");
    var depth = Scan(Zip(("inner.zip", Zip(("x", new byte[] { 1 })))), limits: new ScanLimits(MaxDepth: 1));
    Check(depth.Incomplete, "Depth exhaustion is explicit");
    var count = Scan(Zip(("a", new byte[] { 1 }), ("b", new byte[] { 2 })), limits: new ScanLimits(MaxEntries: 2));
    Check(count.Incomplete, "Entry exhaustion is explicit");
    var large = Scan(new byte[100], limits: new ScanLimits(MaxFileBytes: 20));
    Check(large.Incomplete && large.Files.Count == 0, "Oversized root is not labeled clean");
    var rar = Scan(Encoding.ASCII.GetBytes("Rar!unsupported-data"));
    Check(rar.Incomplete, "Unsupported archive is not labeled clean");
    var malformed = Scan(new byte[] { 0x50, 0x4b, 3, 4, 1 });
    Check(malformed.Incomplete, "Corrupt ZIP is not labeled clean");
    var exe = Scan(Encoding.ASCII.GetBytes("MZnot-a-valid-pe"));
    Check(exe.Incomplete, "Malformed PE is not labeled clean");
    Check(UpdateSources.GitHubRepository("https://github.com/example/project/releases") == "example/project", "Release page maps to exact repository");
    Check(UpdateSources.GitHubRepository("https://github.com.evil.invalid/example/project/releases") is null, "Lookalike host is not trusted");
    bool rejected = false; try { UpdateSources.ValidateHttps("file:///C:/Windows/test.exe"); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Local file URLs rejected");
    rejected = false; try { UpdateSources.ValidateHttps("https://user:password@example.com/releases"); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Credentials in update URL rejected");
    Check(UpdateSources.IsNewer("1.0.0", "v1.1.0") && !UpdateSources.IsNewer("2.0.0", "1.9.9"), "Version ordering");
    var canceled = new CancellationToken(true); rejected = false;
    try { new ModScanner().Scan(Path.Combine(root, "input.bin"), canceled); } catch (OperationCanceledException) { rejected = true; }
    Check(rejected, "Cancellation respected");
    var folder = Path.Combine(root, "folder"); Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "safe.txt"), "ordinary config");
    Directory.CreateDirectory(Path.Combine(folder, "nested"));
    File.WriteAllText(Path.Combine(folder, "nested", "mod.txt"), "CreateRemoteThread");
    var folderReport = new ModScanner().Scan(folder);
    Check(folderReport.Files.Count == 2 && folderReport.Findings.Any(f => f.Code == "process-injection-api"), "Folder scan reaches nested files");
    Check(new ModScanner(new ScanLimits(MaxEntries: 1)).Scan(folder).Incomplete, "Folder budget reports incomplete coverage");
    StreamingScanChecks.Run(Check, root);
    SmartRepairChecks.Run(Check);
    AdviceMarkupChecks.Run(Check);
    SelfUpdateChecks.Run(Check, root);
    AppIdentityChecks.Run(Check);
    Console.WriteLine($"{passed} tests passed.");
}
finally { Directory.Delete(root, true); }
