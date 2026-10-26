using System.Windows.Media.Animation;
using SystemColors = System.Windows.SystemColors;
using Baseera.App.Services;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private Border? operationBar;
    private string operationStatusKey = "", operationStatusDetail = "";
    private string OperationStatusText => operationStatusKey.Length == 0 ? "" : I18n.T(operationStatusKey) + operationStatusDetail;
    private void SetOperationStatus(string key, string? detail = null)
    {
        operationStatusKey = key;
        operationStatusDetail = string.IsNullOrEmpty(detail) ? "" : " " + detail;
        status.Text = OperationStatusText;
        if (operationBar is not null) operationBar.Visibility = key.Length == 0 && operation is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private bool? startupDraft, startupScanDraft, explorerDraft;
    private readonly AiConnectionDraft aiConnectionDraft = new();
    private string feedbackTitleDraft = "", feedbackBodyDraft = "";
    private int feedbackKindDraft;
    private bool Arabic => Settings.Current.Language == "ar";
    private static readonly string[] Tools = ["scan", "translate", "updates", "convert", "errors", "search"];
    private static readonly string[] PrimaryPages = ["home", "health", "performance", "doctor", "detective", "security", "cases", "activity"];
    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
    private static StackPanel Stack() => new() { Orientation = Orientation.Vertical };
    private FontWeight HeadingWeight => Arabic ? FontWeights.Medium : FontWeights.SemiBold;

    internal bool? HighContrastOverride { get; set; }
    public void ApplyTheme()
    {
        string[] keys = ["Bg", "Surface", "Ink", "Muted", "Line", "Accent", "Sidebar", "Selected", "Hover", "OnAccent", "Subtle"];
        string[] light = ["#FAFAFA", "#FFFFFF", "#1B1B1B", "#666666", "#E4E4E4", "#005FB8", "#F3F3F3", "#E5E5E5", "#EEEEEE", "#FFFFFF", "#F6F6F6"];
        string[] dark = ["#272727", "#303030", "#F4F4F4", "#B1B1B1", "#414141", "#79B8E8", "#202020", "#383838", "#3A3A3A", "#172534", "#2B2B2B"];
        var palette = Settings.Current.Dark ? dark : light;
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
        if (HighContrastOverride ?? SystemParameters.HighContrast)
        {
            var colors = new[] { SystemColors.WindowColor, SystemColors.WindowColor, SystemColors.WindowTextColor, SystemColors.GrayTextColor, SystemColors.WindowTextColor,
                SystemColors.HighlightColor, SystemColors.ControlColor, SystemColors.ControlColor, SystemColors.ControlColor, SystemColors.HighlightTextColor, SystemColors.WindowColor };
            for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush(colors[i]);
        }
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        FlowDirection = FlowDirection.LeftToRight;
        shell.FlowDirection = Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        frame.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Bg");
        WindowAppearance.Apply(this, Settings.Current.Dark);
    }

    private TextBlock Text(string text, double size = 13, bool muted = false) => new()
    {
        Text = text, FontSize = size, Foreground = Resource(muted ? "Muted" : "Ink"),
        Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap
    };

    private Button Action(string key, Action action, bool primary = false)
    {
        var button = new Button { Content = new TextBlock { Text = I18n.T(key), Foreground = Resource(primary ? "OnAccent" : "Ink") } };
        if (primary) { button.Background = Resource("Accent"); button.BorderBrush = button.Background; }
        System.Windows.Automation.AutomationProperties.SetName(button, I18n.T(key));
        button.Click += (_, _) => action(); return button;
    }
    private Button AsyncAction(string key, Func<CancellationToken, Task> action, bool primary = false) => Action(key, async () => await Run(action), primary);
    internal Action<Exception>? OperationErrorForSmoke { get; set; }
    private async Task Run(Func<CancellationToken, Task> work)
    {
        if (operation is not null) return;
        using var tracked = Services.Tasks.Begin(page, true, lifetime.Token);
        operation = CancellationTokenSource.CreateLinkedTokenSource(tracked.Token);
        if (cancelOperation is not null) cancelOperation.Visibility = Visibility.Visible;
        if (operationBar is not null) operationBar.Visibility = Visibility.Visible;
        SetOperationStatus("working");
        try { await work(operation.Token); tracked.Complete(); SetOperationStatus("done"); }
        catch (OperationCanceledException) { tracked.Cancelled(); SetOperationStatus("cancel"); }
        catch (Exception ex)
        {
            tracked.Failed();
            SetOperationStatus("error");
            if (smokeMode && OperationErrorForSmoke is { } observe) observe(ex);
            else if (ex is ChatGptException { Key: "chatGptUsageLimit" }) ChatGptUi.ShowUsageLimit(this);
            else MessageBox.Show(this, ex.Message, I18n.T("error"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { operation.Dispose(); operation = null; if (cancelOperation is not null) cancelOperation.Visibility = Visibility.Collapsed; }
    }

    private Border Card(UIElement child, double padding = 20) => new()
    {
        Background = Resource("Surface"), BorderBrush = Resource("Line"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 16), Child = child
    };
    private void Heading(StackPanel body, string title, string? subtitle = null)
    {
        var name = Text(I18n.T(title), 27); name.FontWeight = HeadingWeight; name.Margin = new Thickness(0, 0, 0, 9); body.Children.Add(name);
        if (subtitle is not null) { var description = Text(I18n.T(subtitle), 13, true); description.Margin = new Thickness(0, 0, 0, 22); body.Children.Add(description); }
        else body.Children.Add(new Border { Height = 14 });
    }
    private void SectionHeading(StackPanel body, string key)
    {
        var title = Text(I18n.T(key), 15); title.FontWeight = HeadingWeight; title.Margin = new Thickness(0, 12, 0, 10); body.Children.Add(title);
    }
    private void Help(StackPanel body, string key)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 18) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = Icons.Create("info", 16, "Muted"); icon.VerticalAlignment = VerticalAlignment.Top; icon.Margin = new Thickness(0, 2, 0, 0); row.Children.Add(icon);
        var copy = Text(I18n.T(key), 12, true); copy.LineHeight = 19; copy.Margin = new Thickness(0); Grid.SetColumn(copy, 1); row.Children.Add(copy); body.Children.Add(row);
    }
    private void EmptyState(StackPanel body, string icon, string title, string description)
    {
        var empty = Stack(); empty.Margin = new Thickness(18, 28, 18, 28); empty.HorizontalAlignment = HorizontalAlignment.Center;
        var glyph = Icons.Create(icon, 32, "Muted"); glyph.HorizontalAlignment = HorizontalAlignment.Center; glyph.Margin = new Thickness(0, 0, 0, 14); empty.Children.Add(glyph);
        var name = Text(I18n.T(title), 16); name.FontWeight = HeadingWeight; name.TextAlignment = TextAlignment.Center; empty.Children.Add(name);
        var hint = Text(I18n.T(description), 12, true); hint.TextAlignment = TextAlignment.Center; hint.MaxWidth = 420; empty.Children.Add(hint); body.Children.Add(empty);
    }
    private CheckBox Toggle(string key, bool value, Action<bool> change)
    {
        var box = new CheckBox { Content = I18n.T(key), IsChecked = value };
        System.Windows.Automation.AutomationProperties.SetName(box, I18n.T(key));
        box.Checked += (_, _) => change(true); box.Unchecked += (_, _) => change(false); return box;
    }
    private Grid SettingRow(string key, UIElement control, string? hint = null, string? icon = null)
    {
        var row = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(icon is null ? 0 : 42) });
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (icon is not null) { var glyph = Icons.Create(icon, 21, "Muted"); glyph.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(glyph); }
        var labels = Stack(); var title = Text(I18n.T(key), 14); title.Margin = new Thickness(0, 0, 0, hint is null ? 0 : 4); labels.Children.Add(title);
        if (hint is not null) { var detail = Text(I18n.T(hint), 12, true); detail.Margin = new Thickness(0); labels.Children.Add(detail); }
        labels.Margin = new Thickness(0, 0, 22, 0); labels.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(labels, 1); row.Children.Add(labels);
        control.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); Grid.SetColumn(control, 2); row.Children.Add(control); return row;
    }
    private void Divider(StackPanel body) => body.Children.Add(new Border { Height = 1, Background = Resource("Line") });
    private Border? selectedNavigationIndicator;
    private Button? selectedNavigationButton;
    private StackPanel? pageBody;
    public void Navigate(string destination)
    {
        if (page == "detective" && destination != page && detectiveRunning) operation?.Cancel();
        if (page == "network" && destination != page && networkRunning) operation?.Cancel();
        bool changed = page != destination;
        double? previous = selectedNavigationIndicator?.IsLoaded == true ? selectedNavigationIndicator.TranslatePoint(new System.Windows.Point(), sidebar).Y : null;
        page = destination; emphasizeTranslationSettings = pendingTranslationSettings = false; Render();
        if (!changed || !FluentMotion.Allowed) return;
        UpdateLayout();
        if (previous is { } y && selectedNavigationIndicator is { } indicator)
        {
            var move = new TranslateTransform(0, y - indicator.TranslatePoint(new System.Windows.Point(), sidebar).Y);
            indicator.RenderTransform = move; FluentMotion.To(move, TranslateTransform.YProperty, 0, 180);
        }
        if (pageBody is { } body)
        {
            body.Opacity = 0; FluentMotion.Opacity(body, 1, 160);
            var move = new TranslateTransform(0, 6); body.RenderTransform = move;
            FluentMotion.To(move, TranslateTransform.YProperty, 0, 180);
        }
    }
    private bool emphasizeTranslationSettings, pendingTranslationSettings;
    private Border? translationProviderSection;
    private System.Windows.Controls.Control? translationKeyInput;
    private ComboBox? translationModelInput;
    private void OpenTranslationSettings()
    {
        page = "settings"; emphasizeTranslationSettings = pendingTranslationSettings = true; Render();
    }

    public void Render(bool preserveScroll = false)
    {
        StopDeviceMonitor();
        selectedNavigationIndicator = null;
        selectedNavigationButton = null;
        translationProviderSection = null; translationKeyInput = null; translationModelInput = null;
        double offset = preserveScroll ? scroller.VerticalOffset : 0;
        frame.Children.Clear(); frame.RowDefinitions.Clear(); frame.Children.Add(shell);
        scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        status = new TextBlock { Text = OperationStatusText, Foreground = Resource("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetAutomationId(status, "OperationStatus");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(status, System.Windows.Automation.AutomationLiveSetting.Polite);
        shell.Children.Clear(); shell.ColumnDefinitions.Clear(); shell.RowDefinitions.Clear();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        var navigation = new DockPanel { Margin = new Thickness(8, 10, 8, 14) };
        var header = new Grid { Height = 68, Margin = new Thickness(4, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); header.ColumnDefinitions.Add(new ColumnDefinition());
        var menu = new Button { Style = (Style)FindResource("IconButton"), Content = Icons.Create("menu", 18), ToolTip = I18n.T("menu"), HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(menu, I18n.T("menu"));
        menu.Click += (_, _) =>
        {
            double from = sidebar.ActualWidth; expanded = !expanded; Render(true);
            if (FluentMotion.Allowed) sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(from, expanded ? 224 : 68, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        };
        header.Children.Add(menu);
        if (expanded)
        {
            var wordmark = Stack(); wordmark.VerticalAlignment = VerticalAlignment.Center; wordmark.Margin = new Thickness(8, 0, 4, 0);
            var name = Text(Arabic ? "بصيرة" : "Baseera", 21); name.FontWeight = HeadingWeight; name.Margin = new Thickness(0, 0, 0, 2); wordmark.Children.Add(name);
            var label = Text(I18n.T("windowsTools"), 11, true); label.Margin = new Thickness(0); wordmark.Children.Add(label); Grid.SetColumn(wordmark, 1); header.Children.Add(wordmark);
        }
        DockPanel.SetDock(header, Dock.Top); navigation.Children.Add(header);
        var bottom = Stack(); DeviceId(bottom, "SidebarFooter");
        bottom.Children.Add(NavigationButton("feedback"));
        bottom.Children.Add(NavigationButton("settings"));
        if (expanded) { var version = Text("Baseera " + SelfUpdater.CurrentVersion + " · " + I18n.T("preview"), 10, true); version.Margin = new Thickness(16, 10, 0, 0); DeviceId(version, "SidebarVersion"); bottom.Children.Add(version); }
        DockPanel.SetDock(bottom, Dock.Bottom); navigation.Children.Add(bottom);
        var links = Stack(); var primary = Stack(); DeviceId(primary, "SidebarPrimary");
        foreach (string destination in PrimaryPages) primary.Children.Add(NavigationButton(destination)); links.Children.Add(primary);
        if (expanded) { var label = Text(I18n.T("yourTools"), 11, true); label.Margin = new Thickness(16, 22, 0, 8); links.Children.Add(label); }
        else links.Children.Add(new Border { Height = 18 });
        var toolLinks = Stack(); DeviceId(toolLinks, "SidebarTools");
        foreach (string tool in Tools) toolLinks.Children.Add(NavigationButton(tool)); links.Children.Add(toolLinks);
        if (expanded) { var label = Text(I18n.T("hubCenters"), 11, true); label.Margin = new Thickness(16, 22, 0, 8); links.Children.Add(label); }
        var centerLinks = Stack(); DeviceId(centerLinks, "SidebarCenters");
        foreach (string center in InvestigationCenters.Except(PrimaryPages).Except(Tools)) centerLinks.Children.Add(NavigationButton(center)); links.Children.Add(centerLinks);
        var linksScroller = new ScrollViewer { Content = links, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        navigation.Children.Add(linksScroller);
        sidebar.BeginAnimation(WidthProperty, null); sidebar.Width = expanded ? 224 : 68; sidebar.Background = Resource("Sidebar"); sidebar.ClipToBounds = true; sidebar.Child = navigation;
        sidebar.BorderBrush = Resource("Line"); sidebar.BorderThickness = Arabic ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0); shell.Children.Add(sidebar);

        var main = new Grid(); Grid.SetColumn(main, 1); shell.Children.Add(main);
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 32, 0) };
        var languages = LanguageCombo(); languages.MinWidth = 134; languages.Margin = new Thickness(0, 0, 8, 0); languages.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(languages);
        var theme = new Button { Style = (Style)FindResource("IconButton"), Content = Icons.Create(Settings.Current.Dark ? "sun" : "moon", 18), ToolTip = I18n.T("dark") };
        System.Windows.Automation.AutomationProperties.SetName(theme, I18n.T("dark"));
        theme.Click += (_, _) => { Settings.Current.Dark = !Settings.Current.Dark; Settings.Save(); ApplyTheme(); Render(true); }; toolbar.Children.Add(theme); main.Children.Add(toolbar);
        var body = Stack(); pageBody = body; body.Margin = new Thickness(36, 4, 36, 28); scroller.Content = body; Grid.SetRow(scroller, 1); main.Children.Add(scroller);
        var progress = new DockPanel(); cancelOperation = Action("cancel", () => operation?.Cancel()); cancelOperation.Margin = new Thickness(12, 0, 0, 0); cancelOperation.Padding = new Thickness(12, 5, 12, 5);
        cancelOperation.Visibility = operation is null ? Visibility.Collapsed : Visibility.Visible; DockPanel.SetDock(cancelOperation, Dock.Right); progress.Children.Add(cancelOperation); progress.Children.Add(status);
        operationBar = new Border { Child = progress, Background = Resource("Surface"), BorderBrush = Resource("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(36, 10, 36, 10), Visibility = status.Text.Length == 0 && operation is null ? Visibility.Collapsed : Visibility.Visible };
        Grid.SetRow(operationBar, 2); main.Children.Add(operationBar);
        if (!Settings.Current.Onboarded) Setup(body);
        else switch (page)
        {
            case "scan": ScanPage(body); break; case "translate": TranslationPage(body); break;
            case "updates": UpdatesPage(body); break; case "convert": ConvertPage(body); break;
            case "errors": ErrorAssistantPage(body); break;
            case "health": DeviceHealthPage(body); break; case "performance": PerformancePage(body); break;
            case "cases": CasesPage(body); break; case "activity": TasksPage(body); break;
            case "feedback": FeedbackPage(body); break;
            case "settings": SettingsPage(body); break;
            case "doctor": DoctorPage(body); break; case "detective": DetectivePage(body); break; case "security": SecurityPage(body); break; case "network": NetworkPage(body); break;
            case "storage": StoragePage(body); break; case "search": SearchPage(body); break; case "timeline": TimelinePage(body); break; case "usb": UsbPage(body); break;
            default: ControlHome(body); break;
        }
        StartDeviceMonitor();
        if (selectedNavigationButton is { } activeNavigation)
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!activeNavigation.IsLoaded) return;
                if (PrimaryPages.Contains(page)) linksScroller.ScrollToHome();
                else if (InvestigationCenters.Contains(page))
                {
                    linksScroller.UpdateLayout();
                    double top = activeNavigation.TranslatePoint(new System.Windows.Point(), linksScroller).Y + linksScroller.VerticalOffset;
                    linksScroller.ScrollToVerticalOffset(Math.Max(0, top - 56));
                }
                else activeNavigation.BringIntoView();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        if (offset > 0) _ = Dispatcher.InvokeAsync(() => scroller.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        if (pendingTranslationSettings && translationProviderSection is { } section)
        {
            pendingTranslationSettings = false;
            var scroll = scroller;
            var input = translationKeyInput;
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(translationProviderSection, section)) return;
                UpdateLayout();
                double top = section.TransformToAncestor(scroll).Transform(new System.Windows.Point()).Y + scroll.VerticalOffset;
                scroll.ScrollToVerticalOffset(Math.Max(0, top - 12)); scroll.UpdateLayout();
                input?.Focus(); if (input is not null) { System.Windows.Input.Keyboard.Focus(input); input.BringIntoView(); }
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private Button NavigationButton(string id)
    {
        bool selected = Settings.Current.Onboarded && page == id;
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        if (selected)
        {
            selectedNavigationIndicator = new Border { Background = Resource("Accent"), Width = 3, Height = 18, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(selectedNavigationIndicator);
        }
        var icon = Icons.Create(id, 18); icon.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(icon, 1); row.Children.Add(icon);
        if (expanded) { var name = new TextBlock { Text = I18n.T(id), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), FontWeight = selected ? HeadingWeight : FontWeights.Normal, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 150 }; Grid.SetColumn(name, 2); row.Children.Add(name); }
        var button = new Button { Style = (Style)FindResource("NavigationButton"), Content = row, Background = selected ? Resource("Selected") : Brushes.Transparent, ToolTip = I18n.T(id) };
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, "Navigate-" + id);
        System.Windows.Automation.AutomationProperties.SetName(button, I18n.T(id)); button.Click += (_, _) => Navigate(id);
        if (selected) selectedNavigationButton = button;
        return button;
    }
    private ComboBox LanguageCombo()
    {
        var combo = new ComboBox { ItemsSource = new[] { "العربية", "English", "Français", "Español" }, FlowDirection = FlowDirection.LeftToRight,
            SelectedIndex = Array.IndexOf(new[] { "ar", "en", "fr", "es" }, Settings.Current.Language), HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(combo, I18n.T("language"));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < 0) return; Settings.Current.Language = new[] { "ar", "en", "fr", "es" }[combo.SelectedIndex]; Settings.Save(); ApplyTheme(); Render(); }; return combo;
    }
    private bool IsEnabledFeature(string key) => InvestigationCenters.Contains(key) ? !Settings.Current.DisabledCenters.Contains(key) : key switch { "scan" => Settings.Current.ScanEnabled, "translate" => Settings.Current.TranslationEnabled, "updates" => Settings.Current.UpdatesEnabled, "convert" => Settings.Current.ConvertEnabled, "errors" => Settings.Current.ErrorAssistantEnabled, _ => true };
    private void SetFeature(string key, bool value)
    {
        if (InvestigationCenters.Contains(key))
        {
            Settings.Current.DisabledCenters.RemoveAll(x => x == key);
            if (!value) { Settings.Current.DisabledCenters.Add(key); operation?.Cancel(); }
            Settings.Save(); return;
        }
        switch (key)
        {
            case "scan": Settings.Current.ScanEnabled = value; break;
            case "translate": Settings.Current.TranslationEnabled = value; if (!value) overlay?.Close(); break;
            case "updates": Settings.Current.UpdatesEnabled = value; break; case "convert": Settings.Current.ConvertEnabled = value; break;
            case "errors": Settings.Current.ErrorAssistantEnabled = value; if (!value) errorAssistant?.Close(); break;
        }
        if (!value) { operation?.Cancel(); if (key == "updates") automaticUpdate?.Cancel(); }
        Settings.Save(); ApplyExplorerShortcut();
    }
    private void FeatureChoices(StackPanel body)
    {
        var rows = Stack();
        for (int i = 0; i < Tools.Length; i++)
        {
            string key = Tools[i]; var toggle = Toggle(key, IsEnabledFeature(key), value => SetFeature(key, value)); toggle.Content = null; toggle.Margin = new Thickness(0);
            rows.Children.Add(SettingRow(key, toggle, key + "Short", key)); if (i < Tools.Length - 1) Divider(rows);
        }
        body.Children.Add(Card(rows));
    }
    private void Setup(StackPanel body)
    {
        Heading(body, "setupNew", "setupDesc"); FeatureChoices(body);
        var next = Action("continue", () => { Settings.Current.Onboarded = true; Settings.Save(); page = "home"; Render(); }, true); next.HorizontalAlignment = HorizontalAlignment.Left; next.MinWidth = 140; body.Children.Add(next);
        Help(body, "setupHint");
    }
    private bool Guard(StackPanel body, string feature)
    {
        Heading(body, feature, feature + "Short"); if (IsEnabledFeature(feature)) return true;
        var panel = Stack(); panel.Children.Add(Text(I18n.T("disabledDesc"))); var settings = Action("manageTools", () => Navigate("settings"), true); settings.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(settings); body.Children.Add(Card(panel)); return false;
    }
    private void ErrorAssistantPage(StackPanel body)
    {
        if (!Guard(body, "errors")) return;
        var panel = Stack();
        panel.Children.Add(Text(I18n.T("errorAssistantIntro")));
        var open = Action("errorsOpen", ShowErrorAssistant, true);
        open.HorizontalAlignment = HorizontalAlignment.Left;
        open.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(open);
        body.Children.Add(Card(panel));
        Help(body, "errorPrivacy");
    }
    private void FeedbackPage(StackPanel body)
    {
        Heading(body, "feedback", "feedbackDesc");
        var panel = Stack();
        panel.Children.Add(Text(I18n.T("feedbackKind")));
        var kind = new ComboBox { ItemsSource = new[] { I18n.T("feedbackIdea"), I18n.T("feedbackBug"), I18n.T("feedbackOther") }, SelectedIndex = feedbackKindDraft };
        kind.SelectionChanged += (_, _) => feedbackKindDraft = kind.SelectedIndex;
        panel.Children.Add(kind);
        panel.Children.Add(Text(I18n.T("feedbackTitle")));
        var title = new TextBox { Text = feedbackTitleDraft, MaxLength = 120 };
        title.TextChanged += (_, _) => feedbackTitleDraft = title.Text;
        panel.Children.Add(title);
        panel.Children.Add(Text(I18n.T("feedbackDetails")));
        var details = new TextBox { Text = feedbackBodyDraft, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 170,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 4000 };
        details.TextChanged += (_, _) => feedbackBodyDraft = details.Text;
        panel.Children.Add(details);
        var message = Text("", 12, true); panel.Children.Add(message);
        var send = Action("feedbackContinue", () =>
        {
            if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(details.Text))
            { message.Text = I18n.T("feedbackRequired"); return; }
            try
            {
                string category = kind.SelectedIndex switch { 1 => "Bug", 2 => "Other", _ => "Idea" };
                string issueTitle = $"[{category}] {title.Text.Trim()}";
                string issueBody = details.Text.Trim() + $"\n\nBaseera {SelfUpdater.CurrentVersion} · {Settings.Current.Language}";
                string url = "https://github.com/yabs117/baseera/issues/new?title=" + Uri.EscapeDataString(issueTitle) + "&body=" + Uri.EscapeDataString(issueBody);
                if (url.Length > 1800)
                {
                    System.Windows.Clipboard.SetText(issueBody);
                    url = "https://github.com/yabs117/baseera/issues/new?title=" + Uri.EscapeDataString(issueTitle);
                    message.Text = I18n.T("feedbackCopied");
                }
                else message.Text = I18n.T("feedbackReview");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { message.Text = I18n.T("feedbackFailed") + " " + ex.Message; }
        }, true);
        send.HorizontalAlignment = HorizontalAlignment.Left; panel.Children.Add(send);
        body.Children.Add(Card(panel)); Help(body, "feedbackPrivacy");
    }
    private void Home(StackPanel body)
    {
        Heading(body, "home", "overviewDesc");
        SectionHeading(body, "yourTools"); var rows = Stack();
        for (int i = 0; i < Tools.Length; i++)
        {
            string key = Tools[i]; var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            var icon = Icons.Create(key, 23, "Muted"); icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
            var copy = Stack(); var title = Text(I18n.T(key), 15); title.FontWeight = HeadingWeight; title.Margin = new Thickness(0, 0, 0, 5); copy.Children.Add(title); var description = Text(I18n.T(key + "Short"), 12, true); description.Margin = new Thickness(0); copy.Children.Add(description); copy.Margin = new Thickness(0, 0, 20, 0); Grid.SetColumn(copy, 1); row.Children.Add(copy);
            var state = Text(I18n.T(IsEnabledFeature(key) ? "enabled" : "disabled"), 11, true); state.VerticalAlignment = VerticalAlignment.Center; state.Margin = new Thickness(8, 0, 16, 0); Grid.SetColumn(state, 2); row.Children.Add(state);
            var chevron = Icons.Create(Arabic ? "chevronLeft" : "chevronRight", 15, "Muted"); Grid.SetColumn(chevron, 3); row.Children.Add(chevron);
            var button = new Button { Style = (Style)FindResource("ToolRowButton"), Content = row }; System.Windows.Automation.AutomationProperties.SetName(button, I18n.T(key)); button.Click += (_, _) => Navigate(key); rows.Children.Add(button); if (i < Tools.Length - 1) Divider(rows);
        }
        body.Children.Add(Card(rows, 0)); Help(body, "private");
    }
    private void SettingsPage(StackPanel body)
    {
        Heading(body, "settings", "settingsDesc");
        AiSettingsSection(body);
        SelfUpdateSettings(body);
        SectionHeading(body, "appearance"); var appearance = Stack(); var language = LanguageCombo(); language.Margin = new Thickness(0); appearance.Children.Add(SettingRow("language", language)); Divider(appearance);
        var dark = Toggle("dark", Settings.Current.Dark, value => { Settings.Current.Dark = value; Settings.Save(); ApplyTheme(); Render(true); }); dark.Content = null;
        appearance.Children.Add(SettingRow("dark", dark)); Divider(appearance);
        var welcomeStartup = Toggle("welcomeRepeat", Settings.Current.ShowWelcomeOnStartup, value => { Settings.Current.ShowWelcomeOnStartup = value; Settings.Save(); }); welcomeStartup.Content = null;
        appearance.Children.Add(SettingRow("welcomeRepeat", welcomeStartup)); Divider(appearance);
        var welcome = Action("welcomeReopen", ShowWelcome); welcome.Margin = new Thickness(0); appearance.Children.Add(SettingRow("welcomeGuide", welcome)); body.Children.Add(Card(appearance));
        SectionHeading(body, "yourTools"); FeatureChoices(body);
        SectionHeading(body, "hubCenters"); var centers = Stack();
        foreach (string center in InvestigationCenters.Except(Tools)) centers.Children.Add(Toggle(center, IsEnabledFeature(center), enabled => SetFeature(center, enabled)));
        centers.Children.Add(Text(I18n.T("hubLocalFirst"), 12, true)); body.Children.Add(Card(centers));
        var monitoring = Stack(); monitoring.Children.Add(Toggle("deviceAutoRefresh", Settings.Current.AutoRefreshDevice, value => { Settings.Current.AutoRefreshDevice = value; Settings.Save(); })); monitoring.Children.Add(Text(I18n.T("deviceLocalOnly"), 12, true)); body.Children.Add(Card(monitoring));
        SectionHeading(body, "windowsIntegration"); var windows = Stack();
        windows.Children.Add(Toggle("startup", startupDraft ?? Settings.Current.StartWithWindows, value => startupDraft = value));
        windows.Children.Add(Toggle("startupScan", startupScanDraft ?? Settings.Current.ScanAtStartup, value => startupScanDraft = value));
        if (Settings.Current.ScanPath.Length > 0) windows.Children.Add(Text(Settings.Current.ScanPath, 12, true));
        windows.Children.Add(Toggle("explorer", explorerDraft ?? Settings.Current.ExplorerMenu, value => explorerDraft = value)); Help(windows, "explorerNote");
        var apply = AsyncAction("save", _ =>
        {
            Settings.Current.StartWithWindows = startupDraft ?? Settings.Current.StartWithWindows;
            Settings.Current.ScanAtStartup = startupScanDraft ?? Settings.Current.ScanAtStartup;
            Settings.Current.ExplorerMenu = explorerDraft ?? Settings.Current.ExplorerMenu;
            Settings.Save(); Settings.ApplyWindowsIntegration(); ApplyExplorerShortcut(); startupDraft = startupScanDraft = explorerDraft = null; Render(true); return Task.CompletedTask;
        }, true); apply.HorizontalAlignment = HorizontalAlignment.Left; windows.Children.Add(apply); body.Children.Add(Card(windows));
    }
    private void AiSettingsSection(StackPanel body)
    {
        var provider = Stack(); SectionHeading(provider, "aiSettingsTitle");
        provider.Children.Add(Text(I18n.T("aiSettingsHelp"), 12, true));
        var connection = new AiConnectionPanel(aiConnectionDraft, () => SetOperationStatus("done"), probe: ConnectionProbeOverride, openAssistant: ShowErrorAssistant);
        provider.Children.Add(connection);
        translationKeyInput = connection.MissingInput; translationModelInput = connection.ModelInput;
        translationProviderSection = Card(provider);
        System.Windows.Automation.AutomationProperties.SetAutomationId(translationProviderSection, "TranslationProviderSection");
        if (emphasizeTranslationSettings) translationProviderSection.BorderBrush = Resource("Accent");
        body.Children.Add(translationProviderSection);
    }

    internal async Task CheckNavigation()
    {
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        if (!WindowAppearance.HasNativeCaption(this) || WindowStyle != WindowStyle.SingleBorderWindow) throw new InvalidOperationException("Native window controls are missing.");
        Navigate("home"); UpdateLayout();
        Button Menu() => Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("menu"));
        Menu().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); if (expanded) throw new InvalidOperationException("Navigation did not collapse.");
        Menu().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout(); if (!expanded) throw new InvalidOperationException("Navigation did not expand.");
        sidebar.BeginAnimation(WidthProperty, null); sidebar.Width = 224;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var languages = Descendants(frame).OfType<ComboBox>().Single(); languages.IsDropDownOpen = true; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (languages.Template.FindName("PART_Popup", languages) is not System.Windows.Controls.Primitives.Popup { IsOpen: true }) throw new InvalidOperationException("Language menu did not open.");
        languages.IsDropDownOpen = false; languages.SelectedIndex = Arabic ? 1 : 0; UpdateLayout();
        if (shell.FlowDirection != (Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight)) throw new InvalidOperationException("Language direction did not update.");
        var bottomLinks = ((DockPanel)sidebar.Child).Children.OfType<StackPanel>().Single(panel => DockPanel.GetDock(panel) == Dock.Bottom)
            .Children.OfType<Button>().Select(button => System.Windows.Automation.AutomationProperties.GetName(button)).ToArray();
        if (!bottomLinks.SequenceEqual(new[] { I18n.T("feedback"), I18n.T("settings") }))
            throw new InvalidOperationException("Feedback must appear directly above Settings at the end of navigation.");
        Navigate("home"); UpdateLayout();
        if (Descendants(frame).OfType<Button>().Count(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("errors")) != 2)
            throw new InvalidOperationException("Error assistant is missing from Overview or Your tools navigation.");
        Navigate("errors"); UpdateLayout();
        if (!Descendants(frame).OfType<Button>().Any(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("errorsOpen")))
            throw new InvalidOperationException("Error assistant page has no open action.");
        Navigate("feedback"); UpdateLayout();
        var submit = Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("feedbackContinue"));
        submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (!Descendants(frame).OfType<TextBlock>().Any(label => label.Text == I18n.T("feedbackRequired")))
            throw new InvalidOperationException("Empty feedback was not stopped before opening GitHub.");
        Navigate("convert"); UpdateLayout(); if (!Descendants(frame).OfType<ComboBox>().Any(c => c.Items.Contains("PDF"))) throw new InvalidOperationException("Conversion formats unavailable.");
        SystemCommands.MaximizeWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Maximized) throw new InvalidOperationException("Native maximize failed.");
        SystemCommands.RestoreWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Normal) throw new InvalidOperationException("Native restore failed.");
        SystemCommands.MinimizeWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Minimized) throw new InvalidOperationException("Native minimize failed.");
        SystemCommands.RestoreWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        aiConnectionDraft.Provider = "gemini"; aiConnectionDraft.For("gemini").Model = "gemini-3.8-flash";
        aiConnectionDraft.For("gemini").Models = [new("gemini-3.8-flash", "Gemini Flash", "Google", "aiTierVaries")];
        Navigate("settings"); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var password = Descendants(frame).OfType<PasswordBox>().Single(); password.Password = "baseera-ui-draft-not-a-real-key";
        aiConnectionDraft.For("gemini").Models = [new("gemini-3.8-flash", "Gemini Flash", "Google", "aiTierVaries")];
        var theme = Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("dark"));
        theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var picker = Descendants(frame).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetAutomationId(c) == "AiModelPicker");
        if (Descendants(frame).OfType<PasswordBox>().Single().Password != "baseera-ui-draft-not-a-real-key" || (picker.SelectedItem as AiModelOption)?.Id != "gemini-3.8-flash")
            throw new InvalidOperationException("Appearance change discarded unsaved translation settings.");
        aiConnectionDraft.Services.Clear();
    }
}
