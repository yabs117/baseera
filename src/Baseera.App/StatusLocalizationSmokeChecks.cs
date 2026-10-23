using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

public sealed partial class MainWindow
{
    internal async Task CheckStatusLocalization(string output)
    {
        if (!smokeMode) throw new InvalidOperationException("Isolated UI checks only.");
        string originalLanguage = Settings.Current.Language;
        string originalPage = page, originalKey = operationStatusKey, originalDetail = operationStatusDetail, originalSelfKey = selfStatusKey;
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        void SelectLanguage(string language)
        {
            // Exercise the actual language selector, including its rerender and settings persistence.
            var selector = Descendants(frame).OfType<ComboBox>().First(c => System.Windows.Automation.AutomationProperties.GetName(c) == I18n.T("language"));
            selector.SelectedIndex = Array.IndexOf(new[] { "ar", "en", "fr", "es" }, language);
            UpdateLayout();
            if (Settings.Current.Language != language) throw new Exception("Status language selector did not change language.");
        }
        void Verify(string id, string expected)
        {
            UpdateLayout();
            var message = Descendants(frame).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == id);
            if (message.Text != expected) throw new Exception($"Stale {id} translation in {Settings.Current.Language}.");
        }
        string[] languages = ["ar", "en", "fr", "es", "ar"];
        try
        {
            Navigate("home");
            await Run(_ => Task.CompletedTask);
            foreach (string language in languages) { SelectLanguage(language); Verify("OperationStatus", I18n.T("done")); }

            var finish = new TaskCompletionSource();
            Task running = Run(_ => finish.Task);
            try
            {
                foreach (string language in languages) { SelectLanguage(language); Verify("OperationStatus", I18n.T("working")); }
            }
            finally { finish.TrySetResult(); await running; }
            Verify("OperationStatus", I18n.T("done"));
            await Run(_ => Task.FromCanceled(new CancellationToken(true)));
            foreach (string language in languages) { SelectLanguage(language); Verify("OperationStatus", I18n.T("cancel")); }
            foreach (string key in new[] { "error", "selfRestored", "selfFailed" })
            {
                SetOperationStatus(key, "diagnostic-123");
                foreach (string language in languages) { SelectLanguage(language); Verify("OperationStatus", I18n.T(key) + " diagnostic-123"); }
            }
            SetOperationStatus("done");
            Navigate("settings");
            var checkResult = new TaskCompletionSource<SelfRelease?>();
            Task checking = CheckSelfUpdate(true, _ => checkResult.Task);
            try
            {
                foreach (string language in languages) { SelectLanguage(language); Verify("SelfUpdateStatus", I18n.T("selfChecking")); }
            }
            finally { checkResult.TrySetResult(null); await checking; }
            foreach (string language in languages) { SelectLanguage(language); Verify("SelfUpdateStatus", I18n.T("selfLatest")); }
            await CheckSelfUpdate(true, _ => Task.FromException<SelfRelease?>(new HttpRequestException("Offline test")));
            foreach (string language in languages) { SelectLanguage(language); Verify("SelfUpdateStatus", I18n.T("selfCheckFailed")); }
            foreach (string key in new[] { "selfAvailable", "selfSaveDrafts" })
            {
                selfStatusKey = key; Render(true);
                foreach (string language in languages) { SelectLanguage(language); Verify("SelfUpdateStatus", I18n.T(key)); }
            }
            selfStatusKey = "selfLatest"; Render(true);
            foreach (string language in languages)
            {
                SelectLanguage(language); ApplyTheme(); Render(true);
                Verify("OperationStatus", I18n.T("done")); Verify("SelfUpdateStatus", I18n.T("selfLatest"));
                Navigate("home"); Verify("OperationStatus", I18n.T("done"));
                Navigate("settings"); Verify("SelfUpdateStatus", I18n.T("selfLatest"));
            }
            File.WriteAllText(Path.Combine(output, "status-localization-result.txt"), "PASS: actual language selector updates completed, running, canceled, error and recovery statuses; checking, latest, failed, available and unsaved-draft update statuses; all four languages, navigation and appearance rerenders; diagnostic text preserved.");
        }
        finally
        {
            Settings.Current.Language = originalLanguage; Settings.Save();
            operationStatusKey = originalKey; operationStatusDetail = originalDetail; selfStatusKey = originalSelfKey;
            ApplyTheme(); Navigate(originalPage);
        }
    }
}
