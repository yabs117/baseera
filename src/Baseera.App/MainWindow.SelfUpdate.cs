using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

public sealed partial class MainWindow
{
    private readonly System.Windows.Threading.DispatcherTimer selfUpdateTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private DateTime nextSelfCheck = DateTime.MinValue;
    private bool checkingSelf, showingSelf;
    private SelfRelease? pendingSelf;
    private readonly HashSet<string> offeredSelf = [];
    private string selfStatusKey = "";
    internal string? UpdateReceipt { get; set; }

    private async Task CheckSelfUpdate(bool manual = false, Func<CancellationToken, Task<SelfRelease?>>? check = null)
    {
        if ((smokeMode && check is null) || checkingSelf || showingSelf || (!manual && !Settings.Current.CheckBaseeraUpdates)) return;
        checkingSelf = true; bool refreshed = false;
        try
        {
            if (manual || DateTime.UtcNow >= nextSelfCheck)
            {
                refreshed = true;
                selfStatusKey = "selfChecking"; if (manual) Render(true);
                pendingSelf = await (check ?? SelfUpdater.Check)(lifetime.Token);
                nextSelfCheck = DateTime.UtcNow.AddHours(2);
                selfStatusKey = pendingSelf is null ? "selfLatest" : "selfAvailable";
                if (manual) Render(true);
            }
            bool busy = operation is not null || automaticUpdate is not null || OwnedWindows.Cast<Window>().Any(w => w.IsVisible);
            if (pendingSelf is not null && !busy && (manual || (IsActive && WindowState != WindowState.Minimized && !offeredSelf.Contains(pendingSelf.Tag))))
            {
                // Keep drafts in memory until saved. Updating should not silently discard them.
                if (aiConnectionDraft.Services.Any(pair => pair.Value.Key.Length > 0 || pair.Value.Model != Settings.GetModel(pair.Key)) || startupDraft is not null || startupScanDraft is not null || explorerDraft is not null)
                { selfStatusKey = "selfSaveDrafts"; if (manual) Render(true); return; }
                showingSelf = true; offeredSelf.Add(pendingSelf.Tag);
                new UpdateWindow(this, pendingSelf).ShowDialog();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { nextSelfCheck = DateTime.UtcNow.AddMinutes(15); selfStatusKey = "selfCheckFailed"; if (manual) Render(true); }
        finally
        {
            checkingSelf = showingSelf = false;
            if (refreshed && page == "settings" && !lifetime.IsCancellationRequested) Render(true);
        }
    }

    internal async Task CheckSelfUpdateControls()
    {
        if (!smokeMode) throw new InvalidOperationException("Isolated UI checks only.");
        Navigate("settings");
        static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }
        void VerifyRetry()
        {
            UpdateLayout();
            if (!Descendants(frame).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == I18n.T("selfCheck")).IsEnabled)
                throw new Exception("Manual update check cannot be retried.");
        }
        await CheckSelfUpdate(true, _ => Task.FromResult<SelfRelease?>(null)); VerifyRetry();
        await CheckSelfUpdate(true, _ => Task.FromException<SelfRelease?>(new HttpRequestException("Offline test"))); VerifyRetry();
        await CheckSelfUpdate(true, _ => Task.FromResult<SelfRelease?>(null)); VerifyRetry();
    }

    private void SelfUpdateSettings(StackPanel body)
    {
        SectionHeading(body, "selfTitle"); var content = Stack();
        var version = Text("Baseera " + SelfUpdater.CurrentVersion, 14); version.FlowDirection = FlowDirection.LeftToRight; content.Children.Add(version);
        content.Children.Add(Text(I18n.T("selfSettingsNote"), 12, true));
        content.Children.Add(Toggle("selfAuto", Settings.Current.CheckBaseeraUpdates, enabled => { Settings.Current.CheckBaseeraUpdates = enabled; Settings.Save(); }));
        var check = Action("selfCheck", async () => await CheckSelfUpdate(true)); check.IsEnabled = !checkingSelf; check.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(check);
        if (selfStatusKey.Length > 0)
        {
            var message = Text(I18n.T(selfStatusKey), 12, true);
            System.Windows.Automation.AutomationProperties.SetAutomationId(message, "SelfUpdateStatus");
            content.Children.Add(message);
        }
        body.Children.Add(Card(content));
    }
}
