using System.Diagnostics;
using System.Text.Json;
using Baseera.App.Services;
using Baseera.Core;
using Microsoft.Win32;

namespace Baseera.App;

public sealed partial class MainWindow : Window
{
    private readonly Grid shell = new();
    private readonly Grid frame = new();
    private readonly Border sidebar = new();
    private ScrollViewer scroller = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private TextBlock status = new();
    private Button? cancelOperation;
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private CancellationTokenSource? automaticUpdate;
    private TranslationOverlay? overlay;
    private bool expanded = true;
    private string page = "home";
    private List<string> images = [];
    private ScanReport? report;
    private string updateLog = "";
    private ExplorerShortcut? explorerShortcut;
    private ErrorAssistantWindow? errorAssistant;
    private readonly bool smokeMode;
    private readonly System.Windows.Threading.DispatcherTimer updateTimer = new() { Interval = TimeSpan.FromHours(6) };
    public MainWindow(string? conversionFile = null, bool smoke = false, bool launchErrorAssistant = true)
    {
        smokeMode = smoke;
        Title = "Baseera"; Width = 1160; Height = 820; MinWidth = 940; MinHeight = 680;
        WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = Brand.InterfaceFont; FontSize = 13; Icon = Brand.WindowIcon;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        if (conversionFile is not null && File.Exists(conversionFile)) { images.Add(conversionFile); page = "convert"; }
        Content = frame;
        ApplyTheme(); Render();
        ChatGptAuth.SessionSignedOut += OnChatGptSignedOut;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.E && System.Windows.Input.Keyboard.Modifiers == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift))
            { e.Handled = true; ShowErrorAssistant(); }
        };
        deviceTimer.Tick += async (_, _) => { if (IsVisible && WindowState != WindowState.Minimized) await RefreshDevice(); };
        Loaded += (_, _) => StartDeviceMonitor();
        Closed += (_, _) => { StopDeviceMonitor(); ChatGptAuth.SessionSignedOut -= OnChatGptSignedOut; operation?.Cancel(); lifetime.Cancel(); updateTimer.Stop(); selfUpdateTimer.Stop(); overlay?.Close(); explorerShortcut?.Dispose(); };
        if (!smoke)
        {
            ApplyExplorerShortcut();
            updateTimer.Tick += async (_, _) => await AutomaticUpdates(); updateTimer.Start();
            selfUpdateTimer.Tick += async (_, _) => await CheckSelfUpdate(); selfUpdateTimer.Start();
            Loaded += async (_, _) =>
            {
                if (UpdateReceipt is not null)
                {
                    try { var error = SelfUpdater.Acknowledge(UpdateReceipt); if (error is not null) SetOperationStatus("selfRestored", error); }
                    catch (Exception ex) { SetOperationStatus("selfFailed", ex.Message); }
                }
                ShowStartupWelcome();
                if (launchErrorAssistant && Settings.Current.ErrorAssistantEnabled) ShowErrorAssistant();
                await CheckSelfUpdate();
                if (!Settings.Current.Onboarded) return;
                if (Settings.Current.ScanEnabled && Settings.Current.ScanAtStartup && (File.Exists(Settings.Current.ScanPath) || Directory.Exists(Settings.Current.ScanPath)))
                    await Run(async ct => { report = await Task.Run(() => new ModScanner().Scan(Settings.Current.ScanPath, ct), ct); page = "scan"; Render(); });
                await AutomaticUpdates();
            };
        }
    }
    private void OnChatGptSignedOut(string account)
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.InvokeAsync(() => OnChatGptSignedOut(account)); return; }
        if (IsLoaded && page is "translate" or "errors") Render(preserveScroll: true);
    }
    internal ErrorAssistantWindow? CurrentErrorAssistant => errorAssistant;
    internal Func<AiProbeConnection, CancellationToken, Task<AiProbeResult>>? ConnectionProbeOverride { get; set; }
    internal void OpenAiSettings(ErrorAssistantWindow? origin = null)
    {
        if (origin is not null && !ReferenceEquals(errorAssistant, origin))
        {
            errorAssistant = origin;
            origin.Closed += (_, _) => { if (ReferenceEquals(errorAssistant, origin)) errorAssistant = null; };
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate(); OpenTranslationSettings();
    }
    internal void ShowErrorAssistant()
    {
        if (!Settings.Current.ErrorAssistantEnabled) return;
        if (!AiCatalog.Configured()) { OpenAiSettings(); return; }
        if (errorAssistant is { } existing)
        { existing.RefreshSavedConnection(true); if (!existing.IsVisible) existing.Show(); existing.Activate(); existing.FocusComposer(); ApplyPendingDeviceReview(existing); return; }
        var created = new ErrorAssistantWindow(this); errorAssistant = created;
        created.Closed += (_, _) => { if (ReferenceEquals(errorAssistant, created)) errorAssistant = null; };
        created.Show();
        ApplyPendingDeviceReview(created);
    }
    internal bool ShowStartupWelcome()
    {
        if (!WelcomeWindow.ShouldShow(Settings.Current)) return false;
        ShowWelcome(); return true;
    }
    internal void ShowWelcome()
    {
        var welcome = new WelcomeWindow(this); welcome.ShowDialog();
        Settings.Current.WelcomeSeen = true;
        Settings.Current.ShowWelcomeOnStartup = welcome.ShowAgain;
        Settings.Save(); ApplyTheme(); Render(true);
    }
    private void ApplyExplorerShortcut()
    {
        explorerShortcut?.Dispose(); explorerShortcut = null;
        if (smokeMode || !Settings.Current.ConvertEnabled || !Settings.Current.ExplorerMenu) return;
        explorerShortcut = new ExplorerShortcut(files => { images = files; page = "convert"; Show(); WindowState = WindowState.Normal; Activate(); Render(); });
    }
    private async Task AutomaticUpdates()
    {
        if (automaticUpdate is not null || showingSelf || !Settings.Current.Onboarded || !Settings.Current.UpdatesEnabled || Settings.Current.AutoUpdateIds.Count == 0) return;
        automaticUpdate = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try { updateLog = await Updater.UpdateSelected(automaticUpdate.Token); if (page == "updates") Render(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { updateLog = ex.Message; SetOperationStatus("error"); }
        finally { automaticUpdate.Dispose(); automaticUpdate = null; }
    }
}
