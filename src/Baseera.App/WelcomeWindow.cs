using Baseera.App.Services;

namespace Baseera.App;

internal sealed class WelcomeWindow : Window
{
    private readonly MainWindow owner;
    private readonly Grid frame = new();
    internal bool ShowAgain { get; private set; }
    internal static bool ShouldShow(Preferences preferences) => !preferences.WelcomeSeen || preferences.ShowWelcomeOnStartup;

    internal WelcomeWindow(MainWindow owner)
    {
        this.owner = owner; Owner = owner; ShowAgain = Settings.Current.ShowWelcomeOnStartup;
        WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(740, SystemParameters.WorkArea.Width - 32);
        Height = Math.Min(660, SystemParameters.WorkArea.Height - 32);
        FontFamily = Brand.InterfaceFont; FontSize = 13; Icon = Brand.WindowIcon;
        FlowDirection = FlowDirection.LeftToRight; UseLayoutRounding = true; SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink"); Content = frame;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
        Render();
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static StackPanel Stack() => new() { Orientation = Orientation.Vertical };
    private TextBlock Text(string text, double size = 13, bool muted = false) => new()
    { Text = text, FontSize = size, Foreground = Brush(muted ? "Muted" : "Ink"), TextWrapping = TextWrapping.Wrap };
    private void RefreshOwner() { owner.ApplyTheme(); owner.Render(true); Render(); }

    private void Render()
    {
        bool arabic = Settings.Current.Language == "ar";
        Title = I18n.T("welcomeCaption");
        frame.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Bg");
        frame.Children.Clear(); frame.RowDefinitions.Clear(); frame.ColumnDefinitions.Clear();
        var layout = new Grid { FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); frame.Children.Add(layout);

        var header = Stack(); header.Margin = new Thickness(28, 22, 28, 20);
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 22) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition()); toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = Text(arabic ? "بصيرة" : "Baseera", 19); name.FontWeight = arabic ? FontWeights.Medium : FontWeights.SemiBold; name.VerticalAlignment = VerticalAlignment.Center; toolbar.Children.Add(name);
        var choices = new StackPanel { Orientation = Orientation.Horizontal };
        var language = new ComboBox
        {
            ItemsSource = new[] { "العربية", "English", "Français", "Español" }, FlowDirection = FlowDirection.LeftToRight,
            SelectedIndex = Array.IndexOf(new[] { "ar", "en", "fr", "es" }, Settings.Current.Language), MinWidth = 128, FontSize = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(language, I18n.T("language"));
        language.SelectionChanged += (_, _) => { if (language.SelectedIndex < 0) return; Settings.Current.Language = new[] { "ar", "en", "fr", "es" }[language.SelectedIndex]; Settings.Save(); RefreshOwner(); };
        choices.Children.Add(language);
        var theme = new Button { Style = (Style)FindResource("IconButton"), Content = Icons.Create(Settings.Current.Dark ? "sun" : "moon", 18), ToolTip = I18n.T("dark") };
        System.Windows.Automation.AutomationProperties.SetName(theme, I18n.T("dark"));
        theme.Click += (_, _) => { Settings.Current.Dark = !Settings.Current.Dark; Settings.Save(); RefreshOwner(); WindowAppearance.Apply(this, Settings.Current.Dark); };
        choices.Children.Add(theme); Grid.SetColumn(choices, 1); toolbar.Children.Add(choices); header.Children.Add(toolbar);
        var heading = Text(I18n.T("welcomeTitle"), 28); heading.FontWeight = arabic ? FontWeights.Medium : FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 10); header.Children.Add(heading);
        var introduction = Text(I18n.T("welcomeIntro"), 13, true); introduction.LineHeight = 21; header.Children.Add(introduction); layout.Children.Add(header);

        var body = Stack(); body.Margin = new Thickness(28, 0, 28, 16);
        var tools = Stack(); string[] keys = ["scan", "translate", "updates", "convert"];
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i]; var row = new Grid { Margin = new Thickness(20, 15, 20, 15) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = Icons.Create(key, 22, "Muted"); icon.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(icon);
            var copy = Stack(); var title = Text(I18n.T(key), 14); title.FontWeight = arabic ? FontWeights.Medium : FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 0, 5); copy.Children.Add(title);
            var description = Text(I18n.T(key + "Short"), 12, true); description.LineHeight = 19; copy.Children.Add(description); Grid.SetColumn(copy, 1); row.Children.Add(copy); tools.Children.Add(row);
            if (i < keys.Length - 1) tools.Children.Add(new Border { Height = 1, Background = Brush("Line"), Margin = new Thickness(20, 0, 20, 0) });
        }
        body.Children.Add(new Border { Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Child = tools });
        var privacy = new Grid { Margin = new Thickness(0, 16, 0, 0) }; privacy.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) }); privacy.ColumnDefinitions.Add(new ColumnDefinition());
        var info = Icons.Create("info", 16, "Muted"); info.VerticalAlignment = VerticalAlignment.Top; info.Margin = new Thickness(0, 2, 0, 0); privacy.Children.Add(info);
        var detail = Text(I18n.T("welcomePrivacy"), 12, true); detail.LineHeight = 19; Grid.SetColumn(detail, 1); privacy.Children.Add(detail); body.Children.Add(privacy);
        var scroller = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroller, 1); layout.Children.Add(scroller);

        var footer = Stack(); footer.Margin = new Thickness(28, 16, 28, 20);
        var next = Text(I18n.T(Settings.Current.Onboarded ? "welcomeExisting" : "welcomeNext"), 12, true); next.Margin = new Thickness(0, 0, 0, 14); footer.Children.Add(next);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var repeat = new CheckBox { IsChecked = ShowAgain, Content = new TextBlock { Text = I18n.T("welcomeRepeat"), TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(repeat, I18n.T("welcomeRepeat"));
        repeat.Checked += (_, _) => ShowAgain = true; repeat.Unchecked += (_, _) => ShowAgain = false; actions.Children.Add(repeat);
        var start = new Button
        {
            Content = new TextBlock { Text = I18n.T(Settings.Current.Onboarded ? "welcomeBack" : "welcomeStart"), Foreground = Brush("OnAccent") },
            Background = Brush("Accent"), BorderBrush = Brush("Accent"), MinWidth = 148, Margin = new Thickness(0), IsDefault = true
        };
        System.Windows.Automation.AutomationProperties.SetName(start, I18n.T(Settings.Current.Onboarded ? "welcomeBack" : "welcomeStart"));
        start.Click += (_, _) => Close(); Grid.SetColumn(start, 1); actions.Children.Add(start); footer.Children.Add(actions);
        var footerBorder = new Border { Child = footer, BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brush("Surface") }; Grid.SetRow(footerBorder, 2); layout.Children.Add(footerBorder);
    }
}
