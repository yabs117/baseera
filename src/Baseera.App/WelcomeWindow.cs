using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Markup;
using Baseera.App.Services;

namespace Baseera.App;

internal sealed class WelcomeWindow : Window
{
    private const double CompactWidth = 640;
    private readonly MainWindow owner;
    private readonly Grid frame = new();
    private bool compact;

    internal bool ShowAgain { get; private set; }
    internal static bool ShouldShow(Preferences preferences) => !preferences.WelcomeSeen || preferences.ShowWelcomeOnStartup;

    internal WelcomeWindow(MainWindow owner)
    {
        this.owner = owner;
        Owner = owner;
        ShowAgain = Settings.Current.ShowWelcomeOnStartup;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(800, SystemParameters.WorkArea.Width - 32);
        MaxHeight = SystemParameters.WorkArea.Height - 32;
        SizeToContent = SizeToContent.Height;
        Title = I18n.T("welcomeCaption");
        Language = XmlLanguage.GetLanguage(Settings.Current.Language);
        FontFamily = Brand.InterfaceFont;
        FontSize = 13;
        Icon = Brand.WindowIcon;
        FlowDirection = FlowDirection.LeftToRight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "Bg");
        SetResourceReference(ForegroundProperty, "Ink");
        frame.SetResourceReference(Grid.BackgroundProperty, "Bg");
        Content = frame;

