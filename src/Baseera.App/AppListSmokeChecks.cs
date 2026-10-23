using Baseera.App.Services;

namespace Baseera.App;

public sealed partial class MainWindow
{
    internal async Task CheckAppList(string output)
    {
        if (!smokeMode) throw new InvalidOperationException("Isolated UI check only.");
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        T Find<T>(string id) where T : DependencyObject => Descendants(frame).OfType<T>().Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == id);
        int Rows() => Descendants(frame).OfType<Border>().Count(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x).StartsWith("AppRow:"));
        void Require(bool test, string message) { if (!test) throw new Exception("App list: " + message); }
        var originalIds = Settings.Current.AutoUpdateIds.ToList(); var originalSources = new Dictionary<string, string>(Settings.Current.ManualSources);
        string language = Settings.Current.Language; bool dark = Settings.Current.Dark; double width = Width, height = Height;
        string explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var icon = await Task.Run(() => InstalledAppCatalog.ReadIcon(explorerPath));
        Require(icon is { IsFrozen: true }, "local executable icon did not load");
        Require(ReferenceEquals(icon, InstalledAppCatalog.ReadIcon(explorerPath)), "icon cache did not reuse the frozen image");
        Require(InstalledAppCatalog.ReadIcon(Path.Combine(output, "missing-icon.exe")) is null, "missing icon did not fall back");
        var explorer = icon;
        var notepad = await Task.Run(() => InstalledAppCatalog.ReadIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe")));
        Require(explorer is not null, "Windows Explorer icon did not load");
        var packaged = await Task.Run(PackagedAppIcons.Read); int packagedIcons = 0;
        foreach (var app in packaged.Take(8)) if (await Task.Run(() => PackagedAppIcons.Load(app.AppUserModelId)) is not null) packagedIcons++;
        Require(packaged.Count == 0 || packagedIcons > 0, "packaged Windows app icons did not load");
        File.WriteAllText(Path.Combine(output, "app-icon-checks.txt"), $"Local executable icons: PASS; cached images: PASS; packaged entries: {packaged.Count}; icons sampled: {packagedIcons}; missing resource fallback: PASS.");
        try
        {
            Settings.Current.AutoUpdateIds = []; Settings.Current.ManualSources = []; Settings.Current.UpdatesEnabled = true;
            installedApps = [new(new("Example.Explorer", "1.0", "winget"), "Windows Explorer", "Microsoft", explorer),
                new(new("Example.Notepad", "1.0", "winget"), "Notepad", "Microsoft", notepad),
                new(new("Example.MissingIcon", "", "winget"), "Sample app", "Preview", null)];
            installedApps = installedApps.OrderBy(a => a.Name).ToList(); appListPreview = true;
            Settings.Current.Language = "en"; ApplyTheme(); Navigate("updates"); UpdateLayout(); Require(Rows() == 3, "loaded apps not displayed");
            Find<TextBox>("AppSearch").Text = "EXPLORER"; Require(Rows() == 1, "case-insensitive name filter failed");
            Find<TextBox>("AppSearch").Text = "Example.Notepad"; Require(Rows() == 1, "package identifier filter failed");
            Find<TextBox>("AppSearch").Text = "no-match"; Require(Rows() == 0, "empty search results not shown");
            Find<TextBox>("AppSearch").Text = "";
            Find<CheckBox>("AutoUpdate:Example.Explorer").IsChecked = true;
            Require(Settings.Current.AutoUpdateIds.SequenceEqual(new[] { "Example.Explorer" }), "per-app selection changed other apps");
            var filter = Descendants(frame).OfType<CheckBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == I18n.T("appsSelectedOnly"));
            filter.IsChecked = true; Require(Rows() == 1, "selected-only filter failed"); filter.IsChecked = false;
            Find<Button>("AppSource:Example.Explorer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Find<CheckBox>("ManualSource:Example.Explorer").IsChecked = true;
            Find<TextBox>("ManualUrl:Example.Explorer").Text = "http://invalid.example/download";
            Find<Button>("SaveSource:Example.Explorer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Settings.Current.ManualSources["Example.Explorer"] == "", "invalid manual source was saved");
            Find<TextBox>("ManualUrl:Example.Explorer").Text = "https://github.com/example/project/releases";
            Find<Button>("SaveSource:Example.Explorer").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Settings.Current.ManualSources["Example.Explorer"] == "https://github.com/example/project/releases", "valid manual source not saved");
            Find<TextBox>("ManualUrl:Example.Explorer").Text = "https://github.com/example/draft/releases";
            Settings.Current.Dark = !Settings.Current.Dark; ApplyTheme(); Render(true); UpdateLayout();
            Require(Rows() == 3 && Find<CheckBox>("AutoUpdate:Example.Explorer").IsChecked == true, "appearance change lost the catalog or selection");
            Require(Find<TextBox>("ManualUrl:Example.Explorer").Text == "https://github.com/example/draft/releases", "source draft lost on render");
            Navigate("home"); Navigate("updates"); UpdateLayout(); Require(Rows() == 3, "navigation lost the loaded list");
            expandedAppSources.Clear();
            foreach (string locale in new[] { "en", "ar", "fr", "es" })
            foreach (bool night in new[] { false, true })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = night; ApplyTheme(); Navigate("updates");
                Activate(); UpdateLayout(); await Task.Delay(150);
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (locale == "en" && !night || locale == "ar" && night) WindowAppearance.SavePreview(this, Path.Combine(output, "apps-list-" + locale + (night ? "-dark" : "") + ".png"));
            }
            Width = 940; Height = 680; Settings.Current.Language = "ar"; ApplyTheme(); Navigate("updates");
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var first = Find<Border>("AppRow:Example.Notepad"); Require(first.ActualWidth > 0 && first.ActualWidth <= scroller.ActualWidth, "compact RTL app card overflowed");
        }
        finally
        {
            Settings.Current.AutoUpdateIds = originalIds; Settings.Current.ManualSources = originalSources;
            Settings.Current.Language = language; Settings.Current.Dark = dark; Width = width; Height = height;
            installedApps = null; appListPreview = false; appSearch = ""; selectedAppsOnly = false; expandedAppSources.Clear(); appSourceDrafts.Clear();
            Settings.Save(); ApplyTheme(); Navigate("home");
        }
    }
}
