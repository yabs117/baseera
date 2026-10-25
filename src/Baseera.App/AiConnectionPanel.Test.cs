using Baseera.App.Services;

namespace Baseera.App;

internal sealed partial class AiConnectionPanel
{
    private readonly Func<AiProbeConnection, CancellationToken, Task<AiProbeResult>> probe;
    private readonly Action? openAssistant;
    private Button testConnection = null!, cancelTest = null!, openChat = null!;
    private readonly TextBlock testStatus = new() { TextWrapping = TextWrapping.Wrap };
    private CancellationTokenSource? testing;
    private int testRevision;
    private string passedScope = "";
    private string ProbeScope(AiProbeConnection connection) => connection.Provider + "|" + connection.Model + "|" +
        (connection.ChatGpt ? "account|" + connection.Account + "|" + ChatGptAuth.Fingerprint(connection.Account) : "api|" + Fingerprint(connection.Key));
    private AiProbeConnection ProbeDraft()
    {
        if (ModelInput.SelectedItem is not AiModelOption chosen || !AiCatalog.ValidModel(draft.Provider, chosen.Id)) throw new InvalidOperationException(I18n.T("aiChooseModel"));
        if (UsingChatGpt)
        {
            if (!ChatGptAuth.Connected(draft.ChatGptAccount)) throw new InvalidOperationException(I18n.T("chatGptReconnect"));
            bool unchanged = draft.ChatGptAccount == Settings.Current.ChatGptAccount && chosen.Id == Settings.Current.ChatGptModel;
            if (!unchanged && (draft.ChatGpt.Fingerprint != ChatGptAuth.Fingerprint(draft.ChatGptAccount) || !draft.ChatGpt.Models.Any(x => x.Id == chosen.Id)))
                throw new InvalidOperationException(I18n.T("aiChooseModel"));
            return new(draft.Provider, chosen.Id, "", draft.ChatGptAccount);
        }
        var state = draft.For(draft.Provider); string key = state.Key.Length > 0 ? state.Key : Settings.GetKey(draft.Provider);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(I18n.T("aiNeedKey"));
        bool saved = state.Key.Length == 0 && chosen.Id == Settings.GetModel(draft.Provider) && File.Exists(Settings.KeyPath(draft.Provider));
        if (!saved && (state.Fingerprint != Fingerprint(key) || !state.Models.Any(x => x.Id == chosen.Id))) throw new InvalidOperationException(I18n.T("aiRefreshAfterKey"));
        return new(draft.Provider, chosen.Id, key);
    }
    private bool DraftIsSaved()
    {
        try
        {
            var connection = ProbeDraft();
            return AiCatalog.Configured() && connection.Provider == AiCatalog.Provider && connection.Model == AiCatalog.Model &&
                (connection.ChatGpt ? Settings.Current.OpenAiAuth == "chatgpt" && connection.Account == Settings.Current.ChatGptAccount :
                    !(connection.Provider == "openai" && Settings.Current.OpenAiAuth == "chatgpt") && Fingerprint(connection.Key) == Fingerprint(Settings.GetKey(connection.Provider)));
        }
        catch { return false; }
    }
    private void BuildTestArea()
    {
        var label = new TextBlock { Text = I18n.T("aiProbeHelp"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); AddControl(label);
        testConnection = Button("aiProbe", async () => await TestConnection()); Id(testConnection, "AiTestConnection"); AddControl(testConnection);
        cancelTest = Button("cancel", () => { CancelTest(); testStatus.Text = I18n.T("aiProbeCancelled"); }); Id(cancelTest, "AiCancelTest");
        cancelTest.Visibility = Visibility.Collapsed; AddControl(cancelTest);
        testStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Id(testStatus, "AiTestStatus");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(testStatus, System.Windows.Automation.AutomationLiveSetting.Polite); AddControl(testStatus);
        openChat = Button("errorsOpen", () =>
        {
            if (!DraftIsSaved()) testStatus.Text = I18n.T("aiSaveBeforeChat");
            else if (!Settings.Current.ErrorAssistantEnabled) testStatus.Text = I18n.T("disabledDesc");
            else openAssistant?.Invoke();
        });
        Id(openChat, "AiOpenAssistant"); openChat.Visibility = openAssistant is null ? Visibility.Collapsed : Visibility.Visible; AddControl(openChat);
        RefreshTestControls();
    }
    private void RefreshTestControls()
    {
        if (testConnection is null) return;
        testConnection.IsEnabled = testing is null && loading is null && signInOperation is null;
        cancelTest.Visibility = testing is null ? Visibility.Collapsed : Visibility.Visible;
        openChat.IsEnabled = testing is null && DraftIsSaved();
    }
    private void CancelTest()
    {
        testRevision++; testing?.Cancel(); testing = null; passedScope = "";
        testStatus.Text = ""; RefreshTestControls();
    }
    internal async Task TestConnection()
    {
        if (testing is not null || loading is not null || signInOperation is not null) return;
        CancelTest(); int revision = testRevision;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        try
        {
            var connection = ProbeDraft(); string scope = ProbeScope(connection);
            testing = cancellation; RefreshTestControls(); testStatus.Text = I18n.T("aiProbeRunning");
            var result = await probe(connection, cancellation.Token); cancellation.Token.ThrowIfCancellationRequested();
            if (revision != testRevision || scope != ProbeScope(ProbeDraft())) return;
            passedScope = result.ImageAnswerMatched ? scope : "";
            testStatus.Text = string.Format(I18n.T(result.ImageAnswerMatched ? "aiProbePassed" : "aiProbePartial"), result.Duration.TotalSeconds.ToString("0.0")) +
                "\n" + I18n.T(DraftIsSaved() ? "aiChatReady" : "aiSaveBeforeChat");
        }
        catch (OperationCanceledException) { if (revision == testRevision) testStatus.Text = I18n.T("aiProbeTimedOut"); }
        catch (InvalidOperationException ex) when (revision == testRevision && testing is null) { testStatus.Text = ex.Message; }
        catch (Exception) { if (revision == testRevision) testStatus.Text = I18n.T("aiProbeFailed"); }
        finally { if (revision == testRevision) { testing = null; RefreshTestControls(); } }
    }
    private void ConnectionSaved()
    {
        RefreshTestControls();
        if (passedScope.Length > 0)
        {
            try { if (passedScope == ProbeScope(ProbeDraft())) testStatus.Text = I18n.T("aiSavedAndTested"); }
            catch { passedScope = ""; }
        }
    }
}
