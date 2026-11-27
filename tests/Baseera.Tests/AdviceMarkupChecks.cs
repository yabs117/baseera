using Baseera.Core;

internal static class AdviceMarkupChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var inline = AdviceMarkup.Parse("Read **the status** and `0x80070005` first.").Single();
        check(inline.Text == "Read the status and 0x80070005 first." && inline.Inlines.Any(x => x.Kind == AdviceInlineKind.Strong && x.Value == "the status") &&
            inline.Inlines.Any(x => x.Kind == AdviceInlineKind.Code && x.Value == "0x80070005"), "Response markup preserves prose while exposing bold and inline code as inert data");
        var code = AdviceMarkup.Parse("Review first.\n```powershell\nGet-Item 'C:\\Program Files'\n```\nThen check again.");
        check(code.Count == 3 && code[1].Code && code[1].Language == "powershell" && code[1].Text == "Get-Item 'C:\\Program Files'" && code[2].Text == "Then check again.", "Fenced code keeps command contents and whitespace separate from prose");
        string unsafeText = "<script>alert(1)</script> [link](file:///private) <Button Command='Run' />";
        check(AdviceMarkup.Parse(unsafeText).Single().Text == unsafeText && AdviceMarkup.Parse("**unfinished `value").Single().Text == "**unfinished `value",
            "HTML, navigation syntax and unmatched formatting remain literal text");
        check(AdviceMarkup.Parse("مُشكلة **عربية** e\u0301 `👨‍👩‍👧‍👦`").Single().Text == "مُشكلة عربية e\u0301 👨‍👩‍👧‍👦" &&
            AdviceMarkup.Parse("```text\nunclosed").Single().Text == "```text\nunclosed", "Markup retains Unicode text elements and unfinished code fences without silently deleting evidence");
        bool rejected = false; try { AdviceMarkup.Parse(new string('x', 24001)); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Response markup input is bounded before parsing");
        rejected = false; try { AdviceMarkup.Arrange(new string('x', 24001)); } catch (ArgumentException) { rejected = true; }
        check(rejected, "Response arrangement input is bounded before grouping");

        Preserves(check, "افتح OneDrive من شريط المهام واقرأ رسالة الحالة الظاهرة.", "A plain Arabic step");
        var plain = AdviceMarkup.Arrange("افتح OneDrive من شريط المهام واقرأ رسالة الحالة الظاهرة.");
        check(plain.Count == 1 && !plain[0].List && plain[0].MarkerLength == 0 && plain[0].Start == 0 && plain[0].Length == "افتح OneDrive من شريط المهام واقرأ رسالة الحالة الظاهرة.".Length,
            "A plain step stays one paragraph and is not rewritten");

        string answer = "اقرأ **الحالة** ثم `0x10`.\n\n1. أول خطوة\n2. الخطوة الثانية\n\n- هل تظهر في ملف واحد؟\n- هل توجد رسالة؟";
        var parsed = AdviceMarkup.Parse(answer).Single();
        check(parsed.Text == "اقرأ الحالة ثم 0x10.\n\n1. أول خطوة\n2. الخطوة الثانية\n\n- هل تظهر في ملف واحد؟\n- هل توجد رسالة؟",
            "Arrangement starts from the same text markup already displays");
        var grouped = AdviceMarkup.Arrange(parsed.Text);
        check(grouped.Count == 5 && !grouped[0].List && grouped[0].BlankBefore == false && grouped[1].List && grouped[1].BlankBefore && grouped[2].List && !grouped[2].BlankBefore &&
            grouped[3].List && grouped[3].BlankBefore && grouped[4].List && !grouped[4].BlankBefore, "Paragraphs and lists keep their own spacing groups");
        check(Slice(parsed.Text, grouped[0]) == "اقرأ الحالة ثم 0x10." && Slice(parsed.Text, grouped[1]) == "1. أول خطوة" &&
            Slice(parsed.Text, grouped[2]) == "2. الخطوة الثانية" && Slice(parsed.Text, grouped[3]) == "- هل تظهر في ملف واحد؟" &&
            Slice(parsed.Text, grouped[4]) == "- هل توجد رسالة؟", "List markers and words stay in the original order");
        check(grouped[1].MarkerLength == "1. ".Length && grouped[3].MarkerLength == "- ".Length, "Hanging indent covers only the existing marker");
        Preserves(check, parsed.Text, "Arabic answer");

        string shaped = "١. افتح التطبيق\n٢) تحقق من المساحة\n(3) أعد المحاولة\n• سؤال أخير";
        var shapes = AdviceMarkup.Arrange(shaped);
        check(shapes.Count == 4 && shapes.All(x => x.List) && Slice(shaped, shapes[0]) == "١. افتح التطبيق" && shapes[0].MarkerLength == "١. ".Length &&
            Slice(shaped, shapes[1]) == "٢) تحقق من المساحة" && Slice(shaped, shapes[2]) == "(3) أعد المحاولة" && Slice(shaped, shapes[3]) == "• سؤال أخير",
            "Arabic-Indic, parenthesized and bullet markers stay visible");
        Preserves(check, shaped, "Shaped lists");

        string prose = "سطر أول\r\nما زال نفس الفقرة.\r\n\r\nفقرة جديدة";
        var lines = AdviceMarkup.Arrange(prose);
        check(lines.Count == 2 && Slice(prose, lines[0]) == "سطر أول\r\nما زال نفس الفقرة." && Slice(prose, lines[1]) == "فقرة جديدة" && lines[1].BlankBefore,
            "A single Windows line break stays inside the paragraph and a blank line starts another");
        Preserves(check, prose, "Windows paragraphs");

        string notLists = "بقي 1.5 GB متاحة.\n*ليست قائمة*\n-بدون مسافة\n---\n10:30 موعد\nNote. This stays prose.";
        var ordinary = AdviceMarkup.Arrange(notLists);
        check(ordinary.Count == 1 && !ordinary[0].List && Slice(notLists, ordinary[0]) == notLists, "Decimals, emphasis, rules and abbreviations are not turned into lists");
        Preserves(check, notLists, "Ordinary prose");

        string nested = "1. Parent line\n  - nested item\n    extra detail";
        var nest = AdviceMarkup.Arrange(nested);
        check(nest.Count == 3 && nest[0].List && nest[1].List && !nest[2].List && Slice(nested, nest[0]) == "1. Parent line" &&
            Slice(nested, nest[1]) == "  - nested item" && nest[1].MarkerLength == "  - ".Length && Slice(nested, nest[2]) == "    extra detail",
            "Nested markers and indented detail keep their original spaces");
        Preserves(check, nested, "Nested list");
    }

    private static string Slice(string value, AdviceArrangement part) => value.Substring(part.Start, part.Length);

    private static void Preserves(Action<bool, string> check, string value, string name)
    {
        var parts = AdviceMarkup.Arrange(value);
        var seen = new bool[value.Length];
        int cursor = 0; bool intact = true;
        foreach (var part in parts)
        {
            if (part.Start < cursor || part.Length <= 0 || part.Start + part.Length > value.Length) intact = false;
            if (part.MarkerLength < 0 || part.MarkerLength > part.Length || part.List != part.MarkerLength > 0) intact = false;
            for (int i = part.Start; i < part.Start + part.Length && i < seen.Length; i++)
            {
                if (seen[i]) intact = false;
                seen[i] = true;
            }
            cursor = part.Start + Math.Max(0, part.Length);
        }
        for (int i = 0; i < value.Length; i++) if (!seen[i] && value[i] is not '\n' and not '\r' and not ' ' and not '\t') intact = false;
        string words = string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        string kept = string.Concat(parts.Select(part => Slice(value, part)).SelectMany(slice => slice).Where(c => !char.IsWhiteSpace(c)));
        check(intact && words == kept, name + " keeps every word in order and drops only blank separators");
    }
}
