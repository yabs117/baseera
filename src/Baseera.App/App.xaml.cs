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
        var window = new MainWindow(input, smoke); MainWindow = window;
        if (smoke) window.Loaded += async (_, _) =>
        {
            try
            {
                string output = e.Args.SkipWhile(x => x != "--smoke").Skip(1).FirstOrDefault() ?? "artifacts";
                Directory.CreateDirectory(output);
                Settings.Current.Dark = false;
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
                foreach (string language in new[] { "en", "ar", "fr", "es" })
                foreach (bool dark in new[] { false, true })
                {
                    Settings.Current.Language = language; Settings.Current.Dark = dark; window.ApplyTheme(); window.Navigate("home");
                    await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Snapshot(window, Path.Combine(output, "home-" + language + (dark ? "-dark" : "") + ".png"));
                }
                SmokeChecks.Run(output);
                await window.CheckNavigation();
                Settings.Current.Language = "en"; Settings.Current.Dark = false; window.Width = 960; window.Height = 700;
                window.ApplyTheme(); window.Navigate("home");
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Snapshot(window, Path.Combine(output, "home-en-compact.png"));
                File.WriteAllText(Path.Combine(output, "smoke-result.txt"), "PASS: 19 UI renders; bundled Arabic font resolves without installed fonts; opaque screenshots; navigation collapse/expand; language dropdown and RTL switch; format selection; image conversion; PDF; translation response validation; DPAPI key storage; language coverage.");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory("artifacts"); File.WriteAllText("artifacts/smoke-error.txt", ex.ToString()); Shutdown(1);
            }
        };
        window.Show();
    }
    private static void Snapshot(Window window, string path)
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
