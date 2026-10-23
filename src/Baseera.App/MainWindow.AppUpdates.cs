using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private List<AppListItem>? installedApps;
    private string appSearch = "", appListError = "";
    private bool selectedAppsOnly, appListLoading;
    private bool appListPreview;
    private readonly HashSet<string> expandedAppSources = [];
    private readonly Dictionary<string, string> appSourceDrafts = [];

    private async Task LoadInstalledApps()
    {
        if (appListLoading || operation is not null) return;
        await Run(async ct =>
        {
            appListLoading = true; appListError = ""; Render(true);
            try
            {
                var packages = await Updater.List(ct);
                installedApps = await Task.Run(() => InstalledAppCatalog.Decorate(packages, InstalledAppCatalog.ReadInstalledApps(), ct), ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { appListError = ex.Message; }
            finally { appListLoading = false; if (page == "updates") Render(true); }
        });
    }

    private void InstalledAppsSection(StackPanel body)
    {
        SectionHeading(body, "installedApps");
        if (appListPreview) body.Children.Add(Text(I18n.T("appsPreview"), 12, true));
        var toolbar = new WrapPanel();
        var refresh = Action("refresh", async () => await LoadInstalledApps()); refresh.IsEnabled = !appListLoading; toolbar.Children.Add(refresh);
        var update = AsyncAction("updateNow", async ct => { updateLog = await Updater.UpdateSelected(ct); Render(true); }, true); toolbar.Children.Add(update);
        body.Children.Add(toolbar);
        if (appListLoading) { var loading = Text(I18n.T("appsLoading"), 12, true); loading.Margin = new Thickness(0, 10, 0, 12); body.Children.Add(loading); }
        if (appListError.Length > 0)
        {
            var error = Text(I18n.T("appsLoadFailed"), 12, true); error.ToolTip = appListError; body.Children.Add(Card(error, 14));
        }
        var summary = Text("", 12, true); summary.Margin = new Thickness(0, 10, 0, 14);
        var filter = new Grid { Margin = new Thickness(0, 10, 0, 0) }; filter.ColumnDefinitions.Add(new ColumnDefinition()); filter.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var search = new TextBox { Text = appSearch, Tag = I18n.T("appsSearch"), Margin = new Thickness(0, 0, 16, 0), MinWidth = 150 };
        System.Windows.Automation.AutomationProperties.SetName(search, I18n.T("appsSearch")); System.Windows.Automation.AutomationProperties.SetAutomationId(search, "AppSearch"); filter.Children.Add(search);
        var selected = Toggle("appsSelectedOnly", selectedAppsOnly, value => selectedAppsOnly = value); selected.Margin = new Thickness(0); selected.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(selected, 1); filter.Children.Add(selected); body.Children.Add(filter); body.Children.Add(summary);
        var rows = Stack(); body.Children.Add(rows);
        void DrawRows()
        {
            rows.Children.Clear(); var all = installedApps ?? [];
            var visible = all.Where(a => (!selectedAppsOnly || Settings.Current.AutoUpdateIds.Contains(a.Package.Id)) &&
                (appSearch.Length == 0 || (a.Name + " " + a.Package.Id + " " + a.Publisher).Contains(appSearch, StringComparison.CurrentCultureIgnoreCase))).ToList();
            summary.Text = string.Format(I18n.T("appsSummary"), visible.Count, all.Count, all.Count(a => Settings.Current.AutoUpdateIds.Contains(a.Package.Id)));
            update.IsEnabled = !appListLoading && all.Any(a => Settings.Current.AutoUpdateIds.Contains(a.Package.Id));
            if (installedApps is null)
            {
                var empty = Stack(); EmptyState(empty, "updates", appListLoading ? "appsLoading" : "installedApps", "refreshHint"); rows.Children.Add(Card(empty, 0)); return;
            }
            if (visible.Count == 0)
            {
                var empty = Stack(); EmptyState(empty, "updates", "appsNoResults", all.Count == 0 ? "refreshHint" : "appsSearchHint"); rows.Children.Add(Card(empty, 0)); return;
            }
            foreach (var app in visible) rows.Children.Add(AppRow(app, () =>
            {
                summary.Text = string.Format(I18n.T("appsSummary"), visible.Count, all.Count, all.Count(a => Settings.Current.AutoUpdateIds.Contains(a.Package.Id)));
                update.IsEnabled = !appListLoading && all.Any(a => Settings.Current.AutoUpdateIds.Contains(a.Package.Id));
                if (selectedAppsOnly) _ = Dispatcher.InvokeAsync(DrawRows, System.Windows.Threading.DispatcherPriority.Background);
            }));
        }
        search.TextChanged += (_, _) => { appSearch = search.Text.Trim(); DrawRows(); };
        selected.Checked += (_, _) => DrawRows(); selected.Unchecked += (_, _) => DrawRows(); DrawRows();
        Help(body, "appIconsNote"); Help(body, "updateNote");
        if (installedApps is null && !appListLoading && appListError.Length == 0 && !smokeMode)
            _ = Dispatcher.InvokeAsync(async () => { if (page == "updates") await LoadInstalledApps(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private Border AppRow(AppListItem app, Action selectionChanged)
    {
        var package = app.Package; var content = Stack();
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        UIElement icon = app.Icon is { } image ? new System.Windows.Controls.Image { Source = image, Width = 36, Height = 36, Stretch = Stretch.Uniform, FlowDirection = FlowDirection.LeftToRight } : Icons.Create("application", 28, "Muted");
        var iconBox = new Border { Width = 44, Height = 44, Background = Resource("Subtle"), CornerRadius = new CornerRadius(7), Child = icon, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(iconBox, app.Name); row.Children.Add(iconBox);
        var identity = Stack(); identity.Margin = new Thickness(0, 0, 18, 0);
        var name = Text(app.Name, 15); name.ToolTip = package.Id; name.FontWeight = HeadingWeight; name.FlowDirection = FlowDirection.LeftToRight; name.TextAlignment = Arabic ? TextAlignment.Right : TextAlignment.Left; identity.Children.Add(name);
        string version = string.IsNullOrWhiteSpace(package.Version) ? I18n.T("appsUnknownVersion") : package.Version;
        var detail = Text(version + (app.Publisher.Length == 0 ? "" : "  ·  " + app.Publisher), 12, true); detail.FlowDirection = FlowDirection.LeftToRight; detail.TextAlignment = name.TextAlignment; identity.Children.Add(detail);
        Grid.SetColumn(identity, 1); row.Children.Add(identity);
        var automatic = Toggle("auto", Settings.Current.AutoUpdateIds.Contains(package.Id), value =>
        {
            if (value && !Settings.Current.AutoUpdateIds.Contains(package.Id)) Settings.Current.AutoUpdateIds.Add(package.Id);
            else if (!value) Settings.Current.AutoUpdateIds.Remove(package.Id);
            Settings.Save(); selectionChanged();
        });
        automatic.Margin = new Thickness(0); automatic.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetAutomationId(automatic, "AutoUpdate:" + package.Id); automatic.ToolTip = I18n.T("auto") + " · " + app.Name;
        System.Windows.Automation.AutomationProperties.SetName(automatic, I18n.T("auto") + " · " + app.Name);
        var controls = Stack(); controls.Children.Add(automatic); Grid.SetColumn(controls, 2); row.Children.Add(controls); content.Children.Add(row);
        var options = Stack(); options.Margin = new Thickness(56, 12, 0, 0); options.Visibility = expandedAppSources.Contains(package.Id) ? Visibility.Visible : Visibility.Collapsed;
        var idLabel = Text(package.Id, 11, true); idLabel.FlowDirection = FlowDirection.LeftToRight; options.Children.Add(idLabel);
        var arrow = Icons.Create(Arabic ? "chevronLeft" : "chevronRight", 12, "Muted"); arrow.FlowDirection = FlowDirection.LeftToRight; arrow.RenderTransformOrigin = new System.Windows.Point(.5, .5);
        void RotateArrow() => arrow.RenderTransform = new RotateTransform(options.Visibility == Visibility.Visible ? (Arabic ? -90 : 90) : 0);
        RotateArrow();
        var source = Action("appsSource", () =>
        {
            if (expandedAppSources.Remove(package.Id)) options.Visibility = Visibility.Collapsed;
            else { expandedAppSources.Add(package.Id); options.Visibility = Visibility.Visible; }
            RotateArrow();
        }); source.Padding = new Thickness(8, 4, 8, 4); source.HorizontalAlignment = HorizontalAlignment.Left; source.Margin = new Thickness(0, 6, 0, 0);
        var caption = new StackPanel { Orientation = Orientation.Horizontal }; caption.Children.Add(arrow);
        var sourceText = Text(I18n.T("appsSource"), 11, true); sourceText.Margin = new Thickness(6, 0, 0, 0); caption.Children.Add(sourceText); source.Content = caption;
        source.ToolTip = Settings.Current.ManualSources.ContainsKey(package.Id) ? I18n.T("manualPerApp") : "WinGet";
        System.Windows.Automation.AutomationProperties.SetAutomationId(source, "AppSource:" + package.Id); controls.Children.Add(source);
        var custom = new TextBox { Text = appSourceDrafts.GetValueOrDefault(package.Id, Settings.Current.ManualSources.GetValueOrDefault(package.Id, "")), Tag = "https://github.com/owner/project/releases", FlowDirection = FlowDirection.LeftToRight };
        System.Windows.Automation.AutomationProperties.SetName(custom, I18n.T("releasePage") + " · " + app.Name);
        System.Windows.Automation.AutomationProperties.SetAutomationId(custom, "ManualUrl:" + package.Id);
        custom.TextChanged += (_, _) => appSourceDrafts[package.Id] = custom.Text;
        var manual = Toggle("manualPerApp", Settings.Current.ManualSources.ContainsKey(package.Id), value =>
        {
            custom.IsEnabled = value;
            source.ToolTip = value ? I18n.T("manualPerApp") : "WinGet";
            if (value) Settings.Current.ManualSources[package.Id] = custom.Text.Trim(); else Settings.Current.ManualSources.Remove(package.Id);
            Settings.Save();
        });
        System.Windows.Automation.AutomationProperties.SetAutomationId(manual, "ManualSource:" + package.Id);
        custom.IsEnabled = manual.IsChecked == true; options.Children.Add(manual); options.Children.Add(custom);
        var feedback = Text(I18n.T("releaseNote"), 11, true); options.Children.Add(feedback);
        var save = Action("save", () =>
        {
            if (manual.IsChecked == true)
            {
                bool valid = false;
                try { valid = UpdateSources.GitHubRepository(custom.Text.Trim()) is not null; } catch (ArgumentException) { }
                if (!valid) { feedback.Text = I18n.T("unsupportedSource"); custom.Focus(); return; }
                Settings.Current.ManualSources[package.Id] = custom.Text.Trim();
            }
            Settings.Save(); feedback.Text = I18n.T("done");
        }); save.HorizontalAlignment = HorizontalAlignment.Left; System.Windows.Automation.AutomationProperties.SetAutomationId(save, "SaveSource:" + package.Id); options.Children.Add(save); content.Children.Add(options);
        var card = Card(content, 16); card.Margin = new Thickness(0, 0, 0, 10);
        System.Windows.Automation.AutomationProperties.SetAutomationId(card, "AppRow:" + package.Id); return card;
    }
}
