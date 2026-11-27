using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Baseera.Core;

public sealed record IndexedDocument(string Path, string Name, string Extension, long Bytes, DateTime ModifiedUtc, string Text, string Coverage);
public sealed record SearchFilter(IReadOnlyList<string> Terms, string? Extension, DateTime? SinceUtc);
public sealed record SearchIndex(DateTimeOffset Built, string Root, IReadOnlyList<IndexedDocument> Documents, IReadOnlyList<string> Notices);
public static class LocalSearch
{
    public static string Normalize(string text)
    {
        text = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant(); var result = new StringBuilder();
        foreach (char c in text) if (c != 'ـ' && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) result.Append(c switch { 'أ' or 'إ' or 'آ' => 'ا', 'ى' => 'ي', _ => c }); return result.ToString();
    }
    public static SearchFilter Parse(string query, DateTime utcNow)
    {
        if (query.Length > 500) throw new ArgumentException("Query too long."); string normalized = Normalize(query); string? extension = null;
        foreach (var group in new[] { ("pdf", new[] { "pdf" }), ("docx", new[] { "word", "docx", "وورد" }), ("xlsx", new[] { "excel", "xlsx", "اكسل" }), ("pptx", new[] { "powerpoint", "pptx", "بوربوينت" }) })
            foreach (string term in group.Item2) if (Regex.IsMatch(normalized, @"(?<!\w)" + Regex.Escape(term) + @"(?!\w)")) { extension = "." + group.Item1; normalized = Regex.Replace(normalized, @"(?<!\w)" + Regex.Escape(term) + @"(?!\w)", " "); }
        DateTime? since = null;
        foreach (var rule in new[] { ("last week", 7), ("this week", 7), ("الاسبوع الماضي", 7), ("الاسبوع", 7), ("last month", 30), ("الشهر الماضي", 30), ("today", 1), ("اليوم", 1) })
            if (normalized.Contains(rule.Item1)) { since = utcNow.AddDays(-rule.Item2); normalized = normalized.Replace(rule.Item1, " "); }
        var stop = new HashSet<string>(["find", "search", "files", "file", "documents", "about", "from", "the", "for", "ابحث", "عن", "ملفات", "ملف", "المستندات", "التي", "خلال", "في"]);
        var terms = Regex.Matches(normalized, @"[\p{L}\p{N}_-]+").Select(m => m.Value).Where(s => !stop.Contains(s)).Take(20).ToArray();
        return new(terms, extension, since);
    }
    public static IReadOnlyList<IndexedDocument> Find(SearchIndex index, SearchFilter filter, CancellationToken token = default)
    {
        var results = new List<IndexedDocument>();
        foreach (var d in index.Documents)
        {
            token.ThrowIfCancellationRequested(); if (filter.Extension is not null && d.Extension != filter.Extension || filter.SinceUtc is not null && d.ModifiedUtc < filter.SinceUtc) continue;
            string text = Normalize(d.Name + " " + d.Text); if (filter.Terms.All(text.Contains)) results.Add(d);
        }
        return results.OrderByDescending(d => d.ModifiedUtc).Take(100).ToArray();
    }
    public static string OfficeText(string path, int maximum = 8000)
    {
        using var archive = ZipFile.OpenRead(path); if (archive.Entries.Count > 5000) throw new InvalidDataException("Office entry limit.");
        long bytes = 0; var text = new StringBuilder();
        foreach (var entry in archive.Entries.Where(e => e.FullName is "word/document.xml" or "xl/sharedStrings.xml" || e.FullName.StartsWith("xl/worksheets/") && e.FullName.EndsWith(".xml") || e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml")).Take(100))
        {
            if ((bytes += entry.Length) > 8388608 || entry.Length > 2097152) throw new InvalidDataException("Office expansion limit.");
            using var source = entry.Open(); using var reader = XmlReader.Create(source, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 });
            while (reader.Read())
            {
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA) { text.Append(reader.Value); text.Append(' '); if (text.Length >= maximum) return text.ToString(0, maximum); }
            }
        }
        return text.ToString();
    }
}
