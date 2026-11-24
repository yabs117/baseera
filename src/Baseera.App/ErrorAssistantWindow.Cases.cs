using System.Windows.Automation;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using Baseera.Core;
using Baseera.App.Services;
using Baseera.App.ViewModels;

namespace Baseera.App;

internal sealed partial class ErrorAssistantWindow
{
    private readonly ApplicationServices services;
    private readonly RepairCaseViewModel caseWorkflow;
    private readonly bool customDiagnostics;
    private readonly CheckBox diagnosticServices = new(), diagnosticDrivers = new();
    private readonly Border workflowPanel = new();
    private readonly Expander workflowDetails = new();
    private readonly TextBox caseTitle = new() { Height = double.NaN, MinHeight = 38, Padding = new Thickness(10, 6, 10, 6) },
        caseObserved = new() { Height = double.NaN, MinHeight = 38, Padding = new Thickness(10, 6, 10, 6), FlowDirection = FlowDirection.LeftToRight, MaxLength = 16 };
    private readonly CheckBox caseStoreEvidence = new();
    private readonly StackPanel caseStages = new(), caseComparison = new();
    private Button caseSave = new(), caseRetest = new();
    private CaseEvidence? displayedEvidence;
    private bool closingSavedCase;
    private async Task<CaseEvidence> CaptureCaseEvidence(DiagnosticOptions options, CancellationToken token)
    {
        if (!customDiagnostics) return await services.Diagnostics.Capture(options, token);
        string text = await collectDiagnosticData(options, token);
        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, services.Cases.HostScope, options,
            DiagnosticPrivacy.Sanitize(text, Environment.UserName, Environment.MachineName), null, [], false);
    }
    private Border BuildCaseWorkflow()
    {
        var body = new StackPanel();
        Add(body, caseStages, 8); Add(body, Text(I18n.T("caseWorkflowHelp"), 12, true), 10);
        Add(body, Text(I18n.T("caseTitle"), 12), 4); AutomationProperties.SetAutomationId(caseTitle, "CaseTitle");
        AutomationProperties.SetName(caseTitle, I18n.T("caseTitle")); caseTitle.MaxLength = 160;
        caseTitle.SetBinding(TextBox.TextProperty, new Binding(nameof(RepairCaseViewModel.Title)) { Source = caseWorkflow, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); Add(body, caseTitle, 8);
        caseObserved.Tag = "yyyy-MM-dd HH:mm"; AutomationProperties.SetName(caseObserved, I18n.T("caseFirstObserved")); AutomationProperties.SetAutomationId(caseObserved, "CaseFirstObserved");
        caseObserved.TextChanged += (_, _) => { if (caseWorkflow.Current is { } current && caseObserved.Text != (current.FirstObservedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "")) caseWorkflow.Touch(); };
        Add(body, Text(I18n.T("caseFirstObserved"), 12), 4); Add(body, caseObserved, 8);
        caseStoreEvidence.Content = Text(I18n.T("caseStoreEvidence"), 12); AutomationProperties.SetAutomationId(caseStoreEvidence, "CaseStoreEvidence");
        caseStoreEvidence.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(RepairCaseViewModel.StoreEvidence)) { Source = caseWorkflow, Mode = BindingMode.TwoWay }); Add(body, caseStoreEvidence, 8);
        var actions = new WrapPanel();
        caseSave = Action("caseSave", async () => await caseWorkflow.Save(caseObserved.Text, diagnosticPreview.Text, CancellationToken.None)); AutomationProperties.SetAutomationId(caseSave, "CaseSave"); actions.Children.Add(caseSave);
        caseRetest = Action("caseRetest", async () => await RetestCase()); AutomationProperties.SetAutomationId(caseRetest, "CaseRetest"); actions.Children.Add(caseRetest);
        actions.Children.Add(Action("caseArchive", () => { if (Owner is MainWindow main) { Hide(); main.Navigate("cases"); main.Activate(); } })); Add(body, actions, 8);
        var status = Text("", 12, true); status.SetBinding(TextBlock.TextProperty, new Binding(nameof(RepairCaseViewModel.Status)) { Source = caseWorkflow }); AutomationProperties.SetAutomationId(status, "CaseStatus"); Add(body, status, 8);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(caseComparison, "CaseComparison"); Add(body, caseComparison, 0);
        workflowDetails.Header = I18n.T("caseWorkflow"); workflowDetails.Content = body;
        workflowPanel.Child = workflowDetails; workflowPanel.Padding = new Thickness(12); workflowPanel.CornerRadius = new CornerRadius(5); workflowPanel.Background = Brush("Surface"); workflowPanel.BorderBrush = Brush("Line"); workflowPanel.BorderThickness = new Thickness(1);
        AutomationProperties.SetAutomationId(workflowPanel, "CaseWorkflow"); RefreshCaseWorkflow(); return workflowPanel;
    }
    private void RefreshCaseWorkflow()
    {
        workflowPanel.Visibility = caseWorkflow.Current is null ? Visibility.Collapsed : Visibility.Visible;
        bool idle = !NetworkBusy && recorder?.Recording != true && !caseWorkflow.Saving;
        caseSave.IsEnabled = idle && caseWorkflow.Current is not null;
        caseRetest.IsEnabled = idle && caseWorkflow.LatestEvidence is { Sources.ExternalReport: false };
        caseTitle.IsEnabled = caseObserved.IsEnabled = caseStoreEvidence.IsEnabled = idle;
        caseStages.Children.Clear();
        var row = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        foreach (string stage in new[] { "caseDiscover", "caseAnalyze", "caseRepair", "caseVerify" })
        { var text = Text(I18n.T(stage), 12); text.Foreground = Brush(caseWorkflow.Stage == stage ? "Accent" : "Muted"); text.TextAlignment = TextAlignment.Center; row.Children.Add(text); }
        caseStages.Children.Add(row); caseComparison.Children.Clear();
        if (caseWorkflow.Current?.Retests.LastOrDefault() is { } retest)
        {
            Add(caseComparison, Text(I18n.T("caseResult-" + retest.Comparison.Status), 14), 6);
            foreach (var metric in retest.Comparison.Measurements) Add(caseComparison, Text($"{I18n.T(metric.Key)} {metric.Target}: {metric.Before:0.0} → {metric.After:0.0}", 12), 4);
            foreach (var service in retest.Comparison.Services) Add(caseComparison, Text($"{service.Name}: {I18n.T("service-" + service.Before)} → {I18n.T("service-" + service.After)}", 12), 4);
            foreach (string limit in retest.Comparison.Limits) Add(caseComparison, Text(I18n.T(limit), 11, true), 3);
        }
    }
    private async Task RetestCase()
    {
        if (NetworkBusy || recorder?.Recording == true || caseWorkflow.LatestEvidence is not { Sources.ExternalReport: false } baseline) return;
        using var tracked = services.Tasks.Begin("caseRetest", true);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(tracked.Token); diagnosticRequest = request; RefreshSmartControls();
        try
        {
            var after = await CaptureCaseEvidence(baseline.Sources, request.Token); request.Token.ThrowIfCancellationRequested();
            if (closed) return;
            caseWorkflow.Retest(after); displayedEvidence = after; diagnosticPreview.Text = after.Text; InvalidateDiagnosticConsent(); tracked.Complete();
            workflowDetails.IsExpanded = true; feedback.Text = I18n.T("caseNotVerifiedFix");
        }
        catch (OperationCanceledException) { tracked.Cancelled(); }
        catch (Exception) { tracked.Failed(); if (!closed) feedback.Text = I18n.T("caseRetestFailed"); }
        finally { diagnosticRequest = null; if (!closed) RefreshSmartControls(); }
    }
    internal bool PrepareSavedCase(RepairCaseDocument value)
    {
        if (NetworkBusy || recorder?.Recording == true || caseWorkflow.Saving) return false;
        if (caseWorkflow.Dirty && caseWorkflow.Current is { } pending && pending.Id != value.Id)
        { feedback.Text = I18n.T("caseFinishBeforeSwitch"); return false; }
        RepairCaseCodec.Validate(value); ClearConversation(false); caseWorkflow.Load(value);
        caseObserved.Text = value.FirstObservedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
        var sample = value.Evidence.LastOrDefault();
        if (sample is not null)
        {
            diagnosticSystem.IsChecked = sample.Sources.SystemInfo; diagnosticApplication.IsChecked = sample.Sources.ApplicationEvents; diagnosticEvents.IsChecked = sample.Sources.SystemEvents;
            diagnosticHealth.IsChecked = sample.Sources.DeviceHealth; diagnosticServices.IsChecked = sample.Sources.ServiceStatus;
            diagnosticDrivers.IsChecked = sample.Sources.Drivers;
            diagnosticRange.SelectedIndex = Array.IndexOf(new[] { 1, 6, 24, 72 }, sample.Sources.Hours); diagnosticMatch.Text = sample.Sources.MatchText;
            displayedEvidence = sample; diagnosticPreview.Text = sample.Text;
        }
        problem.Text = value.Analyses.FirstOrDefault()?.Description ?? ""; InvalidateDiagnosticConsent(); workflowDetails.IsExpanded = true; RefreshSmartControls(); return true;
    }
    internal bool ReviewInvestigation(string report, CaseEvidence? sample = null)
    {
        if (closed || NetworkBusy || recorder?.Recording == true) { feedback.Text = I18n.T("deviceBusy"); return false; }
        diagnosticSystem.IsChecked = sample?.Sources.SystemInfo == true; diagnosticApplication.IsChecked = sample?.Sources.ApplicationEvents == true; diagnosticEvents.IsChecked = sample?.Sources.SystemEvents == true;
        diagnosticHealth.IsChecked = sample?.Sources.DeviceHealth == true; diagnosticServices.IsChecked = sample?.Sources.ServiceStatus == true; diagnosticDrivers.IsChecked = sample?.Sources.Drivers == true;
        if (sample is not null) { diagnosticRange.SelectedIndex = Array.IndexOf(new[] { 1, 6, 24, 72 }, sample.Sources.Hours); diagnosticMatch.Text = sample.Sources.MatchText; }
        diagnosticPreview.Text = DiagnosticPrivacy.Sanitize(report, Environment.UserName, Environment.MachineName);
        displayedEvidence = sample ?? new(Guid.NewGuid(), DateTimeOffset.UtcNow, services.Cases.HostScope, new(ExternalReport: true), diagnosticPreview.Text, null, [], false);
        caseWorkflow.Capture(displayedEvidence); InvalidateDiagnosticConsent(); assistantTools.IsExpanded = true;
        if (string.IsNullOrWhiteSpace(problem.Text)) problem.Text = I18n.T("hubReviewQuestion");
        feedback.Text = I18n.T("hubReviewBeforeSend"); RefreshSmartControls(); return true;
    }
    private async void SaveCaseOnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (caseWorkflow.Saving) { e.Cancel = true; return; }
        if (e.Cancel || closingSavedCase || caseWorkflow.Current is not { Revision: > 0 } || !caseWorkflow.Dirty) return;
        e.Cancel = true;
        var decision = MessageBox.Show(this, I18n.T("caseSaveOnClose"), I18n.T("caseWorkflow"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        if (decision == MessageBoxResult.Cancel) return;
        if (decision == MessageBoxResult.Yes && !await caseWorkflow.Save(caseObserved.Text, diagnosticPreview.Text, CancellationToken.None)) return;
        closingSavedCase = true; Close();
    }
}
