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
                Find<TextBox>(dialog, "ErrorDescription").Text = "Edited after sending";
                if (Find<AdviceTextView>(presentation, "AdviceUserText").VisibleText != "OneDrive sync error")
                    throw new Exception("Conversation rewrote the sent message after editing the composer.");
                Find<TextBox>(dialog, "ErrorDescription").Text = "OneDrive sync error";
                var sample = Sample(language); pending.SetResult(sample); await Settle(owner);
                if (Find<TextBlock>(dialog, "AdviceProviderName").Text != "OpenAI" || Find<TextBlock>(dialog, "AdviceModelName").Text != originalModel)
                    throw new Exception("Provider identity changed after the request started.");
                settings.AiProvider = "openai";
                // Force motion for this test even on CI hosts with Windows animation disabled.
                presentation.Reveal(sample, animate: true); await Task.Delay(300);
                var greeting = Find<AdviceTextView>(presentation, "AdviceGreeting");
                if (!presentation.IsTyping || greeting.VisibleText.Length == 0 || greeting.VisibleText == AdviceLocale.Interface.Greeting ||
                    !Find<Button>(presentation, "AdviceRevealAll").IsVisible || Find<Button>(presentation, "errorCopyAdvice").IsVisible)
                    throw new Exception("Advice was not progressively revealed.");
                if (language == "ar" && presentation.FlowDirection != FlowDirection.RightToLeft) throw new Exception("Arabic answer is not RTL.");
                if (language == "en")
                {
                    greeting.Focus(); greeting.Selection.Select(greeting.Document.ContentStart, greeting.Document.ContentEnd);
                    await Task.Delay(80); string pausedText = greeting.VisibleText; await Task.Delay(160);
                    if (greeting.VisibleText != pausedText || !presentation.IsTyping) throw new Exception("Revealing text destroyed an active reader selection.");
                    greeting.Selection.Select(greeting.Document.ContentEnd, greeting.Document.ContentEnd); await Task.Delay(180);
                    if (greeting.VisibleText.Length <= pausedText.Length) throw new Exception("Reveal did not resume after selection was released.");
                }
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
                if (presentation.IsTyping || !Children(presentation).OfType<AdviceTextView>().Any(t => t.VisibleText == sample.Summary) ||
                    !Children(presentation).OfType<AdviceTextView>().Any(t => t.VisibleText == sample.Checks[0]) ||
                    !Find<Button>(presentation, "errorCopyAdvice").IsVisible || Find<Button>(presentation, "AdviceRevealAll").IsVisible)
                    throw new Exception("Show all did not finish the complete response.");
                WindowAppearance.SavePreview(dialog, Path.Combine(output, "advice-complete-" + language + ".png"));
                presentation.BringIntoView(); await Settle(owner);
                if (language is "ar" or "en") WindowAppearance.SavePreview(dialog, Path.Combine(output, "advice-conversation-" + language + ".png"));

                // A new request replaces the old timer, then closing detaches the active timer.
                presentation.Reveal(sample, animate: true); Click(dialog, "errorAnalyze"); await Settle(owner);
                if (presentation.IsTyping || ReferenceEquals(presentation, Children(dialog).OfType<AdvicePresenter>().Single()))
                    throw new Exception("Reanalysis retained the previous animation.");
                var replacement = Children(dialog).OfType<AdvicePresenter>().Single(); replacement.Reveal(sample, animate: true);
                await Task.Delay(100); dialog.Close();
                string stopped = string.Join("|", Children(replacement).OfType<AdviceTextView>().Select(t => t.VisibleText));
                await Task.Delay(120);
                if (replacement.IsTyping || stopped != string.Join("|", Children(replacement).OfType<AdviceTextView>().Select(t => t.VisibleText)))
                    throw new Exception("Closing the dialog left a typing timer alive.");
            }

            // Exercise the actual conversation/formatting controls for every bundled service.
            using var fixtureBitmap = new System.Drawing.Bitmap(240, 80);
            using (var graphics = System.Drawing.Graphics.FromImage(fixtureBitmap))
            {
                graphics.Clear(System.Drawing.Color.White);
                using var fixtureFont = new System.Drawing.Font("Arial", 14);
                graphics.DrawString("Example error 0x80070005", fixtureFont, System.Drawing.Brushes.Black, 8, 24);
            }
            using var fixturePng = new MemoryStream(); fixtureBitmap.Save(fixturePng, System.Drawing.Imaging.ImageFormat.Png);
            var attachment = ErrorAssistant.Prepare(fixturePng.ToArray(), "fixture-error.png");
            foreach (bool dark in new[] { false, true })
            {
                settings.Dark = dark; settings.Language = dark ? "ar" : "en"; owner.ApplyTheme();
                var gallery = new Window { Owner = owner, Title = "Baseera — conversation preview", Width = 780, Height = 760, ShowInTaskbar = false,
                    FontFamily = Brand.InterfaceFont, FontSize = 13, FlowDirection = FlowDirection.LeftToRight, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                gallery.SetResourceReference(Window.BackgroundProperty, "Bg"); gallery.SetResourceReference(Window.ForegroundProperty, "Ink");
                gallery.Show();
                foreach (var provider in AiProviders.All)
                {
                    string copied = ""; bool ar = settings.Language == "ar";
                    string submitted = ar ? "توقفت مزامنة OneDrive. ماذا أفحص أولًا؟" : "OneDrive stopped syncing. What should I check first?";
                    var styled = new AdvicePresenter(provider.Id, "example-vision-model", provider.Id == "openai", submitted,
                        provider.Id == "gemini" ? attachment : null, sharedDiagnostics: true, clipboardWriter: text => copied = text);
                    var canvas = new StackPanel { Margin = new Thickness(28), FlowDirection = ar ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
                    canvas.Children.Add(styled);
                    var viewport = new ScrollViewer { Content = canvas, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                    gallery.Content = viewport; await Settle(owner);
                    if (styled.IsTyping || !Find<TextBlock>(styled, "AdviceState").Text.Contains(I18n.T("adviceWaiting"))) throw new Exception("Pending response invented content before service completion.");
                    if (provider.Id == "gemini")
                    {
                        if (Find<System.Windows.Controls.Image>(styled, "AdviceUserImage").Source is null) throw new Exception("Submitted image thumbnail is missing.");
                        WindowAppearance.SavePreview(gallery, Path.Combine(output, "conversation-waiting-" + (ar ? "ar" : "en") + ".png"));
                    }
                    var styledAdvice = Sample(settings.Language) with
                    {
                        Cause = ar ? "ابدأ بمراجعة **رسالة الحالة**. احتفظ برمز `0x80070005` كما هو؛ الصورة وحدها لا تؤكد السبب." : "Start with the **status message**. Keep the code `0x80070005` unchanged; a screenshot alone cannot confirm the cause.",
                        Steps = ar ? ["افتح OneDrive واقرأ حالة المزامنة.", "إذا كان العطل متعلقًا باستعلام DNS، فهذا نص للمراجعة فقط:\n```text\nipconfig.exe /displaydns\n```\nنسخ النص لا ينفّذه."] :
                            ["Open OneDrive and read its sync status.", "If the failed operation is a DNS lookup, inspect this text first:\n```text\nipconfig.exe /displaydns\n```\nCopying this text does not run it."]
                    };
                    styled.Reveal(styledAdvice, animate: false); await Settle(owner);
                    var richViews = Children(styled).OfType<AdviceTextView>().ToArray();
                    if (richViews.Any(v => !v.IsReadOnly || v.ActualWidth <= 0 || v.ActualHeight <= 0 || v.ActualWidth > 730)) throw new Exception("Conversation text lost selection/readability or overflowed its viewport.");
                    if (!richViews.SelectMany(v => v.Document.Blocks.OfType<System.Windows.Documents.Paragraph>()).SelectMany(p => p.Inlines).OfType<System.Windows.Documents.Run>().Any(r => r.FontWeight == FontWeights.SemiBold))
                        throw new Exception("Bold response emphasis was lost.");
                    var command = richViews.Single(v => v.VisibleText == "ipconfig.exe /displaydns");
                    if (command.FlowDirection != FlowDirection.LeftToRight) throw new Exception("Code direction followed Arabic prose.");
                    command.Selection.Select(command.Document.ContentStart, command.Document.ContentEnd);
                    if (!command.Selection.Text.Contains("ipconfig.exe /displaydns")) throw new Exception("Response text cannot be selected.");
                    Click(styled, "AdviceCopyCode"); if (copied != "ipconfig.exe /displaydns") throw new Exception("Copy code changed or executed the displayed text.");
                    Click(styled, "errorCopyAdvice"); if (!copied.Contains(styledAdvice.Cause) || !copied.Contains(styledAdvice.Summary)) throw new Exception("Full-response copy omitted advice.");
                    viewport.ScrollToHome(); await Settle(owner);
                    if (provider.Id is "openai" or "anthropic" or "gemini" or "xai") WindowAppearance.SavePreview(gallery, Path.Combine(output, "conversation-" + provider.Id + (dark ? "-ar-dark" : "-en") + ".png"));
                    if (provider.Id == "anthropic")
                    {
                        command.BringIntoView(); await Settle(owner);
                        WindowAppearance.SavePreview(gallery, Path.Combine(output, "conversation-code-" + (dark ? "ar" : "en") + ".png"));
                    }
                    styled.Dispose();
                }
                gallery.Close();
            }

            settings.Language = "en"; settings.Dark = false; owner.ApplyTheme(); owner.Render();
            var host = new Window { Owner = owner, Width = 620, Height = 500, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var unicode = new AdvicePresenter("openai", "vision-model", true); host.Content = unicode; host.Show();
            var accents = new ErrorAdvice("مُشكلة e\u0301 👨‍👩‍👧‍👦 — Unicode", "Cause", ["One safe step."], [], "Moderate");
            unicode.Reveal(accents, animate: true);
            for (int i = 0; i < 8; i++)
            {
                await Task.Delay(45);
                var value = Find<AdviceTextView>(unicode, "AdviceSummary").VisibleText;
                if (value.Length != accents.Summary.Length && !StringInfo.ParseCombiningCharacters(accents.Summary).Contains(value.Length))
                    throw new Exception("Typing split a Unicode text element.");
            }
            // Respect reduced motion: all text is immediately available and no animation is scheduled.
            unicode.Reveal(accents, animate: false);
            if (unicode.IsTyping || !Children(unicode).OfType<AdviceTextView>().Any(t => t.VisibleText == accents.Summary)) throw new Exception("Reduced motion failed.");
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
                "PASS: 20 bundled provider logos and rendered conversation layouts in both themes; immutable submitted-message/image context; request-scoped provider/model identity; inert bold/inline/fenced-code formatting; selectable read-only text and selection-aware reveal pause/resume; exact code and full-response copy through a fake clipboard; LTR code inside RTL replies; real waiting state; progressive four-language responses; Unicode text elements; show all; natural completion; reduced motion; reanalysis/cancel/close/late-response cleanup. No live AI requests, clipboard changes or command execution.");
        }
        finally
        {
            settings.Language = languageBefore; settings.AiProvider = providerBefore; settings.OpenAiAuth = authBefore; settings.Dark = darkBefore;
            settings.AiModels = modelsBefore;
            owner.ApplyTheme(); owner.Render();
        }
    }
}
