using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using Baseera.App.Services;

namespace Baseera.App;

internal enum SettingsSection { Appearance, Ai, Tools, Diagnostics, Windows, Updates }

public sealed partial class MainWindow
{
    private static readonly (SettingsSection Section, string Title, string Hint, string Icon)[] SettingsCategories =
    [
        (SettingsSection.Appearance, "appearance", "settingsAppearanceHint", "sun"),
        (SettingsSection.Ai, "aiSettingsTitle", "settingsAiHint", "cpu"),
        (SettingsSection.Tools, "yourTools", "settingsToolsHint", "toolbox"),
        (SettingsSection.Diagnostics, "hubCenters", "settingsDiagnosticsHint", "health"),
        (SettingsSection.Windows, "windowsIntegration", "settingsWindowsHint", "application"),
        (SettingsSection.Updates, "selfTitle", "settingsUpdatesHint", "updates")
    ];

    private SettingsSection? settingsSection;
    private TextBlock? settingsHeading;
    private readonly Dictionary<SettingsSection, Button> settingsCategoryButtons = [];
    private readonly AiConnectionDraft aiConnectionDraft = new();
    private bool? startupDraft, startupScanDraft, explorerDraft;

    internal void OpenSettingsSection(SettingsSection section, bool focusTranslation = false)
    {
        Navigate("settings", section, focusTranslation);
        if (!focusTranslation && settingsHeading is { } heading) QueueSettingsFocus(heading);
    }

    private void ReturnToSettings(SettingsSection section)
    {
        Navigate("settings");
        if (settingsCategoryButtons.TryGetValue(section, out var category)) QueueSettingsFocus(category);
    }

    private void QueueSettingsFocus(FrameworkElement target) =>
        _ = Dispatcher.InvokeAsync(() => FocusSettingsTarget(target), DispatcherPriority.Loaded);

