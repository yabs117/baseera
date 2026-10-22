using System.IO.Compression;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Baseera.Core;

public sealed record Finding(string Path, string Code, string Severity, string Evidence);
public sealed record InspectedFile(string Path, long Size, string Sha256, string Kind);
public sealed class ScanReport
{
    public string Target { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<InspectedFile> Files { get; } = [];
    public List<Finding> Findings { get; } = [];
    public List<string> Limitations { get; } = ["Static analysis only; files are never executed. No finding is not proof of safety."];
    public bool Incomplete { get; set; }
    public string Verdict => Incomplete ? "incomplete" : Findings.Count > 0 ? "review" : "no-indicators";
}
public sealed record ScanLimits(long MaxFileBytes = 64 * 1024 * 1024, long MaxTotalBytes = 256 * 1024 * 1024,
    int MaxEntries = 3000, int MaxDepth = 4, double MaxCompressionRatio = 200);

public sealed class ModScanner(ScanLimits? limits = null)
{
    private readonly ScanLimits limits = limits ?? new();
    private static readonly (string Code, string Pattern)[] Rules = [
        ("encoded-powershell", @"(?i)(?:powershell|pwsh)(?:\.exe)?[^\r\n]{0,160}-(?:enc(?:odedcommand)?|e)\s+[A-Za-z0-9+/=]{16,}"),
        ("download-and-execute", @"(?is)(?:DownloadString|Invoke-WebRequest|\biwr\b|\bcurl\b|\bwget\b).{0,400}(?:Invoke-Expression|\biex\b|Start-Process|\bexec\b|\beval\b)"),
        ("browser-data-reference", @"(?i)(?:Login Data|Local State|Web Data|Cookies|logins\.json|key4\.db)"),
        ("startup-persistence", @"(?i)(?:CurrentVersion[\\/]+Run(?:Once)?\b|schtasks(?:\.exe)?\s+/create|Startup[\\/])"),
        ("defender-setting-change", @"(?i)(?:Set-MpPreference|Add-MpPreference|DisableRealtimeMonitoring|ExclusionPath)"),
        ("process-injection-api", @"(?:CreateRemoteThread|WriteProcessMemory|VirtualAllocEx|NtQueueApcThread)"),
        ("credential-api", @"(?:CryptUnprotectData|CredEnumerate[AW]?|LsaRetrievePrivateData)")
    ];

