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
    private readonly Border sidebar = new();
    private ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private TextBlock status = new();
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
        Title = "Baseera · بصيرة"; Width = 1140; Height = 800; MinWidth = 820; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 14;
        if (conversionFile is not null && File.Exists(conversionFile)) { images.Add(conversionFile); page = "convert"; }
        Content = shell;
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
        var colors = Settings.Current.Dark ? new[] { "#182024", "#222D32", "#EDF2F1", "#A5B8BA", "#354449", "#4CB6A4" } :
            new[] { "#F5F5F3", "#FFFFFF", "#202B30", "#647479", "#E2E7E6", "#176B61" };
        string[] keys = ["Bg", "Surface", "Ink", "Muted", "Line", "Accent"];
        for (int i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
    private TextBlock Text(string text, double size = 14, bool muted = false) => new()
    { Text = text, FontSize = size, Foreground = Resource(muted ? "Muted" : "Ink"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private Button Action(string key, Action action, bool primary = false)
    {
        var b = new Button { Content = I18n.T(key), HorizontalContentAlignment = HorizontalAlignment.Center };
        if (primary) { b.Background = Resource("Accent"); b.Foreground = Settings.Current.Dark ? Brushes.Black : Brushes.White; b.Content = new TextBlock { Text = I18n.T(key), Foreground = b.Foreground }; }
        b.Click += (_, _) => action(); return b;
    }
    private Button AsyncAction(string key, Func<CancellationToken, Task> action, bool primary = false) =>
        Action(key, async () => await Run(action), primary);
    private async Task Run(Func<CancellationToken, Task> work)
    {
        if (operation is not null) return;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        status.Text = I18n.T("working");
        try { await work(operation.Token); status.Text = I18n.T("done"); }
        catch (OperationCanceledException) { status.Text = I18n.T("cancel"); }
        catch (Exception ex) { status.Text = I18n.T("error"); MessageBox.Show(this, ex.Message, I18n.T("error"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        finally { operation.Dispose(); operation = null; }
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
    { body.Children.Add(Text(I18n.T(title), 30)); if (subtitle is not null) body.Children.Add(Text(I18n.T(subtitle), 14, true)); }
    public void Navigate(string destination) { page = destination; Render(); }
    public void Render()
    {
        scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        status = new TextBlock { Text = status.Text };
        shell.Children.Clear(); shell.ColumnDefinitions.Clear(); shell.RowDefinitions.Clear();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); shell.ColumnDefinitions.Add(new ColumnDefinition());
        var nav = Stack(); nav.Margin = new Thickness(14, 20, 14, 18);
        var menu = new Button { Content = Icons.Create("menu"), ToolTip = I18n.T("menu"), Width = 44, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10) };
        System.Windows.Automation.AutomationProperties.SetName(menu, I18n.T("menu"));
        menu.Click += (_, _) => { expanded = !expanded; Render(); sidebar.BeginAnimation(WidthProperty, new DoubleAnimation(expanded ? 76 : 228, expanded ? 228 : 76, TimeSpan.FromMilliseconds(160))); };
        nav.Children.Add(menu);
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 20, 0, 34) };
        brand.Children.Add(Icons.Create("mark", 28));
        if (expanded) brand.Children.Add(new TextBlock { Text = Settings.Current.Language == "ar" ? "بصيرة" : "Baseera", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 12, 0) });
        nav.Children.Add(brand);
        foreach (var id in new[] { "home", "scan", "translate", "updates", "convert", "settings" })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Icons.Create(id, 20));
            if (expanded) row.Children.Add(new TextBlock { Text = I18n.T(id), Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = row, ToolTip = I18n.T(id), Padding = new Thickness(10, 13, 10, 13), BorderThickness = new Thickness(0),
                Background = page == id ? Resource("Line") : Brushes.Transparent, Margin = new Thickness(0, 0, 0, 5) };
            button.Click += (_, _) => { page = id; Render(); }; nav.Children.Add(button);
        }
        if (expanded) nav.Children.Add(Text(I18n.T("early"), 12, true));
        sidebar.Width = expanded ? 228 : 76; sidebar.Background = Resource("Surface"); sidebar.Child = nav;
        sidebar.BorderBrush = Resource("Line"); sidebar.BorderThickness = new Thickness(0, 0, 1, 0); shell.Children.Add(sidebar);
        var right = new Grid(); right.RowDefinitions.Add(new RowDefinition()); right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetColumn(right, 1); shell.Children.Add(right);
        var body = Stack(); body.Margin = new Thickness(36, 36, 36, 24);
        scroller.Content = body; right.Children.Add(scroller);
        var footer = new DockPanel { Margin = new Thickness(36, 0, 28, 12) };
        status.Foreground = Resource("Muted"); status.FontSize = 12; status.VerticalAlignment = VerticalAlignment.Center;
        var cancel = Action("cancel", () => operation?.Cancel()); cancel.Padding = new Thickness(8, 3, 8, 3); DockPanel.SetDock(cancel, Dock.Right);
        footer.Children.Add(cancel); footer.Children.Add(status); Grid.SetRow(footer, 1); right.Children.Add(footer);
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
        var languages = new ComboBox { ItemsSource = new[] { "العربية", "English", "Français", "Español" },
            SelectedIndex = Array.IndexOf(new[] { "ar", "en", "fr", "es" }, Settings.Current.Language), HorizontalAlignment = HorizontalAlignment.Left };
        languages.SelectionChanged += (_, _) => { Settings.Current.Language = new[] { "ar", "en", "fr", "es" }[languages.SelectedIndex]; Settings.Save(); ApplyTheme(); Render(); };
        body.Children.Add(languages);
    }
    private void FeatureChoices(StackPanel body)
    {
        foreach (var key in new[] { "scan", "translate", "updates", "convert" })
        {
            var card = Stack();
            card.Children.Add(Toggle(key, IsEnabledFeature(key), value =>
            {
                switch (key) { case "scan": Settings.Current.ScanEnabled = value; break; case "translate": Settings.Current.TranslationEnabled = value; if (!value) overlay?.Close(); break; case "updates": Settings.Current.UpdatesEnabled = value; break; case "convert": Settings.Current.ConvertEnabled = value; break; }
                if (!value) { operation?.Cancel(); if (key == "updates") automaticUpdate?.Cancel(); } Settings.Save(); ApplyExplorerShortcut();
            }));
            card.Children.Add(Text(I18n.T(key + "Desc"), 13, true)); body.Children.Add(Card(card));
        }
    }
    private void Setup(StackPanel body)
    {
        Heading(body, "setup", "setupDesc"); LanguagePicker(body); FeatureChoices(body);
        body.Children.Add(Action("continue", () => { Settings.Current.Onboarded = true; Settings.Save(); page = "home"; Render(); }, true));
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
        Heading(body, "welcome", "intro");
        body.Children.Add(new Border { Height = 20 });
        foreach (var key in new[] { "scan", "translate", "updates", "convert" })
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Icons.Create(key, 27); icon.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(icon);
            var details = Stack(); Grid.SetColumn(details, 1); row.Children.Add(details);
            details.Children.Add(Text(I18n.T(key), 19)); details.Children.Add(Text(I18n.T(key + "Desc"), 13, true));
            var link = Action(IsEnabledFeature(key) ? "enabled" : "disabled", () => Navigate(key)); link.HorizontalAlignment = HorizontalAlignment.Left; details.Children.Add(link);
            body.Children.Add(Card(row));
        }
        body.Children.Add(Text(I18n.T("private"), 12, true));
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
