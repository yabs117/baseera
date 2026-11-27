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
    private bool overviewToolsOpen, overviewCentersOpen;
    private readonly Queue<double?> cpuReadings = new();
    private const int MaxCpuReadings = 16;
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
            deviceSnapshot = collected;
            if (cpuReadings.Count == MaxCpuReadings) cpuReadings.Dequeue();
            cpuReadings.Enqueue(collected.CpuPercent);
            drawDevice?.Invoke();
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
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        var heading = new DockPanel();
        var glyph = new Border { Background = Resource("Tint"), CornerRadius = new CornerRadius(7), Width = 32, Height = 32, Margin = new Thickness(0, 0, 8, 0), Child = Icons.Create(icon, 17, "AccentInk") };
        DockPanel.SetDock(glyph, Dock.Left); heading.Children.Add(glyph);
        var headingLabel = Text(I18n.T(label), 12, true); headingLabel.Margin = new Thickness(0); headingLabel.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(headingLabel); content.Children.Add(heading);
        var reading = Text(value, 22); reading.FontWeight = HeadingWeight; reading.Margin = new Thickness(0, 9, 0, 5); Grid.SetRow(reading, 1); content.Children.Add(reading);
        if (percentage is not null) { reading.FlowDirection = FlowDirection.LeftToRight; reading.TextAlignment = Arabic ? TextAlignment.Right : TextAlignment.Left; }
        var bottom = new DockPanel();
        var caption = Text(detail, 11, true); caption.Margin = new Thickness(0); caption.VerticalAlignment = VerticalAlignment.Top;
        if (label == "deviceMemory" && percentage is not null)
        {
            caption.FlowDirection = FlowDirection.LeftToRight;
            caption.TextAlignment = Arabic ? TextAlignment.Right : TextAlignment.Left;
        }
        if (label != "health")
        {
            double progress = percentage is double sample && double.IsFinite(sample) ? Math.Clamp(sample, 0, 100) : 0;
            var meter = new Grid { Height = 4, Margin = new Thickness(0, 10, 0, 0), IsHitTestVisible = false };
            meter.ColumnDefinitions.Add(new() { Width = new GridLength(progress, GridUnitType.Star) }); meter.ColumnDefinitions.Add(new() { Width = new GridLength(100 - progress, GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(2), Background = Resource("Line") };
            Grid.SetColumnSpan(track, 2); meter.Children.Add(track);
            if (progress > 0) meter.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Resource("Accent") });
            DockPanel.SetDock(meter, Dock.Bottom); bottom.Children.Add(meter);
        }
        bottom.Children.Add(caption);
        Grid.SetRow(bottom, 2); content.Children.Add(bottom);
        var button = new Button { Content = content, Padding = new Thickness(14), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Resource("Surface"), BorderBrush = Resource("Line"), BorderThickness = new Thickness(1), MinHeight = 142, Margin = new Thickness(0, 0, 10, 10) };
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
        if (!performance)
        {
            string diskDetail = system is null ? I18n.T("deviceDiskLimits") :
                "\u2066" + system.Name + "\u2069 · " + string.Format(I18n.T("workspaceDiskTotal"), "\u2066" + SizeText(system.Total) + "\u2069");
            grid.Children.Add(MetricCard("deviceStorage", "disk", system is null ? I18n.T("deviceUnknown") : SizeText(system.Free), diskDetail,
                system?.FreePercent is { } free ? 100 - free : null, "DeviceStorageCard", () => Navigate("health")));
        }
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
        var trend = AddOverviewHeader(body);
        var readings = new DockPanel { Margin = new Thickness(0, 1, 0, 10) };
        var observations = Text("", 12, true); DeviceId(observations, "DeviceObservationCount");
        observations.Margin = new Thickness(14, 0, 0, 0); observations.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(observations, Dock.Right); readings.Children.Add(observations);
        var health = Text(I18n.T("workspaceReadings"), 16); health.FontWeight = HeadingWeight; health.Margin = new Thickness(0);
        readings.Children.Add(health); body.Children.Add(readings);
        var metrics = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -10, 2) }; DeviceId(metrics, "DeviceMetrics"); body.Children.Add(metrics);
        metrics.SizeChanged += (_, args) =>
        {
            if (args.WidthChanged) metrics.Columns = args.NewSize.Width >= 815 ? 4 : 2;
        };
        SectionHeading(body, "workspaceQuickTasks");
        var quick = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, -10, 2) };
        foreach (string key in new[] { "scan", "errors", "translate" })
        {
            var button = QuickAccess(key, () => Navigate(key));
            DeviceId(button, "HomeFavorite-" + key); quick.Children.Add(button);
        }
        body.Children.Add(quick);
        var tools = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, -10, 0) };
        foreach (string key in Tools)
        {
            var shortcut = key == "errors"
                ? QuickAccess(key, ReviewDeviceWithAssistant, I18n.T("deviceAnalyze"))
                : QuickAccess(key, () => Navigate(key), key == "updates" ? I18n.T("deviceUpdatesOnRequest") : null);
            tools.Children.Add(shortcut);
        }
        var centers = new UniformGrid { Columns = 2, Margin = new Thickness(0, 8, -10, 0) };
        centers.Children.Add(QuickAccess("performance", () => Navigate("performance")));
        foreach (string center in InvestigationCenters.Except(Tools))
            centers.Children.Add(QuickAccess(center, () => Navigate(center), I18n.T(center + "Short")));
        body.Children.Add(OverviewSection("OverviewTools", "yourTools", "toolbox", tools, overviewToolsOpen, open => overviewToolsOpen = open));
        body.Children.Add(OverviewSection("OverviewCenters", "hubCenters", "centers", centers, overviewCentersOpen, open => overviewCentersOpen = open));
        Help(body, "deviceLocalOnly");
        drawDevice = () =>
        {
            DrawMetrics(metrics);
            observations.Visibility = deviceSnapshot is null ? Visibility.Collapsed : Visibility.Visible;
            observations.Text = deviceSnapshot is null ? "" : string.Format(I18n.T("deviceObservations"), deviceSnapshot.Findings.Count);
            trend(cpuReadings.ToArray());
        };
        drawDevice();
    }
    private Expander OverviewSection(string id, string label, string icon, FrameworkElement content, bool isOpen, Action<bool> setOpen)
    {
        var section = DisclosureSection(id, label, icon, content, isOpen);
        section.Margin = new Thickness(0, 4, 0, 8);
        section.SetResourceReference(BackgroundProperty, "Surface");
        section.SetResourceReference(BorderBrushProperty, "Line");
        section.Expanded += (_, args) => { if (ReferenceEquals(args.OriginalSource, section)) setOpen(true); };
        section.Collapsed += (_, args) => { if (ReferenceEquals(args.OriginalSource, section)) setOpen(false); };
        return section;
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
