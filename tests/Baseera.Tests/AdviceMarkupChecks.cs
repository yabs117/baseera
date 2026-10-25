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
    }
}
