using System.Windows.Media.Imaging;
using Baseera.App.Services;

namespace Baseera.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--apply-update")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int result;
            try { result = await Task.Run(() => SelfUpdater.Apply(e.Args[1])); }
            catch { result = 1; }
            Shutdown(result); return;
        }
        Settings.Load();
        try { await ChatGptAuth.PruneSignedOutProfiles(CancellationToken.None); }
        catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
        { /* Connection settings report an unreadable credential store; startup remains available. */ }
        bool smoke = e.Args.Contains("--smoke");
        string? input = e.Args.Length > 1 && e.Args[0] == "--convert" ? e.Args[1] : null;
        var window = new MainWindow(input, smoke, e.Args.Length == 0); MainWindow = window;
        if (!smoke && e.Args.Length == 2 && e.Args[0] == "--update-result") window.UpdateReceipt = e.Args[1];
        if (smoke) window.Loaded += async (_, _) =>
        {
            try
            {
                string output = e.Args.SkipWhile(x => x != "--smoke").Skip(1).FirstOrDefault() ?? "artifacts";
                Directory.CreateDirectory(output);
                if (e.Args.Contains("--conversation-only"))
                {
                    Settings.Current.Onboarded = true; window.ApplyTheme(); window.Render();
                    await ConversationSmokeChecks.Run(window, output); Shutdown(0); return;
                }
                if (e.Args.Contains("--advice-only"))
                {
                    Settings.Current.Onboarded = true; window.ApplyTheme(); window.Render();
                    await AdvicePresentationSmokeChecks.Run(window, output); Shutdown(0); return;
                }
                if (e.Args.Contains("--smart-repair-only"))
                {
                    Settings.Current.Onboarded = true; window.ApplyTheme(); window.Render();
                    await SmartRepairSmokeChecks.Run(window, output); Shutdown(0); return;
                }
                int renders = 0;
                void SaveView(string name) { Snapshot(window, Path.Combine(output, name + ".png")); renders++; }
                Settings.Current.Onboarded = false; Settings.Current.ShowWelcomeOnStartup = false;
                foreach (string language in new[] { "ar", "en", "fr", "es" })
                foreach (bool dark in new[] { false, true })
                {
                    Settings.Current.Language = language; Settings.Current.Dark = dark; window.ApplyTheme(); window.Navigate("home");
                    var welcome = new WelcomeWindow(window); welcome.Show();
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    await Task.Delay(220);
                    Snapshot(welcome, Path.Combine(output, "welcome-" + language + (dark ? "-dark" : "") + ".png")); renders++;
                    if ((language == "en" && !dark) || (language == "ar" && dark))
                        WindowAppearance.SavePreview(welcome, Path.Combine(output, "welcome-window-" + language + (dark ? "-dark" : "") + ".png"));
                    welcome.Close();
                }
                Settings.Current.Dark = false;
                foreach (string language in new[] { "ar", "en", "fr", "es" })
                {
                    Settings.Current.Language = language; window.ApplyTheme();
                    Settings.Current.Onboarded = false; window.Navigate("home");
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    await Task.Delay(220);
                    SaveView("setup-" + language);
                }
                Settings.Current.Onboarded = true; Settings.Current.ScanEnabled = true; Settings.Current.TranslationEnabled = true;
                Settings.Current.UpdatesEnabled = true; Settings.Current.ConvertEnabled = true; Settings.Current.ErrorAssistantEnabled = true;
                foreach (string language in new[] { "en", "ar" })
                foreach (bool dark in new[] { false, true })
                foreach (string page in new[] { "home", "scan", "translate", "updates", "convert", "errors", "feedback", "settings" })
                {
                    Settings.Current.Language = language; Settings.Current.Dark = dark; window.ApplyTheme();
                    window.Navigate(page); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    await Task.Delay(220);
                    SaveView(page + "-" + language + (dark ? "-dark" : ""));
                    if (page == "home" && ((language == "en" && !dark) || (language == "ar" && dark)))
                        WindowAppearance.SavePreview(window, Path.Combine(output, "window-" + language + (dark ? "-dark" : "") + ".png"));
                }
                foreach (string language in new[] { "fr", "es" })
                foreach (bool dark in new[] { false, true })
                {
                    Settings.Current.Language = language; Settings.Current.Dark = dark; window.ApplyTheme(); window.Navigate("home");
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    await Task.Delay(220);
                    SaveView("home-" + language + (dark ? "-dark" : ""));
                }
                SmokeChecks.Run(output);
                await window.CheckNavigation();
                window.CheckAiServices();
                await WelcomeSmokeChecks.Run(window);
                await SelfUpdateSmokeChecks.Run(output);
                await window.CheckSelfUpdateControls();
                await window.CheckStatusLocalization(output);
                await TranslationSmokeChecks.Run();
                await ErrorAssistantSmokeChecks.Run(window, output);
                window.Activate();
                await window.CheckTranslationSettingsLink(output);
                await window.CheckAppList(output);
                await window.CheckInteractionsAndInspection(output);
                await SmartRepairSmokeChecks.Run(window, output);
                await ConversationSmokeChecks.Run(window, output);
                foreach (string language in new[] { "en", "ar", "fr", "es" })
                foreach (bool dark in new[] { false, true })
                {
                    Settings.Current.Language = language; Settings.Current.Dark = dark; window.ApplyTheme(); window.Navigate("settings");
                    // Future-version fixture: no network request or installation in UI smoke checks.
                    var current = Baseera.Core.SelfRelease.ParseVersion(SelfUpdater.CurrentVersion)!;
                    var next = new Version(current.Major, current.Minor, current.Build + 1); string tag = $"v{next}-preview";
                    var example = new Baseera.Core.SelfRelease(tag, next, I18n.T("selfPreviewNotes"),
                        new Uri($"https://github.com/yabs117/baseera/releases/download/{tag}/Baseera-v{next}-Windows-x64.exe"), 173318766, new string('0', 64));
                    var dialog = new UpdateWindow(window, example); dialog.Show();
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    await Task.Delay(220);
                    Snapshot(dialog, Path.Combine(output, "update-" + language + (dark ? "-dark" : "") + ".png")); renders++;
                    if (language == "en" && !dark || language == "ar" && dark)
                        WindowAppearance.SavePreview(dialog, Path.Combine(output, "update-window-" + language + (dark ? "-dark" : "") + ".png"));
                    dialog.Close();
                }
                window.Width = 940; window.Height = 680; Settings.Current.Dark = false;
                foreach (string language in new[] { "en", "ar" })
                {
                    Settings.Current.Language = language; window.ApplyTheme(); window.Navigate("home");
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    await Task.Delay(220);
                    SaveView("home-" + language + "-compact");
                }
                File.WriteAllText(Path.Combine(output, "smoke-result.txt"), $"PASS: {renders} UI renders; actual executable and packaged-app icons, app search/filter/selection, source validation and draft persistence; real visible/hidden/minimized/cloaked/tool/owned/passive/closed/off-screen window checks; Arabic/English provider shortcut scrolling and missing-field focus; verified, corrupt, partial, canceled and unsafe-redirect self-update downloads; four-language update dialogs; first-run welcome, restart persistence, repeat preference, modal dismissal, tool choices preserved; native frame previews; native maximize/minimize/restore; settings drafts survive theme changes; bundled Arabic font; opaque screenshots; navigation collapse/expand; language dropdown and RTL switch; format selection; image conversion; PDF; translation response validation; DPAPI key storage; language coverage.");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/smoke-error.txt", ex.ToString()); Shutdown(1);
            }
        };
        window.Show();
    }
    internal static void Snapshot(Window window, string path)
    {
        window.UpdateLayout();
        // Render the opaque LTR frame, including its RTL descendants.
        // Rendering an RTL content root in isolation loses the parent's mirroring transform.
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
            drawing.DrawRectangle((Brush)Current.FindResource("Bg"), null, bounds);
            drawing.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.Fill }, null, bounds);
        }
        bitmap.Render(visual);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 255) throw new InvalidOperationException("Screenshot has transparent pixels: " + path);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
