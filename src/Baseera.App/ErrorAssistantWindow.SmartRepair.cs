using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal sealed partial class ErrorAssistantWindow
{
    private readonly CheckBox diagnosticSystem = new(), diagnosticApplication = new(), diagnosticEvents = new(), diagnosticShare = new();
    private readonly TextBox diagnosticPreview = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = DiagnosticPrivacy.MaxCharacters,
        Height = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight };
    private readonly StackPanel repairActions = new();
    private readonly TextBlock repairStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox diagnosticRange = new() { MinWidth = 160, SelectedIndex = 2 };
    private readonly TextBox diagnosticMatch = new() { MaxLength = 100, FlowDirection = FlowDirection.LeftToRight };
    private readonly StackPanel evidenceSummary = new(), sessionActivity = new();
    private readonly Border sessionPanel = new();
    private readonly Expander sessionDetails = new();
    private readonly TextBlock caseNotice = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox reportDiagnostics = new();
    private readonly SmartRepairSession repairSession = new();
    private string analyzedDescription = "";
    private ErrorImage? analyzedImage;
    private bool CurrentCase => repairSession.Advice is not null && problem.Text.Length == 0 && image is null && video is null;
    private Button reportButton = new(), resolvedButton = new(), unresolvedButton = new();
    private Button collectDiagnostics = new();
    private CancellationTokenSource? diagnosticRequest;
    private bool repairRunning;
    private string diagnosticRecipient = "";
    private readonly Func<DiagnosticOptions, CancellationToken, Task<string>> collectDiagnosticData;
    private readonly Func<string, ErrorImage?, string, string, CancellationToken, Task<ErrorAdvice>>? analyzeDiagnosticData;
    private readonly Func<string, RepairApproval?>? approveRepair;
    private readonly RepairRunner repairRunner;

    private static string DiagnosticRecipient
    {
        get
        {
            if (ChatGptAuth.Selected) return AiCatalog.Provider + "|chatgpt|" + Settings.Current.ChatGptAccount;
            try { return AiCatalog.Provider + "|api|" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Settings.GetKey(AiCatalog.Provider)))); }
            catch { return AiCatalog.Provider + "|api|unavailable"; }
        }
    }
    private Expander BuildDiagnostics()
    {
        var body = new StackPanel();
        Add(body, Text(I18n.T("smartDiagnosticsHelp"), 12, true));
        void Source(CheckBox check, string key)
        {
            check.Content = I18n.T(key); System.Windows.Automation.AutomationProperties.SetAutomationId(check, key);
            check.Checked += (_, _) => ClearDiagnostics(); check.Unchecked += (_, _) => ClearDiagnostics();
            Add(body, check, 4);
        }
        Source(diagnosticSystem, "smartSystemInfo"); Source(diagnosticApplication, "smartApplicationEvents"); Source(diagnosticEvents, "smartSystemEvents");
        Add(body, Text(I18n.T("smartTimeRange"), 12, true), 4);
        foreach (int hours in new[] { 1, 6, 24, 72 }) diagnosticRange.Items.Add(new ComboBoxItem { Content = string.Format(I18n.T("smartHours"), hours), Tag = hours });
        System.Windows.Automation.AutomationProperties.SetAutomationId(diagnosticRange, "SmartDiagnosticRange");
        diagnosticRange.SelectionChanged += (_, _) => ClearDiagnostics(); Add(body, diagnosticRange, 8);
        Add(body, Text(I18n.T("smartMatchHelp"), 12, true), 4);
        System.Windows.Automation.AutomationProperties.SetAutomationId(diagnosticMatch, "SmartDiagnosticMatch");
        diagnosticMatch.TextChanged += (_, _) => ClearDiagnostics(); Add(body, diagnosticMatch, 8);
        collectDiagnostics = Action("smartCollect", async () => await CollectDiagnostics());
        Add(body, collectDiagnostics, 8);
        System.Windows.Automation.AutomationProperties.SetAutomationId(evidenceSummary, "SmartEvidenceSummary");
        Add(body, evidenceSummary, 8);
        System.Windows.Automation.AutomationProperties.SetAutomationId(diagnosticPreview, "SmartDiagnosticPreview");
        diagnosticPreview.TextChanged += (_, _) => { InvalidateDiagnosticConsent(); RenderEvidence(); }; Add(body, diagnosticPreview, 8);
        diagnosticShare.Content = Text(I18n.T("smartShareConsent") + "\n" + AiProviderBadge.Name(AiCatalog.Provider, ChatGptAuth.Selected));
        System.Windows.Automation.AutomationProperties.SetAutomationId(diagnosticShare, "SmartDiagnosticConsent");
        diagnosticShare.Checked += (_, _) => diagnosticRecipient = DiagnosticRecipient;
        diagnosticShare.Unchecked += (_, _) => diagnosticRecipient = "";
        Add(body, diagnosticShare, 8);
        var clear = Action("smartClear", ClearDiagnostics); Add(body, clear, 0);
        var expander = new Expander { Header = I18n.T("smartDiagnostics"), Content = body, IsExpanded = false,
            Padding = new Thickness(12), Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(1) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(expander, "SmartDiagnostics"); return expander;
    }
    private void InvalidateDiagnosticConsent() { diagnosticShare.IsChecked = false; diagnosticRecipient = ""; }
    private void ClearDiagnostics() { diagnosticRequest?.Cancel(); diagnosticPreview.Clear(); InvalidateDiagnosticConsent(); }
    private void RefreshSmartControls()
    {
        diagnosticShare.Content = Text(I18n.T("smartShareConsent") + "\n" + AiProviderBadge.Name(AiCatalog.Provider, ChatGptAuth.Selected));
        bool idle = !NetworkBusy && recorder?.Recording != true;
        analyzeButton.IsEnabled = idle && !modelChoiceRequired && (!realAnalyzer || Configured());
        collectDiagnostics.IsEnabled = idle;
        foreach (var check in new[] { diagnosticSystem, diagnosticApplication, diagnosticEvents }) check.IsEnabled = idle;
        diagnosticRange.IsEnabled = idle;
        diagnosticMatch.IsEnabled = idle;
        reportButton.IsEnabled = idle && repairSession.Advice is not null && presentation?.IsTyping != true;
        resolvedButton.IsEnabled = unresolvedButton.IsEnabled = reportButton.IsEnabled && CurrentCase;
        caseNotice.Visibility = repairSession.Advice is not null && !CurrentCase ? Visibility.Visible : Visibility.Collapsed;
        repairActions.IsEnabled = idle && presentation?.IsTyping != true && CurrentCase;
        repairActions.Visibility = presentation?.IsTyping == true ? Visibility.Collapsed : Visibility.Visible;
        sessionPanel.Visibility = repairSession.Advice is null || repairSession.Advice.Message is not null || presentation?.IsTyping == true ? Visibility.Collapsed : Visibility.Visible;
        cancelButton.IsEnabled = active is not null || diagnosticRequest is not null || mediaRequest is not null || voiceRequest is not null;
        RefreshConversationControls(idle);
    }
    private async Task CollectDiagnostics()
    {
        if (active is not null || diagnosticRequest is not null || repairRunning) return;
        int hours = diagnosticRange.SelectedItem is ComboBoxItem { Tag: int selected } ? selected : 24;
        var options = new DiagnosticOptions(diagnosticSystem.IsChecked == true, diagnosticApplication.IsChecked == true, diagnosticEvents.IsChecked == true, hours, diagnosticMatch.Text);
        if (!options.SystemInfo && !options.ApplicationEvents && !options.SystemEvents) { feedback.Text = I18n.T("smartChooseSource"); return; }
        diagnosticPreview.Clear();
        using var request = new CancellationTokenSource(); diagnosticRequest = request; InvalidateDiagnosticConsent(); RefreshSmartControls();
        feedback.Text = I18n.T("smartCollecting");
        try
        {
            var collected = await collectDiagnosticData(options, request.Token); request.Token.ThrowIfCancellationRequested();
            if (closed) return;
            diagnosticPreview.Text = DiagnosticPrivacy.Sanitize(collected, Environment.UserName, Environment.MachineName);
            feedback.Text = I18n.T("smartReviewData");
        }
        catch (OperationCanceledException) { if (!closed) feedback.Text = I18n.T("cancel"); }
        catch (Exception ex) { if (!closed) feedback.Text = I18n.T("smartCollectFailed") + " " + ex.GetType().Name; }
        finally { diagnosticRequest = null; if (!closed) RefreshSmartControls(); }
    }
    private string ApprovedDiagnostics()
    {
        if (diagnosticShare.IsChecked != true) return "";
        if (diagnosticRecipient != DiagnosticRecipient) { InvalidateDiagnosticConsent(); throw new InvalidOperationException(I18n.T("smartReviewRecipient")); }
        string value = diagnosticPreview.Text;
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException(I18n.T("smartReviewData"));
        // Per-request approval. Editing, collecting, changing providers, or a new
        // analysis requires another explicit check. Only the displayed text is sent.
        InvalidateDiagnosticConsent(); return value;
    }
    private void ShowRepairActions(ErrorAdvice advice)
    {
        repairActions.Children.Clear();
        foreach (string id in RepairCatalog.ValidateRecommendations(advice.RepairActions ?? Array.Empty<string>()))
        {
            var action = RepairCatalog.Get(id); var section = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            Add(section, Text(I18n.T("repair-" + id), 15), 5); Add(section, Text(I18n.T("repairEffect-" + id), 12, true), 8);
            var review = Action("smartReviewRepair", async () => await RunRepair(action.Id));
            System.Windows.Automation.AutomationProperties.SetAutomationId(review, "SmartRepair-" + action.Id);
            var command = Text(action.Command, 12, true); command.FlowDirection = FlowDirection.LeftToRight;
            Add(section, command, 8); Add(section, review, 0);
            repairActions.Children.Add(new Border { Child = section, Padding = new Thickness(16, 4, 16, 16),
                Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(5),
                BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), Background = Brush("Surface") });
        }
        RefreshSmartControls();
    }
    private async Task RunRepair(string actionId)
    {
        if (closed || active is not null || diagnosticRequest is not null || repairRunning || presentation?.IsTyping == true || !CurrentCase) return;
        RepairApproval? approval;
        if (approveRepair is not null) approval = approveRepair(actionId);
        else { var dialog = new RepairApprovalWindow(this, actionId); approval = dialog.ShowDialog() == true ? dialog.Approval : null; }
        if (closed || !CurrentCase) return;
        if (approval is null) { repairSession.Record(actionId, "declined"); RenderSession(); repairStatus.Text = feedback.Text = I18n.T("smartRepairDeclined"); return; }
        repairRunning = true; RefreshSmartControls(); repairStatus.Text = I18n.T("smartRepairRunning"); feedback.Text = I18n.T("working");
        try
        {
            var execution = await repairRunner.Execute(approval, CancellationToken.None);
            repairSession.Record(actionId, execution.Started ? "completed" : "declined", execution.ExitCode);
            if (closed) return;
            RenderSession();
            repairStatus.Text = !execution.Started ? I18n.T("smartRepairDeclined") :
                I18n.T("repair-" + execution.ActionId) + " · " + I18n.T("smartExitCode") + ": " + execution.ExitCode + "\n" +
                I18n.T(execution.ExitCode == 3010 ? "smartRestartNeeded" : execution.ExitCode == 0 ? "smartRepairCompleted" : "smartRepairFailed");
            feedback.Text = execution.Started ? I18n.T("smartExitCode") + ": " + execution.ExitCode : I18n.T("smartRepairDeclined");
        }
        catch (Exception ex) { repairSession.Record(actionId, "execution-error"); if (!closed) { RenderSession(); repairStatus.Text = I18n.T("smartRepairFailed") + " " + ex.GetType().Name; feedback.Text = I18n.T("error"); } }
        finally { repairRunning = false; if (!closed) RefreshSmartControls(); }
    }

    private void RenderEvidence()
    {
        evidenceSummary.Children.Clear(); if (string.IsNullOrWhiteSpace(diagnosticPreview.Text)) return;
        var evidence = DiagnosticEvidence.Read(diagnosticPreview.Text);
        var body = new StackPanel();
        var heading = Text(I18n.T("smartEvidenceTitle"), 14); heading.FontWeight = FontWeights.SemiBold; Add(body, heading, 6);
        Add(body, Text(string.Format(I18n.T("smartEvidenceCounts"), evidence.EventCount, evidence.ErrorCodes.Count, evidence.CoverageNotices), 12, true), 6);
        foreach (var group in evidence.Groups.Take(6))
        {
            var item = Text($"{group.Provider} · {group.Channel} · ID {group.EventId} · ×{group.Count}\n{group.Latest:yyyy-MM-dd HH:mm:ss} UTC", 12);
            item.FlowDirection = FlowDirection.LeftToRight; Add(body, item, 7);
        }
        if (evidence.ErrorCodes.Count > 0)
        {
            var codes = Text(string.Join(" · ", evidence.ErrorCodes), 12); codes.FlowDirection = FlowDirection.LeftToRight; Add(body, codes, 6);
        }
        Add(body, Text(I18n.T("smartEvidenceLimits"), 12, true), 0);
        evidenceSummary.Children.Add(new Border { Child = body, Padding = new Thickness(14), CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1), BorderBrush = Brush("Line"), Background = Brush("Bg") });
    }

    private Border BuildSession()
    {
        var body = new StackPanel(); var title = Text(I18n.T("smartSessionTitle"), 15); title.FontWeight = FontWeights.SemiBold;
        Add(body, Text(I18n.T("smartSessionHelp"), 12, true), 12);
        caseNotice.Text = I18n.T("smartCaseChanged"); caseNotice.Foreground = Brush("Muted"); Add(body, caseNotice, 8);
        Add(body, sessionActivity, 8);
        var outcomes = new WrapPanel();
        resolvedButton = Action("smartResolved", () => RecordOutcome("resolved"));
        unresolvedButton = Action("smartUnresolved", () => RecordOutcome("still-present"));
        foreach (var button in new[] { resolvedButton, unresolvedButton }) { button.Margin = new Thickness(0, 0, 8, 8); outcomes.Children.Add(button); }
        Add(body, outcomes, 8);
        reportDiagnostics.Content = Text(I18n.T("smartReportInclude"), 12); reportDiagnostics.IsChecked = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(reportDiagnostics, "SmartReportDiagnostics");
        Add(body, reportDiagnostics, 8);
        reportButton = Action("smartSaveReport", SaveSessionReport); reportButton.HorizontalAlignment = HorizontalAlignment.Left; Add(body, reportButton, 6);
        Add(body, Text(I18n.T("smartReportPrivacy"), 12, true), 0);
        body.Margin = new Thickness(0, 12, 0, 0); sessionDetails.Header = title; sessionDetails.Content = body;
        System.Windows.Automation.AutomationProperties.SetAutomationId(sessionDetails, "SmartSessionDetails");
        sessionPanel.Child = sessionDetails; sessionPanel.Padding = new Thickness(16); sessionPanel.CornerRadius = new CornerRadius(5);
        sessionPanel.BorderBrush = Brush("Line"); sessionPanel.BorderThickness = new Thickness(1); sessionPanel.Background = Brush("Surface");
        sessionPanel.Visibility = Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetAutomationId(sessionPanel, "SmartRepairSession"); return sessionPanel;
    }

    private void RecordOutcome(string outcome)
    {
        if (active is not null || diagnosticRequest is not null || repairRunning || presentation?.IsTyping == true || !CurrentCase) return;
        repairSession.Observe(outcome); RenderSession();
        feedback.Text = I18n.T("smartObservationSaved");
        if (outcome == "still-present") { InvalidateDiagnosticConsent(); problem.Focus(); }
    }
    private void RenderSession()
    {
        sessionPanel.Visibility = repairSession.Advice is null || repairSession.Advice.Message is not null || presentation?.IsTyping == true ? Visibility.Collapsed : Visibility.Visible;
        if (repairSession.Activity.Count > 0) sessionDetails.IsExpanded = true;
        sessionActivity.Children.Clear();
        foreach (var entry in repairSession.Activity.TakeLast(5))
        {
            string state = entry.Status switch { "declined" => I18n.T("smartRepairDeclined"), "execution-error" => I18n.T("smartRepairFailed"), _ => I18n.T("smartExitCode") + ": " + entry.ExitCode };
            Add(sessionActivity, Text($"{entry.Time.ToLocalTime():t} · {I18n.T("repair-" + entry.ActionId)}\n{state}", 12), 8);
        }
        if (repairSession.Observations.LastOrDefault() is { } observation)
            Add(sessionActivity, Text(I18n.T(observation.Outcome == "resolved" ? "smartUserResolved" : "smartUserUnresolved"), 12, true), 0);
    }
    private void SaveSessionReport()
    {
        if (active is not null || diagnosticRequest is not null || repairRunning || presentation?.IsTyping == true || repairSession.Advice is null) return;
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "Baseera-Smart-Repair-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json", AddExtension = true, DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, repairSession.Export(reportDiagnostics.IsChecked == true), new System.Text.UTF8Encoding(false)); feedback.Text = I18n.T("smartReportSaved"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { feedback.Text = I18n.T("error") + " " + ex.GetType().Name; }
    }
}
