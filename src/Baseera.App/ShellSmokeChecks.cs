using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using Baseera.App.Services;

namespace Baseera.App;

internal static class ShellSmokeChecks
{
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    { for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var next in Nodes(child)) yield return next; } }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(n => AutomationProperties.GetAutomationId(n) == id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task Settle(Window owner) { await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(220); owner.UpdateLayout(); }
    private static ScrollViewer SidebarScroll(MainWindow owner)
    {
        DependencyObject? ancestor = Find<StackPanel>(owner, "SidebarPrimary");
        while (ancestor is not null && ancestor is not ScrollViewer)
            ancestor = VisualTreeHelper.GetParent(ancestor);
        return ancestor as ScrollViewer ?? throw new InvalidOperationException("The sidebar has no scrolling area.");
    }
    private static ToggleButton Header(Expander section) => (ToggleButton)section.Template.FindName("HeaderSite", section);
    private static void ToggleSection(Expander section)
    {
        var header = new ToggleButtonAutomationPeer(Header(section));
        ((IToggleProvider)header.GetPattern(PatternInterface.Toggle)).Toggle();
    }
    private static void CheckDisclosure(MainWindow owner, Expander section, string name, bool open)
    {
        var header = Header(section);
        var automation = (IExpandCollapseProvider)new ExpanderAutomationPeer(section).GetPattern(PatternInterface.ExpandCollapse);
        Require(section.IsExpanded == open && header.IsChecked == open && ((FrameworkElement)section.Content).IsVisible == open,
            "Disclosure header, items and expanded state disagree.");
        Require(AutomationProperties.GetName(header) == name && automation.ExpandCollapseState == (open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed),
            "The keyboard/screen-reader control lost its localized label or state.");
        var glyph = (System.Windows.Shapes.Path)header.Template.FindName("Chevron", header);
        var drawing = glyph.TransformToAncestor(owner);
        var direction = open
            ? drawing.Transform(new System.Windows.Point(6, 8)) - drawing.Transform(new System.Windows.Point(6, 4))
            : drawing.Transform(new System.Windows.Point(8, 6)) - drawing.Transform(new System.Windows.Point(4, 6));
        Require(open ? direction.Y > 0 : Settings.Current.Language == "ar" ? direction.X < 0 : direction.X > 0,
            "Disclosure arrow points away from its logical direction.");
    }
    private static void CheckIconDirections(MainWindow owner)
    {
        foreach (string destination in new[] { "scan", "security", "performance", "activity", "translate", "storage", "search", "usb", "feedback" })
        {
            var button = Find<Button>(owner, "Navigate-" + destination);
            var glyph = Nodes(button).OfType<System.Windows.Shapes.Path>().Single();
            var drawing = glyph.TransformToAncestor(owner);
            Require(drawing.Transform(new System.Windows.Point(24, 0)).X > drawing.Transform(new System.Windows.Point(0, 0)).X,
                $"The {destination} icon is mirrored in {Settings.Current.Language}.");
        }
    }
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
        CheckIconDirections(owner);
    }
    internal static async Task Run(MainWindow originalOwner, string output)
    {
        string preferences = JsonSerializer.Serialize(Settings.Current), key = Settings.GetKey("gemini"); MainWindow? owner = null;
        try
        {
            Settings.Current.Onboarded = Settings.Current.WelcomeSeen = true;
            Settings.Current.ShowWelcomeOnStartup = Settings.Current.AutoRefreshDevice = Settings.Current.CheckBaseeraUpdates = Settings.Current.UpdatesEnabled = Settings.Current.ScanAtStartup = Settings.Current.ExplorerMenu = false;
            Settings.Current.ErrorAssistantEnabled = true; Settings.Current.AiProvider = "gemini";
            Settings.Current.DisabledCenters.RemoveAll(key => key == "search");
            // Use the normal Loaded path, with all external/background actions disabled.
            foreach (bool configured in new[] { false, true })
            {
                Settings.Current.GeminiModel = configured ? "gemini-fixture" : ""; Settings.SetKey("gemini", configured ? "fixture-not-live" : "");
                owner = new MainWindow(); owner.Show(); await Settle(owner);
                Require(owner.CurrentErrorAssistant is null && Nodes(owner).Any(n => AutomationProperties.GetAutomationId(n) == "DeviceMetrics"),
                    "A normal launch opened the assistant or redirected Overview to AI Settings.");
                Require(new[] { "SidebarToolsSection", "SidebarCentersSection", "OverviewTools", "OverviewCenters" }.All(id => !Find<Expander>(owner, id).IsExpanded),
                    "A clean startup displayed all tool rows before the user opened a group.");
                if (!configured) { owner.Close(); owner = null; }
            }
            foreach (string language in new[] { "en", "ar", "fr", "es" })
            foreach (bool dark in new[] { false, true })
            {
                Settings.Current.Language = language; Settings.Current.Dark = dark; owner!.ApplyTheme(); owner.Navigate("home"); await Settle(owner);
                var tools = Find<Expander>(owner, "SidebarToolsSection"); var centers = Find<Expander>(owner, "SidebarCentersSection");
                CheckDisclosure(owner, tools, I18n.T("yourTools"), false);
                CheckDisclosure(owner, centers, I18n.T("hubCenters"), false);
                App.Snapshot(owner, Path.Combine(output, "shell-home-" + language + (dark ? "-dark" : "") + ".png"));
                ToggleSection(tools); owner.UpdateLayout();
                CheckDisclosure(owner, tools, I18n.T("yourTools"), true);
                CheckDisclosure(owner, centers, I18n.T("hubCenters"), false);
                ToggleSection(centers); owner.UpdateLayout(); CheckNavigation(owner);
                ToggleSection(tools); ToggleSection(centers); await Settle(owner);
            }
            owner!.Height = owner.MinHeight;
            Settings.Current.Language = "ar"; Settings.Current.Dark = true; owner.ApplyTheme(); owner.Navigate("home"); await Settle(owner);
            var overviewTools = Find<Expander>(owner, "OverviewTools"); ToggleSection(overviewTools); await Settle(owner);
            CheckDisclosure(owner, overviewTools, I18n.T("yourTools"), true);
            Find<Button>(owner, "Quick-search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(owner);
            var searchSection = Find<Expander>(owner, "SidebarToolsSection");
            CheckDisclosure(owner, searchSection, I18n.T("yourTools"), true);
            Require(Find<TextBox>(owner, "HubSearchQuery").IsVisible, "The Overview tool did not open the destination.");
            ToggleSection(searchSection); owner.Render(true); await Settle(owner);
            searchSection = Find<Expander>(owner, "SidebarToolsSection");
            CheckDisclosure(owner, searchSection, I18n.T("yourTools"), false);
            Require(Find<TextBox>(owner, "HubSearchQuery").IsVisible && Equals(searchSection.Background, Application.Current.FindResource("Selected")),
                "Closing the selected tool's group lost the page or its group highlight.");
            owner.Navigate("home"); await Settle(owner);
            overviewTools = Find<Expander>(owner, "OverviewTools");
            CheckDisclosure(owner, overviewTools, I18n.T("yourTools"), true);
            ToggleSection(overviewTools);
            ToggleSection(overviewTools);
            var shortTools = Find<Expander>(owner, "SidebarToolsSection"); ToggleSection(shortTools); await Settle(owner);
            var shortSidebar = SidebarScroll(owner);
            double sectionTop = shortTools.TranslatePoint(new System.Windows.Point(), shortSidebar).Y;
            Require(sectionTop >= -1 && sectionTop + shortTools.ActualHeight <= shortSidebar.ViewportHeight + 1,
                "An opened group that fits in the compact viewport was still partly hidden.");
            App.Snapshot(owner, Path.Combine(output, "shell-tools-open-ar-dark.png"));
            ToggleSection(Find<Expander>(owner, "SidebarCentersSection")); await Settle(owner); CheckNavigation(owner);
            var menu = Nodes(owner).OfType<Button>().Single(b => AutomationProperties.GetName(b) == I18n.T("menu")); menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(owner);
            Require(!Nodes(owner).Any(n => AutomationProperties.GetAutomationId(n) == "SidebarVersion"), "Collapsed navigation retained overflowing version text.");
            var compactTools = Find<Expander>(owner, "SidebarToolsSection");
            CheckDisclosure(owner, compactTools, I18n.T("yourTools"), true);
            ToggleSection(compactTools); owner.UpdateLayout(); CheckDisclosure(owner, compactTools, I18n.T("yourTools"), false);
            ToggleSection(compactTools); await Settle(owner);
            CheckIconDirections(owner);
            menu = Nodes(owner).OfType<Button>().Single(b => AutomationProperties.GetName(b) == I18n.T("menu")); menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(owner); CheckNavigation(owner);
            var navigationScroll = SidebarScroll(owner);
            navigationScroll.ScrollToBottom(); await Settle(owner);
            double sidebarOffset = navigationScroll.VerticalOffset;
            Require(sidebarOffset > 0, "Compact navigation fixture did not scroll.");
            Find<Button>(owner, "Navigate-settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(owner);
            Require(Math.Abs(SidebarScroll(owner).VerticalOffset - sidebarOffset) < 1, "Selecting Settings lost the sidebar scroll position.");
            var search = Nodes(owner).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == I18n.T("search")); search.IsChecked = false;
            Require(Settings.Current.DisabledCenters.Contains("search"), "Search tool toggle did not disable search."); search.IsChecked = true;
            Require(!Settings.Current.DisabledCenters.Contains("search"), "Search tool toggle did not enable search.");
            owner.ShowErrorAssistant(); await Settle(owner);
            Require(owner.CurrentErrorAssistant?.IsVisible == true, "Explicit manual assistant launch stopped working.");
            File.WriteAllText(Path.Combine(output, "shell-result.txt"), "PASS: startup stays on Overview, assistant opens on request, disclosures start collapsed, tool/center groups toggle independently through the actual headers, labels and arrow directions work in four languages and both themes, section state survives render/navigation, direct navigation reveals the destination group, selected page survives manual collapse, compact sidebar retains tool access, Settings preserves sidebar scroll, and the version/footer and original icon directions are intact.");
        }
        finally
        {
            owner?.CurrentErrorAssistant?.Close(); owner?.Close(); Settings.SetKey("gemini", key);
            await File.WriteAllTextAsync(Path.Combine(Settings.Folder, "settings.json"), preferences); Settings.Load(); originalOwner.ApplyTheme(); originalOwner.Navigate("home");
        }
    }
}
