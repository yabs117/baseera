using System.Globalization;
using System.Text;
using System.Windows.Automation;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

public sealed partial class MainWindow
{
    internal static readonly string[] InvestigationCenters = ["doctor", "detective", "security", "network", "storage", "search", "timeline", "usb"];
    private string? pendingInvestigation;
    private CaseEvidence? pendingInvestigationSample;
    private string HubTime(DateTimeOffset time) => time.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo(Settings.Current.Language));
    private Button HubButton(string key, Func<CancellationToken, Task> work, bool primary = false)
    {
        var button = AsyncAction(key, work, primary); DeviceId(button, "Hub-" + page + "-" + key); return button;
    }
    private TextBox HubTextBox(string automationId, string text = "", bool readOnly = false, double height = double.NaN)
    {
        var box = new TextBox { Text = text, IsReadOnly = readOnly, Height = height, MinHeight = 38, Padding = new Thickness(10, 6, 10, 6),
            FlowDirection = FlowDirection.LeftToRight, TextWrapping = readOnly ? TextWrapping.Wrap : TextWrapping.NoWrap,
            AcceptsReturn = readOnly, VerticalScrollBarVisibility = readOnly ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
        DeviceId(box, automationId); AutomationProperties.SetName(box, I18n.T(I18n.Words.ContainsKey(automationId) ? automationId : "hubInput")); return box;
    }
    private static string HubValue(string value) => value.StartsWith("state:", StringComparison.Ordinal) ? I18n.T(value[6..]) : string.IsNullOrWhiteSpace(value) ? I18n.T("hubUnavailable") : value;
    private static string HubFactValue(InvestigationFact fact)
    {
        if (fact.Label == "hubReputation")
        {
            var counts = fact.Value.Split(" / ", StringSplitOptions.None);
            if (counts.Length == 2 && int.TryParse(counts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int flagged) &&
                int.TryParse(counts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int total) && flagged >= 0 && total >= flagged)
                return string.Format(I18n.T("hubFlaggedOf"), flagged, total);
        }
        return HubValue(fact.Value);
    }
    private static void ValidateHubReport(InvestigationReport report)
    {
        if (report is null || report.Facts is null || report.Limits is null || report.Facts.Count > 512 || report.Limits.Count > 40 || report.Kind is null || report.Kind.Length > 100 ||
            report.Facts.Any(f => f is null || f.Label is null || f.Label.Length > 100 || f.Value is null || f.Value.Length > 4096 || f.Detail is null || f.Detail.Length > 4096) || report.Limits.Any(s => s is null || s.Length > 100))
            throw new InvalidDataException("Invalid investigation report.");
    }
    private string HubReportText(InvestigationReport report)
    {
        ValidateHubReport(report); var text = new StringBuilder(I18n.T(report.Kind) + "\n" + report.Time.ToString("O") + "\n");
        foreach (var fact in report.Facts.Take(150)) text.AppendLine(I18n.T(fact.Label) + ": " + HubFactValue(fact) + " " + (I18n.Words.ContainsKey(fact.Detail) ? I18n.T(fact.Detail) : fact.Detail));
        foreach (string limit in report.Limits) text.AppendLine(I18n.T(limit));
        return DiagnosticPrivacy.Sanitize(text.ToString(), Environment.UserName, Environment.MachineName);
    }
    private void HubReport(StackPanel body, InvestigationReport report)
    {
        ValidateHubReport(report); var stamp = Text(HubTime(report.Time), 11, true); DeviceId(stamp, "HubReportTime"); body.Children.Add(stamp);
        foreach (var fact in report.Facts.Take(200))
        {
            var row = Stack(); row.Children.Add(Text(I18n.T(fact.Label), 12, true)); row.Children.Add(Text(HubFactValue(fact), 15));
            if (fact.Detail.Length > 0) row.Children.Add(Text(I18n.Words.ContainsKey(fact.Detail) ? I18n.T(fact.Detail) : fact.Detail, 12, true)); body.Children.Add(Card(row, 14));
        }
        foreach (string limit in report.Limits.Distinct()) body.Children.Add(Text(I18n.T(limit), 12, true));
        var preview = new Expander { Header = I18n.T("hubReportPreview"), Content = HubTextBox("HubReportPreview", HubReportText(report), true, 220), Margin = new Thickness(0, 8, 0, 16) }; body.Children.Add(preview);
    }
    private void HubShare(StackPanel body, string text, CaseEvidence? sample = null, string suffix = "")
    {
        var discuss = Action("hubDiscuss", () =>
        {
            pendingDeviceReview = null; pendingInvestigation = text; pendingInvestigationSample = sample; ShowErrorAssistant();
        });
        discuss.IsEnabled = Settings.Current.ErrorAssistantEnabled; DeviceId(discuss, "Hub-" + page + "-share" + suffix); body.Children.Add(discuss);
        body.Children.Add(Text(I18n.T("hubReviewBeforeSend"), 12, true));
    }
}
