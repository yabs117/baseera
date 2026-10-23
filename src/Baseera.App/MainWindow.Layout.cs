using System.Windows.Media.Animation;
using Baseera.App.Services;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private Border? operationBar;
    private bool? startupDraft, startupScanDraft, explorerDraft;
    private string providerKeyDraft = "";
    private string? modelDraft;
    private bool Arabic => Settings.Current.Language == "ar";
    private static readonly string[] Tools = ["scan", "translate", "updates", "convert"];
    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
    private static StackPanel Stack() => new() { Orientation = Orientation.Vertical };
    private FontWeight HeadingWeight => Arabic ? FontWeights.Medium : FontWeights.SemiBold;

    public void ApplyTheme()
    {
        string[] keys = ["Bg", "Surface", "Ink", "Muted", "Line", "Accent", "Sidebar", "Selected", "Hover", "OnAccent", "Subtle"];
        string[] light = ["#FAFAFA", "#FFFFFF", "#1B1B1B", "#666666", "#E4E4E4", "#005FB8", "#F3F3F3", "#E5E5E5", "#EEEEEE", "#FFFFFF", "#F6F6F6"];
        string[] dark = ["#272727", "#303030", "#F4F4F4", "#B1B1B1", "#414141", "#79B8E8", "#202020", "#383838", "#3A3A3A", "#172534", "#2B2B2B"];
        var palette = Settings.Current.Dark ? dark : light;
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
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
    private async Task Run(Func<CancellationToken, Task> work)
    {
        if (operation is not null) return;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        if (cancelOperation is not null) cancelOperation.Visibility = Visibility.Visible;
        if (operationBar is not null) operationBar.Visibility = Visibility.Visible;
        status.Text = I18n.T("working");
        try { await work(operation.Token); status.Text = I18n.T("done"); }
        catch (OperationCanceledException) { status.Text = I18n.T("cancel"); }
        catch (Exception ex) { status.Text = I18n.T("error"); MessageBox.Show(this, ex.Message, I18n.T("error"), MessageBoxButton.OK, MessageBoxImage.Warning); }
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
    public void Navigate(string destination) { page = destination; emphasizeTranslationSettings = pendingTranslationSettings = false; Render(); }
    private bool emphasizeTranslationSettings, pendingTranslationSettings;
    private Border? translationProviderSection;
    private PasswordBox? translationKeyInput;
    private TextBox? translationModelInput;
    private void OpenTranslationSettings()
    {
        page = "settings"; emphasizeTranslationSettings = pendingTranslationSettings = true; Render();
    }

    public void Render(bool preserveScroll = false)
    {
        translationProviderSection = null; translationKeyInput = null; translationModelInput = null;
        double offset = preserveScroll ? scroller.VerticalOffset : 0;
        frame.Children.Clear(); frame.RowDefinitions.Clear(); frame.Children.Add(shell);
        scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        status = new TextBlock { Text = status.Text, Foreground = Resource("Muted"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        shell.Children.Clear(); shell.ColumnDefinitions.Clear(); shell.RowDefinitions.Clear();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        var navigation = new DockPanel { Margin = new Thickness(8, 10, 8, 14) };
        var header = new Grid { Height = 68, Margin = new Thickness(4, 0, 0, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); header.ColumnDefinitions.Add(new ColumnDefinition());
        var menu = new Button { Style = (Style)FindResource("IconButton"), Content = Icons.Create("menu", 18), ToolTip = I18n.T("menu"), HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(menu, I18n.T("menu"));
        menu.Click += (_, _) =>
        {
            double from = expanded ? 224 : 68; expanded = !expanded; Render();
            sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(from, expanded ? 224 : 68, TimeSpan.FromMilliseconds(130)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
        };
        header.Children.Add(menu);
        if (expanded)
        {
            var wordmark = Stack(); wordmark.VerticalAlignment = VerticalAlignment.Center; wordmark.Margin = new Thickness(8, 0, 4, 0);
            var name = Text(Arabic ? "بصيرة" : "Baseera", 21); name.FontWeight = HeadingWeight; name.Margin = new Thickness(0, 0, 0, 2); wordmark.Children.Add(name);
            var label = Text(I18n.T("windowsTools"), 11, true); label.Margin = new Thickness(0); wordmark.Children.Add(label); Grid.SetColumn(wordmark, 1); header.Children.Add(wordmark);
        }
        DockPanel.SetDock(header, Dock.Top); navigation.Children.Add(header);
        var bottom = Stack(); bottom.Children.Add(NavigationButton("settings"));
        if (expanded) { var version = Text("Baseera " + SelfUpdater.CurrentVersion + " · " + I18n.T("preview"), 10, true); version.Margin = new Thickness(16, 18, 0, 0); bottom.Children.Add(version); }
        DockPanel.SetDock(bottom, Dock.Bottom); navigation.Children.Add(bottom);
        var links = Stack(); links.Children.Add(NavigationButton("home"));
        if (expanded) { var label = Text(I18n.T("yourTools"), 11, true); label.Margin = new Thickness(16, 22, 0, 8); links.Children.Add(label); }
        else links.Children.Add(new Border { Height = 18 });
        foreach (string tool in Tools) links.Children.Add(NavigationButton(tool));
        navigation.Children.Add(links);
        sidebar.BeginAnimation(WidthProperty, null); sidebar.Width = expanded ? 224 : 68; sidebar.Background = Resource("Sidebar"); sidebar.Child = navigation;
        sidebar.BorderBrush = Resource("Line"); sidebar.BorderThickness = Arabic ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0); shell.Children.Add(sidebar);

        var main = new Grid(); Grid.SetColumn(main, 1); shell.Children.Add(main);
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) }); main.RowDefinitions.Add(new RowDefinition()); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 0, 32, 0) };
        var languages = LanguageCombo(); languages.MinWidth = 134; languages.Margin = new Thickness(0, 0, 8, 0); languages.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(languages);
        var theme = new Button { Style = (Style)FindResource("IconButton"), Content = Icons.Create(Settings.Current.Dark ? "sun" : "moon", 18), ToolTip = I18n.T("dark") };
        System.Windows.Automation.AutomationProperties.SetName(theme, I18n.T("dark"));
        theme.Click += (_, _) => { Settings.Current.Dark = !Settings.Current.Dark; Settings.Save(); ApplyTheme(); Render(true); }; toolbar.Children.Add(theme); main.Children.Add(toolbar);
        var body = Stack(); body.Margin = new Thickness(36, 4, 36, 28); scroller.Content = body; Grid.SetRow(scroller, 1); main.Children.Add(scroller);
        var progress = new DockPanel(); cancelOperation = Action("cancel", () => operation?.Cancel()); cancelOperation.Margin = new Thickness(12, 0, 0, 0); cancelOperation.Padding = new Thickness(12, 5, 12, 5);
        cancelOperation.Visibility = operation is null ? Visibility.Collapsed : Visibility.Visible; DockPanel.SetDock(cancelOperation, Dock.Right); progress.Children.Add(cancelOperation); progress.Children.Add(status);
        operationBar = new Border { Child = progress, Background = Resource("Surface"), BorderBrush = Resource("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(36, 10, 36, 10), Visibility = status.Text.Length == 0 && operation is null ? Visibility.Collapsed : Visibility.Visible };
        Grid.SetRow(operationBar, 2); main.Children.Add(operationBar);
        if (!Settings.Current.Onboarded) Setup(body);
        else switch (page)
        {
            case "scan": ScanPage(body); break; case "translate": TranslationPage(body); break;
            case "updates": UpdatesPage(body); break; case "convert": ConvertPage(body); break;
            case "settings": SettingsPage(body); break; default: Home(body); break;
        }
        if (offset > 0) _ = Dispatcher.InvokeAsync(() => scroller.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        if (pendingTranslationSettings && translationProviderSection is { } section)
        {
            pendingTranslationSettings = false;
            var scroll = scroller;
            var input = !File.Exists(Path.Combine(Settings.Folder, "secrets.bin")) ? (System.Windows.Controls.Control?)translationKeyInput : translationModelInput;
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(translationProviderSection, section)) return;
                UpdateLayout();
                double top = section.TransformToAncestor(scroll).Transform(new System.Windows.Point()).Y + scroll.VerticalOffset;
                scroll.ScrollToVerticalOffset(Math.Max(0, top - 12)); scroll.UpdateLayout();
                input?.Focus(); if (input is not null) System.Windows.Input.Keyboard.Focus(input);
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private Button NavigationButton(string id)
    {
        bool selected = Settings.Current.Onboarded && page == id;
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        if (selected) row.Children.Add(new Border { Background = Resource("Accent"), Width = 3, Height = 18, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center });
        var icon = Icons.Create(id, 18); icon.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(icon, 1); row.Children.Add(icon);
        if (expanded) { var name = new TextBlock { Text = I18n.T(id), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), FontWeight = selected ? HeadingWeight : FontWeights.Normal, TextWrapping = TextWrapping.NoWrap }; Grid.SetColumn(name, 2); row.Children.Add(name); }
        var button = new Button { Style = (Style)FindResource("NavigationButton"), Content = row, Background = selected ? Resource("Selected") : Brushes.Transparent, ToolTip = I18n.T(id) };
        System.Windows.Automation.AutomationProperties.SetName(button, I18n.T(id)); button.Click += (_, _) => Navigate(id); return button;
    }
    private ComboBox LanguageCombo()
    {
        var combo = new ComboBox { ItemsSource = new[] { "العربية", "English", "Français", "Español" }, FlowDirection = FlowDirection.LeftToRight,
            SelectedIndex = Array.IndexOf(new[] { "ar", "en", "fr", "es" }, Settings.Current.Language), HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(combo, I18n.T("language"));
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex < 0) return; Settings.Current.Language = new[] { "ar", "en", "fr", "es" }[combo.SelectedIndex]; Settings.Save(); ApplyTheme(); Render(); }; return combo;
    }
    private bool IsEnabledFeature(string key) => key switch { "scan" => Settings.Current.ScanEnabled, "translate" => Settings.Current.TranslationEnabled, "updates" => Settings.Current.UpdatesEnabled, "convert" => Settings.Current.ConvertEnabled, _ => true };
    private void SetFeature(string key, bool value)
    {
        switch (key)
        {
            case "scan": Settings.Current.ScanEnabled = value; break;
            case "translate": Settings.Current.TranslationEnabled = value; if (!value) overlay?.Close(); break;
            case "updates": Settings.Current.UpdatesEnabled = value; break; case "convert": Settings.Current.ConvertEnabled = value; break;
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
        SelfUpdateSettings(body);
        SectionHeading(body, "appearance"); var appearance = Stack(); var language = LanguageCombo(); language.Margin = new Thickness(0); appearance.Children.Add(SettingRow("language", language)); Divider(appearance);
        var dark = Toggle("dark", Settings.Current.Dark, value => { Settings.Current.Dark = value; Settings.Save(); ApplyTheme(); Render(true); }); dark.Content = null;
        appearance.Children.Add(SettingRow("dark", dark)); Divider(appearance);
        var welcomeStartup = Toggle("welcomeRepeat", Settings.Current.ShowWelcomeOnStartup, value => { Settings.Current.ShowWelcomeOnStartup = value; Settings.Save(); }); welcomeStartup.Content = null;
        appearance.Children.Add(SettingRow("welcomeRepeat", welcomeStartup)); Divider(appearance);
        var welcome = Action("welcomeReopen", ShowWelcome); welcome.Margin = new Thickness(0); appearance.Children.Add(SettingRow("welcomeGuide", welcome)); body.Children.Add(Card(appearance));
        SectionHeading(body, "yourTools"); FeatureChoices(body);
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
        var provider = Stack(); SectionHeading(provider, "translationProvider"); provider.Children.Add(Text(I18n.T("apiKey")));
        var key = new PasswordBox { FlowDirection = FlowDirection.LeftToRight, Password = providerKeyDraft }; key.PasswordChanged += (_, _) => providerKeyDraft = key.Password; provider.Children.Add(key);
        translationKeyInput = key; System.Windows.Automation.AutomationProperties.SetName(key, I18n.T("apiKey"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(key, "GeminiApiKey");
        if (File.Exists(Path.Combine(Settings.Folder, "secrets.bin")))
        {
            provider.Children.Add(Text(I18n.T("keySaved"), 12, true)); var forget = Action("forgetKey", () => { providerKeyDraft = ""; Settings.SetKey(""); Render(true); }); forget.HorizontalAlignment = HorizontalAlignment.Left; provider.Children.Add(forget);
        }
        provider.Children.Add(Text(I18n.T("model"))); var model = new TextBox { Text = modelDraft ?? Settings.Current.GeminiModel, FlowDirection = FlowDirection.LeftToRight }; model.TextChanged += (_, _) => modelDraft = model.Text; provider.Children.Add(model);
        translationModelInput = model; System.Windows.Automation.AutomationProperties.SetName(model, I18n.T("model"));
        System.Windows.Automation.AutomationProperties.SetAutomationId(model, "GeminiModelId");
        var save = AsyncAction("save", _ =>
        {
            if (!string.IsNullOrWhiteSpace(key.Password)) Settings.SetKey(key.Password);
            Settings.Current.GeminiModel = model.Text.Trim(); Settings.Save(); providerKeyDraft = ""; modelDraft = null; Render(true); return Task.CompletedTask;
        }, true); save.HorizontalAlignment = HorizontalAlignment.Left; provider.Children.Add(save);
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
        Navigate("convert"); UpdateLayout(); if (!Descendants(frame).OfType<ComboBox>().Any(c => c.Items.Contains("PDF"))) throw new InvalidOperationException("Conversion formats unavailable.");
        SystemCommands.MaximizeWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Maximized) throw new InvalidOperationException("Native maximize failed.");
        SystemCommands.RestoreWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Normal) throw new InvalidOperationException("Native restore failed.");
        SystemCommands.MinimizeWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (WindowState != WindowState.Minimized) throw new InvalidOperationException("Native minimize failed.");
        SystemCommands.RestoreWindow(this); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Navigate("settings"); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var password = Descendants(frame).OfType<PasswordBox>().Single(); password.Password = "baseera-ui-draft-not-a-real-key";
        var draft = Descendants(frame).OfType<TextBox>().Single(); draft.Text = "draft-model";
        var theme = Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("dark"));
        theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (Descendants(frame).OfType<PasswordBox>().Single().Password != "baseera-ui-draft-not-a-real-key" || Descendants(frame).OfType<TextBox>().Single().Text != "draft-model")
            throw new InvalidOperationException("Appearance change discarded unsaved translation settings.");
        providerKeyDraft = ""; modelDraft = null;
    }
}
