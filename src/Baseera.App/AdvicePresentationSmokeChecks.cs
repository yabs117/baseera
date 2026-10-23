using System.Globalization;
using System.Windows.Automation;
using System.Windows.Threading;
using Baseera.App.Services;

namespace Baseera.App;

internal static class AdvicePresentationSmokeChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Children(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Settle(Window owner) { await owner.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(100); }
    private static ErrorAdvice Sample(string language) => language switch
    {
        "ar" => new("تعذّر إكمال المزامنة في OneDrive", "قد يكون الاتصال متوقفًا أو يحتاج الحساب إلى تسجيل الدخول من جديد. الصورة وحدها لا تكفي لتحديد السبب بدقة.",
            ["افتح OneDrive من شريط المهام واقرأ رسالة الحالة الظاهرة.", "تحقق من اتصال الإنترنت ومساحة التخزين المتاحة.", "سجّل نص الخطأ قبل تجربة الإصلاح من إعدادات التطبيق الرسمية."],
            ["هل تظهر المشكلة في ملف واحد أم في جميع الملفات؟", "هل توجد رسالة تطلب تسجيل الدخول؟"], "متوسطة"),
        "fr" => new("La synchronisation OneDrive est interrompue", "La connexion peut être interrompue ou le compte peut nécessiter une nouvelle connexion. Une capture ne permet pas de confirmer la cause.",
            ["Ouvrez OneDrive dans la barre des tâches et lisez son état.", "Vérifiez votre connexion et l’espace de stockage disponible.", "Notez le message exact avant d’utiliser la réparation officielle."],
            ["Le problème concerne-t-il un fichier ou tous les fichiers ?"], "Modérée"),
        "es" => new("La sincronización de OneDrive se ha detenido", "Puede faltar conexión o ser necesario iniciar sesión de nuevo. Una captura no basta para confirmar la causa.",
            ["Abre OneDrive desde la barra de tareas y consulta su estado.", "Comprueba la conexión y el espacio de almacenamiento disponible.", "Anota el error antes de usar la reparación oficial de la aplicación."],
            ["¿Afecta a un archivo o a todos los archivos?"], "Moderada"),
        _ => new("OneDrive sync needs your attention", "The connection may be interrupted, or the account may need you to sign in again. A screenshot alone cannot confirm the cause.",
            ["Open OneDrive from the taskbar and read its current status.", "Check your internet connection and available storage.", "Record the exact error before trying the app’s official repair options."],
            ["Does this affect one file or every file?", "Is there a request to sign in?"], "Moderate")
    };

    internal static async Task Run(MainWindow owner, string output)
    {
        var settings = Settings.Current;
        string languageBefore = settings.Language, providerBefore = settings.AiProvider, authBefore = settings.OpenAiAuth;
        var modelsBefore = new Dictionary<string, string>(settings.AiModels);
        bool darkBefore = settings.Dark;
        try
        {
            settings.AiProvider = "openai"; settings.OpenAiAuth = "api";
            Settings.SetModel("openai", "gpt-4.1");
            foreach (bool dark in new[] { false, true })
            {
                settings.Dark = dark; owner.ApplyTheme();
                foreach (var provider in AiProviders.All)
                {
                    var mark = AiProviderBadge.Mark(provider.Id);
                    if (mark.Source is not DrawingImage { Drawing: DrawingGroup group } || group.Children.Count < 2 ||
                        group.Bounds.Width < 1 || group.Bounds.Height < 1) throw new Exception("Provider mark missing: " + provider.Id);
                    if (group.Children.OfType<GeometryDrawing>().Skip(1).Any(d => d.Geometry.Bounds.IsEmpty)) throw new Exception("Empty provider path.");
                }
            }
            foreach (string language in new[] { "ar", "en", "fr", "es" })
            {
                settings.Language = language; settings.Dark = language == "ar"; owner.ApplyTheme(); owner.Render();
                var pending = new TaskCompletionSource<ErrorAdvice>(TaskCreationOptions.RunContinuationsAsynchronously);
                var dialog = new ErrorAssistantWindow(owner, (_, _, _, _) => pending.Task);
                dialog.Show(); Find<TextBox>(dialog, "ErrorDescription").Text = "OneDrive sync error";
                Click(dialog, "errorAnalyze"); await Settle(owner);
                var presentation = Children(dialog).OfType<AdvicePresenter>().Single();
                if (Find<TextBlock>(dialog, "AdviceProviderName").Text != "OpenAI" ||
                    Find<TextBlock>(dialog, "AdviceModelName").Text != AiCatalog.Model ||
                    Find<System.Windows.Controls.Image>(dialog, "AdviceProviderLogo").Source is null)
                    throw new Exception("Response provider identity is missing.");
                string originalModel = AiCatalog.Model; settings.AiProvider = "anthropic";
                var sample = Sample(language); pending.SetResult(sample); await Settle(owner);
                if (Find<TextBlock>(dialog, "AdviceProviderName").Text != "OpenAI" || Find<TextBlock>(dialog, "AdviceModelName").Text != originalModel)
                    throw new Exception("Provider identity changed after the request started.");
                settings.AiProvider = "openai";
                // Force motion for this test even on CI hosts with Windows animation disabled.
                presentation.Reveal(sample, animate: true); await Task.Delay(300);
                var greeting = Children(presentation).OfType<TextBlock>().First(t => AdviceLocale.Interface.Greeting.StartsWith(t.Text, StringComparison.Ordinal) && t.FontSize == 14);
                if (!presentation.IsTyping || greeting.Text.Length == 0 || greeting.Text == AdviceLocale.Interface.Greeting ||
                    !Find<Button>(presentation, "AdviceRevealAll").IsVisible || Find<Button>(presentation, "errorCopyAdvice").IsVisible)
                    throw new Exception("Advice was not progressively revealed.");
                if (language == "ar" && presentation.FlowDirection != FlowDirection.RightToLeft) throw new Exception("Arabic answer is not RTL.");
                if (language is "ar" or "en")
                {
                    await Task.Delay(1100); dialog.UpdateLayout();
                    WindowAppearance.SavePreview(dialog, Path.Combine(output, "advice-writing-" + language + ".png"));
                }
                if (language == "en")
                {
                    var scroll = Find<ScrollViewer>(dialog, "ErrorAssistantScroll");
                    double earlier = Math.Max(0, scroll.VerticalOffset - 130); scroll.ScrollToVerticalOffset(earlier); await Settle(owner);
                    double paused = scroll.VerticalOffset; await Task.Delay(550);
                    if (Math.Abs(scroll.VerticalOffset - paused) > 2) throw new Exception("Typing pulled the reader away after scrolling up.");
                    scroll.ScrollToEnd(); await Settle(owner); await Task.Delay(250);
                    if (scroll.ScrollableHeight - scroll.VerticalOffset > 44) throw new Exception("Answer following did not resume at the bottom.");
                }
                Click(presentation, "AdviceRevealAll"); await Settle(owner);
                if (presentation.IsTyping || !Children(presentation).OfType<TextBlock>().Any(t => t.Text == sample.Summary) ||
                    !Children(presentation).OfType<TextBlock>().Any(t => t.Text == sample.Checks[0]) ||
                    !Find<Button>(presentation, "errorCopyAdvice").IsVisible || Find<Button>(presentation, "AdviceRevealAll").IsVisible)
                    throw new Exception("Show all did not finish the complete response.");
                WindowAppearance.SavePreview(dialog, Path.Combine(output, "advice-complete-" + language + ".png"));

                // A new request replaces the old timer, then closing detaches the active timer.
                presentation.Reveal(sample, animate: true); Click(dialog, "errorAnalyze"); await Settle(owner);
                if (presentation.IsTyping || ReferenceEquals(presentation, Children(dialog).OfType<AdvicePresenter>().Single()))
                    throw new Exception("Reanalysis retained the previous animation.");
                var replacement = Children(dialog).OfType<AdvicePresenter>().Single(); replacement.Reveal(sample, animate: true);
                await Task.Delay(100); dialog.Close();
                string stopped = string.Join("|", Children(replacement).OfType<TextBlock>().Select(t => t.Text));
                await Task.Delay(120);
                if (replacement.IsTyping || stopped != string.Join("|", Children(replacement).OfType<TextBlock>().Select(t => t.Text)))
                    throw new Exception("Closing the dialog left a typing timer alive.");
            }

            settings.Language = "en"; settings.Dark = false; owner.ApplyTheme(); owner.Render();
            var host = new Window { Owner = owner, Width = 620, Height = 500, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var unicode = new AdvicePresenter("openai", "vision-model", true); host.Content = unicode; host.Show();
            var accents = new ErrorAdvice("مُشكلة e\u0301 👨‍👩‍👧‍👦 — Unicode", "Cause", ["One safe step."], [], "Moderate");
            unicode.Reveal(accents, animate: true);
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(45);
                var value = Children(unicode).OfType<TextBlock>().Single(t => t.FontSize == 18).Text;
                if (value.Length != accents.Summary.Length && !StringInfo.ParseCombiningCharacters(accents.Summary).Contains(value.Length))
                    throw new Exception("Typing split a Unicode text element.");
            }
            // Respect reduced motion: all text is immediately available and no animation is scheduled.
            unicode.Reveal(accents, animate: false);
            if (unicode.IsTyping || !Children(unicode).OfType<TextBlock>().Any(t => t.Text == accents.Summary)) throw new Exception("Reduced motion failed.");
            unicode.Reveal(new("A", "B", ["C"], [], "D"), animate: true);
            for (int i = 0; i < 50 && unicode.IsTyping; i++) await Task.Delay(60);
            if (unicode.IsTyping || !Find<Button>(unicode, "errorCopyAdvice").IsVisible) throw new Exception("Typing did not naturally complete.");
            host.Close();

            var late = new TaskCompletionSource<ErrorAdvice>(TaskCreationOptions.RunContinuationsAsynchronously);
            var closing = new ErrorAssistantWindow(owner, (_, _, _, _) => late.Task);
            closing.Show(); Find<TextBox>(closing, "ErrorDescription").Text = "Error"; Click(closing, "errorAnalyze");
            var canceled = Children(closing).OfType<AdvicePresenter>().Single(); closing.Close(); late.SetResult(Sample("en")); await Settle(owner);
            if (canceled.IsTyping) throw new Exception("A late response animated a closed window.");

            var blocked = new ErrorAssistantWindow(owner, async (_, _, _, ct) => { await Task.Delay(Timeout.Infinite, ct); return Sample("en"); });
            blocked.Show(); Find<TextBox>(blocked, "ErrorDescription").Text = "Error"; Click(blocked, "errorAnalyze"); Click(blocked, "cancel"); await Settle(owner);
            if (Children(blocked).OfType<AdvicePresenter>().Any()) throw new Exception("Canceled request retained a response panel.");
            blocked.Close();
            File.WriteAllText(Path.Combine(output, "advice-presentation-result.txt"),
                "PASS: 20 bundled provider logos in both themes; request-scoped provider/model identity; progressive four-language responses and RTL; Unicode text elements; show all; complete sections; natural completion; reduced motion; reanalysis, cancellation, close and late response cleanup. No live AI requests.");
        }
        finally
        {
            settings.Language = languageBefore; settings.AiProvider = providerBefore; settings.OpenAiAuth = authBefore; settings.Dark = darkBefore;
            settings.AiModels = modelsBefore;
            owner.ApplyTheme(); owner.Render();
        }
    }
}
