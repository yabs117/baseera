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
    private string selfStatus = "";
    internal string? UpdateReceipt { get; set; }

    private async Task CheckSelfUpdate(bool manual = false)
    {
        if (smokeMode || checkingSelf || showingSelf || (!manual && !Settings.Current.CheckBaseeraUpdates)) return;
        checkingSelf = true;
        try
        {
            if (manual || DateTime.UtcNow >= nextSelfCheck)
            {
                selfStatus = I18n.T("selfChecking"); if (manual) Render(true);
                pendingSelf = await SelfUpdater.Check(lifetime.Token);
                nextSelfCheck = DateTime.UtcNow.AddHours(2);
                selfStatus = I18n.T(pendingSelf is null ? "selfLatest" : "selfAvailable");
                if (manual) Render(true);
            }
            bool busy = operation is not null || automaticUpdate is not null || OwnedWindows.Cast<Window>().Any(w => w.IsVisible);
            if (pendingSelf is not null && !busy && (manual || (IsActive && WindowState != WindowState.Minimized && !offeredSelf.Contains(pendingSelf.Tag))))
            {
                // Keep drafts in memory until saved. Updating should not silently discard them.
                if (providerKeyDraft.Length > 0 || modelDraft is not null || startupDraft is not null || startupScanDraft is not null || explorerDraft is not null)
                { selfStatus = I18n.T("selfSaveDrafts"); if (manual) Render(true); return; }
                showingSelf = true; offeredSelf.Add(pendingSelf.Tag);
                new UpdateWindow(this, pendingSelf).ShowDialog();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch { nextSelfCheck = DateTime.UtcNow.AddMinutes(15); selfStatus = I18n.T("selfCheckFailed"); if (manual) Render(true); }
        finally { checkingSelf = showingSelf = false; }
    }

    private void SelfUpdateSettings(StackPanel body)
    {
        SectionHeading(body, "selfTitle"); var content = Stack();
        var version = Text("Baseera " + SelfUpdater.CurrentVersion, 14); version.FlowDirection = FlowDirection.LeftToRight; content.Children.Add(version);
        content.Children.Add(Text(I18n.T("selfSettingsNote"), 12, true));
        content.Children.Add(Toggle("selfAuto", Settings.Current.CheckBaseeraUpdates, enabled => { Settings.Current.CheckBaseeraUpdates = enabled; Settings.Save(); }));
        var check = Action("selfCheck", async () => await CheckSelfUpdate(true)); check.IsEnabled = !checkingSelf; check.HorizontalAlignment = HorizontalAlignment.Left; content.Children.Add(check);
        if (selfStatus.Length > 0) content.Children.Add(Text(selfStatus, 12, true));
        body.Children.Add(Card(content));
    }
}
