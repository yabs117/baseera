using Baseera.App.Services;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Documents;
using System.Windows.Threading;

namespace Baseera.App;

internal static class WelcomeSmokeChecks
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    private static T Control<T>(WelcomeWindow dialog, string id) where T : FrameworkElement =>
        Descendants(dialog).OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);

    internal static async Task<int> RenderPreviews(MainWindow owner, string output)
    {
        var previous = (Settings.Current.Language, Settings.Current.Dark, Settings.Current.Onboarded);
        int renders = 0;

        async Task Preview(string language, bool dark, bool compact)
        {
            Settings.Current.Language = language;
            Settings.Current.Dark = dark;
            Settings.Current.Onboarded = compact;
            owner.ApplyTheme();
            owner.Navigate("home");
            Uri? openedPolicy = null;
            var dialog = new WelcomeWindow(owner, uri => openedPolicy = uri);
            if (compact)
            {
                dialog.Width = Math.Min(dialog.Width, 530);
                dialog.MaxHeight = Math.Min(dialog.MaxHeight, 490);
            }
            try
            {
                dialog.Show();
                await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                dialog.UpdateLayout();
                var title = Control<TextBlock>(dialog, "WelcomeTitle");
                var next = Control<Button>(dialog, "WelcomeContinue");
                var scroll = Descendants(dialog).OfType<ScrollViewer>().Single();
                string action = I18n.T(compact ? "welcomeBack" : "welcomeStart");
                if (title.Text != I18n.T("welcomeTitle") ||
                    AutomationProperties.GetHeadingLevel(title) != AutomationHeadingLevel.Level1 ||
                    title.FlowDirection != (language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight) ||
                    !next.IsVisible || !next.IsDefault || AutomationProperties.GetName(next) != action ||
                    !Control<CheckBox>(dialog, "WelcomeRepeat").IsVisible ||
                    scroll.ScrollableWidth != 0)
                    throw new InvalidOperationException($"Welcome content did not fit or retain its accessible layout for {language}.");
                if (compact && (scroll.ScrollableHeight <= 0 || Descendants(scroll).Contains(next)))
                    throw new InvalidOperationException("Compact welcome did not keep Continue outside the scrolling content.");

                const string privacyUrl = "https://github.com/yabs117/baseera/blob/main/PRIVACY.md";
                var policy = Control<TextBlock>(dialog, "WelcomePrivacyFooter");
                var link = policy.Inlines.OfType<Hyperlink>().Single();
                string footerLabel = string.Concat(policy.Inlines.OfType<Run>().Select(run => run.Text));
                string linkLabel = string.Concat(link.Inlines.OfType<Run>().Select(run => run.Text));
                var root = (UIElement)dialog.Content;
                double actionBottom = next.TranslatePoint(new System.Windows.Point(0, next.ActualHeight), root).Y;
                double privacyTop = policy.TranslatePoint(new System.Windows.Point(), root).Y;
                if (!policy.IsVisible || policy.ActualHeight == 0 || privacyTop < actionBottom || Descendants(scroll).Contains(policy) ||
                    !footerLabel.Contains(I18n.T("welcomePrivacyPolicy"), StringComparison.Ordinal) ||
                    linkLabel != I18n.T("welcomePrivacyLink") ||
                    !link.Focusable || link.NavigateUri.AbsoluteUri != privacyUrl || openedPolicy is not null)
                    throw new InvalidOperationException($"Welcome privacy footer failed for {language}: visible={policy.IsVisible}, " +
                        $"top={privacyTop}, action bottom={actionBottom}, focusable={link.Focusable}, label='{footerLabel}', link='{linkLabel}', URL={link.NavigateUri}.");
                if (language == "en" && dark)
                {
                    var peer = new HyperlinkAutomationPeer(link);
                    if (peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
                        throw new InvalidOperationException("The welcome privacy link does not support accessible activation.");
                    invoke.Invoke();
                    await dialog.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    if (openedPolicy?.AbsoluteUri != privacyUrl || !dialog.IsVisible)
                        throw new InvalidOperationException("Activating the privacy link did not open the policy or dismissed Welcome.");
                }

                string suffix = language + (dark ? "-dark" : "") + (compact ? "-compact" : "");
                App.Snapshot(dialog, Path.Combine(output, $"welcome-{suffix}.png"));
                renders++;
                if ((language == "en" && !dark) || (language == "ar" && dark && !compact))
                    WindowAppearance.SavePreview(dialog, Path.Combine(output, $"welcome-window-{suffix}.png"));
                if (language == "ar" && dark && !compact)
                {
                    var theme = Control<Button>(dialog, "WelcomeTheme");
                    theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (Settings.Current.Dark || AutomationProperties.GetName(theme) != I18n.T("welcomeThemeDark"))
                        throw new InvalidOperationException("The welcome theme button did not switch to the light theme.");
                    theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (!Settings.Current.Dark || AutomationProperties.GetName(theme) != I18n.T("welcomeThemeLight"))
                        throw new InvalidOperationException("The welcome theme button did not switch back to the dark theme.");
                }
            }
            finally { dialog.Close(); }
        }

        try
        {
            foreach (var locale in InterfaceLocales.All)
            foreach (bool dark in new[] { false, true })
                await Preview(locale.Code, dark, compact: false);
            await Preview("ar", dark: true, compact: true);
        }
        finally
        {
            (Settings.Current.Language, Settings.Current.Dark, Settings.Current.Onboarded) = previous;
            owner.ApplyTheme();
            owner.Render(true);
        }
        return renders;
    }

    internal static async Task Run(MainWindow owner)
    {
        var upgraded = JsonSerializer.Deserialize<Preferences>("{\"Onboarded\":true}")!;
        if (!WelcomeWindow.ShouldShow(upgraded)) throw new Exception("Existing profiles do not receive the new welcome.");
        bool onboarded = Settings.Current.Onboarded;
        var features = (Settings.Current.ScanEnabled, Settings.Current.TranslationEnabled, Settings.Current.UpdatesEnabled, Settings.Current.ConvertEnabled);
        Settings.Current.Onboarded = false; Settings.Current.WelcomeSeen = false; Settings.Current.ShowWelcomeOnStartup = false;
        Settings.Current.ScanEnabled = Settings.Current.TranslationEnabled = Settings.Current.UpdatesEnabled = Settings.Current.ConvertEnabled = false;
        Settings.Save(); Settings.Load();

        async Task Dismiss(bool startup, bool repeat, bool continueButton)
        {
            Exception? failure = null; bool observed = false;
            var interaction = owner.Dispatcher.InvokeAsync(() =>
            {
                WelcomeWindow? dialog = null;
                try
                {
                    dialog = Application.Current.Windows.OfType<WelcomeWindow>().Single(); observed = true;
                    if (WindowAppearance.IsInputEnabled(owner)) throw new Exception("Welcome does not block interaction with its owner.");
                    dialog.UpdateLayout();
                    Descendants(dialog).OfType<CheckBox>().Single().IsChecked = repeat;
                    if (continueButton)
                        Descendants(dialog).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("welcomeStart")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else dialog.Close();
                }
                catch (Exception ex) { failure = ex; dialog?.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            if (startup) owner.ShowStartupWelcome(); else owner.ShowWelcome();
            await interaction;
            if (failure is not null) throw failure;
            if (!observed || !WindowAppearance.IsInputEnabled(owner)) throw new Exception("Welcome did not dismiss cleanly.");
        }

        await Dismiss(startup: true, repeat: false, continueButton: true);
        Settings.Load();
        if (!Settings.Current.WelcomeSeen || WelcomeWindow.ShouldShow(Settings.Current) || owner.ShowStartupWelcome()) throw new Exception("Welcome repeats after completion and restart.");
        if (Settings.Current.Onboarded || Settings.Current.ScanEnabled || Settings.Current.TranslationEnabled || Settings.Current.UpdatesEnabled || Settings.Current.ConvertEnabled)
            throw new Exception("Welcome changed feature choices or skipped tool selection.");
        await Dismiss(startup: false, repeat: true, continueButton: true);
        Settings.Load(); if (!WelcomeWindow.ShouldShow(Settings.Current)) throw new Exception("Repeat-on-startup choice was not saved.");
        await Dismiss(startup: true, repeat: false, continueButton: false);
        Settings.Load(); if (WelcomeWindow.ShouldShow(Settings.Current)) throw new Exception("Closing welcome did not save its preference.");
        Settings.Current.Onboarded = onboarded;
        (Settings.Current.ScanEnabled, Settings.Current.TranslationEnabled, Settings.Current.UpdatesEnabled, Settings.Current.ConvertEnabled) = features;
        Settings.Save();
    }
}
