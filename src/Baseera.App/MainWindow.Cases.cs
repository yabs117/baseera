using System.Windows.Automation;
using Baseera.Core;
using Baseera.App.Services;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private Action? drawTasks;
    private void OnTasksChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.InvokeAsync(() => { if (page == "activity") drawTasks?.Invoke(); });
    }
    private void TasksPage(StackPanel body)
    {
        Heading(body, "activity", "taskHelp"); var rows = Stack(); body.Children.Add(rows);
        drawTasks = () =>
        {
            rows.Children.Clear(); var tasks = Services.Tasks.Items;
            if (tasks.Count == 0) rows.Children.Add(Text(I18n.T("taskEmpty"), 13, true));
            foreach (var item in tasks)
            {
                var row = Stack(); row.Children.Add(Text(I18n.T(item.Kind) + " · " + I18n.T("task-" + item.State), 14));
                row.Children.Add(Text(item.Started.ToLocalTime().ToString("g") + (item.Progress is { } progress ? " · " + progress.ToString("P0") : ""), 11, true));
                if (item.State == "running")
                {
                    if (item.CanCancel) { var cancel = Action("cancel", () => Services.Tasks.Cancel(item.Id)); AutomationProperties.SetName(cancel, I18n.T("cancel") + ": " + I18n.T(item.Kind)); AutomationProperties.SetAutomationId(cancel, "TaskCancel-" + item.Id); row.Children.Add(cancel); }
                    else row.Children.Add(Text(I18n.T("taskNativeWait"), 12, true));
                }
                rows.Children.Add(Card(row, 14));
            }
        }; drawTasks();
    }
    private void CasesPage(StackPanel body)
    {
        Heading(body, "cases", "caseArchiveHelp");
        var actions = new WrapPanel(); var list = new ComboBox { MinWidth = 260, Margin = new Thickness(0, 8, 0, 12) };
        var details = Stack(); var message = Text("", 12, true); body.Children.Add(actions); body.Children.Add(message); body.Children.Add(list); body.Children.Add(details);
        System.Windows.Automation.AutomationProperties.SetAutomationId(list, "CaseArchivePicker");
        var refresh = Action("refresh", async () =>
        {
            try
            {
                using var job = Services.Tasks.Begin("cases", true, lifetime.Token);
                var values = await Services.Cases.List(job.Token); job.Complete(); list.ItemsSource = values.Select(v => new ComboBoxItem { Content = v.Title, Tag = v }).ToArray(); message.Text = values.Count == 0 ? I18n.T("caseArchiveEmpty") : "";
                if (Services.Cases.UnreadableCount > 0) message.Text += " " + I18n.T("caseUnreadable") + " " + Services.Cases.UnreadableCount;
                if (Services.Cases.ListLimited) message.Text += " " + I18n.T("caseListLimited");
                if (values.Count > 0) list.SelectedIndex = 0;
            }
            catch (Exception) { message.Text = I18n.T("caseReadFailed"); }
        }); AutomationProperties.SetAutomationId(refresh, "CaseArchiveRefresh"); actions.Children.Add(refresh);
        actions.Children.Add(Action("caseNew", () => OpenCaseWorkspace(RepairCaseDocument.Create(I18n.T("caseUntitled")))));
        var prepare = Action("casePrepare", () => { if (list.SelectedItem is ComboBoxItem { Tag: RepairCaseDocument value }) OpenCaseWorkspace(value); }); AutomationProperties.SetAutomationId(prepare, "CasePrepare"); actions.Children.Add(prepare);
        list.SelectionChanged += (_, _) =>
        {
            details.Children.Clear(); if (list.SelectedItem is not ComboBoxItem { Tag: RepairCaseDocument value }) return;
            details.Children.Add(Text(value.Title, 18)); details.Children.Add(Text($"{I18n.T("caseCreated")}: {value.CreatedUtc.ToLocalTime():g}\n{I18n.T("caseFirstObserved")}: {value.FirstObservedUtc?.ToLocalTime().ToString("g") ?? I18n.T("deviceUnknown")}", 12, true));
            details.Children.Add(Text(I18n.T("caseLoadedReadOnly"), 12, true));
            foreach (var analysis in value.Analyses.TakeLast(5)) details.Children.Add(Card(Text($"{analysis.Time.ToLocalTime():g}\n{analysis.Summary}\n{analysis.Cause}\n" + string.Join("\n", analysis.Steps.Select((s, i) => $"{i + 1}. {s}")) + "\n" + analysis.Confidence, 13), 14));
            foreach (var action in value.Actions.TakeLast(20)) details.Children.Add(Text($"{action.Time.ToLocalTime():g} · {I18n.T("repair-" + action.ActionId)} · {I18n.T("task-" + action.Status)} · {action.ExitCode}", 12));
            foreach (var retest in value.Retests.TakeLast(10))
            {
                var row = Stack(); row.Children.Add(Text($"{retest.Time.ToLocalTime():g} · {I18n.T("caseResult-" + retest.Comparison.Status)}", 14));
                foreach (var change in retest.Comparison.Measurements) row.Children.Add(Text($"{I18n.T(change.Key)} {change.Target}: {change.Before:0.0} → {change.After:0.0}", 12));
                foreach (var change in retest.Comparison.Services) row.Children.Add(Text($"{change.Name}: {I18n.T("service-" + change.Before)} → {I18n.T("service-" + change.After)}", 12));
                foreach (string limit in retest.Comparison.Limits) row.Children.Add(Text(I18n.T(limit), 11, true)); details.Children.Add(Card(row, 14));
            }
        };
        if (!smokeMode) _ = Dispatcher.InvokeAsync(() => refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
    }
    internal void OpenCaseWorkspace(RepairCaseDocument value)
    {
        if (errorAssistant is null)
        {
            var created = new ErrorAssistantWindow(this); errorAssistant = created; created.Closed += (_, _) => { if (ReferenceEquals(errorAssistant, created)) errorAssistant = null; }; created.Show();
        }
        errorAssistant.PrepareSavedCase(value);
        if (!errorAssistant.IsVisible) errorAssistant.Show(); errorAssistant.Activate();
    }
}
