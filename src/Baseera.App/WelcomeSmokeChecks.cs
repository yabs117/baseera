using Baseera.App.Services;
using System.Text.Json;
using System.Windows.Threading;

namespace Baseera.App;

internal static class WelcomeSmokeChecks
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
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
