using System.Diagnostics;
using System.Windows.Automation;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

public sealed partial class MainWindow
{
    internal async Task CheckInteractionsAndInspection(string output)
    {
        if (!smokeMode) throw new InvalidOperationException("Isolated checks only.");
        static IEnumerable<DependencyObject> Nodes(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; }
        }
        async Task Settle() { await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(230); UpdateLayout(); }
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        string language = Settings.Current.Language, source = Settings.Current.ScanPath;
        bool appearance = Settings.Current.Dark;
        var previousReport = report;
        try
        {
            foreach (string locale in new[] { "en", "ar", "fr", "es" })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = locale == "ar"; ApplyTheme(); Navigate("home"); await Settle();
                var scan = Nodes(frame).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Navigate-scan");
                scan.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = MouseEnterEvent });
                await Settle();
                var overlay = (Border)scan.Template.FindName("HoverOverlay", scan);
                var content = (FrameworkElement)scan.Template.FindName("ContentLayer", scan);
                Require(overlay.Opacity > 0 && content.Opacity == 1, "Hover feedback must preserve readable content.");
                scan.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0) { RoutedEvent = MouseLeaveEvent });
                await Settle(); Require(overlay.Opacity == 0, "Hover must return to its resting state.");
                scan.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Settle();
                Require(page == "scan" && pageBody?.Opacity == 1 && selectedNavigationIndicator?.Height == 18 &&
                    (selectedNavigationIndicator.RenderTransform as TranslateTransform)?.Y is null or 0, "Navigation did not settle on the requested page.");
                // Fast changes exercise interruption without waiting for intermediate transitions.
                Navigate("settings"); Navigate("home"); Navigate("scan"); await Settle();
                Require(page == "scan" && pageBody?.Opacity == 1, "Interrupted navigation left a page invisible.");
                var combo = Nodes(frame).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == I18n.T("language"));
                combo.IsDropDownOpen = true; await Settle();
                var popup = (System.Windows.Controls.Primitives.Popup)combo.Template.FindName("PART_Popup", combo);
                Require(popup.IsOpen && popup.Child is FrameworkElement { Opacity: 1 }, "Language flyout was invisible.");
                combo.IsDropDownOpen = false; await Settle();
                Navigate("settings"); await Settle();
                var toggle = Nodes(frame).OfType<CheckBox>().First(c => c.IsVisible);
                bool original = toggle.IsChecked == true; toggle.IsChecked = !original; await Settle();
                var dot = (FrameworkElement)toggle.Template.FindName("Dot", toggle);
                Require(dot.RenderTransform is TranslateTransform move && move.X == (!original ? 18 : 0), "Switch position disagrees with its state, including RTL.");
                toggle.IsChecked = original; await Settle();
            }
            Settings.Current.Language = "en"; Settings.Current.Dark = false; ApplyTheme();
            // Exercise the actual Inspect button against this running executable. In packaged
            // checks it is the complete self-contained release, well above the old 64 MiB cap.
            string executable = Environment.ProcessPath!; Settings.Current.ScanPath = executable;
            report = null; Navigate("scan"); await Settle();
            var inspect = Nodes(frame).OfType<Button>().Single(b => AutomationProperties.GetName(b) == I18n.T("inspect"));
            var timer = Stopwatch.StartNew(); inspect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (operation is not null) { await Task.Delay(30); if (timer.Elapsed > TimeSpan.FromMinutes(2)) throw new Exception("Executable inspection did not finish."); }
            await Settle();
            Require(report is { Incomplete: false } && report.Files.Single().Size == new FileInfo(executable).Length &&
                report.Files.Single().Sha256 == UpdateTransaction.Hash(executable), "Inspect button did not read/hash every byte of the running executable.");
            Require(status.Text == I18n.T("done") && Nodes(frame).OfType<TextBox>().Any(t => t.Text.StartsWith("SHA-256  ")), "Completed scan did not show its readable file details.");
            // Publish a demo path rather than a local profile/workspace path.
            Nodes(frame).OfType<TextBox>().Single(t => t.Text == executable).Text = @"C:\Apps\Baseera\Baseera.exe";
            await Settle(); // Allow the native preview to repaint with the demo path.
            WindowAppearance.SavePreview(this, Path.Combine(output, "inspection-complete-en.png"));
            File.WriteAllText(Path.Combine(output, "interaction-inspection-result.txt"), $"PASS: routed hover/resting feedback retains text opacity; actual navigation buttons, interrupted navigation, visible language flyouts, animated switch state and RTL in four languages; actual Inspect button fully read {report!.Files.Single().Size:N0} bytes and verified SHA-256 in {timer.Elapsed.TotalSeconds:0.0}s. System motion allowed: {FluentMotion.Allowed}. No physical keyboard or reduced-motion OS setting was changed.");
        }
        finally
        {
            Settings.Current.Language = language; Settings.Current.Dark = appearance; Settings.Current.ScanPath = source;
            report = previousReport; Settings.Save(); ApplyTheme(); Navigate("home");
        }
    }
}
