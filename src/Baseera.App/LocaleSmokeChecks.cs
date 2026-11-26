using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Baseera.App.Services;

namespace Baseera.App;

internal static class LocaleSmokeChecks
{
    private static string[] FormatFields(string text) =>
        Regex.Matches(text, @"(?<!\{)\{\d+[^{}]*\}").Select(match => match.Value).Order(StringComparer.Ordinal).ToArray();

    internal static void VerifyCatalog()
    {
        var catalog = InterfaceLocales.Additional.Value;
        if (!catalog.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(I18n.Words.Keys))
            throw new InvalidDataException("The interface locale catalog does not match the source keys.");

        foreach (var (key, translations) in catalog)
        {
            string[] sourceFields = FormatFields(I18n.Words[key][1]);
            if (translations.Length != 3 || translations.Any(text => string.IsNullOrWhiteSpace(text) ||
                !FormatFields(text).SequenceEqual(sourceFields)))
                throw new InvalidDataException($"Missing translations or changed format fields for {key}.");
        }
        if (I18n.Translate("settings", "es") != "Configuración")
            throw new InvalidOperationException("Spanish no longer resolves to the existing catalog.");
        if (InterfaceLocales.ResponseLocale("zh-Hant-HK") != "zh-TW" || InterfaceLocales.ResponseLocale("zh-Hans-CN") != "zh-CN" ||
            InterfaceLocales.ResponseLocale("vi-VN") != "vi")
            throw new InvalidOperationException("Reply locales did not preserve the script or language.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static ComboBox LanguagePicker(Window window) => Descendants(window).OfType<ComboBox>()
        .Single(combo => AutomationProperties.GetAutomationId(combo) == "InterfaceLanguage");

    private static void VerifyLanguageLabels(DependencyObject visual, IEnumerable<string> expected, string description)
    {
        var actual = Descendants(visual).OfType<TextBlock>().Where(text => text.IsVisible).Select(text => text.Text).ToArray();
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException($"{description} rendered [{string.Join(", ", actual)}] instead of the language names.");
    }

    private static void VerifyGlyphs(InterfaceLocale locale, string text)
    {
        var visual = new DrawingVisual();
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo(locale.Code), FlowDirection.LeftToRight,
            new Typeface(Brand.FontFor(locale.Code), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 16, Brushes.Black, 1);
        using (var context = visual.RenderOpen()) context.DrawText(formatted, new System.Windows.Point());
        static IEnumerable<ushort> Glyphs(Drawing drawing) => drawing switch
        {
            GlyphRunDrawing textDrawing => textDrawing.GlyphRun.GlyphIndices,
            DrawingGroup group => group.Children.SelectMany(Glyphs),
            _ => []
        };
        var glyphs = Glyphs(visual.Drawing).ToArray();
        if (glyphs.Length == 0 || glyphs.Contains((ushort)0))
            throw new InvalidOperationException($"At least one glyph was missing for {locale.Code}.");
    }

    internal static async Task Run(MainWindow owner, string output)
    {
        VerifyCatalog();
        var defaults = new Preferences();
        if (defaults.Language != "en" || defaults.TargetLanguage != "English" || defaults.ReleaseChannel != "stable")
            throw new InvalidOperationException("An initial install did not default to English and the Stable channel.");
        Settings.Current.Onboarded = true;
        Settings.Current.TranslationEnabled = true;
        owner.ApplyTheme();
        owner.Navigate("home");
        async Task Settle()
        {
            await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            await Task.Delay(200);
            owner.UpdateLayout();
        }
        async Task VerifyOpenLanguagePicker(ComboBox picker, string description, string? screenshot = null)
        {
            picker.IsDropDownOpen = true;
            try
            {
                await Settle();
                var popup = (Popup)picker.Template.FindName("PART_Popup", picker);
                VerifyLanguageLabels(popup.Child, picker.Items.Cast<InterfaceLocale>().Select(locale => locale.Name), description);
                if (screenshot is not null)
                    App.Snapshot(owner, Path.Combine(output, screenshot), popup.Child as FrameworkElement);
            }
            finally { picker.IsDropDownOpen = false; }
        }
        foreach (var locale in InterfaceLocales.All)
        {
            await Settle();
            if (Descendants(owner).OfType<ComboBox>().Any(combo => AutomationProperties.GetAutomationId(combo) == "InterfaceLanguage"))
                throw new InvalidOperationException("The interface language selector is outside Appearance settings.");
            owner.OpenSettingsSection(SettingsSection.Appearance);
            await Settle();
            var picker = LanguagePicker(owner);
            if (picker.Items.Count != InterfaceLocales.All.Length)
                throw new InvalidOperationException("The interface language selector is missing options.");
            picker.SelectedValue = locale.Code;
            Settings.Load();
            if (Settings.Current.Language != locale.Code || !owner.Language.IetfLanguageTag.Equals(locale.Code, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The interface language did not switch or persist for {locale.Code}.");
            await Settle();
            picker = LanguagePicker(owner);
            VerifyLanguageLabels(picker, [locale.Name], $"The selected interface language for {locale.Code}");
            if (locale.Code == "ar")
            {
                App.Snapshot(owner, Path.Combine(output, "interface-language-ar.png"));
                await VerifyOpenLanguagePicker(picker, "The open interface language picker", "interface-languages-ar.png");
            }

            owner.Navigate("settings");
            await Settle();
            if (!Descendants(owner).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == I18n.T("appearance")))
                throw new InvalidOperationException($"Localized Settings did not render for {locale.Code}.");
            VerifyGlyphs(locale, I18n.T("appearance") + " " + I18n.T("workspaceFinder"));
            App.Snapshot(owner, Path.Combine(output, $"settings-{locale.Code}.png"));

            owner.Navigate("translate");
            await Settle();
            var target = Descendants(owner).OfType<ComboBox>().Single(combo => combo.ItemsSource == InterfaceLocales.All);
            target.SelectedValue = locale.Target;
            Settings.Load();
            if (Settings.Current.TargetLanguage != locale.Target)
                throw new InvalidOperationException($"The translation target did not persist for {locale.Code}.");
            await Settle();
            VerifyLanguageLabels(target, [locale.Name], $"The selected translation language for {locale.Code}");
            if (locale.Code == "ar")
                await VerifyOpenLanguagePicker(target, "The open translation language picker");
            owner.Navigate("home");
        }

        owner.OpenSettingsSection(SettingsSection.Appearance);
        await Settle();
        LanguagePicker(owner).SelectedValue = "zh-TW";
        var welcome = new WelcomeWindow(owner);
        try
        {
            welcome.Show();
            await Settle();
            welcome.UpdateLayout();
            if (welcome.Title != I18n.Translate("welcomeCaption", "zh-TW") ||
                Descendants(welcome).OfType<ComboBox>().Any(combo => AutomationProperties.GetName(combo) == I18n.T("language")))
                throw new InvalidOperationException("Welcome did not follow the saved settings or exposes a language selector.");
            App.Snapshot(welcome, Path.Combine(output, "welcome-zh-TW.png"));
        }
        finally { welcome.Close(); }
        File.WriteAllText(Path.Combine(output, "localization-result.txt"),
            $"PASS: {I18n.Words.Count} keys per new locale, format-field parity, distinct Chinese scripts, existing Spanish, Windows glyphs, English defaults, persisted interface and translation choices, all seven language labels in both open and closed pickers, and selector restricted to Settings.");
    }
}
