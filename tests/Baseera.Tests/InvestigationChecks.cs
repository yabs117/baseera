using System.IO.Compression;
using System.Text;
using System.Xml;
using Baseera.Core;

internal static class InvestigationChecks
{
    internal static void Run(Action<bool, string> check, string parent)
    {
        string root = Path.Combine(parent, "investigation-fixture"); Directory.CreateDirectory(root);
        bool Reject(Action action)
        { try { action(); return false; } catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or InvalidOperationException or XmlException) { return true; } }
        string Put(string name, string text, bool old = false)
        {
            string path = Path.GetFullPath(Path.Combine(root, name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false));
            if (old) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-10)); return path;
        }
        StorageFile Snapshot(string path) { var f = new FileInfo(path); return new(f.FullName, f.Length, f.LastWriteTimeUtc); }
        check(Enumerable.Range(0, 16).All(n => LocalPathSafety.IsCloudTag(0x9000001Au | ((uint)n << 12))) &&
            !LocalPathSafety.IsCloudTag(0xA0000003) && !LocalPathSafety.IsCloudTag(0xA000000C) && !LocalPathSafety.IsCloudTag(0x8000001A),
            "Only the sixteen Cloud Files tags are distinguished from junctions, symbolic links and unknown reparse tags");
        check(!LocalPathSafety.RequiresDownload((FileAttributes)0x80420) &&
            new uint[] { 0x1000, 0x40000, 0x400000 }.All(flag => LocalPathSafety.RequiresDownload((FileAttributes)(flag | 0x400))),
            "Locally available cloud files are readable while offline/recall states require a prior download");
        string a = Put("one/a.txt", "same contents"), b = Put("two/b.txt", "same contents"), c = Put("two/c.txt", "xxxx contents");
        var report = StorageExplorer.Inspect(root, true, CancellationToken.None).GetAwaiter().GetResult();
        check(report.Bytes == new[] { a, b, c }.Sum(p => new FileInfo(p).Length) && report.Entries == 3 && report.Folders.Count == 2, "Storage totals reflect bounded selected-folder files without double-counting nested folders");
        check(report.Duplicates.Count == 1 && report.Duplicates[0].Files.Select(f => f.Path).Order().SequenceEqual(new[] { a, b }.Order()) && report.Duplicates[0].Sha256.Length == 64,
            "Duplicate detection hashes actual bytes; equal size alone does not establish duplicate content (groups=" + report.Duplicates.Count + ", notices=" + string.Join(",", report.Notices) + ")");
        var namesOnly = StorageExplorer.Inspect(root, false, CancellationToken.None).GetAwaiter().GetResult();
        check(namesOnly.Duplicates.Count == 0, "Folder size inspection does not read file contents for duplicate analysis without opt-in");
        var bounded = StorageExplorer.Inspect(root, true, CancellationToken.None, new(Entries: 1)).GetAwaiter().GetResult();
        check(bounded.Notices.Contains("hubPartial") && bounded.Entries <= 1, "Storage entry budget reports partial coverage rather than claiming a complete scan");
        var hashBound = StorageExplorer.Inspect(root, true, CancellationToken.None, new(HashFiles: 1)).GetAwaiter().GetResult();
        check(hashBound.Notices.Contains("hubHashLimit") && hashBound.Duplicates.Count == 0, "Hash budget cannot produce a duplicate set from an unhashed match");
        var fileBound = StorageExplorer.Inspect(root, true, CancellationToken.None, new(MaxHashFile: 1)).GetAwaiter().GetResult();
        check(fileBound.Notices.Contains("hubHashLimit") && fileBound.Duplicates.Count == 0, "Oversized duplicate candidates remain explicitly unverified");
        bool canceled = false; try { StorageExplorer.Inspect(root, true, new CancellationToken(true)).GetAwaiter().GetResult(); } catch (OperationCanceledException) { canceled = true; }
        check(canceled, "Storage enumeration respects cancellation");
        string sibling = Path.Combine(parent, "investigation-fixture-other"); Directory.CreateDirectory(sibling); string outside = Path.Combine(sibling, "outside.tmp"); File.WriteAllText(outside, "outside"); File.SetLastWriteTimeUtc(outside, DateTime.UtcNow.AddDays(-10));
        check(!LocalFileScope.Contains(root, outside) && Reject(() => LocalFileScope.RequireFile(root, outside)), "File scope rejects a sibling sharing the root path prefix");
        check(LocalFileScope.Contains(Path.GetPathRoot(root)!, a) && LocalFileScope.Contains(root + Path.DirectorySeparatorChar, a), "File scope handles both volume roots and trailing directory separators");
        check(Reject(() => LocalFileScope.Root("relative-folder")), "Relative folder scopes are refused");
        var changed = Snapshot(a); File.AppendAllText(a, "changed");
        check(!LocalFileScope.Unchanged(changed) && Reject(() => LocalFileScope.Hash(changed, CancellationToken.None).GetAwaiter().GetResult()), "Changed files cannot retain a previously captured hash identity");

        string cleanRoot = Path.Combine(root, "cleanup"); Directory.CreateDirectory(cleanRoot);
        string eligible = Put("cleanup/old.tmp", "eligible", true), recent = Put("cleanup/new.tmp", "recent"), unsupported = Put("cleanup/document.txt", "document", true), readOnly = Put("cleanup/readonly.tmp", "protected", true), system = Put("cleanup/system.tmp", "protected", true);
        try
        {
            File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
            File.SetAttributes(system, File.GetAttributes(system) | FileAttributes.System);
            var candidates = CleanupApproval.Candidates(cleanRoot, CancellationToken.None);
            check(candidates.Select(f => f.Path).SequenceEqual(new[] { eligible }), "Cleanup preview excludes recent, unsupported, read-only and system files");
            check(Reject(() => CleanupApproval.Confirm(candidates, false)) && Reject(() => CleanupApproval.Confirm([], true)), "Cleanup needs acknowledgement and a nonempty explicit selection");
            check(Reject(() => CleanupApproval.Confirm([Snapshot(eligible), Snapshot(eligible)], true)), "Duplicate cleanup selection is rejected");
            var approved = CleanupApproval.Confirm([Snapshot(eligible)], true); var consumed = approved.Consume(cleanRoot);
            check(consumed.Count == 1 && File.Exists(eligible) && Reject(() => approved.Consume(cleanRoot)), "Cleanup approval is single-use and validation alone never deletes a file");
            check(Reject(() => CleanupApproval.Confirm([Snapshot(outside)], true).Consume(cleanRoot)), "Cleanup cannot act outside its fixed allowed root");
            var stale = CleanupApproval.Confirm([Snapshot(eligible)], true); File.AppendAllText(eligible, "modified after review");
            check(Reject(() => stale.Consume(cleanRoot)), "Cleanup rechecks file size and timestamp after preview approval");
            File.SetLastWriteTimeUtc(eligible, DateTime.UtcNow.AddDays(-10));
            using (File.Open(eligible, FileMode.Open, FileAccess.Read, FileShare.None))
                check(Reject(() => CleanupApproval.Confirm([Snapshot(eligible)], true).Consume(cleanRoot)), "A locked cleanup candidate is refused rather than force-closed");
            check(Reject(() => CleanupApproval.Confirm([Snapshot(readOnly)], true).Consume(cleanRoot)) && Reject(() => CleanupApproval.Confirm([Snapshot(system)], true).Consume(cleanRoot)), "Forged protected-file selections cannot bypass cleanup safety checks");
        }
        finally { File.SetAttributes(readOnly, FileAttributes.Normal); File.SetAttributes(system, FileAttributes.Normal); }

        string link = Path.Combine(root, "external-link");
        try
        {
            Directory.CreateSymbolicLink(link, sibling);
            var linked = StorageExplorer.Inspect(root, false, CancellationToken.None).GetAwaiter().GetResult();
            check(linked.Notices.Contains("hubLinksSkipped") && !linked.Largest.Any(f => f.Path.Contains("outside.tmp")) && Reject(() => LocalFileScope.RequireFile(root, Path.Combine(link, "outside.tmp"))), "Storage and file scope exclude linked folders and their outside contents");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException || ex is IOException && (ex.HResult & 0xffff) == 1314)
        { Console.WriteLine("SKIP: symbolic-link fixture requires local symbolic-link privilege."); }

        string Office(string name, params (string Path, string Text)[] entries)
        {
            string path = Path.Combine(root, name); using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Path).Open(), new UTF8Encoding(false)); writer.Write(entry.Text); }
            return path;
        }
        string docx = Office("example.docx", ("word/document.xml", "<w:document xmlns:w='urn:w'><w:p><w:t>أخطاء التعريف Driver report</w:t></w:p></w:document>"));
        string xlsx = Office("example.xlsx", ("xl/sharedStrings.xml", "<sst><si><t>ميزانية Budget</t></si></sst>"), ("xl/worksheets/sheet1.xml", "<worksheet><v>123</v></worksheet>"));
        string pptx = Office("example.pptx", ("ppt/slides/slide1.xml", "<p:sld xmlns:p='urn:p'><p:t>Network slides</p:t></p:sld>"));
        check(LocalSearch.OfficeText(docx).Contains("أخطاء التعريف") && LocalSearch.OfficeText(xlsx).Contains("ميزانية") && LocalSearch.OfficeText(pptx).Contains("Network slides"), "Local Office extraction reads DOCX, XLSX and PPTX text without opening Office");
        check(LocalSearch.OfficeText(docx, 9).Length == 9, "Office text extraction honors its output character cap");
        string dtd = Office("dtd.docx", ("word/document.xml", "<!DOCTYPE x [<!ENTITY payload SYSTEM 'file:///missing-private-file'>]><x>&payload;</x>"));
        check(Reject(() => LocalSearch.OfficeText(dtd)), "Office extraction prohibits DTDs and external entities");
        string expansion = Office("oversize.docx", ("word/document.xml", new string('x', 2097153)));
        check(Reject(() => LocalSearch.OfficeText(expansion)), "Office expansion budget rejects oversized XML before parsing");
        string malformed = Put("broken.docx", "not a ZIP archive");
        check(Reject(() => LocalSearch.OfficeText(malformed)), "Malformed Office input is rejected safely");

        var now = new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc);
        var english = LocalSearch.Parse("find PDF files about driver last week", now);
        var arabic = LocalSearch.Parse("ابحث عن ملفات PDF عن أَخطاء الأسبوع الماضي", now);
        check(english.Extension == ".pdf" && english.SinceUtc == now.AddDays(-7) && english.Terms.SequenceEqual(new[] { "driver" }) && arabic.Terms.SequenceEqual(new[] { "اخطاء" }), "Local Arabic/English query interpretation extracts document type, rolling date scope and search terms");
        check(LocalSearch.Normalize("مـلف إِصْلَاح") == LocalSearch.Normalize("ملف اصلاح"), "Arabic search normalizes diacritics, hamza and tatweel without splitting a word");
        var index = new SearchIndex(DateTimeOffset.UtcNow, root,
            [new(a, "driver.pdf", ".pdf", 2, now.AddDays(-1), "Windows driver إصلاح أخطاء", "hubTextLimited"), new(b, "driver-old.pdf", ".pdf", 2, now.AddDays(-20), "Windows driver", "hubTextLimited"), new(c, "driver.docx", ".docx", 2, now, "Windows driver", "hubTextLimited")], []);
        check(LocalSearch.Find(index, english).Single().Path == a && LocalSearch.Find(index, arabic).Single().Path == a, "Search applies type/date/content filters together and finds Arabic indexed content");
        canceled = false; try { LocalSearch.Find(index, english, new CancellationToken(true)); } catch (OperationCanceledException) { canceled = true; }
        check(canceled && Reject(() => LocalSearch.Parse(new string('x', 501), now)), "Search cancellation and query-length limits are enforced");
        var many = index with { Documents = Enumerable.Range(0, 150).Select(i => new IndexedDocument(a + i, "item" + i, ".txt", 1, now, "", "hubNameOnly")).ToArray() };
        check(LocalSearch.Find(many, LocalSearch.Parse("", now)).Count == 100, "Search display results are bounded independently of index size");

        check(PerformanceEvidence.Correlation([(1, 2), (2, 4), (3, 6), (4, 8)]) is > .999 && PerformanceEvidence.Correlation([(1, 8), (2, 6), (3, 4), (4, 2)]) is < -.999, "Performance covariance preserves positive and negative correlation");
        check(PerformanceEvidence.Correlation([(1, 1), (1, 2), (1, 3), (1, 4)]) is null && PerformanceEvidence.Correlation([(1, 1), (2, 2), (3, 3)]) is null && PerformanceEvidence.Correlation([(1, 1), (2, 2), (3, 3), (double.NaN, 4)]) is null && PerformanceEvidence.Correlation([(1e308, 1e308), (1e308, 1e308), (1e308, 1e308), (1e308, 1e308)]) is null, "Insufficient, constant, non-finite and overflowing samples cannot invent a correlation");
        var points = Enumerable.Range(1, 4).Select(i => new PerformancePoint(new DateTimeOffset(now).AddSeconds(i * 4), 90, 92, i * 10000000d, 25, [new(10, 100, "Example application", 35, 100, i * 2000000d)])).ToArray();
        var findings = PerformanceEvidence.Analyze(points);
        check(findings.Any(f => f.Kind == "hubCpuCoincidence") && findings.Any(f => f.Kind == "hubIoCoincidence") && findings.All(f => f.Kind.EndsWith("Coincidence")), "Sustained resource observations are reported as coincidences rather than a proven slowdown cause");
        var reused = points.Select((p, i) => p with { DiskBytes = null, DiskLatencyMs = null, Processes = [new(10, i < 2 ? 100 : 200, "Reused PID", 35, 100, null)] }).ToArray();
        check(PerformanceEvidence.Analyze(reused).Count == 0, "Reused process IDs cannot combine into a sustained-activity finding");
        var disconnected = points.Take(3).Select((p, i) => p with { Time = p.Time.AddDays(i), DiskBytes = null, DiskLatencyMs = null }).ToArray();
        check(PerformanceEvidence.Analyze(disconnected).Count == 0 && PerformanceEvidence.Analyze(points.Reverse().ToArray()).Count == 0, "Separated sessions and non-increasing timestamps cannot establish a sustained performance coincidence");
        check(Reject(() => PerformanceEvidence.ValidateHistory([points[0] with { Cpu = double.NaN }])) && Reject(() => PerformanceEvidence.ValidateHistory([points[0] with { Memory = 101 }])) && Reject(() => PerformanceEvidence.ValidateHistory([points[0] with { Processes = Enumerable.Repeat(points[0].Processes[0], 17).ToArray() }])) && Reject(() => PerformanceEvidence.ValidateHistory(Enumerable.Repeat(points[0], 121).ToArray())), "Loaded performance histories enforce finite readings, percentages, per-sample process caps and history bounds");

        var reading = new NetworkReading(DateTimeOffset.UtcNow, "example.com", 12, 6, [10, 11, 9], "fixture-up", "");
        check(NetworkEvidence.Loss(reading) == 50 && NetworkEvidence.Interpret(reading).Contains("hubPacketLoss") && NetworkEvidence.Interpret(reading).Contains("hubNetworkLimits"), "Network measurements preserve actual loss and retain diagnostic limits");
        var blocked = reading with { DnsMs = null, Replies = [] };
        check(NetworkEvidence.Interpret(blocked).Contains("hubDnsFailed") && NetworkEvidence.Interpret(blocked).Contains("hubIcmpUnknown"), "Missing DNS and blocked ICMP remain uncertain observations, not a dead-network claim");
        var direct = blocked with { Host = "127.0.0.1" };
        check(NetworkEvidence.Interpret(direct).Contains("hubDnsNotRequired") && !NetworkEvidence.Interpret(direct).Contains("hubDnsFailed"), "A direct IP measurement cannot be mislabeled as a failed or timed DNS lookup");
        check(NetworkEvidence.Loss(reading with { Sent = 0, Replies = [] }) is null && NetworkEvidence.Loss(reading with { Sent = 1 }) is null && Reject(() => NetworkEvidence.Validate(reading with { Sent = 1 })), "Inconsistent network history cannot yield fabricated negative loss");
        check(NetworkEvidence.Host("example.com") == "example.com" && NetworkEvidence.Host("127.0.0.1") == "127.0.0.1" && NetworkEvidence.Host("مثال.إختبار").StartsWith("xn--"), "Network target validation supports literal addresses and internationalized hostnames");
        check(new[] { "https://example.com", "user@example.com", "example.com/path", "example.com:443", "foo\nbar", new string('a', 254) }.All(s => Reject(() => NetworkEvidence.Host(s))), "Network targets reject URLs, credentials, ports, paths, controls and oversized hostnames");
        check(Reject(() => NetworkEvidence.Validate(reading with { DnsMs = double.NaN })) && Reject(() => NetworkEvidence.Validate(reading with { Replies = [-1] })), "Persisted network timings and reply values are validated before interpretation");
        check(Reject(() => NetworkEvidence.Validate(reading with { WifiQuality = 101 })) && Reject(() => NetworkEvidence.Validate(reading with { WifiLinkMbps = -1 })), "Persisted Wi-Fi signal and link-rate readings retain physical range checks");
    }
}