    public ScanReport Scan(string path, CancellationToken token = default)
    {
        var report = new ScanReport { Target = Path.GetFullPath(path) };
        long consumed = 0; int entries = 0;
        if (File.Exists(path))
        {
            using var input = File.OpenRead(path);
            Inspect(input, input.Length, Path.GetFileName(path), 0);
        }
        else if (Directory.Exists(path))
        {
            var directories = new Stack<string>(); directories.Push(Path.GetFullPath(path));
            int visited = 0;
            while (directories.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var directory = directories.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { Skip(directory, "Directory link skipped."); continue; }
                try
                {
                    foreach (var item in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++visited > limits.MaxEntries || entries >= limits.MaxEntries || consumed >= limits.MaxTotalBytes)
                        { Skip(directory, "Folder scan budget exhausted."); return report; }
                        try
                        {
                            var attributes = File.GetAttributes(item);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { Skip(item, "Filesystem link skipped."); continue; }
                            if ((attributes & FileAttributes.Directory) != 0) { directories.Push(item); continue; }
                            using var input = File.OpenRead(item);
                            Inspect(input, input.Length, Path.GetRelativePath(path, item), 0);
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Skip(item, "Cannot read: " + ex.GetType().Name); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Skip(directory, "Cannot enumerate: " + ex.GetType().Name); }
            }
        }
        else throw new FileNotFoundException("File or folder not found.", path);
        return report;

        void Skip(string name, string reason)
        {
            report.Incomplete = true;
            report.Findings.Add(new(name, "not-inspected", "coverage", reason));
        }
        void Inspect(Stream stream, long length, string name, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (++entries > limits.MaxEntries) { Skip(name, "Entry limit reached."); return; }
            if (length > limits.MaxFileBytes || length > limits.MaxTotalBytes - consumed)
            { Skip(name, "File or total decompressed byte limit reached."); return; }
            using var buffer = new MemoryStream();
            var chunk = new byte[81920]; int read;
            while ((read = stream.Read(chunk)) > 0)
            {
                token.ThrowIfCancellationRequested();
                consumed += read;
                if (buffer.Length + read > limits.MaxFileBytes || consumed > limits.MaxTotalBytes)
                { Skip(name, "Actual decompressed data exceeds limit."); return; }
                buffer.Write(chunk, 0, read);
            }
            var data = buffer.ToArray();
            bool zip = data.Length >= 4 && data[0] == 0x50 && data[1] == 0x4b &&
                (data[2] == 3 || data[2] == 5 || data[2] == 7);
            bool pe = data.Length >= 2 && data[0] == 'M' && data[1] == 'Z';
            bool unsupportedArchive = data.AsSpan().StartsWith(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }) ||
                data.AsSpan().StartsWith("Rar!"u8) || data.AsSpan().StartsWith(new byte[] { 0x1f, 0x8b }) ||
                (data.Length > 262 && data.AsSpan(257, 5).SequenceEqual("ustar"u8));
            report.Files.Add(new(name, data.Length, Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                zip ? "ZIP" : pe ? "PE" : unsupportedArchive ? "unsupported archive" : "data/text"));
            if (unsupportedArchive) { Skip(name, "Archive format not supported; inner files were not inspected."); return; }
            if (zip)
            {
                if (depth >= limits.MaxDepth) { Skip(name, "Nested archive depth limit reached."); return; }
                try
                {
                    using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
                    foreach (var entry in archive.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        if (entries >= limits.MaxEntries || consumed >= limits.MaxTotalBytes)
                        { Skip(name, "Archive entry/byte budget exhausted; remaining entries skipped."); break; }
                        var child = name + "!/" + entry.FullName;
                        if (entry.FullName.EndsWith('/')) continue;
                        if (entry.FullName.Replace('\\', '/').Split('/').Contains("..") ||
                            entry.FullName.StartsWith('/') || entry.FullName.Contains(':'))
                            report.Findings.Add(new(child, "archive-path", "review", "Path could escape an unsafe extractor. This scanner never extracts to disk."));
                        if ((entry.ExternalAttributes >> 16 & 0xf000) == 0xa000)
                            report.Findings.Add(new(child, "archive-symlink", "review", "Symbolic link entry."));
                        if (entry.Length > Math.Max(entry.CompressedLength, 1) * limits.MaxCompressionRatio)
                        { Skip(child, "Compression ratio exceeds limit."); continue; }
                        try { using var inner = entry.Open(); Inspect(inner, entry.Length, child, depth + 1); }
                        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
                        { Skip(child, "Encrypted, damaged, or unsupported entry: " + ex.GetType().Name); }
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
                { Skip(name, "Cannot read archive: " + ex.GetType().Name); }
                return;
            }
            if (pe)
            {
                try
                {
                    using var reader = new PEReader(new MemoryStream(data));
                    foreach (var section in reader.PEHeaders.SectionHeaders)
                        if (section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) &&
                            section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite))
                            report.Findings.Add(new(name, "writable-executable-section", "review", section.Name));
                    if (reader.PEHeaders.PEHeader is null) Skip(name, "Missing PE optional header.");
                    else if (reader.PEHeaders.PEHeader.CertificateTableDirectory.Size == 0)
                        report.Findings.Add(new(name, "unsigned-pe", "info", "No embedded certificate table. This alone is not evidence of malware."));
                    report.Limitations.Add(name + ": executable inspected statically; no unpacking, execution, or certificate trust validation.");
                }
                catch (BadImageFormatException) { Skip(name, "Malformed executable header."); }
            }
            // Inspect both byte-oriented strings and UTF-16 LE/BE strings, regardless of the extension.
            var texts = new[] { Encoding.UTF8.GetString(data), Encoding.Unicode.GetString(data), Encoding.BigEndianUnicode.GetString(data) };
            foreach (var rule in Rules)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var match = texts.Select(t => Regex.Match(t, rule.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(350)))
                        .FirstOrDefault(m => m.Success);
                    if (match is not null)
                        report.Findings.Add(new(name, rule.Code, "review", new string(match.Value.Take(180).Where(c => !char.IsControl(c)).ToArray())));
                }
                catch (RegexMatchTimeoutException) { Skip(name, "Pattern analysis time budget exceeded."); }
            }
        }
    }
}
