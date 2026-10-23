using System.Runtime.InteropServices;
using Baseera.App.Services;
using Forms = System.Windows.Forms;

namespace Baseera.App;

internal static class TranslationSmokeChecks
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    private sealed class PassiveForm : Forms.Form
    {
        protected override bool ShowWithoutActivation => true;
        protected override Forms.CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000; return p; } }
    }
    internal static async Task Run()
    {
        using var form = new Forms.Form { Text = "Baseera window selection fixture", StartPosition = Forms.FormStartPosition.Manual,
            Location = new System.Drawing.Point(100, 100), Size = new System.Drawing.Size(320, 180), ShowInTaskbar = true };
        bool Selectable() => WindowTranslator.IsSelectableWindow(form.Handle, false);
        void Require(bool condition, string state) { if (!condition) throw new Exception("Window selection: " + state); }
        form.Show(); await Task.Delay(100);
        Require(Selectable(), "normal open window excluded");
        Require(!WindowTranslator.IsSelectableWindow(form.Handle), "own application was included");
        form.Hide(); Require(!Selectable(), "hidden window included");
        bool rejected = false;
        try { await WindowTranslator.CaptureWindow(form.Handle, CancellationToken.None); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected, "hidden window reached capture");
        form.Show(); form.WindowState = Forms.FormWindowState.Minimized; Require(!Selectable(), "minimized window included");
        form.WindowState = Forms.FormWindowState.Normal;
        form.Location = new System.Drawing.Point(-30000, -30000); Require(!Selectable(), "off-screen window included");
        form.Location = new System.Drawing.Point(100, 100); form.Text = ""; Require(!Selectable(), "untitled helper included"); form.Text = "Baseera window selection fixture";
        int cloak = 1; Require(DwmSetWindowAttribute(form.Handle, 13, ref cloak, sizeof(int)) == 0, "could not create cloaked fixture");
        await Task.Delay(100); Require(!Selectable(), "DWM-cloaked background window included");
        cloak = 0; DwmSetWindowAttribute(form.Handle, 13, ref cloak, sizeof(int)); await Task.Delay(100); Require(Selectable(), "uncloaked window did not return");
        using (var owned = new Forms.Form { Text = "Baseera owned helper fixture", ShowInTaskbar = false })
        { owned.Show(form); Require(!WindowTranslator.IsSelectableWindow(owned.Handle, false), "owned helper window included"); }
        using (var tool = new Forms.Form { Text = "Baseera tool fixture", FormBorderStyle = Forms.FormBorderStyle.FixedToolWindow, ShowInTaskbar = false })
        { tool.Show(); Require(!WindowTranslator.IsSelectableWindow(tool.Handle, false), "tool window included"); }
        using (var passive = new PassiveForm { Text = "Baseera passive fixture" })
        { passive.Show(); Require(!WindowTranslator.IsSelectableWindow(passive.Handle, false), "non-activating window included"); }
        IntPtr closed = form.Handle; form.Close(); Require(!WindowTranslator.IsSelectableWindow(closed, false), "closed window included");
    }
}

public sealed partial class MainWindow
{
    internal async Task CheckTranslationSettingsLink(string output)
    {
        if (!smokeMode) throw new InvalidOperationException("Isolated UI check only.");
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        string originalLanguage = Settings.Current.Language, originalModel = Settings.Current.GeminiModel;
        double originalWidth = Width, originalHeight = Height;
        try
        {
            Settings.Current.TranslationEnabled = true; Settings.Current.GeminiModel = ""; Width = 940; Height = 680;
            foreach (string language in new[] { "en", "ar" })
            foreach (bool savedKey in new[] { false, true })
            {
                Settings.Current.Language = language; ApplyTheme(); Settings.SetKey(savedKey ? "baseera-smoke-fixture-not-a-real-key" : "");
                Navigate("translate"); UpdateLayout();
                var button = Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "TranslationProviderSettings");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                UpdateLayout();
                if (page != "settings" || translationProviderSection is null || scroller.VerticalOffset <= 0) throw new Exception("Translation provider was not scrolled into view.");
                var expected = savedKey ? (System.Windows.Controls.Control?)translationModelInput : translationKeyInput;
                if (expected is null || !expected.IsKeyboardFocusWithin) throw new Exception("Missing translation field did not receive keyboard focus.");
                var bounds = expected.TransformToAncestor(scroller).TransformBounds(new Rect(0, 0, expected.ActualWidth, expected.ActualHeight));
                if (bounds.Top < 0 || bounds.Bottom > scroller.ActualHeight) throw new Exception("Focused translation field is outside the visible settings viewport.");
                if (!savedKey)
                    WindowAppearance.SavePreview(this, Path.Combine(output, "translation-settings-" + language + ".png"));
            }
        }
        finally
        {
            Settings.SetKey(""); Settings.Current.Language = originalLanguage; Settings.Current.GeminiModel = originalModel;
            Width = originalWidth; Height = originalHeight; ApplyTheme(); Navigate("home");
        }
    }
}
