using System.Diagnostics;
using System.Text.Json;
using System.Windows.Media.Animation;
using Baseera.App.Services;
using Baseera.Core;
using Microsoft.Win32;

namespace Baseera.App;

public sealed class MainWindow : Window
{
    private readonly Grid shell = new();
    private readonly Grid frame = new();
    private readonly Border sidebar = new();
    private ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private TextBlock status = new();
    private Button? cancelOperation;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private CancellationTokenSource? automaticUpdate;
    private TranslationOverlay? overlay;
    private bool expanded = true;
    private string page = "home";
    private List<string> images = [];
    private ScanReport? report;
    private string updateLog = "";
    private ExplorerShortcut? explorerShortcut;
    private readonly bool smokeMode;
    private readonly System.Windows.Threading.DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    public MainWindow(string? conversionFile = null, bool smoke = false)
    {
        smokeMode = smoke;
        Title = "Baseera"; Width = 1220; Height = 860; MinWidth = 960; MinHeight = 700;
        WindowStyle = WindowStyle.None;
        System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
        { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(12) });
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 14;
        if (conversionFile is not null && File.Exists(conversionFile)) { images.Add(conversionFile); page = "convert"; }
        Content = frame;
        ApplyTheme(); Render();
        Closed += (_, _) => { operation?.Cancel(); lifetime.Cancel(); updateTimer.Stop(); overlay?.Close(); explorerShortcut?.Dispose(); };
        if (!smoke)
        {
            ApplyExplorerShortcut();
            updateTimer.Tick += async (_, _) => await AutomaticUpdates(); updateTimer.Start();
            Loaded += async (_, _) =>
            {
                if (!Settings.Current.Onboarded) return;
                if (Settings.Current.ScanEnabled && Settings.Current.ScanAtStartup && (File.Exists(Settings.Current.ScanPath) || Directory.Exists(Settings.Current.ScanPath)))
                    await Run(async ct => { report = await Task.Run(() => new ModScanner().Scan(Settings.Current.ScanPath, ct), ct); page = "scan"; Render(); });
                await AutomaticUpdates();
            };
        }
    }
    private void ApplyExplorerShortcut()
    {
        explorerShortcut?.Dispose(); explorerShortcut = null;
        if (smokeMode || !Settings.Current.ConvertEnabled || !Settings.Current.ExplorerMenu) return;
        explorerShortcut = new ExplorerShortcut(files => { images = files; page = "convert"; Show(); WindowState = WindowState.Normal; Activate(); Render(); });
    }
    private async Task AutomaticUpdates()
    {
        if (!Settings.Current.Onboarded || !Settings.Current.UpdatesEnabled || Settings.Current.AutoUpdateIds.Count == 0) return;
        automaticUpdate = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try { updateLog = await Updater.UpdateSelected(automaticUpdate.Token); if (page == "updates") Render(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { updateLog = ex.Message; status.Text = I18n.T("error"); }
        finally { automaticUpdate.Dispose(); automaticUpdate = null; }
    }
    private static Brush Resource(string key) => (Brush)Application.Current.FindResource(key);
    public void ApplyTheme()
    {
        var colors = Settings.Current.Dark ? new[] { "#161C20", "#20282D", "#EEF3F2", "#A6B5BC", "#344148", "#85CDB5", "#1A2226", "#293E38", "#2B363C", "#142D24", "#C3ADEB", "#353044", "#9ABFE5", "#283A4B", "#DFC28C", "#40392B" } :
            new[] { "#F7F9F8", "#FFFFFF", "#223530", "#657570", "#E0E7E3", "#28674F", "#EEF2EF", "#E5F0E9", "#E6EBE7", "#FFFFFF", "#78619F", "#F0EBF7", "#4A749B", "#EAF1F8", "#997338", "#F8F0DF" };
        string[] keys = ["Bg", "Surface", "Ink", "Muted", "Line", "Accent", "Sidebar", "AccentSoft", "Hover", "OnAccent", "Violet", "VioletSoft", "Blue", "BlueSoft", "Amber", "AmberSoft"];
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        FlowDirection = FlowDirection.LeftToRight;
        shell.FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        frame.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Bg");
    }
    private TextBlock Text(string text, double size = 14, bool muted = false) => new()
    { Text = text, FontSize = size, Foreground = Resource(muted ? "Muted" : "Ink"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private Button Action(string key, Action action, bool primary = false)
    {
        var b = new Button { Content = I18n.T(key), HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) { b.Background = Resource("Accent"); b.Foreground = Resource("OnAccent"); b.BorderBrush = b.Background; b.Content = new TextBlock { Text = I18n.T(key), Foreground = b.Foreground }; }
        b.Click += (_, _) => action(); return b;
    }
    private Button AsyncAction(string key, Func<CancellationToken, Task> action, bool primary = false) =>
        Action(key, async () => await Run(action), primary);
    private async Task Run(Func<CancellationToken, Task> work)
    {
        if (operation is not null) return;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        if (cancelOperation is not null) cancelOperation.Visibility = Visibility.Visible;
        status.Text = I18n.T("working");
        try { await work(operation.Token); status.Text = I18n.T("done"); }
        catch (OperationCanceledException) { status.Text = I18n.T("cancel"); }
        catch (Exception ex) { status.Text = I18n.T("error"); MessageBox.Show(this, ex.Message, I18n.T("error"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { operation.Dispose(); operation = null; if (cancelOperation is not null) cancelOperation.Visibility = Visibility.Collapsed; }
    }
    private static StackPanel Stack() => new() { Orientation = Orientation.Vertical };
    private Border Card(UIElement child) => new() { Background = Resource("Surface"), BorderBrush = Resource("Line"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 14), Child = child };
    private CheckBox Toggle(string key, bool value, Action<bool> change)
    {
        var box = new CheckBox { Content = I18n.T(key), IsChecked = value };
        box.Checked += (_, _) => change(true); box.Unchecked += (_, _) => change(false); return box;
    }
    private void Heading(StackPanel body, string title, string? subtitle = null)
    { var heading = Text(I18n.T(title), 32); heading.FontWeight = FontWeights.SemiBold; body.Children.Add(heading); if (subtitle is not null) body.Children.Add(Text(I18n.T(subtitle), 14, true)); }
    public void Navigate(string destination) { page = destination; Render(); }
    internal async Task CheckNavigation()
    {
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        Navigate("home"); UpdateLayout();
        var menu = Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("menu"));
        menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout();
        if (expanded) throw new InvalidOperationException("Navigation did not collapse.");
        menu = Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("menu"));
        menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout();
        if (!expanded) throw new InvalidOperationException("Navigation did not expand.");
        sidebar.BeginAnimation(WidthProperty, null); sidebar.Width = 236;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var languages = Descendants(frame).OfType<ComboBox>().Single();
        languages.IsDropDownOpen = true; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (languages.Template.FindName("PART_Popup", languages) is not System.Windows.Controls.Primitives.Popup { IsOpen: true })
            throw new InvalidOperationException("Language menu did not open.");
        languages.IsDropDownOpen = false;
        languages.SelectedIndex = Settings.Current.Language == "ar" ? 1 : 0; UpdateLayout();
        if (shell.FlowDirection != (Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight))
            throw new InvalidOperationException("Language selection did not update layout direction.");
        Navigate("convert"); UpdateLayout();
        if (!Descendants(frame).OfType<ComboBox>().Any(c => c.Items.Contains("PDF"))) throw new InvalidOperationException("Conversion formats unavailable after navigation.");
    }
    private void TitleBar()
    {
        frame.Children.Clear(); frame.RowDefinitions.Clear();
        frame.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) }); frame.RowDefinitions.Add(new RowDefinition());
        var bar = new Grid { Background = Resource("Sidebar") };
        bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = "Baseera", FontSize = 12, Foreground = Resource("Muted"), Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(title);
        bar.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else DragMove(); };
        var controls = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(controls, 1); bar.Children.Add(controls);
        foreach (var id in new[] { "minimize", "maximize", "close" })
        {
            var button = new Button { Content = Icons.Create(id, 12), Width = 46, Height = 34, Margin = new Thickness(0, 3, 3, 3), Padding = new Thickness(10), BorderThickness = new Thickness(0), Background = Brushes.Transparent, ToolTip = I18n.T(id) };
            System.Windows.Automation.AutomationProperties.SetName(button, I18n.T(id));
            button.Click += (_, _) => { if (id == "close") Close(); else if (id == "minimize") WindowState = WindowState.Minimized; else WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; };
            controls.Children.Add(button);
        }
        frame.Children.Add(bar); Grid.SetRow(shell, 1); frame.Children.Add(shell);
    }
    public void Render()
    {
        TitleBar();
        scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        status = new TextBlock { Text = status.Text };
        shell.Children.Clear(); shell.ColumnDefinitions.Clear(); shell.RowDefinitions.Clear();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        var navLayout = new DockPanel { Margin = new Thickness(16, 14, 16, 20) };
        var nav = Stack();
        var menu = new Button { Content = Icons.Create("menu", 18), ToolTip = I18n.T("menu"), Width = 40, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
        System.Windows.Automation.AutomationProperties.SetName(menu, I18n.T("menu"));
        menu.Click += (_, _) => { expanded = !expanded; Render(); sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(expanded ? 80 : 236, expanded ? 236 : 80, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }); };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 30) };
        brand.Children.Add(new Border { Background = Resource("Accent"), CornerRadius = new CornerRadius(12), Width = 44, Height = 44, Child = Icons.Create("mark", 25, "OnAccent") });
        if (expanded) brand.Children.Add(new TextBlock { Text = Settings.Current.Language == "ar" ? "بصيرة" : "Baseera", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        nav.Children.Add(brand);
        if (expanded) { var label = Text(I18n.T("workspace"), 11, true); label.Margin = new Thickness(12, 0, 0, 14); nav.Children.Add(label); }
        Button NavButton(string id)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Icons.Create(id, 19, page == id ? "Accent" : "Muted"));
            if (expanded) row.Children.Add(new TextBlock { Text = I18n.T(id), FontSize = 13, Foreground = Resource(page == id ? "Accent" : "Ink"), FontWeight = page == id ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = row, ToolTip = I18n.T(id), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 13, 10, 13), BorderThickness = new Thickness(0),
                Background = page == id ? Resource("AccentSoft") : Brushes.Transparent, Margin = new Thickness(0, 0, 0, 5) };
            System.Windows.Automation.AutomationProperties.SetName(button, I18n.T(id));
            button.Click += (_, _) => Navigate(id); return button;
        }
        foreach (var id in new[] { "home", "scan", "translate", "updates", "convert" }) nav.Children.Add(NavButton(id));
        var bottom = Stack(); bottom.Children.Add(NavButton("settings"));
        if (expanded) { bottom.Children.Add(new Border { Height = 1, Background = Resource("Line"), Margin = new Thickness(10, 14, 10, 18) }); var label = Text(I18n.T("early"), 11, true); label.Margin = new Thickness(12, 0, 0, 0); bottom.Children.Add(label); }
        DockPanel.SetDock(bottom, Dock.Bottom); navLayout.Children.Add(bottom); navLayout.Children.Add(nav);
        sidebar.BeginAnimation(WidthProperty, null); sidebar.Width = expanded ? 236 : 80; sidebar.Background = Resource("Sidebar"); sidebar.Child = navLayout;
        sidebar.BorderBrush = Resource("Line"); sidebar.BorderThickness = new Thickness(0, 0, 1, 0); shell.Children.Add(sidebar);
        var right = new Grid(); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(62) }); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(right, 1); shell.Children.Add(right);
        var toolbar = new DockPanel { Margin = new Thickness(28, 0, 30, 0), LastChildFill = true };
        DockPanel.SetDock(menu, Dock.Left); menu.VerticalAlignment = VerticalAlignment.Center; menu.Margin = new Thickness(0, 0, 12, 0); toolbar.Children.Add(menu);
        var theme = new Button { Content = Icons.Create(Settings.Current.Dark ? "sun" : "moon", 18), ToolTip = I18n.T("dark"), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(10), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(theme, I18n.T("dark"));
        theme.Click += (_, _) => { Settings.Current.Dark = !Settings.Current.Dark; Settings.Save(); ApplyTheme(); Render(); };
        DockPanel.SetDock(theme, Dock.Right); toolbar.Children.Add(theme);
        var language = LanguageCombo(); language.MinWidth = 125; language.FontSize = 12; language.Margin = new Thickness(0); language.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(language, Dock.Right); toolbar.Children.Add(language);
        toolbar.Children.Add(new TextBlock { Text = I18n.T(Settings.Current.Onboarded ? page : "setup"), FontSize = 12, Foreground = Resource("Muted"), VerticalAlignment = VerticalAlignment.Center });
        right.Children.Add(new Border { Child = toolbar, BorderBrush = Resource("Line"), BorderThickness = new Thickness(0, 0, 0, 1) });
        var body = Stack(); body.Margin = new Thickness(40, 30, 40, 16);
        scroller.Content = body; Grid.SetRow(scroller, 1); right.Children.Add(scroller);
        var footer = new DockPanel { Margin = new Thickness(40, 0, 40, 16) };
        status.Foreground = Resource("Muted"); status.FontSize = 12; status.VerticalAlignment = VerticalAlignment.Center;
        var cancel = Action("cancel", () => operation?.Cancel()); cancel.Padding = new Thickness(8, 3, 8, 3); cancel.Visibility = operation is null ? Visibility.Collapsed : Visibility.Visible; DockPanel.SetDock(cancel, Dock.Right);
        cancelOperation = cancel;
        footer.Children.Add(cancel); footer.Children.Add(status); Grid.SetRow(footer, 2); right.Children.Add(footer);
        if (!Settings.Current.Onboarded) { Setup(body); return; }
        switch (page)
        {
            case "scan": ScanPage(body); break;
            case "translate": TranslationPage(body); break;
            case "updates": UpdatesPage(body); break;
            case "convert": ConvertPage(body); break;
            case "settings": SettingsPage(body); break;
            default: Home(body); break;
        }
    }
    private void LanguagePicker(StackPanel body)
    {
        body.Children.Add(Text(I18n.T("language"), 13, true));
        body.Children.Add(LanguageCombo());
    }
    private ComboBox LanguageCombo()
    {
        var languages = new ComboBox { ItemsSource = new[] { "العربية", "English", "Français", "Español" },
            FlowDirection = FlowDirection.LeftToRight, SelectedIndex = Array.IndexOf(new[] { "ar", "en", "fr", "es" }, Settings.Current.Language), HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(languages, I18n.T("language"));
        languages.SelectionChanged += (_, _) => { if (languages.SelectedIndex < 0) return; Settings.Current.Language = new[] { "ar", "en", "fr", "es" }[languages.SelectedIndex]; Settings.Save(); ApplyTheme(); Render(); };
        return languages;
    }
    private void FeatureChoices(StackPanel body)
    {
        var grid = FeatureGrid(); int index = 0;
        foreach (var key in new[] { "scan", "translate", "updates", "convert" })
        {
            var card = Stack();
            card.Children.Add(FeatureIcon(key));
            card.Children.Add(Toggle(key, IsEnabledFeature(key), value =>
            {
                switch (key) { case "scan": Settings.Current.ScanEnabled = value; break; case "translate": Settings.Current.TranslationEnabled = value; if (!value) overlay?.Close(); break; case "updates": Settings.Current.UpdatesEnabled = value; break; case "convert": Settings.Current.ConvertEnabled = value; break; }
                if (!value) { operation?.Cancel(); if (key == "updates") automaticUpdate?.Cancel(); } Settings.Save(); ApplyExplorerShortcut();
            }));
            card.Children.Add(Text(I18n.T(key + "Desc"), 13, true)); AddFeatureCard(grid, card, index++);
        }
        body.Children.Add(grid);
    }
    private static Grid FeatureGrid()
    {
        var grid = new Grid { Margin = new Thickness(-7, 10, -7, 14) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }
    private void AddFeatureCard(Grid grid, UIElement content, int index)
    {
        var card = Card(content); card.Margin = new Thickness(7); card.Padding = new Thickness(22); Grid.SetColumn(card, index % 2); Grid.SetRow(card, index / 2); grid.Children.Add(card);
    }
    private Border FeatureIcon(string key)
    {
        string color = key switch { "translate" => "Violet", "updates" => "Blue", "convert" => "Amber", _ => "Accent" };
        return new Border { Child = Icons.Create(key, 23, color), Background = Resource(color + "Soft"), CornerRadius = new CornerRadius(12), Width = 46, Height = 46, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16) };
    }
    private void Setup(StackPanel body)
    {
        Heading(body, "setup", "setupDesc"); FeatureChoices(body);
        var next = Action("continue", () => { Settings.Current.Onboarded = true; Settings.Save(); page = "home"; Render(); }, true); next.HorizontalAlignment = HorizontalAlignment.Left; next.MinWidth = 180; body.Children.Add(next);
    }
    private bool IsEnabledFeature(string key) => key switch { "scan" => Settings.Current.ScanEnabled, "translate" => Settings.Current.TranslationEnabled,
        "updates" => Settings.Current.UpdatesEnabled, "convert" => Settings.Current.ConvertEnabled, _ => true };
    private bool Guard(StackPanel body, string feature)
    {
        Heading(body, feature, feature + "Desc");
        if (IsEnabledFeature(feature)) return true;
        body.Children.Add(Card(Text(I18n.T("disabledDesc")))); body.Children.Add(Action("settings", () => Navigate("settings"))); return false;
    }
    private void Home(StackPanel body)
    {
        var eyebrow = Text(I18n.T("everyday"), 11, true); eyebrow.Margin = new Thickness(0, 0, 0, 10); body.Children.Add(eyebrow);
        Heading(body, "welcome", "intro");
        var section = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var manage = Action("manageTools", () => Navigate("settings")); manage.FontSize = 12; manage.Padding = new Thickness(10, 5, 10, 5); manage.Margin = new Thickness(0); manage.Background = Brushes.Transparent; manage.BorderThickness = new Thickness(0); DockPanel.SetDock(manage, Dock.Right); section.Children.Add(manage);
        var title = Text(I18n.T("yourTools"), 16); title.FontWeight = FontWeights.SemiBold; title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0); section.Children.Add(title); body.Children.Add(section);
        var grid = FeatureGrid(); int index = 0;
        foreach (var key in new[] { "scan", "translate", "updates", "convert" })
        {
            bool enabled = IsEnabledFeature(key);
            var details = Stack(); var top = new DockPanel();
            var badge = new Border { Background = Resource(enabled ? "AccentSoft" : "Bg"), CornerRadius = new CornerRadius(8), Padding = new Thickness(9, 4, 9, 4), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = I18n.T(enabled ? "enabled" : "disabled"), FontSize = 11, Foreground = Resource(enabled ? "Accent" : "Muted") } };
            DockPanel.SetDock(badge, Dock.Right); top.Children.Add(badge); top.Children.Add(FeatureIcon(key)); details.Children.Add(top);
            var name = Text(I18n.T(key), 19); name.FontWeight = FontWeights.SemiBold; details.Children.Add(name);
            var desc = Text(I18n.T(key + "Desc"), 13, true); desc.MinHeight = 44; desc.LineHeight = 20; details.Children.Add(desc);
            var link = Action(enabled ? "openTool" : "setUp", () => Navigate(enabled ? key : "settings")); link.HorizontalAlignment = HorizontalAlignment.Left; link.Padding = new Thickness(12, 7, 12, 7); link.Margin = new Thickness(0, 4, 0, 0); link.FontSize = 12; details.Children.Add(link);
            AddFeatureCard(grid, details, index++);
        }
        body.Children.Add(grid);
        var note = new DockPanel(); var shield = Icons.Create("scan", 16, "Muted"); shield.Margin = new Thickness(0, 0, 10, 0); note.Children.Add(shield); note.Children.Add(new TextBlock { Text = I18n.T("private"), FontSize = 12, Foreground = Resource("Muted"), VerticalAlignment = VerticalAlignment.Center }); body.Children.Add(note);
    }
    private void SettingsPage(StackPanel body)
    {
        Heading(body, "settings"); LanguagePicker(body);
        body.Children.Add(Toggle("dark", Settings.Current.Dark, value => { Settings.Current.Dark = value; Settings.Save(); ApplyTheme(); Render(); }));
        FeatureChoices(body);
        body.Children.Add(Toggle("startup", Settings.Current.StartWithWindows, v => Settings.Current.StartWithWindows = v));
        body.Children.Add(Toggle("startupScan", Settings.Current.ScanAtStartup, v => Settings.Current.ScanAtStartup = v));
        body.Children.Add(Text(Settings.Current.ScanPath.Length > 0 ? Settings.Current.ScanPath : I18n.T("choose"), 12, true));
        body.Children.Add(Toggle("explorer", Settings.Current.ExplorerMenu, v => Settings.Current.ExplorerMenu = v));
        body.Children.Add(Text(I18n.T("explorerNote"), 12, true));
        body.Children.Add(Text(I18n.T("apiKey")));
        var key = new PasswordBox { FlowDirection = FlowDirection.LeftToRight }; body.Children.Add(key);
        if (File.Exists(Path.Combine(Settings.Folder, "secrets.bin"))) body.Children.Add(Text(I18n.T("keySaved"), 12, true));
        body.Children.Add(Action("forgetKey", () => { Settings.SetKey(""); Render(); }));
        body.Children.Add(Text(I18n.T("model")));
        var model = new TextBox { Text = Settings.Current.GeminiModel, FlowDirection = FlowDirection.LeftToRight }; body.Children.Add(model);
        body.Children.Add(AsyncAction("save", _ =>
        {
            if (!string.IsNullOrWhiteSpace(key.Password)) Settings.SetKey(key.Password);
            Settings.Current.GeminiModel = model.Text.Trim(); Settings.Save(); Settings.ApplyWindowsIntegration(); ApplyExplorerShortcut(); Render(); return Task.CompletedTask;
        }, true));
    }
    private void ScanPage(StackPanel body)
    {
        if (!Guard(body, "scan")) return;
        var content = Stack(); body.Children.Add(Card(content)); body = content;
        body.Children.Add(Text(I18n.T("scanNote"), 13, true));
        var path = new TextBox { Text = Settings.Current.ScanPath, FlowDirection = FlowDirection.LeftToRight, IsReadOnly = true }; body.Children.Add(path);
        var actions = new WrapPanel(); body.Children.Add(actions);
        actions.Children.Add(Action("choose", () => { var picker = new OpenFileDialog(); if (picker.ShowDialog(this) == true) { Settings.Current.ScanPath = picker.FileName; Settings.Save(); path.Text = picker.FileName; } }));
        actions.Children.Add(Action("chooseFolder", () => { var picker = new OpenFolderDialog(); if (picker.ShowDialog(this) == true) { Settings.Current.ScanPath = picker.FolderName; Settings.Save(); path.Text = picker.FolderName; } }));
        actions.Children.Add(AsyncAction("inspect", async ct =>
        {
            if (!Settings.Current.ScanEnabled) return;
            report = await Task.Run(() => new ModScanner().Scan(Settings.Current.ScanPath, ct), ct); Render();
        }, true));
        if (report is null) return;
        var summary = Stack(); summary.Children.Add(Text(I18n.T(report.Verdict), 20));
        summary.Children.Add(Text($"{I18n.T("files")}: {report.Files.Count}   ·   {I18n.T("findings")}: {report.Findings.Count}", 13, true));
        body.Children.Add(Card(summary));
        body.Children.Add(AsyncAction("export", async ct => { var save = new SaveFileDialog { Filter = "JSON|*.json", FileName = "baseera-report.json" }; if (save.ShowDialog(this) == true) await File.WriteAllTextAsync(save.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), ct); }));
        foreach (var finding in report.Findings.Take(100))
        {
            var card = Stack(); card.Children.Add(Text(finding.Code, 15)); card.Children.Add(Text(finding.Path, 12, true)); card.Children.Add(Text(finding.Evidence, 13));
            card.FlowDirection = FlowDirection.LeftToRight; body.Children.Add(Card(card));
        }
    }
    private void TranslationPage(StackPanel body)
    {
        if (!Guard(body, "translate")) return;
        var content = Stack(); body.Children.Add(Card(content)); body = content;
        body.Children.Add(Text(I18n.T("translateNote"), 13, true));
        var windows = new ComboBox { ItemsSource = WindowTranslator.ListWindows(), DisplayMemberPath = "Title" }; body.Children.Add(windows);
        body.Children.Add(Action("refresh", () => windows.ItemsSource = WindowTranslator.ListWindows()));
        body.Children.Add(Text(I18n.T("target")));
        var target = new ComboBox { ItemsSource = new[] { "Arabic", "English", "French", "Spanish" }, SelectedItem = Settings.Current.TargetLanguage }; body.Children.Add(target);
        target.SelectionChanged += (_, _) => { Settings.Current.TargetLanguage = target.SelectedItem?.ToString() ?? "Arabic"; Settings.Save(); };
        body.Children.Add(AsyncAction("translateNow", async ct =>
        {
            if (!Settings.Current.TranslationEnabled) return;
            if (windows.SelectedItem is not WindowChoice window) throw new InvalidOperationException(I18n.T("selectWindow"));
            if (string.IsNullOrWhiteSpace(Settings.GetKey()) || string.IsNullOrWhiteSpace(Settings.Current.GeminiModel)) throw new InvalidOperationException(I18n.T("needsKey"));
            overlay?.Close();
            var capture = await WindowTranslator.CaptureWindow(window.Handle, ct);
            var blocks = await WindowTranslator.Translate(capture, ct);
            ct.ThrowIfCancellationRequested();
            if (!Settings.Current.TranslationEnabled) return;
            if (blocks.Count == 0) throw new InvalidOperationException(I18n.T("noText"));
            overlay = new TranslationOverlay(capture, blocks); overlay.Show(); overlay.Activate();
        }, true));
        body.Children.Add(Action("hideOverlay", () => overlay?.Close()));
    }
    private void UpdatesPage(StackPanel body)
    {
        if (!Guard(body, "updates")) return;
        body.Children.Add(Text(I18n.T("updateNote"), 13, true));
        var packages = Stack();
        packages.Children.Add(Text(I18n.T("refreshHint"), 13, true));
        body.Children.Add(AsyncAction("refresh", async ct =>
        {
            var list = await Updater.List(ct); packages.Children.Clear();
            foreach (var package in list)
            {
                var box = new CheckBox { Content = package.Id + "  ·  " + package.Version, IsChecked = Settings.Current.AutoUpdateIds.Contains(package.Id), FlowDirection = FlowDirection.LeftToRight };
                box.Checked += (_, _) => { if (!Settings.Current.AutoUpdateIds.Contains(package.Id)) Settings.Current.AutoUpdateIds.Add(package.Id); Settings.Save(); };
                box.Unchecked += (_, _) => { Settings.Current.AutoUpdateIds.Remove(package.Id); Settings.Save(); }; packages.Children.Add(box);
                var custom = new TextBox { Text = Settings.Current.ManualSources.GetValueOrDefault(package.Id, ""), FlowDirection = FlowDirection.LeftToRight,
                    ToolTip = I18n.T("manualPerApp"), FontSize = 12 };
                var manualEnabled = Toggle("manualPerApp", Settings.Current.ManualSources.ContainsKey(package.Id), value =>
                {
                    custom.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                    if (value) Settings.Current.ManualSources[package.Id] = custom.Text.Trim(); else Settings.Current.ManualSources.Remove(package.Id);
                    Settings.Save();
                });
                custom.Visibility = manualEnabled.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                custom.LostFocus += (_, _) => { if (manualEnabled.IsChecked == true) { Settings.Current.ManualSources[package.Id] = custom.Text.Trim(); Settings.Save(); } };
                packages.Children.Add(manualEnabled); packages.Children.Add(custom);
            }
            if (list.Count == 0) packages.Children.Add(Text(I18n.T("empty")));
        }));
        body.Children.Add(Card(packages));
        body.Children.Add(AsyncAction("updateNow", async ct => { updateLog = await Updater.UpdateSelected(ct); Render(); }, true));
        if (updateLog.Length > 0) body.Children.Add(new TextBox { Text = updateLog, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FlowDirection = FlowDirection.LeftToRight });
        body.Children.Add(Text(I18n.T("releasePage"), 20)); body.Children.Add(Text(I18n.T("releaseNote"), 13, true));
        var url = new TextBox { Text = Settings.Current.ReleasePage, FlowDirection = FlowDirection.LeftToRight }; body.Children.Add(url);
        var releases = Stack();
        body.Children.Add(AsyncAction("checkRelease", async ct =>
        {
            var release = await Updater.CheckRelease(url.Text.Trim(), ct); Settings.Current.ReleasePage = url.Text.Trim(); Settings.Save();
            releases.Children.Clear(); releases.Children.Add(Text(release.Version, 20));
            foreach (var asset in release.Assets)
            {
                var content = Stack(); content.Children.Add(Text(asset.Name)); content.Children.Add(Text($"{asset.Size / 1024 / 1024} MB · {asset.Download.Host}", 12, true));
                content.Children.Add(Action("openDownload", () => Process.Start(new ProcessStartInfo(asset.Download.AbsoluteUri) { UseShellExecute = true })));
                releases.Children.Add(Card(content));
            }
        })); body.Children.Add(releases);
    }
    private void ConvertPage(StackPanel body)
    {
        if (!Guard(body, "convert")) return;
        var content = Stack(); body.Children.Add(Card(content)); body = content;
        body.Children.Add(Text(I18n.T("convertNote"), 13, true));
        var list = new ListBox { ItemsSource = images, Height = 150, Background = Resource("Surface"), Foreground = Resource("Ink"), BorderBrush = Resource("Line"), FlowDirection = FlowDirection.LeftToRight };
        body.Children.Add(list);
        var controls = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) }; body.Children.Add(controls);
        controls.Children.Add(Action("chooseImages", () => { var picker = new OpenFileDialog { Multiselect = true, Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff" }; if (picker.ShowDialog(this) == true) { images.AddRange(picker.FileNames); Render(); } }));
        controls.Children.Add(Action("up", () => { int index = list.SelectedIndex; if (index > 0) { (images[index - 1], images[index]) = (images[index], images[index - 1]); Render(); } }));
        controls.Children.Add(Action("down", () => { int index = list.SelectedIndex; if (index >= 0 && index < images.Count - 1) { (images[index + 1], images[index]) = (images[index], images[index + 1]); Render(); } }));
        controls.Children.Add(Action("remove", () => { if (list.SelectedIndex >= 0) { images.RemoveAt(list.SelectedIndex); Render(); } }));
        body.Children.Add(Text(I18n.T("output"))); var format = new ComboBox { ItemsSource = new[] { "PNG", "JPEG", "BMP", "TIFF", "PDF" }, SelectedIndex = 0 }; body.Children.Add(format);
        body.Children.Add(Text(I18n.T("quality"))); var quality = new Slider { Minimum = 30, Maximum = 100, Value = 90, TickFrequency = 10, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 16) }; body.Children.Add(quality);
        body.Children.Add(AsyncAction("convertNow", _ =>
        {
            if (!Settings.Current.ConvertEnabled || images.Count == 0) throw new InvalidOperationException(I18n.T("chooseImages"));
            if ((string)format.SelectedItem == "PDF")
            {
                var save = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = "Baseera-images.pdf" };
                if (save.ShowDialog(this) == true) ImageConverter.ToPdf(images, save.FileName, (int)quality.Value);
            }
            else
            {
                var folder = new OpenFolderDialog(); if (folder.ShowDialog(this) == true)
                    foreach (var image in images) ImageConverter.ConvertImage(image, folder.FolderName, (string)format.SelectedItem, (int)quality.Value);
            }
            return Task.CompletedTask;
        }, true));
    }
}
