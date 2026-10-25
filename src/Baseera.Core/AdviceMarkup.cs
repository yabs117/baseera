namespace Baseera.Core;

public enum AdviceInlineKind { Text, Strong, Code }
public sealed record AdviceInline(string Value, AdviceInlineKind Kind = AdviceInlineKind.Text);
public sealed record AdviceBlock(IReadOnlyList<AdviceInline> Inlines, bool Code = false, string Language = "")
{
    public string Text => string.Concat(Inlines.Select(x => x.Value));
}

// Deliberately small, data-only markup. No HTML, XAML, URI navigation or executable content.
public static class AdviceMarkup
{
    public static IReadOnlyList<AdviceBlock> Parse(string value)
    {
        if (value.Length > 24000) throw new ArgumentException("Response text is too long.");
        value = value.Replace("\r\n", "\n"); var lines = value.Split('\n');
        var blocks = new List<AdviceBlock>(); var prose = new List<string>();
        void Flush() { if (prose.Count == 0) return; blocks.Add(new(Inline(string.Join("\n", prose)))); prose.Clear(); }
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) && line.Length <= 27 &&
                line[3..].All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '+' or '#'))
            {
                int end = Array.FindIndex(lines, i + 1, l => l.Trim() == "```");
                if (end > i + 1)
                {
                    Flush(); blocks.Add(new(Array.AsReadOnly(new[] { new AdviceInline(string.Join("\n", lines[(i + 1)..end])) }), true, line[3..]));
                    i = end; continue;
                }
            }
            prose.Add(lines[i]);
        }
        Flush(); return blocks.AsReadOnly();
    }
    private static IReadOnlyList<AdviceInline> Inline(string value)
    {
        var result = new List<AdviceInline>(); int plain = 0;
        for (int i = 0; i < value.Length; i++)
        {
            string marker = value[i] == '`' ? "`" : i + 1 < value.Length && value[i] == '*' && value[i + 1] == '*' ? "**" : "";
            if (marker.Length == 0) continue;
            int end = value.IndexOf(marker, i + marker.Length, StringComparison.Ordinal);
            if (end <= i + marker.Length || value.AsSpan(i + marker.Length, end - i - marker.Length).Contains('\n')) continue;
            if (i > plain) result.Add(new(value[plain..i]));
            result.Add(new(value[(i + marker.Length)..end], marker == "`" ? AdviceInlineKind.Code : AdviceInlineKind.Strong));
            i = end + marker.Length - 1; plain = i + 1;
        }
        if (plain < value.Length) result.Add(new(value[plain..]));
        return result.AsReadOnly();
    }
}
