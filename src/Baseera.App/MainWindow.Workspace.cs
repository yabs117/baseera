using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
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

internal sealed record ToolChoice(string Id, string Title, string Detail);

internal sealed class ToolFinder : Window
{
    private readonly TextBox input;
    private readonly ListBox results;
    private readonly TextBlock empty;
    private readonly ToolChoice[] choices;
    public string? Destination { get; private set; }

    internal ToolFinder(MainWindow owner, ToolChoice[] choices)
    {
        this.choices = choices;
        Owner = owner;
        Title = I18n.T("workspaceFinder");
        Width = 555;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FontFamily = Brand.InterfaceFont;
        FontSize = 13;
        Icon = Brand.WindowIcon;
        UseLayoutRounding = true;
        SetResourceReference(BackgroundProperty, "Bg");
        var content = new Grid
        {
            FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(280) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        input = new TextBox
        {
            Tag = I18n.T("workspaceFinderHint"), Height = 48, Padding = new Thickness(12, 8, 12, 7),
            Margin = new Thickness(0, 0, 0, 10), MaxLength = 128
        };
        AutomationProperties.SetName(input, Title);
        AutomationProperties.SetAutomationId(input, "ToolFinderQuery");
        content.Children.Add(input);

        results = new ListBox { Padding = new Thickness(4), BorderThickness = new Thickness(1) };
        results.ItemContainerStyle = (Style)FindResource("ToolFinderItem");
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(results, Title);
        AutomationProperties.SetAutomationId(results, "ToolFinderResults");
        Grid.SetRow(results, 1);
        content.Children.Add(results);

        empty = new TextBlock
        {
            Text = I18n.T("workspaceFinderEmpty"), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
        };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(empty, AutomationLiveSetting.Polite);
        Grid.SetRow(empty, 1);
        content.Children.Add(empty);

        var hint = new TextBlock { Text = I18n.T("workspaceFinderKeys"), Margin = new Thickness(0, 11, 0, 0), FontSize = 11 };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        Grid.SetRow(hint, 2);
        content.Children.Add(hint);
        Content = new Border { Padding = new Thickness(16), FlowDirection = FlowDirection.LeftToRight, Child = content };

        input.TextChanged += (_, _) => UpdateResults();
        results.MouseDoubleClick += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(results, source) is ListBoxItem { IsSelected: true })
                OpenSelected();
        };
        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; Close(); }
            else if (args.Key == Key.Enter) { args.Handled = true; OpenSelected(); }
            else if (input.IsKeyboardFocusWithin && args.Key is Key.Up or Key.Down)
            {
                args.Handled = true;
                int next = results.SelectedIndex + (args.Key == Key.Up ? -1 : 1);
                if (next >= 0 && next < results.Items.Count)
                {
                    results.SelectedIndex = next;
                    results.ScrollIntoView(results.SelectedItem);
                }
            }
        };
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        Loaded += (_, _) => input.Focus();
        UpdateResults();
    }

    private void UpdateResults()
    {
        var comparer = CultureInfo.GetCultureInfo(Settings.Current.Language).CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
        var terms = input.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        results.Items.Clear();
        foreach (var item in choices.Where(item => terms.All(term => comparer.IndexOf(item.Title + " " + item.Detail + " " + item.Id, term, options) >= 0)))
        {
            var row = new DockPanel();
            var icon = Icons.Create(item.Id, 18, "AccentInk");
            icon.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            var labels = new StackPanel();
            labels.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold });
            if (item.Detail.Length > 0)
            {
                var detail = new TextBlock { Text = item.Detail, FontSize = 11, Margin = new Thickness(0, 3, 0, 0) };
                detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
                labels.Children.Add(detail);
            }
            row.Children.Add(labels);
            var entry = new ListBoxItem { Content = row, Tag = item.Id };
            AutomationProperties.SetName(entry, item.Title);
            results.Items.Add(entry);
        }
        if (results.Items.Count > 0) results.SelectedIndex = 0;
        empty.Visibility = results.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenSelected()
    {
        if (results.SelectedItem is not ListBoxItem { Tag: string id }) return;
        Destination = id;
        DialogResult = true;
    }
}
