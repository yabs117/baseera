namespace Baseera.Core;

public enum AdviceInlineKind { Text, Strong, Code }
public sealed record AdviceInline(string Value, AdviceInlineKind Kind = AdviceInlineKind.Text);
public sealed record AdviceBlock(IReadOnlyList<AdviceInline> Inlines, bool Code = false, string Language = "")
{
    public string Text => string.Concat(Inlines.Select(x => x.Value));
}

// A display grouping over an existing string. Start/Length address that string; the slice is copied, never rewritten.
public readonly record struct AdviceArrangement(int Start, int Length, int MarkerLength, bool List, bool BlankBefore);

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

    // Groups paragraphs and lists for spacing and hanging indents. Every non-blank character stays in one slice, in order.
    public static IReadOnlyList<AdviceArrangement> Arrange(string value)
    {
        if (value.Length > 24000) throw new ArgumentException("Response text is too long.");
        var result = new List<AdviceArrangement>();
        int pendingStart = -1, pendingEnd = 0, pendingMarker = 0;
        bool pendingList = false, pendingBlank = false, gap = false;
        void Flush()
        {
            if (pendingStart < 0 || pendingEnd <= pendingStart) { pendingStart = -1; return; }
            result.Add(new(pendingStart, pendingEnd - pendingStart, pendingList ? pendingMarker : 0, pendingList, pendingBlank));
            pendingStart = -1;
        }
        int i = 0;
        while (i < value.Length)
        {
            int lineStart = i;
            while (i < value.Length && value[i] is not '\n' and not '\r') i++;
            int lineEnd = i;
            if (i < value.Length && value[i] == '\r') i++;
            if (i < value.Length && value[i] == '\n') i++;
            string line = value[lineStart..lineEnd];
            if (line.Trim().Length == 0) { Flush(); gap = true; continue; }
            if (TryListMarker(line, out int marker))
            {
                Flush();
                pendingStart = lineStart; pendingEnd = lineEnd; pendingMarker = marker; pendingList = true; pendingBlank = gap; gap = false;
            }
            else if (pendingStart >= 0 && !pendingList && !gap) pendingEnd = lineEnd;
            else
            {
                Flush();
                pendingStart = lineStart; pendingEnd = lineEnd; pendingMarker = 0; pendingList = false; pendingBlank = gap; gap = false;
            }
        }
        Flush();
        return result.AsReadOnly();
    }

    private static bool TryListMarker(string line, out int markerLength)
    {
        markerLength = 0;
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t') i++;
        if (i >= line.Length) return false;
        int content;
        char c = line[i];
        if (c is '-' or '*' or '+' or '•' or '·' or '–' or '—' or '▪' or '◦') content = i + 1;
        else if (c == '(' || char.IsDigit(c))
        {
            int digits = i;
            bool wrapped = line[digits] == '(';
            if (wrapped) digits++;
            int startDigits = digits;
            while (digits < line.Length && digits - startDigits < 3 && char.IsDigit(line[digits])) digits++;
            if (digits == startDigits || digits >= line.Length) return false;
            if (wrapped) { if (line[digits] != ')') return false; content = digits + 1; }
            else { if (line[digits] is not '.' and not ')') return false; content = digits + 1; }
        }
        else return false;
        if (content >= line.Length || line[content] is not ' ' and not '\t') return false;
        while (content < line.Length && line[content] is ' ' or '\t') content++;
        if (content >= line.Length) return false;
        markerLength = content;
        return true;
    }
}
