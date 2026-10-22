using System.Windows.Media.Imaging;
using Baseera.App.Services;

namespace Baseera.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); Settings.Load();
        bool smoke = e.Args.Contains("--smoke");
        string? input = e.Args.Length > 1 && e.Args[0] == "--convert" ? e.Args[1] : null;
        var window = new MainWindow(input, smoke); MainWindow = window; window.Show();
        if (smoke) window.Loaded += async (_, _) =>
        {
            try
            {
                string output = e.Args.SkipWhile(x => x != "--smoke").Skip(1).FirstOrDefault() ?? "artifacts";
                Directory.CreateDirectory(output);
                foreach (string language in new[] { "ar", "en", "fr", "es" })
                {
                    Settings.Current.Language = language; window.ApplyTheme();
                    Settings.Current.Onboarded = false; window.Navigate("home");
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Snapshot(window, Path.Combine(output, "setup-" + language + ".png"));
                }
                Settings.Current.Onboarded = true; Settings.Current.ScanEnabled = true; Settings.Current.TranslationEnabled = true;
                Settings.Current.UpdatesEnabled = true; Settings.Current.ConvertEnabled = true;
                Settings.Current.Language = "ar"; window.ApplyTheme();
                foreach (string page in new[] { "home", "scan", "translate", "updates", "convert", "settings" })
                {
                    window.Navigate(page); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Snapshot(window, Path.Combine(output, page + "-ar.png"));
                }
                Settings.Current.Dark = true; window.ApplyTheme(); window.Navigate("home");
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Snapshot(window, Path.Combine(output, "home-ar-dark.png"));
                SmokeChecks.Run(output);
                File.WriteAllText(Path.Combine(output, "smoke-result.txt"), "PASS: 11 UI renders; image conversion; PDF; translation response validation; DPAPI key storage; language coverage.");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/smoke-error.txt", ex.ToString()); Shutdown(1);
            }
        };
    }
    private static void Snapshot(Window window, string path)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