        compact = Width < CompactWidth;
        Render();
        SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged || (e.NewSize.Width < CompactWidth) == compact) return;
            compact = e.NewSize.Width < CompactWidth;
            Render();
        };
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Close();
        };
    }

    private bool Arabic => Settings.Current.Language == "ar";
    private FontWeight HeadingWeight => Arabic ? FontWeights.Medium : FontWeights.SemiBold;

    private static TextBlock Copy(string key, double size, string color = "Ink")
    {
        var copy = new TextBlock { Text = I18n.T(key), FontSize = size, TextWrapping = TextWrapping.Wrap };
        copy.SetResourceReference(TextBlock.ForegroundProperty, color);
        return copy;
    }

    private void Render()
    {
        frame.Children.Clear();
        var layout = new Grid { FlowDirection = Arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.Children.Add(layout);

        double inset = compact ? 18 : 26;
        var content = new StackPanel { Margin = new Thickness(inset, 22, inset, 18) };
        content.Children.Add(BuildHero());

        var toolsHeading = Copy("yourTools", 16);
        toolsHeading.FontWeight = HeadingWeight;
        toolsHeading.Margin = new Thickness(0, 20, 0, 9);
        AutomationProperties.SetHeadingLevel(toolsHeading, AutomationHeadingLevel.Level2);
        content.Children.Add(toolsHeading);
        content.Children.Add(BuildTools());
        content.Children.Add(BuildPrivacyNote());

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        layout.Children.Add(scroll);

        var footer = BuildFooter(inset);
        Grid.SetRow(footer, 1);
        layout.Children.Add(footer);
    }

    private Border BuildHero()
    {
        var hero = new Grid();
        hero.ColumnDefinitions.Add(new ColumnDefinition());
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!compact) hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var heading = Copy("welcomeTitle", compact ? 27 : 33, "HeroInk");
        heading.FontWeight = HeadingWeight;
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
        AutomationProperties.SetAutomationId(heading, "WelcomeTitle");
        var intro = Copy("welcomeIntro", 13, "HeroMuted");
        intro.LineHeight = 21;
        intro.Margin = new Thickness(0, 7, 0, 0);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 12, 2) };
        copy.Children.Add(heading);
        copy.Children.Add(intro);
        hero.Children.Add(copy);

        var badge = new Border
        {
            Width = compact ? 44 : 94,
            Height = compact ? 44 : 94,
            CornerRadius = new CornerRadius(compact ? 10 : 18),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, compact ? 0 : 12, 0),
            Child = new System.Windows.Controls.Image
            {
                Source = Brand.WindowIcon,
                Width = compact ? 30 : 64,
                Height = compact ? 30 : 64,
                FlowDirection = FlowDirection.LeftToRight,
                IsHitTestVisible = false
            }
        };
        badge.SetResourceReference(Border.BorderBrushProperty, "HeroLine");

        var theme = new Button { Style = (Style)FindResource("IconButton"), VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetAutomationId(theme, "WelcomeTheme");
        void RefreshThemeButton()
        {
            bool dark = Settings.Current.Dark;
            theme.Content = Icons.Create(dark ? "sun" : "moon", 18, "HeroInk");
            string label = I18n.T(dark ? "welcomeThemeLight" : "welcomeThemeDark");
            theme.ToolTip = label;
            AutomationProperties.SetName(theme, label);
        }
        theme.Click += (_, _) =>
        {
            Settings.Current.Dark = !Settings.Current.Dark;
            Settings.Save();
            owner.ApplyTheme();
            owner.Render(true);
            WindowAppearance.Apply(this, Settings.Current.Dark);
            RefreshThemeButton();
        };
        RefreshThemeButton();

        if (compact)
        {
            var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(badge);
            theme.HorizontalAlignment = HorizontalAlignment.Center;
            theme.Margin = new Thickness(0, 5, 0, 0);
            controls.Children.Add(theme);
            Grid.SetColumn(controls, 1);
            hero.Children.Add(controls);
        }
        else
        {
            Grid.SetColumn(badge, 1);
            Grid.SetColumn(theme, 2);
            hero.Children.Add(badge);
            hero.Children.Add(theme);
        }

        var card = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(compact ? 16 : 24, 18, compact ? 14 : 16, 18),
            Child = hero
        };
        card.SetResourceReference(Border.BackgroundProperty, "Hero");
        return card;
    }

    private StackPanel BuildTools()
    {
        string[] keys = ["scan", "translate", "updates", "convert"];
        int columns = compact ? 1 : 2;
        var matrix = new Grid();
        for (int i = 0; i < columns; i++) matrix.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < keys.Length / columns; i++) matrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < keys.Length; i++)
        {
            int column = i % columns;
            var cell = new Border
            {
                BorderThickness = new Thickness(0, 0, column < columns - 1 ? 1 : 0, i < keys.Length - columns ? 1 : 0),
                Padding = new Thickness(15, 12, 15, 12),
                Child = ToolSummary(keys[i])
            };
            cell.SetResourceReference(Border.BorderBrushProperty, "Line");
            Grid.SetRow(cell, i / columns);
            Grid.SetColumn(cell, column);
            matrix.Children.Add(cell);
        }

        var tools = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = matrix };
        tools.SetResourceReference(Border.BackgroundProperty, "Surface");
        tools.SetResourceReference(Border.BorderBrushProperty, "Line");

        var repair = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(15, 12, 15, 12),
            Child = ToolSummary("errors", featured: true)
        };
        repair.SetResourceReference(Border.BackgroundProperty, "Tint");
        repair.SetResourceReference(Border.BorderBrushProperty, "Line");

        var guide = new StackPanel();
        guide.Children.Add(tools);
        guide.Children.Add(repair);
        return guide;
    }

    private Grid ToolSummary(string key, bool featured = false)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = Icons.Create(key, 19, "AccentInk");
        var tile = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = icon
        };
        tile.SetResourceReference(Border.BackgroundProperty, featured ? "Surface" : "Tint");
        row.Children.Add(tile);

        var title = Copy(key, 14, featured ? "AccentInk" : "Ink");
        title.FontWeight = HeadingWeight;
        var description = Copy(key + "Short", 12, featured ? "AccentInk" : "Muted");
        description.LineHeight = 18;
        description.Margin = new Thickness(0, 3, 0, 0);

        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        copy.Children.Add(title);
        copy.Children.Add(description);
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        return row;
    }

    private Grid BuildPrivacyNote()
    {
        var note = new Grid { Margin = new Thickness(1, 13, 1, 0) };
        note.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        note.ColumnDefinitions.Add(new ColumnDefinition());
        var icon = Icons.Create("info", 16, "Muted");
        icon.Margin = new Thickness(0, 2, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        note.Children.Add(icon);

        var copy = Copy("welcomePrivacy", 12, "Muted");
        copy.LineHeight = 18;
        Grid.SetColumn(copy, 1);
        note.Children.Add(copy);
        return note;
    }

    private Border BuildFooter(double inset)
    {
        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (compact)
        {
            actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            actions.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var repeat = new CheckBox
        {
            IsChecked = ShowAgain,
            Content = Copy("welcomeRepeat", 12),
            Margin = new Thickness(0, 0, compact ? 0 : 16, compact ? 10 : 0),
            MinHeight = 32,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(repeat, I18n.T("welcomeRepeat"));
        AutomationProperties.SetAutomationId(repeat, "WelcomeRepeat");
        repeat.Checked += (_, _) => ShowAgain = true;
        repeat.Unchecked += (_, _) => ShowAgain = false;
        actions.Children.Add(repeat);
        if (compact) Grid.SetColumnSpan(repeat, 2);

        string action = Settings.Current.Onboarded ? "welcomeBack" : "welcomeStart";
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        var text = Copy(action, 13, "OnAccent");
        text.FontWeight = HeadingWeight;
        label.Children.Add(text);
        var arrow = Icons.Create(Arabic ? "chevronLeft" : "chevronRight", 14, "OnAccent");
        arrow.Margin = new Thickness(8, 0, 0, 0);
        label.Children.Add(arrow);

        var start = new Button
        {
            Content = label,
            MinHeight = 42,
            MinWidth = 146,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0),
            IsDefault = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        start.SetResourceReference(BackgroundProperty, "Accent");
        start.SetResourceReference(ForegroundProperty, "OnAccent");
        start.SetResourceReference(Button.BorderBrushProperty, "Line");
        AutomationProperties.SetName(start, I18n.T(action));
        AutomationProperties.SetAutomationId(start, "WelcomeContinue");
        start.Click += (_, _) => Close();
        if (compact) Grid.SetRow(start, 1);
        Grid.SetColumn(start, 1);
        actions.Children.Add(start);

        var footer = new Border
        {
            Padding = new Thickness(inset, 12, inset, 13),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = actions
        };
        footer.SetResourceReference(Border.BackgroundProperty, "Surface");
        footer.SetResourceReference(Border.BorderBrushProperty, "Line");
        return footer;
    }
}
