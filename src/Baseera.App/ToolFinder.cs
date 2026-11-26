using System.Globalization;
using System.Windows.Automation;
using System.Windows.Input;
using Baseera.App.Services;

namespace Baseera.App;

internal sealed record ToolChoice(string Id, string Title, string Detail);

internal sealed class ToolFinder : Window
{
    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreWidth;
    private readonly ToolChoice[] choices;
    private readonly CultureInfo culture = CultureInfo.GetCultureInfo(Settings.Current.Language);
    private readonly TextBox input = new() { MaxLength = 128 };
    private readonly ListBox results = new();
    private readonly TextBlock section = new(), count = new();
    private readonly StackPanel empty = new();
    private readonly Button clear, open;

    public string? Destination { get; private set; }

    internal ToolFinder(MainWindow owner, ToolChoice[] choices)
    {
        this.choices = choices;
        Owner = owner;
        Title = I18n.T("workspaceFinder");
        Width = Math.Clamp(Math.Min(owner.ActualWidth, SystemParameters.WorkArea.Width) - 36, 300, 608);
        Height = Math.Clamp(Math.Min(owner.ActualHeight, SystemParameters.WorkArea.Height) - 36, 340, 486);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        FontFamily = Brand.InterfaceFont;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(culture.Name);
        FontSize = 13;
        Icon = Brand.WindowIcon;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        var content = new Grid { FlowDirection = culture.Name == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, Margin = new Thickness(18, 15, 18, 12) };
        for (int row = 0; row < 5; row++)
            content.RowDefinitions.Add(new RowDefinition { Height = row == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        KeyboardNavigation.SetTabNavigation(content, KeyboardNavigationMode.Cycle);
        AddRow(content, BuildHeader(), 0);
        clear = IconButton("workspaceFinderClear", "close", "ToolFinderClear", () => { input.Clear(); input.Focus(); });
        clear.TabIndex = 1;
        AddRow(content, BuildSearchField(), 1);
        AddRow(content, BuildResultsHeader(), 2);
        AddRow(content, results, 3);
        AddRow(content, empty, 3);
        ConfigureResults();
        open = BuildOpenButton();
        AddRow(content, BuildFooter(), 4);

        var surface = new Border
        {
            CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1),
            Child = content, FlowDirection = FlowDirection.LeftToRight
        };
        surface.SetResourceReference(Border.BackgroundProperty, "Surface");
        surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        AutomationProperties.SetAutomationId(surface, "ToolFinderSurface");
        Content = surface;

        input.TextChanged += (_, _) => UpdateResults();
        results.SelectionChanged += (_, _) => open.IsEnabled = results.SelectedItem is not null;
        results.MouseLeftButtonUp += (_, args) =>
        {
            if (args.OriginalSource is DependencyObject source &&
                ItemsControl.ContainerFromElement(results, source) is ListBoxItem { IsSelected: true })
                OpenSelected();
        };
        PreviewKeyDown += HandleKeys;
        Loaded += (_, _) => input.Focus();
        UpdateResults();
    }

    private static void AddRow(Grid grid, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        grid.Children.Add(element);
    }

    private static TextBlock Label(string key, double size = 13, bool muted = false)
    {
        var label = new TextBlock { Text = I18n.T(key), FontSize = size, VerticalAlignment = VerticalAlignment.Center };
        if (muted) label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return label;
    }

    private Button IconButton(string label, string icon, string id, Action click)
    {
        var button = new Button
        {
            Style = (Style)FindResource("IconButton"), Content = Icons.Create(icon, 16, "Muted"),
            ToolTip = I18n.T(label), VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(button, I18n.T(label));
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    private DockPanel BuildHeader()
    {
        var header = new DockPanel { Margin = new Thickness(2, 0, 0, 11) };
        var close = IconButton("close", "close", "ToolFinderClose", Close);
        close.TabIndex = 4;
        AutomationProperties.SetAcceleratorKey(close, "Esc");
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var title = Label("workspaceFinder", 20);
        title.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        header.Children.Add(title);
        title.MouseLeftButtonDown += (_, args) =>
        {
            if (args.LeftButton == MouseButtonState.Pressed) DragMove();
        };
        return header;
    }

    private Border BuildSearchField()
    {
        input.Tag = I18n.T("workspaceFinderHint");
        input.Padding = new Thickness(4, 10, 4, 9);
        input.Margin = new Thickness(0);
        input.BorderThickness = new Thickness(0);
        input.Background = Brushes.Transparent;
        input.FontSize = 14;
        input.TabIndex = 0;
        AutomationProperties.SetName(input, Title);
        AutomationProperties.SetHelpText(input, I18n.T("workspaceFinderHint"));
        AutomationProperties.SetAutomationId(input, "ToolFinderQuery");

        var row = new DockPanel { Margin = new Thickness(11, 3, 5, 3) };
        var glyph = Icons.Create("search", 20, "AccentInk");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(glyph, Dock.Left);
        row.Children.Add(glyph);
        DockPanel.SetDock(clear, Dock.Right);
        row.Children.Add(clear);
        row.Children.Add(input);

        var style = new Style(typeof(Border));
        style.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Line")));
        var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent")));
        style.Triggers.Add(focus);
        var search = new Border
        {
            Style = style, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 14), Child = row
        };
        search.SetResourceReference(Border.BackgroundProperty, "Bg");
        return search;
    }

    private DockPanel BuildResultsHeader()
    {
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 5) };
        count.FontSize = section.FontSize = 12;
        count.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        section.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetAutomationId(count, "ToolFinderCount");
        AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);
        DockPanel.SetDock(count, Dock.Right);
        header.Children.Add(count);
        header.Children.Add(section);
        return header;
    }

    private void ConfigureResults()
    {
        results.Padding = new Thickness(0, 0, 4, 0);
        results.Background = Brushes.Transparent;
        results.BorderThickness = new Thickness(0);
        results.TabIndex = 2;
        results.ItemContainerStyle = (Style)FindResource("ToolFinderItem");
        var scrollbarType = typeof(System.Windows.Controls.Primitives.ScrollBar);
        var scrollbarStyle = new Style(scrollbarType, (Style)FindResource(scrollbarType));
        scrollbarStyle.Setters.Add(new Setter(BackgroundProperty, Brushes.Transparent));
        results.Resources[scrollbarType] = scrollbarStyle;
        ScrollViewer.SetHorizontalScrollBarVisibility(results, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(results, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(results, I18n.T("workspaceFinderMatches"));
        AutomationProperties.SetAutomationId(results, "ToolFinderResults");

        empty.HorizontalAlignment = HorizontalAlignment.Center;
        empty.VerticalAlignment = VerticalAlignment.Center;
        empty.Margin = new Thickness(18, 0, 18, 0);
        empty.IsHitTestVisible = false;
        var mark = Icons.Create("search", 26, "Muted");
        mark.HorizontalAlignment = HorizontalAlignment.Center;
        mark.Margin = new Thickness(0, 0, 0, 8);
        empty.Children.Add(mark);
        var heading = Label("workspaceFinderEmpty", 15);
        heading.TextAlignment = TextAlignment.Center;
        heading.FontWeight = FontWeights.SemiBold;
        empty.Children.Add(heading);
        var hint = Label("workspaceFinderEmptyHint", 12, true);
        hint.Margin = new Thickness(0, 4, 0, 0);
        hint.TextAlignment = TextAlignment.Center;
        empty.Children.Add(hint);
    }

    private Button BuildOpenButton()
    {
        var label = Label("workspaceFinderOpen");
        label.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        var caption = new StackPanel { Orientation = Orientation.Horizontal };
        caption.Children.Add(label);
        var shortcut = new TextBlock
        {
            Text = "Enter", FlowDirection = FlowDirection.LeftToRight, FontSize = 10,
            Opacity = 0.85, Margin = new Thickness(9, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        shortcut.SetResourceReference(TextBlock.ForegroundProperty, "OnAccent");
        caption.Children.Add(shortcut);
        var button = new Button { Content = caption, TabIndex = 3, MinHeight = 34, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(6, 0, 0, 0) };
        button.SetResourceReference(BackgroundProperty, "Accent");
        button.SetResourceReference(BorderBrushProperty, "Accent");
        AutomationProperties.SetAutomationId(button, "ToolFinderOpen");
        AutomationProperties.SetName(button, label.Text);
        AutomationProperties.SetAcceleratorKey(button, "Enter");
        button.Click += (_, _) => OpenSelected();
        return button;
    }

    private Border BuildFooter()
    {
        var row = new DockPanel { Margin = new Thickness(2, 10, 0, 0) };
        DockPanel.SetDock(open, Dock.Right);
        row.Children.Add(open);
        var hints = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        hints.Children.Add(KeyHint("↑ ↓", "workspaceFinderNavigate"));
        hints.Children.Add(KeyHint("Esc", "close"));
        row.Children.Add(hints);
        var footer = new Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 0), Child = row };
        footer.SetResourceReference(Border.BorderBrushProperty, "Line");
        return footer;
    }

    private static StackPanel KeyHint(string key, string meaning)
    {
        var hint = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 13, 2) };
        var cap = new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2), Margin = new Thickness(0, 0, 5, 0) };
        cap.SetResourceReference(Border.BackgroundProperty, "Subtle");
        var text = new TextBlock { Text = key, FlowDirection = FlowDirection.LeftToRight, FontSize = 10 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        cap.Child = text;
        hint.Children.Add(cap);
        hint.Children.Add(Label(meaning, 11, true));
        return hint;
    }

    private void HandleKeys(object sender, System.Windows.Input.KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            args.Handled = true;
            Close();
        }
        else if (args.Key == Key.Enter && (input.IsKeyboardFocusWithin || results.IsKeyboardFocusWithin || ReferenceEquals(args.OriginalSource, input)))
        {
            args.Handled = true;
            OpenSelected();
        }
        else if (input.IsKeyboardFocusWithin && args.Key is Key.Up or Key.Down)
        {
            args.Handled = true;
            int next = results.SelectedIndex + (args.Key == Key.Up ? -1 : 1);
            if (next >= 0 && next < results.Items.Count)
            {
                results.SelectedIndex = next;
                results.ScrollIntoView(results.SelectedItem);
            }
        }
    }

    private ListBoxItem CreateResult(ToolChoice item)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        var icon = Icons.Create(item.Id, 19, "AccentInk");
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        row.Children.Add(icon);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        labels.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        if (item.Detail.Length > 0)
        {
            var detail = new TextBlock { Text = item.Detail, FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            labels.Children.Add(detail);
        }
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        var arrow = Icons.Create(culture.Name == "ar" ? "chevronLeft" : "chevronRight", 13, "Muted");
        Grid.SetColumn(arrow, 2);
        row.Children.Add(arrow);
        var entry = new ListBoxItem { Content = row, Tag = item.Id, ToolTip = item.Detail.Length > 0 ? item.Detail : item.Title };
        AutomationProperties.SetName(entry, item.Title);
        AutomationProperties.SetHelpText(entry, item.Detail);
        return entry;
    }

    private void UpdateResults()
    {
        string query = input.Text.Trim();
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var comparer = culture.CompareInfo;
        var matches = choices.Where(item => terms.All(term => comparer.IndexOf(item.Title + " " + item.Detail + " " + item.Id, term, SearchOptions) >= 0));
        if (terms.Length > 0)
            matches = matches.OrderBy(item => comparer.Compare(item.Title, query, SearchOptions) == 0 || comparer.Compare(item.Id, query, SearchOptions) == 0 ? 0 :
                comparer.IsPrefix(item.Title, query, SearchOptions) ? 1 : 2);
        results.Items.Clear();
        foreach (var item in matches) results.Items.Add(CreateResult(item));
        results.SelectedIndex = results.Items.Count > 0 ? 0 : -1;
        if (results.SelectedItem is { } first) results.ScrollIntoView(first);
        clear.Visibility = input.Text.Length == 0 ? Visibility.Hidden : Visibility.Visible;
        section.Text = I18n.T(terms.Length == 0 ? "workspaceFinderAll" : "workspaceFinderMatches");
        count.Text = results.Items.Count.ToString(culture);
        AutomationProperties.SetName(count, section.Text + " " + count.Text);
        empty.Visibility = results.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        open.IsEnabled = results.SelectedItem is not null;
    }

    private void OpenSelected()
    {
        if (results.SelectedItem is not ListBoxItem { Tag: string id }) return;
        Destination = id;
        DialogResult = true;
    }
}
