using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal sealed partial class ErrorAssistantWindow
{
    private readonly CheckBox diagnosticSystem = new(), diagnosticApplication = new(), diagnosticEvents = new(), diagnosticShare = new();
    private readonly TextBox diagnosticPreview = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = DiagnosticPrivacy.MaxCharacters,
        Height = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight };
    private readonly StackPanel repairActions = new();
    private readonly TextBlock repairStatus = new() { TextWrapping = TextWrapping.Wrap };
    private Button collectDiagnostics = new();
    private CancellationTokenSource? diagnosticRequest;
    private bool repairRunning;
    private string diagnosticRecipient = "";
    private readonly Func<DiagnosticOptions, CancellationToken, Task<string>> collectDiagnosticData;
    private readonly Func<string, ErrorImage?, string, string, CancellationToken, Task<ErrorAdvice>>? analyzeDiagnosticData;
    private readonly Func<string, RepairApproval?>? approveRepair;
    private readonly RepairRunner repairRunner;

    private static string DiagnosticRecipient => AiCatalog.Provider + "|" + (ChatGptAuth.Selected ? "chatgpt|" + Settings.Current.ChatGptAccount : "api");
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
        collectDiagnostics = Action("smartCollect", async () => await CollectDiagnostics());
        Add(body, collectDiagnostics, 8);
        System.Windows.Automation.AutomationProperties.SetAutomationId(diagnosticPreview, "SmartDiagnosticPreview");
        diagnosticPreview.TextChanged += (_, _) => InvalidateDiagnosticConsent(); Add(body, diagnosticPreview, 8);
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
        bool idle = active is null && diagnosticRequest is null && !repairRunning;
        analyzeButton.IsEnabled = idle && (!realAnalyzer || Configured());
        collectDiagnostics.IsEnabled = idle;
        foreach (var check in new[] { diagnosticSystem, diagnosticApplication, diagnosticEvents }) check.IsEnabled = idle;
        repairActions.IsEnabled = idle && presentation?.IsTyping != true;
        cancelButton.IsEnabled = active is not null || diagnosticRequest is not null;
    }
    private async Task CollectDiagnostics()
    {
        if (active is not null || diagnosticRequest is not null || repairRunning) return;
        var options = new DiagnosticOptions(diagnosticSystem.IsChecked == true, diagnosticApplication.IsChecked == true, diagnosticEvents.IsChecked == true);
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
            Add(section, review, 0); repairActions.Children.Add(section);
        }
        RefreshSmartControls();
    }
    private async Task RunRepair(string actionId)
    {
        if (active is not null || diagnosticRequest is not null || repairRunning || presentation?.IsTyping == true) return;
        RepairApproval? approval;
        if (approveRepair is not null) approval = approveRepair(actionId);
        else { var dialog = new RepairApprovalWindow(this, actionId); approval = dialog.ShowDialog() == true ? dialog.Approval : null; }
        if (approval is null) { repairStatus.Text = feedback.Text = I18n.T("smartRepairDeclined"); return; }
        repairRunning = true; RefreshSmartControls(); repairStatus.Text = I18n.T("smartRepairRunning"); feedback.Text = I18n.T("working");
        try
        {
            var execution = await repairRunner.Execute(approval, CancellationToken.None);
            if (closed) return;
            repairStatus.Text = !execution.Started ? I18n.T("smartRepairDeclined") :
                I18n.T("repair-" + execution.ActionId) + " · " + I18n.T("smartExitCode") + ": " + execution.ExitCode + "\n" +
                I18n.T(execution.ExitCode == 3010 ? "smartRestartNeeded" : execution.ExitCode == 0 ? "smartRepairCompleted" : "smartRepairFailed");
            feedback.Text = execution.Started ? I18n.T("smartExitCode") + ": " + execution.ExitCode : I18n.T("smartRepairDeclined");
        }
        catch (Exception ex) { if (!closed) { repairStatus.Text = I18n.T("smartRepairFailed") + " " + ex.GetType().Name; feedback.Text = I18n.T("error"); } }
        finally { repairRunning = false; if (!closed) RefreshSmartControls(); }
    }
}
