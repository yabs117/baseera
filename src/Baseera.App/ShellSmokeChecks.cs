using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    private static void CheckWorkspacePalette(MainWindow owner, bool dark)
    {
        if (SystemParameters.HighContrast) return;
        var sidebar = Find<Border>(owner, "WorkspaceSidebar");
        var palette = WorkspacePalette.Resolve(Settings.Current.ColorPalette);
        var workspace = WorkspaceColors.Brush(palette.Colors(dark).Workspace).Color;
        Require(((SolidColorBrush)sidebar.Background).Color == ((SolidColorBrush)Brand.EyeBackground).Color,
            "The sidebar background differs from the eye tile.");
        Require(((SolidColorBrush)owner.Background).Color == workspace,
            "The workspace did not use the selected palette's background for this theme.");
        var home = Find<Button>(owner, "Navigate-home");
        var label = Nodes(home).OfType<TextBlock>().Single();
        Require(Equals(home.Background, sidebar.FindResource("Selected")) &&
            Equals(label.Foreground, sidebar.FindResource("Ink")) &&
            !Equals(label.Foreground, Application.Current.FindResource("Ink")) &&
            Equals(Find<TextBlock>(sidebar, "SidebarVersion").Foreground, sidebar.FindResource("Muted")),
            "Navigation text, highlight or version escaped the sidebar palette.");
    }

    private static double Contrast(uint a, uint b)
    {
        static double Linear(uint channel)
        {
            double scale = (channel & 255) / 255d;
            return scale <= .04045 ? scale / 12.92 : Math.Pow((scale + .055) / 1.055, 2.4);
        }
        static double Luminance(uint rgb) => .2126 * Linear(rgb >> 16) + .7152 * Linear(rgb >> 8) + .0722 * Linear(rgb);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    private static async Task CheckColorPalettes(MainWindow owner, string output)
    {
        Require(WorkspacePalette.Resolve(null).Id == "green" && WorkspacePalette.Resolve("invalid").Id == "green",
            "Missing and obsolete preferences did not resolve to green.");
        Require(WorkspacePalette.Resolve("green").Light.Workspace == 0xE8EEE7 && WorkspacePalette.Resolve("green").Dark.Workspace == 0x131F1A,
            "The current green design was altered.");
        foreach (var palette in WorkspacePalette.Choices)
        foreach (bool dark in new[] { false, true })
        {
            var c = palette.Colors(dark);
            var pairs = new[] { (c.Ink, c.Workspace), (c.Ink, c.Surface), (c.Ink, c.Selected), (c.Muted, c.Workspace), (c.Muted, c.Surface),
                (c.OnAccent, c.Accent), (c.Accent, c.Surface), (c.HeroInk, c.Hero), (c.HeroMuted, c.Hero),
                (c.AccentInk, c.Tint), (0xF3F7F9u, palette.SidebarSelected), (palette.Dark.Accent, 0x2A3036u) };
            Require(pairs.All(pair => Contrast(pair.Item1, pair.Item2) >= 4.5),
                "A palette lost text contrast: " + palette.Id);
        }
        if (SystemParameters.HighContrast) return;

        var before = Settings.Current.ColorPalette;
        Settings.Save();
        foreach (bool dark in new[] { false, true })
        {
            Settings.Current.Dark = dark;
            Settings.Save();
            owner.ApplyTheme();
            foreach (var palette in WorkspacePalette.Choices)
            {
                owner.Navigate("settings"); await Settle(owner);
                var section = Find<UniformGrid>(owner, "ColorPaletteChoices");
                var option = Find<System.Windows.Controls.RadioButton>(section, "Palette-" + palette.Id);
                Require(section.Children.Count == 4 && AutomationProperties.GetName(option) == I18n.T(palette.Label),
                    "A palette option is missing its accessible name.");
                option.IsChecked = true;
                await Settle(owner);
                var current = Find<System.Windows.Controls.RadioButton>(owner, "Palette-" + palette.Id);
                Require(current.IsChecked == true && Settings.Current.ColorPalette == palette.Id &&
                    current.Template.FindName("SelectionMark", current) is FrameworkElement { IsVisible: true },
                    "Selecting a color did not update the preference and visible check mark.");
                Settings.Load();
                Require(Settings.Current.ColorPalette == palette.Id, "Reload lost the palette choice.");
                if (dark && palette.Id == "blue")
                {
                    current.BringIntoView(); await Settle(owner);
                    App.Snapshot(owner, Path.Combine(output, "palette-settings-ar.png"));
                }
                owner.Navigate("home"); await Settle(owner);
                CheckWorkspacePalette(owner, dark);
                App.Snapshot(owner, Path.Combine(output, $"palette-{palette.Id}-{(dark ? "dark" : "light")}.png"));
            }
        }
        owner.HighContrastOverride = true;
        owner.ApplyTheme(); owner.Navigate("settings"); await Settle(owner);
        Require(!Find<UniformGrid>(owner, "ColorPaletteChoices").IsEnabled && ((SolidColorBrush)owner.Background).Color == System.Windows.SystemColors.WindowColor,
            "A palette overrode Windows high contrast.");
        owner.HighContrastOverride = null;
        string invalid = JsonSerializer.Serialize(Settings.Current).Replace("\"ColorPalette\":\"blue\"", "\"ColorPalette\":null", StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(Settings.Folder, "settings.json"), invalid);
        Settings.Load();
        Require(Settings.Current.ColorPalette == "green", "Null settings did not restore the default palette.");
        Settings.Current.ColorPalette = before;
        Settings.Save();
        owner.ApplyTheme(); owner.Navigate("home"); await Settle(owner);
    }

    private static async Task CheckLogoBlink(MainWindow owner)
    {
        var button = Find<Button>(owner, "BaseeraBlinkLogo");
        var eye = (BlinkingEye)button.Content;
        Require(eye.Openness == 1 && !eye.IsBlinking && button.Focusable &&
            new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke) is IInvokeProvider &&
            AutomationProperties.GetName(button) == I18n.T("blinkLogo"),
            "The idle logo is not an open, accessible button.");

        button.RaiseEvent(new System.Windows.Input.MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.MouseEnterEvent });
        Require(!eye.IsBlinking, "Hover alone started the blink.");
        eye.Blink(allowMotion: false);
        Require(!eye.IsBlinking && eye.Openness == 1, "Reduced motion did not preserve the open logo.");

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!FluentMotion.Allowed)
        {
            Require(!eye.IsBlinking, "Click ignored the system motion preference.");
            return;
        }

        Require(eye.IsBlinking, "Click did not start the blink.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        double narrowest = 1;
        while (eye.IsBlinking && deadline.Elapsed < TimeSpan.FromSeconds(4))
        {
            narrowest = Math.Min(narrowest, eye.Openness);
            await Task.Delay(18);
        }
        Require(narrowest < .5 && !eye.IsBlinking && eye.Openness == 1 && owner.CurrentErrorAssistant is null,
            "The logo did not close, reopen and return to idle without changing the page.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(eye.IsBlinking, "The logo cannot replay after completing a blink.");
        owner.Render(true);
        await Settle(owner);
        Require(!eye.IsBlinking && eye.Openness == 1 && ((BlinkingEye)Find<Button>(owner, "BaseeraBlinkLogo").Content).Openness == 1,
            "Rebuilding the sidebar leaked an animation or left the new logo closed.");
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
    private static async Task CheckToolFinder(MainWindow owner, string output)
    {
        var inspect = owner.Dispatcher.InvokeAsync(() =>
        {
            var finder = Application.Current.Windows.OfType<ToolFinder>().Single();
            try
            {
                finder.UpdateLayout();
                var query = Find<TextBox>(finder, "ToolFinderQuery");
                var results = Find<ListBox>(finder, "ToolFinderResults");
                Require(results.Items.Count == 20 && query.FlowDirection == FlowDirection.RightToLeft,
                    "The finder lost a destination, showed duplicates, or ignored Arabic layout.");
                query.Text = "z9-no-tool-7q";
                Require(results.Items.Count == 0 && Nodes(finder).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == I18n.T("workspaceFinderEmpty")),
                    "The finder did not explain an empty result.");
                query.Text = I18n.T("search");
                results.SelectedItem = results.Items.OfType<ListBoxItem>().Single(r => Equals(r.Tag, "search"));
                finder.UpdateLayout();
                App.Snapshot(finder, Path.Combine(output, "workspace-finder-ar-dark.png"));
                query.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(query), Environment.TickCount, Key.Enter)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            }
            finally { if (finder.IsVisible) finder.Close(); }
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Find<Button>(owner, "OpenToolFinder").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await inspect;
        await Settle(owner);
        Require(Find<TextBox>(owner, "HubSearchQuery").IsVisible, "Enter did not open the chosen tool.");
        owner.Navigate("home");
        await Settle(owner);
    }

    private sealed class FeedbackReply(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request, cancellationToken);
    }

    private static async Task CheckFeedback(MainWindow owner, string output)
    {
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TaskCompletionSource<(Uri? Url, HttpMethod Method, string? MediaType, string? Charset, string Json)>(TaskCreationOptions.RunContinuationsAsynchronously);
        int requests = 0;
        using var client = new HttpClient(new FeedbackReply(async (request, ct) =>
        {
            int count = ++requests;
            if (count == 1)
            {
                string json = await request.Content!.ReadAsStringAsync(ct);
                first.SetResult((request.RequestUri, request.Method, request.Content.Headers.ContentType?.MediaType,
                    request.Content.Headers.ContentType?.CharSet, json));
                return await response.Task.WaitAsync(ct);
            }
            if (count == 2) throw new HttpRequestException("Fixture connection failure");
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        owner.FeedbackHttpForSmoke = client;
        try
        {
            owner.Navigate("feedback"); await Settle(owner);
            Button Send() => Find<Button>(owner, "FeedbackSend");
            TextBox Title() => Find<TextBox>(owner, "FeedbackTitle");
            TextBox Details() => Find<TextBox>(owner, "FeedbackDetails");
            string Status() => Find<TextBlock>(owner, "FeedbackStatus").Text;
            void Click() => Send().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click();
            Require(requests == 0 && Status() == I18n.T("feedbackRequired"), "Empty feedback reached the service.");

            Find<ComboBox>(owner, "FeedbackKind").SelectedIndex = 1;
            Title().Text = "  اقتراح تجريبي  ";
            Details().Text = "  السطر الأول\nالسطر الثاني  ";
            string title = Title().Text, details = Details().Text;
            Click();
            var submitted = await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var payload = JsonDocument.Parse(submitted.Json);
            var root = payload.RootElement;
            Require(submitted.Url?.AbsoluteUri == "https://baseera-feedback.yabsganes.workers.dev/" &&
                submitted.Method == HttpMethod.Post && submitted.MediaType == "application/json" && submitted.Charset == "utf-8" &&
                root.EnumerateObject().Count() == 3 && root.GetProperty("type").GetString() == I18n.T("feedbackBug") &&
                root.GetProperty("title").GetString() == title.Trim() && root.GetProperty("details").GetString() == details.Trim(),
                "Feedback did not submit exactly the selected category and trimmed UTF-8 fields.");
            Require(!Send().IsEnabled && ((TextBlock)Send().Content).Text == I18n.T("feedbackSending"),
                "The in-flight feedback button remained active or did not show progress.");
            Click();
            owner.Navigate("home"); owner.Navigate("feedback"); await Settle(owner);
            Require(!Send().IsEnabled && !Title().IsEnabled && !Details().IsEnabled && requests == 1,
                "Rerendering the feedback page allowed duplicate sends or in-flight edits.");
            response.SetResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            await Settle(owner);
            Require(Send().IsEnabled && Title().Text == title && Details().Text == details && Status() == I18n.T("feedbackFailed"),
                "A server error lost the feedback draft or failed to re-enable sending.");
            Click(); await Settle(owner);
            Require(requests == 2 && Title().Text == title && Details().Text == details && Status() == I18n.T("feedbackFailed"),
                "A network failure lost the feedback draft.");
            Click(); await Settle(owner);
            Require(requests == 3 && Send().IsEnabled && Title().Text.Length == 0 && Details().Text.Length == 0 &&
                Status() == I18n.T("feedbackSent"), "Confirmed feedback did not clear the draft or report success.");
            App.Snapshot(owner, Path.Combine(output, "feedback-submitted-ar.png"));
        }
        finally
        {
            response.TrySetCanceled();
            owner.FeedbackHttpForSmoke = null;
            owner.Navigate("home");
            await Settle(owner);
        }
    }
    internal static async Task Run(MainWindow originalOwner, string output)
    {
        string preferences = JsonSerializer.Serialize(Settings.Current), key = Settings.GetKey("gemini"); MainWindow? owner = null;
        try
        {
            Settings.Current.Onboarded = Settings.Current.WelcomeSeen = true;
            Settings.Current.ShowWelcomeOnStartup = Settings.Current.AutoRefreshDevice = Settings.Current.CheckBaseeraUpdates = Settings.Current.UpdatesEnabled = Settings.Current.ScanAtStartup = Settings.Current.ExplorerMenu = false;
            Settings.Current.ErrorAssistantEnabled = true; Settings.Current.AiProvider = "gemini";
            Settings.Current.ColorPalette = WorkspacePalette.DefaultId;
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
                CheckWorkspacePalette(owner, dark);
                if (language == "ar" && dark) await CheckLogoBlink(owner);
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
            double normalWidth = owner.Width;
            Require(Find<UniformGrid>(owner, "DeviceMetrics").Columns == 4, "A wide workspace does not use the four-column metric layout.");
            owner.Width = owner.MinWidth; await Settle(owner);
            Require(Find<UniformGrid>(owner, "DeviceMetrics").Columns == 2, "Metrics do not reflow at the minimum window width.");
            App.Snapshot(owner, Path.Combine(output, "workspace-min-width-ar-dark.png"));
            owner.Width = normalWidth; await Settle(owner);
            await CheckColorPalettes(owner, output);
            var overviewTools = Find<Expander>(owner, "OverviewTools"); ToggleSection(overviewTools); await Settle(owner);
            CheckDisclosure(owner, overviewTools, I18n.T("yourTools"), true);
            Find<Button>(owner, "Quick-search").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle(owner);
            var searchSection = Find<Expander>(owner, "SidebarToolsSection");
            CheckDisclosure(owner, searchSection, I18n.T("yourTools"), true);
            Require(Find<TextBox>(owner, "HubSearchQuery").IsVisible, "The Overview tool did not open the destination.");
            ToggleSection(searchSection); owner.Render(true); await Settle(owner);
            searchSection = Find<Expander>(owner, "SidebarToolsSection");
            CheckDisclosure(owner, searchSection, I18n.T("yourTools"), false);
            Require(Find<TextBox>(owner, "HubSearchQuery").IsVisible && Equals(searchSection.Background, searchSection.FindResource("Selected")),
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
            await CheckToolFinder(owner, output);
            await CheckFeedback(owner, output);
            owner.ShowErrorAssistant(); await Settle(owner);
            Require(owner.CurrentErrorAssistant?.IsVisible == true, "Explicit manual assistant launch stopped working.");
            File.WriteAllText(Path.Combine(output, "shell-result.txt"), "PASS: Overview startup, responsive metric columns, Arabic tool finder and Enter navigation, manual assistant opening, persistent disclosures, accessible layout in four languages and both themes, four saved color palettes with verified text contrast and high-contrast override, RTL arrow and icon directions, opt-in logo blink, replay, reduced motion and sidebar rebuild, compact tool access, sidebar scroll preservation and footer positioning. Feedback: mocked UTF-8 POST, blank input rejected, duplicate clicks blocked, draft retained after HTTP and network failures, cleared only after success. No live feedback sent.");
        }
        finally
        {
            owner?.CurrentErrorAssistant?.Close(); owner?.Close(); Settings.SetKey("gemini", key);
            await File.WriteAllTextAsync(Path.Combine(Settings.Folder, "settings.json"), preferences); Settings.Load(); originalOwner.ApplyTheme(); originalOwner.Navigate("home");
        }
    }
}
