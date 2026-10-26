using System.Text.Json;
using System.Windows.Automation;
using Baseera.App.Services;

namespace Baseera.App;

internal static class ShellSmokeChecks
{
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Nodes(child)) yield return next; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(n => AutomationProperties.GetAutomationId(n) == id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Settle(Window owner) { await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(220); owner.UpdateLayout(); }
    private static void CheckNavigation(MainWindow owner)
    {
        string[] Ids(string group) => Nodes(Find<StackPanel>(owner, group)).OfType<Button>().Select(b => AutomationProperties.GetAutomationId(b).Replace("Navigate-", "")).ToArray();
        Require(Ids("SidebarPrimary").SequenceEqual(new[] { "home", "health", "performance", "doctor", "detective", "security", "cases", "activity" }), "Primary health/diagnostic navigation order is incorrect.");
        Require(Ids("SidebarTools").SequenceEqual(new[] { "scan", "translate", "updates", "convert", "errors", "search" }), "Universal Search is not in Your tools.");
        Require(Ids("SidebarCenters").SequenceEqual(new[] { "network", "storage", "timeline", "usb" }), "Moved pages remain duplicated in exploration navigation.");
        var buttons = Nodes(owner).OfType<Button>().Select(AutomationProperties.GetAutomationId).Where(id => id.StartsWith("Navigate-")).ToArray();
        Require(buttons.Length == 20 && buttons.Distinct().Count() == buttons.Length, "A navigation destination is missing or duplicated.");
        var footer = Find<StackPanel>(owner, "SidebarFooter"); var version = Find<TextBlock>(footer, "SidebarVersion");
        Require(ReferenceEquals(footer.Children[^1], version) && footer.Children.OfType<Button>().Select(AutomationProperties.GetAutomationId).SequenceEqual(new[] { "Navigate-feedback", "Navigate-settings" }), "Version is not the last footer item below Settings.");
        var settings = Find<Button>(footer, "Navigate-settings");
        Require(version.TranslatePoint(new System.Windows.Point(), owner).Y >= settings.TranslatePoint(new System.Windows.Point(), owner).Y + settings.ActualHeight && version.IsVisible,
            "Version overlaps Settings instead of appearing below it.");
    }
    internal static async Task Run(MainWindow originalOwner, string output)
    {
        string preferences = JsonSerializer.Serialize(Settings.Current), key = Settings.GetKey("gemini"); MainWindow? owner = null;
        try
        {
            Settings.Current.Onboarded = Settings.Current.WelcomeSeen = true;
            Settings.Current.ShowWelcomeOnStartup = Settings.Current.AutoRefreshDevice = Settings.Current.CheckBaseeraUpdates = Settings.Current.UpdatesEnabled = Settings.Current.ScanAtStartup = Settings.Current.ExplorerMenu = false;
            Settings.Current.ErrorAssistantEnabled = true; Settings.Current.AiProvider = "gemini";
            // Use the normal Loaded path, with all external/background actions disabled.
            foreach (bool configured in new[] { false, true })
            {
                Settings.Current.GeminiModel = configured ? "gemini-fixture" : ""; Settings.SetKey("gemini", configured ? "fixture-not-live" : "");
                owner = new MainWindow(); owner.Show(); await Settle(owner);
                Require(owner.CurrentErrorAssistant is null && Nodes(owner).Any(n => AutomationProperties.GetAutomationId(n) == "DeviceMetrics"),
                    "A normal launch opened the assistant or redirected Overview to AI Settings.");
                if (!configured) { owner.Close(); owner = null; }
            }
            foreach (string language in new[] { "en", "ar", "fr", "es" })
            foreach (bool dark in new[] { false, true })
            {
                Settings.Current.Language = language; Settings.Current.Dark = dark; owner!.ApplyTheme(); owner.Navigate("home"); await Settle(owner); CheckNavigation(owner);
                App.Snapshot(owner, Path.Combine(output, "shell-home-" + language + (dark ? "-dark" : "") + ".png"));
            }
            owner!.Height = owner.MinHeight; owner.Navigate("home"); await Settle(owner); CheckNavigation(owner);
            var menu = Nodes(owner).OfType<Button>().Single(b => AutomationProperties.GetName(b) == I18n.T("menu")); menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(owner);
            Require(!Nodes(owner).Any(n => AutomationProperties.GetAutomationId(n) == "SidebarVersion"), "Collapsed navigation retained overflowing version text.");
            menu = Nodes(owner).OfType<Button>().Single(b => AutomationProperties.GetName(b) == I18n.T("menu")); menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(owner); CheckNavigation(owner);
            owner.Navigate("settings"); await Settle(owner);
            var search = Nodes(owner).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == I18n.T("search")); search.IsChecked = false;
            Require(Settings.Current.DisabledCenters.Contains("search"), "Search tool toggle did not disable search."); search.IsChecked = true;
            Require(!Settings.Current.DisabledCenters.Contains("search"), "Search tool toggle did not enable search.");
            owner.ShowErrorAssistant(); await Settle(owner);
            Require(owner.CurrentErrorAssistant?.IsVisible == true, "Explicit manual assistant launch stopped working.");
            File.WriteAllText(Path.Combine(output, "shell-result.txt"), "PASS: normal launch with/without AI setup stays on Overview without opening an assistant; explicit manual open works; primary diagnostic grouping, Search in Your tools, no duplicate destinations, version last below Settings in four languages/both themes/minimum height; collapsed menu and Search enable/disable. No API, OS integration, native repair or background update ran.");
        }
        finally
        {
            owner?.CurrentErrorAssistant?.Close(); owner?.Close(); Settings.SetKey("gemini", key);
            await File.WriteAllTextAsync(Path.Combine(Settings.Folder, "settings.json"), preferences); Settings.Load(); originalOwner.ApplyTheme(); originalOwner.Navigate("home");
        }
    }
}