    private void FocusSettingsTarget(FrameworkElement target)
    {
        if (!target.IsVisible || !target.IsEnabled || !scroller.IsAncestorOf(target)) return;
        target.BringIntoView();
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(target), target);
        if (IsActive) Keyboard.Focus(target);
    }

    private void SettingsPage(StackPanel body)
    {
        if (settingsSection is not { } section)
        {
            var heading = Heading(body, "settings", "settingsDesc");
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1);
            var list = Stack();
            AutomationProperties.SetAutomationId(list, "SettingsIndex");
            foreach (var category in SettingsCategories)
            {
                if (list.Children.Count > 0) Divider(list);
                var button = SettingsCategoryButton(category.Section, category.Title, category.Hint, category.Icon);
                settingsCategoryButtons.Add(category.Section, button);
                list.Children.Add(button);
            }
            body.Children.Add(Card(list, 0));
            return;
        }

        var breadcrumb = new StackPanel { Orientation = Orientation.Horizontal };
        breadcrumb.Children.Add(Icons.Create(Arabic ? "chevronRight" : "chevronLeft", 14, "AccentInk"));
        breadcrumb.Children.Add(new TextBlock { Text = I18n.T("allSettings"), Margin = new Thickness(7, 0, 0, 0) });
        var back = Action("allSettings", () => ReturnToSettings(section));
        back.Content = breadcrumb;
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Margin = new Thickness(0, 0, 0, 15);
        AutomationProperties.SetAutomationId(back, "SettingsBack");
        body.Children.Add(back);

        var selected = SettingsCategories.Single(category => category.Section == section);
        settingsHeading = Heading(body, selected.Title);
        settingsHeading.Focusable = true;
        KeyboardNavigation.SetIsTabStop(settingsHeading, false);
        AutomationProperties.SetHeadingLevel(settingsHeading, AutomationHeadingLevel.Level1);
        AutomationProperties.SetAutomationId(settingsHeading, "SettingsDetailHeading");
        switch (section)
        {
            case SettingsSection.Appearance: AppearanceSettings(body); break;
            case SettingsSection.Ai: AiSettingsSection(body); break;
            case SettingsSection.Tools: FeatureChoices(body); break;
            case SettingsSection.Diagnostics: DiagnosticsSettings(body); break;
            case SettingsSection.Windows: WindowsSettings(body); break;
            case SettingsSection.Updates: SelfUpdateSettings(body); break;
        }
    }

    private Button SettingsCategoryButton(SettingsSection section, string title, string hint, string icon)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(53) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.Children.Add(new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(8), Background = Resource("Tint"),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = Icons.Create(icon, 19, "AccentInk")
        });

        var labels = Stack();
        labels.VerticalAlignment = VerticalAlignment.Center;
        labels.Margin = new Thickness(0, 0, 9, 0);
        var name = Text(I18n.T(title), 15);
        name.FontWeight = HeadingWeight;
        name.Margin = new Thickness(0, 0, 0, 3);
        var summary = Text(I18n.T(hint), 12, true);
        summary.Margin = new Thickness(0);
        labels.Children.Add(name);
        labels.Children.Add(summary);
        Grid.SetColumn(labels, 1);
        row.Children.Add(labels);
        var chevron = Icons.Create(Arabic ? "chevronLeft" : "chevronRight", 15, "Muted");
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);

        var button = new Button
        {
            Style = (Style)FindResource("ToolRowButton"), Content = row,
            MinHeight = 69, Padding = new Thickness(15, 11, 13, 11)
        };
        AutomationProperties.SetAutomationId(button, $"SettingsCategory-{section}");
        AutomationProperties.SetName(button, name.Text);
        AutomationProperties.SetHelpText(button, summary.Text);
        button.Click += (_, _) => OpenSettingsSection(section);
        return button;
    }

    private void AppearanceSettings(StackPanel body)
    {
        var options = Stack();
        var language = new ComboBox
        {
            ItemsSource = InterfaceLocales.All.OrderByDescending(locale => locale.Code == InterfaceLocales.DefaultCode),
            DisplayMemberPath = nameof(InterfaceLocale.Name), SelectedValuePath = nameof(InterfaceLocale.Code),
            SelectedValue = Settings.Current.Language, FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 134, Margin = new Thickness(0)
        };
        AutomationProperties.SetName(language, I18n.T("language"));
        AutomationProperties.SetAutomationId(language, "InterfaceLanguage");
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedValue is not string code) return;
            Settings.Current.Language = code;
            Settings.Save();
            ApplyTheme();
            Render(true);
        };
        options.Children.Add(SettingRow("language", language));
        Divider(options);
        var dark = Toggle("dark", Settings.Current.Dark, value =>
        {
            Settings.Current.Dark = value;
            Settings.Save();
            ApplyTheme();
            Render(true);
        });
        dark.Content = null;
        options.Children.Add(SettingRow("dark", dark));
        Divider(options);
        options.Children.Add(ColorPalettePicker());
        Divider(options);
        var welcomeStartup = Toggle("settingsWelcomeStartup", Settings.Current.ShowWelcomeOnStartup, value =>
        {
            Settings.Current.ShowWelcomeOnStartup = value;
            Settings.Save();
        });
        welcomeStartup.Content = null;
        options.Children.Add(SettingRow("settingsWelcomeStartup", welcomeStartup));
        Divider(options);
        var welcome = Action("welcomeReopen", ShowWelcome);
        welcome.Margin = new Thickness(0);
        options.Children.Add(SettingRow("welcomeGuide", welcome));
        body.Children.Add(Card(options));
    }

    private void AiSettingsSection(StackPanel body)
    {
        var provider = Stack();
        provider.Children.Add(Text(I18n.T("aiSettingsHelp"), 12, true));
        var connection = new AiConnectionPanel(aiConnectionDraft, () => SetOperationStatus("done"), probe: ConnectionProbeOverride, openAssistant: ShowErrorAssistant);
        provider.Children.Add(connection);
        translationKeyInput = connection.MissingInput;
        translationModelInput = connection.ModelInput;
        translationProviderSection = Card(provider);
        AutomationProperties.SetAutomationId(translationProviderSection, "TranslationProviderSection");
        if (emphasizeTranslationSettings) translationProviderSection.BorderBrush = Resource("Accent");
        body.Children.Add(translationProviderSection);
    }

    private void DiagnosticsSettings(StackPanel body)
    {
        var centers = Stack();
        foreach (string center in InvestigationCenters.Except(Tools))
            centers.Children.Add(Toggle(center, IsEnabledFeature(center), enabled => SetFeature(center, enabled)));
        centers.Children.Add(Text(I18n.T("hubLocalFirst"), 12, true));
        body.Children.Add(Card(centers));

        SectionHeading(body, "workspaceReadings");
        var monitoring = Stack();
        monitoring.Children.Add(Toggle("deviceAutoRefresh", Settings.Current.AutoRefreshDevice, value =>
        {
            Settings.Current.AutoRefreshDevice = value;
            Settings.Save();
        }));
        monitoring.Children.Add(Text(I18n.T("deviceLocalOnly"), 12, true));
        body.Children.Add(Card(monitoring));
    }

    private void WindowsSettings(StackPanel body)
    {
        var options = Stack();
        options.Children.Add(Toggle("startup", startupDraft ?? Settings.Current.StartWithWindows, value => startupDraft = value));
        options.Children.Add(Toggle("startupScan", startupScanDraft ?? Settings.Current.ScanAtStartup, value => startupScanDraft = value));
        if (Settings.Current.ScanPath.Length > 0) options.Children.Add(Text(Settings.Current.ScanPath, 12, true));
        options.Children.Add(Toggle("explorer", explorerDraft ?? Settings.Current.ExplorerMenu, value => explorerDraft = value));
        Help(options, "explorerNote");
        var apply = AsyncAction("save", _ =>
        {
            Settings.Current.StartWithWindows = startupDraft ?? Settings.Current.StartWithWindows;
            Settings.Current.ScanAtStartup = startupScanDraft ?? Settings.Current.ScanAtStartup;
            Settings.Current.ExplorerMenu = explorerDraft ?? Settings.Current.ExplorerMenu;
            Settings.Save();
            Settings.ApplyWindowsIntegration();
            ApplyExplorerShortcut();
            startupDraft = startupScanDraft = explorerDraft = null;
            Render(true);
            return Task.CompletedTask;
        }, true);
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        options.Children.Add(apply);
        body.Children.Add(Card(options));
    }
}
