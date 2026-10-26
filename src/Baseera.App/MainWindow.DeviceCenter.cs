using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Automation;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer deviceTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private CancellationTokenSource? deviceRequest;
    private int deviceRevision;
    private DeviceSnapshot? deviceSnapshot, pendingDeviceReview;
    private Action? drawDevice;
    private Button? deviceRefresh;
    private TextBlock? deviceStatus;
    private string deviceError = "", processFilter = "";
    private bool processByMemory;
    internal Func<bool, CancellationToken, Task<DeviceSnapshot>>? DeviceSourceOverride { get; set; }
    private static void DeviceId(DependencyObject element, string id) => AutomationProperties.SetAutomationId(element, id);
    private bool DevicePage => page is "home" or "health" or "performance";
    private void StopDeviceMonitor()
    {
        deviceTimer.Stop(); deviceRevision++; deviceRequest?.Cancel(); deviceRequest = null; drawDevice = null; deviceRefresh = null; deviceStatus = null;
    }
    private void StartDeviceMonitor()
    {
        if (smokeMode || !IsLoaded || !Settings.Current.Onboarded || !DevicePage || !Settings.Current.AutoRefreshDevice) return;
        deviceTimer.Start(); _ = RefreshDevice();
    }
    internal async Task RefreshDevice()
    {
        if (!DevicePage || deviceRequest is not null) return;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); deviceRequest = request;
        int revision = deviceRevision; deviceError = ""; if (deviceRefresh is not null) deviceRefresh.IsEnabled = false;
        if (deviceStatus is not null) deviceStatus.Text = I18n.T("deviceLoading");
        try
        {
            var collected = await (DeviceSourceOverride?.Invoke(page == "performance", request.Token) ?? DeviceMonitor.Collect(page == "performance", request.Token));
            request.Token.ThrowIfCancellationRequested(); if (revision != deviceRevision || !DevicePage) return;
            deviceSnapshot = collected; drawDevice?.Invoke();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (revision == deviceRevision) { deviceError = I18n.T("deviceReadFailed"); if (deviceStatus is not null) deviceStatus.Text = deviceError; } }
        finally
        {
            if (ReferenceEquals(deviceRequest, request))
            {
                deviceRequest = null; if (deviceRefresh is not null) deviceRefresh.IsEnabled = true;
                if (deviceStatus is not null && deviceError.Length == 0) deviceStatus.Text = SnapshotTime();
            }
        }
    }
    private string SnapshotTime() => deviceSnapshot is null ? I18n.T("deviceNoData") : string.Format(I18n.T("deviceLastRead"), deviceSnapshot.CollectedUtc.ToLocalTime().ToString("HH:mm:ss"));
    private static string SizeText(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("0.0") + " GiB";
    private string Metric(double? value) => value?.ToString("0.0") + (value is null ? I18n.T("deviceUnknown") : "%");
    private string HealthState() => deviceSnapshot is not { } snapshot ? I18n.T("deviceUnknown") :
        I18n.T(snapshot.Findings.Count > 0 ? "deviceNeedsReview" : snapshot.CpuPercent is null || snapshot.Memory?.UsedPercent is null || !snapshot.Volumes.Any(v => v.System && v.FreePercent is not null) || snapshot.Volumes.Any(v => v.FreePercent is null) || snapshot.Notices.Count > 0 ? "devicePartial" : "deviceWithinRange");
    private void DeviceHeading(StackPanel body, string title, string subtitle)
    {
        Heading(body, title, subtitle);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 16), LastChildFill = true };
        deviceRefresh = Action("deviceRefresh", async () => await RefreshDevice()); DeviceId(deviceRefresh, "DeviceRefresh"); DockPanel.SetDock(deviceRefresh, Dock.Right); toolbar.Children.Add(deviceRefresh);
        deviceStatus = Text(SnapshotTime(), 11, true); deviceStatus.VerticalAlignment = VerticalAlignment.Center; deviceStatus.Margin = new Thickness(0); DeviceId(deviceStatus, "DeviceTimestamp"); toolbar.Children.Add(deviceStatus); body.Children.Add(toolbar);
    }
    private Border MetricCard(string label, string icon, string value, string detail, double? percentage, string id, Action action)
    {
        var content = new Grid(); content.RowDefinitions.Add(new()); content.RowDefinitions.Add(new()); content.RowDefinitions.Add(new());
        var heading = new DockPanel(); var glyph = Icons.Create(icon, 18, "Muted"); DockPanel.SetDock(glyph, Dock.Right); heading.Children.Add(glyph); heading.Children.Add(Text(I18n.T(label), 12, true)); content.Children.Add(heading);
        var reading = Text(value, 23); reading.FontWeight = HeadingWeight; reading.Margin = new Thickness(0, 8, 0, 4); Grid.SetRow(reading, 1); content.Children.Add(reading);
        if (label != "health") { reading.FlowDirection = FlowDirection.LeftToRight; reading.TextAlignment = Arabic ? TextAlignment.Right : TextAlignment.Left; }
        var bottom = Stack(); var caption = Text(detail, 11, true); caption.Margin = new Thickness(0); bottom.Children.Add(caption);
        if (label is "deviceMemory" or "deviceStorage") { caption.FlowDirection = FlowDirection.LeftToRight; caption.TextAlignment = Arabic ? TextAlignment.Right : TextAlignment.Left; }
        if (percentage is { } progress)
        {
            progress = Math.Clamp(progress, 0, 100);
            var meter = new Grid { Height = 3, Background = Resource("Line"), Margin = new Thickness(0, 10, 0, 0), IsHitTestVisible = false };
            meter.ColumnDefinitions.Add(new() { Width = new GridLength(progress, GridUnitType.Star) }); meter.ColumnDefinitions.Add(new() { Width = new GridLength(100 - progress, GridUnitType.Star) });
            meter.Children.Add(new Border { Background = Resource("Accent") }); bottom.Children.Add(meter);
        }
        Grid.SetRow(bottom, 2); content.Children.Add(bottom);
        var button = new Button { Content = content, Padding = new Thickness(18), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Resource("Surface"), BorderBrush = Resource("Line"), BorderThickness = new Thickness(1), MinHeight = 134, Margin = new Thickness(0, 0, 10, 10) };
        DeviceId(button, id); AutomationProperties.SetName(button, I18n.T(label) + ": " + value); button.Click += (_, _) => action();
        return new Border { Child = button };
    }
    private void DrawMetrics(UniformGrid grid, bool performance = false)
    {
        grid.Children.Clear(); var snapshot = deviceSnapshot;
        var system = snapshot?.Volumes.FirstOrDefault(v => v.System && v.FreePercent is not null);
        if (!performance) grid.Children.Add(MetricCard("health", "health", HealthState(), I18n.T("deviceSampled"), null, "DeviceHealthCard", () => Navigate("health")));
        grid.Children.Add(MetricCard("deviceCpu", "cpu", Metric(snapshot?.CpuPercent), I18n.T("deviceCpuCaption"), snapshot?.CpuPercent, "DeviceCpuCard", () => Navigate("performance")));
        grid.Children.Add(MetricCard("deviceMemory", "memory", Metric(snapshot?.Memory?.UsedPercent), snapshot?.Memory is { UsedPercent: not null } memory ? SizeText(memory.Total - memory.Available) + " / " + SizeText(memory.Total) : I18n.T("deviceUnknown"), snapshot?.Memory?.UsedPercent, "DeviceMemoryCard", () => Navigate("performance")));
        if (!performance) grid.Children.Add(MetricCard("deviceStorage", "disk", system is null ? I18n.T("deviceUnknown") : SizeText(system.Free), system is null ? I18n.T("deviceDiskLimits") : system.Name + " · " + I18n.T("deviceFree") + " / " + SizeText(system.Total), system?.FreePercent is { } free ? 100 - free : null, "DeviceStorageCard", () => Navigate("health")));
    }
    private Button QuickAccess(string key, Action action, string? hint = null)
    {
        var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(38) }); row.ColumnDefinitions.Add(new());
        var glyph = Icons.Create(key, 20, "Muted"); row.Children.Add(glyph);
        var copy = Stack(); var title = Text(I18n.T(key), 13); title.Margin = new Thickness(0); copy.Children.Add(title);
        if (hint is not null) { var text = Text(hint, 10, true); text.Margin = new Thickness(0, 4, 0, 0); copy.Children.Add(text); }
        Grid.SetColumn(copy, 1); row.Children.Add(copy);
        var button = new Button { Content = row, Padding = new Thickness(14), Margin = new Thickness(0, 0, 10, 10), HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = IsEnabledFeature(key), ToolTip = I18n.T(IsEnabledFeature(key) ? key + "Short" : "disabledDesc") };
        ToolTipService.SetShowOnDisabled(button, true); DeviceId(button, "Quick-" + key); AutomationProperties.SetName(button, I18n.T(key)); button.Click += (_, _) => action(); return button;
    }
    private void ControlHome(StackPanel body)
    {
        DeviceHeading(body, "deviceCenter", "deviceOverviewDesc");
        var metrics = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 2) }; DeviceId(metrics, "DeviceMetrics"); body.Children.Add(metrics);
        var observations = Text("", 12, true); DeviceId(observations, "DeviceObservationCount"); body.Children.Add(observations);
        SectionHeading(body, "deviceQuick"); var quick = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 4) };
        quick.Children.Add(QuickAccess("errors", ReviewDeviceWithAssistant, I18n.T("deviceAnalyze")));
        quick.Children.Add(QuickAccess("performance", () => Navigate("performance")));
        foreach (string key in new[] { "scan", "translate", "updates", "convert", "search" }) quick.Children.Add(QuickAccess(key, () => Navigate(key), key == "updates" ? I18n.T("deviceUpdatesOnRequest") : null));
        body.Children.Add(quick); Help(body, "deviceLocalOnly");
        SectionHeading(body, "hubCenters"); var centers = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 4) };
        foreach (string center in InvestigationCenters.Except(Tools)) centers.Children.Add(QuickAccess(center, () => Navigate(center), I18n.T(center + "Short"))); body.Children.Add(centers);
        drawDevice = () => { DrawMetrics(metrics); observations.Text = deviceSnapshot is null ? I18n.T("deviceNoData") : string.Format(I18n.T("deviceObservations"), deviceSnapshot.Findings.Count); }; drawDevice();
    }
    private void DeviceHealthPage(StackPanel body)
    {
        DeviceHeading(body, "health", "deviceHealthDesc");
        var metrics = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 4) }; body.Children.Add(metrics);
        var findings = Stack(); DeviceId(findings, "DeviceFindings"); body.Children.Add(findings);
        var disks = Stack(); DeviceId(disks, "DeviceVolumes"); body.Children.Add(disks);
        var analyze = Action("deviceAnalyze", ReviewDeviceWithAssistant, true); analyze.HorizontalAlignment = HorizontalAlignment.Left; DeviceId(analyze, "DeviceReviewWithAi"); body.Children.Add(analyze);
        Help(body, "deviceFindingLimits");
        drawDevice = () =>
        {
            DrawMetrics(metrics); findings.Children.Clear(); disks.Children.Clear(); SectionHeading(findings, "deviceReview");
            if (deviceSnapshot is not { } snapshot) { findings.Children.Add(Text(I18n.T("deviceNoData"), 12, true)); analyze.IsEnabled = false; return; }
            analyze.IsEnabled = Settings.Current.ErrorAssistantEnabled;
            if (snapshot.Findings.Count == 0) findings.Children.Add(Text(I18n.T("deviceNoFindings"), 12, true));
            foreach (var finding in snapshot.Findings)
            {
                var row = Stack(); row.Children.Add(Text(I18n.T(finding.Code) + (finding.Target.Length == 0 ? "" : " · " + finding.Target), 14));
                row.Children.Add(Text(I18n.T(finding.Code + "Help"), 12, true)); findings.Children.Add(Card(row, 16));
            }
            foreach (string notice in snapshot.Notices) findings.Children.Add(Text(I18n.T(notice), 11, true));
            SectionHeading(disks, "deviceStorage");
            foreach (var volume in snapshot.Volumes)
            {
                var row = Text(volume.Name + " · " + SizeText(volume.Free) + " " + I18n.T("deviceFree") + " / " + SizeText(volume.Total), 13); row.FlowDirection = FlowDirection.LeftToRight; row.TextAlignment = Arabic ? TextAlignment.Right : TextAlignment.Left; disks.Children.Add(Card(row, 14));
            }
            disks.Children.Add(Text(I18n.T("deviceDiskLimits"), 11, true));
        }; drawDevice();
    }
    private void PerformancePage(StackPanel body)
    {
        DeviceHeading(body, "performance", "devicePerformanceDesc");
        var metrics = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 4) }; body.Children.Add(metrics);
        SectionHeading(body, "deviceProcesses");
        var filter = new TextBox { Text = processFilter, Tag = I18n.T("deviceProcessFilter"), MaxLength = 120, Margin = new Thickness(0, 0, 0, 8) }; DeviceId(filter, "DeviceProcessFilter"); body.Children.Add(filter);
        var sort = new ComboBox { ItemsSource = new[] { I18n.T("deviceSortCpu"), I18n.T("deviceSortMemory") }, SelectedIndex = processByMemory ? 1 : 0, Margin = new Thickness(0, 0, 0, 10), MinWidth = 220, HorizontalAlignment = HorizontalAlignment.Left }; DeviceId(sort, "DeviceProcessSort"); body.Children.Add(sort);
        var processes = Stack(); DeviceId(processes, "DeviceProcesses"); body.Children.Add(processes); Help(body, "deviceProcessLimits");
        SectionHeading(body, "deviceStartup"); body.Children.Add(Text(I18n.T("deviceStartupLimits"), 12, true));
        var manage = Action("deviceStartupSettings", () =>
        {
            if (smokeMode) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
            catch { SetOperationStatus("error"); }
        }); manage.HorizontalAlignment = HorizontalAlignment.Left; body.Children.Add(manage);
        var startup = Stack(); DeviceId(startup, "DeviceStartup"); body.Children.Add(startup);
        var notices = Text("", 11, true); body.Children.Add(notices);
        void DrawProcesses()
        {
            processes.Children.Clear();
            if (deviceSnapshot?.PerformanceIncluded != true) { processes.Children.Add(Text(I18n.T("deviceNoData"), 12, true)); return; }
            var all = deviceSnapshot?.PerformanceIncluded == true ? deviceSnapshot.Processes : [];
            var filtered = all.Where(p => (p.Name + " " + p.Id).Contains(processFilter, StringComparison.CurrentCultureIgnoreCase));
            var sorted = processByMemory ? filtered.OrderByDescending(p => p.WorkingSet) : filtered.OrderByDescending(p => p.CpuPercent ?? -1).ThenByDescending(p => p.WorkingSet);
            var visible = sorted.Take(12).ToArray();
            foreach (var process in visible)
            {
                var row = Text(process.Name + " · PID " + process.Id + "\nCPU " + Metric(process.CpuPercent) + " · " + (process.WorkingSet / 1048576d).ToString("0") + " MiB", 12); row.FlowDirection = FlowDirection.LeftToRight; processes.Children.Add(Card(row, 12));
            }
            if (visible.Length == 0) processes.Children.Add(Text(I18n.T("deviceProcessEmpty"), 12, true));
        }
        filter.TextChanged += (_, _) => { processFilter = filter.Text.Trim(); DrawProcesses(); };
        sort.SelectionChanged += (_, _) => { processByMemory = sort.SelectedIndex == 1; DrawProcesses(); };
        drawDevice = () =>
        {
            DrawMetrics(metrics, true); DrawProcesses(); startup.Children.Clear();
            if (deviceSnapshot?.PerformanceIncluded != true) { startup.Children.Add(Text(I18n.T("deviceNoData"), 12, true)); return; }
            foreach (var item in deviceSnapshot.Startup) startup.Children.Add(Card(Text(item.Name + " · " + I18n.T(item.Source), 12), 12));
            if (deviceSnapshot.Startup.Count == 0) startup.Children.Add(Text(I18n.T("deviceStartupEmpty"), 12, true));
            notices.Text = string.Join("\n", deviceSnapshot.Notices.Select(I18n.T));
        }; drawDevice();
    }
    private void ReviewDeviceWithAssistant()
    {
        if (!Settings.Current.ErrorAssistantEnabled) { SetOperationStatus("disabledDesc"); return; }
        pendingDeviceReview = deviceSnapshot; ShowErrorAssistant();
    }
    private void ApplyPendingDeviceReview(ErrorAssistantWindow assistant)
    {
        if (pendingInvestigation is { } report) { if (assistant.ReviewInvestigation(report, pendingInvestigationSample)) { pendingInvestigation = null; pendingInvestigationSample = null; } return; }
        if (pendingDeviceReview is not { } snapshot) return;
        if (assistant.ReviewDeviceSnapshot(snapshot)) pendingDeviceReview = null;
    }
}
