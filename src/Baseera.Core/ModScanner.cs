using System.IO.Compression;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
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
public sealed record ScanLimits(long MaxFileBytes = 1024L * 1024 * 1024, long MaxTotalBytes = 4L * 1024 * 1024 * 1024,
    int MaxEntries = 3000, int MaxDepth = 4, double MaxCompressionRatio = 200, long MaxNestedArchiveBytes = 32L * 1024 * 1024);
public sealed record ScanProgress(string Path, long ReadBytes, long FileBytes, long TotalBytes, int InspectedFiles);

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

    public ScanReport Scan(string path, CancellationToken token = default, IProgress<ScanProgress>? progress = null)
    {
        var report = new ScanReport { Target = Path.GetFullPath(path) };
        long consumed = 0; int entries = 0; var progressClock = Stopwatch.StartNew();
        if (File.Exists(path))
        {
            var attributes = File.GetAttributes(path);
            if (LocalPathSafety.IsLink(path, attributes)) { Skip(path, "Filesystem link skipped."); return report; }
            if (LocalPathSafety.RequiresDownload(attributes)) { Skip(path, "Cloud content is not fully available locally. Download the file first."); return report; }
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
                try
                {
                    var directoryAttributes = File.GetAttributes(directory);
                    if (LocalPathSafety.IsLink(directory, directoryAttributes)) { Skip(directory, "Directory link skipped."); continue; }
                    if (LocalPathSafety.RequiresDownload(directoryAttributes)) { Skip(directory, "Cloud folder is not fully available locally."); continue; }
                    foreach (var item in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        if (++visited > limits.MaxEntries || entries >= limits.MaxEntries || consumed >= limits.MaxTotalBytes)
                        { Skip(directory, "Folder scan budget exhausted."); return report; }
                        try
                        {
                            var attributes = File.GetAttributes(item);
                            if (LocalPathSafety.IsLink(item, attributes)) { Skip(item, "Filesystem link skipped."); continue; }
                            if (LocalPathSafety.RequiresDownload(attributes)) { Skip(item, "Cloud content is not fully available locally. Download the file first."); continue; }
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
            if (length > limits.MaxFileBytes)
            { Skip(name, $"File size {length} exceeds the per-file limit of {limits.MaxFileBytes} bytes."); return; }
            if (length > limits.MaxTotalBytes - consumed)
            { Skip(name, "Total decompressed byte limit reached."); return; }
            long start = stream.CanSeek ? stream.Position : 0;
            byte[] prefix = new byte[512]; int prefixLength = 0, next;
            while (prefixLength < prefix.Length && (next = stream.Read(prefix, prefixLength, prefix.Length - prefixLength)) > 0)
            { token.ThrowIfCancellationRequested(); prefixLength += next; }
            bool zip = prefixLength >= 4 && prefix[0] == 0x50 && prefix[1] == 0x4b &&
                (prefix[2] == 3 || prefix[2] == 5 || prefix[2] == 7);
            bool pe = prefixLength >= 2 && prefix[0] == 'M' && prefix[1] == 'Z';
            bool unsupportedArchive = prefix.AsSpan(0, prefixLength).StartsWith(new byte[] { 0x37, 0x7a, 0xbc, 0xaf, 0x27, 0x1c }) ||
                prefix.AsSpan(0, prefixLength).StartsWith("Rar!"u8) || prefix.AsSpan(0, prefixLength).StartsWith(new byte[] { 0x1f, 0x8b }) ||
                (prefixLength > 262 && prefix.AsSpan(257, 5).SequenceEqual("ustar"u8));
            // Seekable ZIPs are indexed directly; only small nested ZIPs need a bounded memory copy.
            using var archiveCopy = zip && !stream.CanSeek && length <= limits.MaxNestedArchiveBytes ? new MemoryStream() : null;
            using var pePrefix = pe && !stream.CanSeek ? new MemoryStream() : null;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            const int chunkSize = 256 * 1024, overlap = 4096;
            var chunk = ArrayPool<byte>.Shared.Rent(chunkSize + overlap);
            long fileRead = 0; var matched = new HashSet<string>(); var timedOut = new HashSet<string>();
            try
            {
                int carry = 0; int read = prefixLength; prefix.AsSpan(0, prefixLength).CopyTo(chunk);
                while (true)
                {
                    token.ThrowIfCancellationRequested(); consumed += read; fileRead += read;
                    if (fileRead > limits.MaxFileBytes || consumed > limits.MaxTotalBytes)
                    { Skip(name, "Actual decompressed data exceeds limit."); return; }
                    if (archiveCopy is not null && archiveCopy.Length + read > limits.MaxNestedArchiveBytes)
                    { Skip(name, "Actual nested archive exceeds the in-memory indexing limit."); return; }
                    hash.AppendData(chunk, carry, read); archiveCopy?.Write(chunk, carry, read);
                    if (pePrefix is not null && pePrefix.Length < 1024 * 1024)
                        pePrefix.Write(chunk, carry, Math.Min(read, 1024 * 1024 - (int)pePrefix.Length));
                    int available = carry + read;
                    if (!zip && !unsupportedArchive)
                    {
                        var texts = new[] { Encoding.UTF8.GetString(chunk, 0, available), Encoding.Unicode.GetString(chunk, 0, available), Encoding.BigEndianUnicode.GetString(chunk, 0, available) };
                        foreach (var rule in Rules)
                        {
                            token.ThrowIfCancellationRequested(); if (matched.Contains(rule.Code) || timedOut.Contains(rule.Code)) continue;
                            try
                            {
                                var match = texts.Select(t => Regex.Match(t, rule.Pattern, RegexOptions.None, TimeSpan.FromMilliseconds(350))).FirstOrDefault(m => m.Success);
                                if (match is not null)
                                { matched.Add(rule.Code); report.Findings.Add(new(name, rule.Code, "review", new string(match.Value.Take(180).Where(c => !char.IsControl(c)).ToArray()))); }
                            }
                            catch (RegexMatchTimeoutException) { timedOut.Add(rule.Code); Skip(name, "Pattern analysis time budget exceeded."); }
                        }
                    }
                    if (progressClock.ElapsedMilliseconds >= 120)
                    { progress?.Report(new(name, fileRead, length, consumed, report.Files.Count)); progressClock.Restart(); }
                    carry = Math.Min(overlap, available); chunk.AsSpan(available - carry, carry).CopyTo(chunk);
                    read = stream.Read(chunk, carry, chunkSize); if (read == 0) break;
                }
            }
            finally { ArrayPool<byte>.Shared.Return(chunk, clearArray: true); }
            report.Files.Add(new(name, fileRead, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                zip ? "ZIP" : pe ? "PE" : unsupportedArchive ? "unsupported archive" : "data/text"));
            progress?.Report(new(name, fileRead, length, consumed, report.Files.Count));
            if (unsupportedArchive) { Skip(name, "Archive format not supported; inner files were not inspected."); return; }
            if (zip)
            {
                if (depth >= limits.MaxDepth) { Skip(name, "Nested archive depth limit reached."); return; }
                try
                {
                    Stream? archiveStream = stream.CanSeek ? stream : archiveCopy;
                    if (archiveStream is null) { Skip(name, "Nested archive exceeds the in-memory indexing limit."); return; }
                    archiveStream.Position = start;
                    // Bound directory allocation before ZipArchive materializes its entry collection.
                    long tailStart = Math.Max(start, archiveStream.Length - 65557);
                    archiveStream.Position = tailStart; var tail = new byte[(int)(archiveStream.Length - tailStart)]; archiveStream.ReadExactly(tail);
                    int eocd = -1;
                    for (int i = tail.Length - 22; i >= 0; i--)
                        if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i, 4)) == 0x06054b50 && i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20, 2)) == tail.Length)
                        { eocd = i; break; }
                    if (eocd < 0) { Skip(name, "Cannot read archive: missing end directory."); return; }
                    int archiveEntries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(eocd + 10, 2));
                    long directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12, 4));
                    if (archiveEntries == ushort.MaxValue || directoryBytes == uint.MaxValue)
                    { Skip(name, "ZIP64 directory is not supported by the bounded indexer."); return; }
                    if (archiveEntries > limits.MaxEntries - entries || directoryBytes > 8 * 1024 * 1024)
                    { Skip(name, "Archive entry/directory budget exhausted."); return; }
                    archiveStream.Position = start;
                    using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
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
                    Stream peStream = stream.CanSeek ? stream : pePrefix!;
                    peStream.Position = start; var headers = new PEHeaders(peStream);
                    foreach (var section in headers.SectionHeaders)
                        if (section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemExecute) &&
                            section.SectionCharacteristics.HasFlag(SectionCharacteristics.MemWrite))
                            report.Findings.Add(new(name, "writable-executable-section", "review", section.Name));
                    if (headers.PEHeader is null) Skip(name, "Missing PE optional header.");
                    else if (headers.PEHeader.CertificateTableDirectory.Size == 0)
                        report.Findings.Add(new(name, "unsigned-pe", "info", "No embedded certificate table. This alone is not evidence of malware."));
                    report.Limitations.Add(name + ": executable inspected statically; no unpacking, execution, or certificate trust validation.");
                }
                catch (Exception ex) when (ex is BadImageFormatException or IOException or ArgumentOutOfRangeException) { Skip(name, "Malformed executable header or header exceeds indexing limit."); }
            }
        }
    }
}
