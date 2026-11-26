using System.Windows.Automation;
using Baseera.App.Services;
using Pen = System.Windows.Media.Pen;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private Action<IReadOnlyList<double?>> AddOverviewHeader(StackPanel body)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        var intro = Stack();
        intro.VerticalAlignment = VerticalAlignment.Center;
        intro.Margin = new Thickness(0, 0, 18, 0);
        var title = Text(I18n.T("deviceCenter"), 33);
        title.FontWeight = HeadingWeight;
        title.Foreground = Resource("HeroInk");
        intro.Children.Add(title);

        var description = Text(I18n.T("deviceOverviewDesc"), 14);
        description.Foreground = Resource("HeroMuted");
        description.Margin = new Thickness(0, 0, 0, 15);
        intro.Children.Add(description);

        var actions = new WrapPanel();
        deviceRefresh = Action("deviceRefresh", async () => await RefreshDevice());
        deviceRefresh.Background = Resource("HeroInk");
        deviceRefresh.BorderBrush = Resource("HeroInk");
        ((TextBlock)deviceRefresh.Content).Foreground = Resource("Hero");
        DeviceId(deviceRefresh, "DeviceRefresh");
        actions.Children.Add(deviceRefresh);

        var doctor = Action("workspaceOpenDoctor", () => Navigate("doctor"));
        doctor.Background = Brushes.Transparent;
        doctor.BorderBrush = Resource("HeroLine");
        ((TextBlock)doctor.Content).Foreground = Resource("HeroInk");
        doctor.IsEnabled = IsEnabledFeature("doctor");
        DeviceId(doctor, "OverviewOpenDoctor");
        actions.Children.Add(doctor);
        intro.Children.Add(actions);

        deviceStatus = Text(SnapshotTime(), 12);
        deviceStatus.Foreground = Resource("HeroMuted");
        deviceStatus.Margin = new Thickness(0, 3, 0, 0);
        DeviceId(deviceStatus, "DeviceTimestamp");
        intro.Children.Add(deviceStatus);
        layout.Children.Add(intro);

        var trendPanel = Stack();
        var trendTitle = Text(I18n.T("workspaceCpuTrend"), 12);
        trendTitle.FontWeight = HeadingWeight;
        trendTitle.Foreground = Resource("HeroInk");
        trendTitle.Margin = new Thickness(0, 0, 0, 10);
        trendPanel.Children.Add(trendTitle);

        var readingPlot = new SessionCpuPlot(Resource("HeroInk"), Resource("HeroLine")) { Height = 58, FlowDirection = FlowDirection.LeftToRight };
        DeviceId(readingPlot, "OverviewCpuPlot");
        trendPanel.Children.Add(readingPlot);

        var trendNote = Text("", 11);
        trendNote.Foreground = Resource("HeroMuted");
        trendNote.Margin = new Thickness(0, 8, 0, 0);
        trendPanel.Children.Add(trendNote);

        var trendCard = new Border
        {
            Child = trendPanel, Background = Resource("Hero"), BorderBrush = Resource("HeroLine"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(13, 12, 13, 12), VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(trendCard, 1);
        layout.Children.Add(trendCard);
        body.Children.Add(new Border
        {
            Background = Resource("Hero"), BorderBrush = Resource("HeroLine"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(13), Padding = new Thickness(21, 19, 21, 18),
            Margin = new Thickness(0, 0, 0, 17), Child = layout
        });
        return readings =>
        {
            readingPlot.SetReadings(readings);
            trendNote.Text = readingPlot.HasTrend ? I18n.T("workspaceCpuTrendScale") : I18n.T("workspaceCpuTrendEmpty");
            AutomationProperties.SetName(readingPlot, I18n.T("workspaceCpuTrend") + ". " + trendNote.Text);
        };
    }

    private Button ToolFinderButton()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Border { Child = Icons.Create("search", 16, "AccentInk"), Margin = new Thickness(0, 0, 7, 0) });
        var label = Text(I18n.T("workspaceFinder"), 12);
        label.Margin = new Thickness(0, 0, 10, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);
        var shortcut = Text("Ctrl+K", 10, true);
        shortcut.FlowDirection = FlowDirection.LeftToRight;
        shortcut.Margin = new Thickness(0);
        shortcut.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(shortcut);
        var button = new Button { Content = row, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 10, 0) };
        AutomationProperties.SetName(button, I18n.T("workspaceFinder"));
        AutomationProperties.SetAcceleratorKey(button, "Ctrl+K");
        DeviceId(button, "OpenToolFinder");
        button.Click += (_, _) => OpenToolFinder();
        return button;
    }

    private void OpenToolFinder()
    {
        if (!Settings.Current.Onboarded) return;
        var destinations = PrimaryPages.Concat(Tools).Concat(InvestigationCenters).Concat(["settings", "feedback"]).Distinct();
        var finder = new ToolFinder(this, destinations.Select(id =>
        {
            string detail = I18n.T(id + "Short");
            if (detail == id + "Short") detail = "";
            if (!IsEnabledFeature(id)) detail = I18n.T("disabledDesc");
            return new ToolChoice(id, I18n.T(id), detail);
        }).ToArray());
        if (finder.ShowDialog() == true && finder.Destination is string destination) Navigate(destination);
    }

    private sealed class SessionCpuPlot(Brush ink, Brush line) : FrameworkElement
    {
        private IReadOnlyList<double?> readings = [];
        public bool HasTrend { get; private set; }

        public void SetReadings(IReadOnlyList<double?> values)
        {
            readings = values;
            HasTrend = Enumerable.Range(1, Math.Max(0, values.Count - 1)).Any(i => Valid(values[i - 1]) && Valid(values[i]));
            InvalidateVisual();
        }

        private static bool Valid(double? value) => value is double amount && double.IsFinite(amount) && amount is >= 0 and <= 100;

        protected override void OnRender(DrawingContext dc)
        {
            double width = RenderSize.Width;
            double height = RenderSize.Height;
            if (width < 2 || height < 4) return;
            var grid = new Pen(line, 1);
            dc.DrawLine(grid, new System.Windows.Point(0, 1), new System.Windows.Point(width, 1));
            dc.DrawLine(grid, new System.Windows.Point(0, height / 2), new System.Windows.Point(width, height / 2));
            dc.DrawLine(grid, new System.Windows.Point(0, height - 1), new System.Windows.Point(width, height - 1));
            if (!HasTrend) return;
            var graph = new StreamGeometry();
            using (var path = graph.Open())
            {
                bool active = false;
                for (int i = 0; i < readings.Count; i++)
                {
                    if (!Valid(readings[i])) { active = false; continue; }
                    var point = new System.Windows.Point(1 + i * (width - 2) / (readings.Count - 1), 1 + (height - 2) * (1 - readings[i]!.Value / 100));
                    if (active) path.LineTo(point, true, false);
                    else path.BeginFigure(point, false, false);
                    active = true;
                }
            }
            graph.Freeze();
            dc.DrawGeometry(null, new Pen(ink, 2), graph);
        }
    }
}
