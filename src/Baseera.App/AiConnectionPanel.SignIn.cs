using Baseera.App.Services;

namespace Baseera.App;

internal sealed partial class AiConnectionPanel
{
    private readonly StackPanel signInArea = new(), accountArea = new();
    private readonly ComboBox connectionMode = new();
    private readonly ComboBox accountPicker = new() { FlowDirection = FlowDirection.LeftToRight, MaxDropDownHeight = 240 };
    private readonly TextBlock accountState = new() { TextWrapping = TextWrapping.Wrap };
    private Button signIn = null!, addAccount = null!, signOut = null!, cancelSignIn = null!;
    private CancellationTokenSource? signInOperation;
    internal bool UsingChatGpt => draft.Provider == "openai" && draft.OpenAiMode == "chatgpt";
    internal System.Windows.Controls.Control MissingInput => UsingChatGpt
        ? accountPicker.SelectedItem is ChatGptProfile { Connected: true } ? ModelInput : signIn
        : File.Exists(Settings.KeyPath(draft.Provider)) ? ModelInput : KeyInput;

    private void BuildSignInArea()
    {
        var label = new TextBlock { Text = I18n.T("aiConnectionMethod"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        signInArea.Children.Add(label);
        connectionMode.ItemsSource = new[] { I18n.T("aiApiConnection"), I18n.T("chatGptConnection") };
        connectionMode.Margin = new Thickness(0, 0, 0, 10); Id(connectionMode, "OpenAiConnectionMethod"); signInArea.Children.Add(connectionMode);
        var note = new TextBlock { Text = I18n.T("chatGptEligibility"), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); signInArea.Children.Add(note);
        signIn = Button("chatGptContinue", async () => await SignIn(false));
        ChatGptUi.BrandSignInButton(signIn); signIn.Margin = new Thickness(0, 0, 0, 10); signInArea.Children.Add(signIn);
        cancelSignIn = Button("cancel", CancelSignIn); Id(cancelSignIn, "CancelChatGptSignIn"); cancelSignIn.Visibility = Visibility.Collapsed; signInArea.Children.Add(cancelSignIn);
        accountPicker.Margin = new Thickness(0, 0, 0, 8); Id(accountPicker, "ChatGptAccountPicker"); accountArea.Children.Add(accountPicker);
        accountState.Margin = new Thickness(0, 0, 0, 8); accountState.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); accountArea.Children.Add(accountState);
        var actions = new WrapPanel();
        addAccount = Button("chatGptAddAccount", async () => await SignIn(true)); actions.Children.Add(addAccount);
        signOut = Button("chatGptSignOut", async () => await SignOut()); actions.Children.Add(signOut);
        actions.Children.Add(Button("chatGptManageUsage", ChatGptUi.OpenUsage)); accountArea.Children.Add(actions); signInArea.Children.Add(accountArea);
        AddControl(signInArea);
        connectionMode.SelectionChanged += (_, _) =>
        {
            if (updating || connectionMode.SelectedIndex < 0) return;
            CancelLoad(); CancelSignIn(); draft.OpenAiMode = connectionMode.SelectedIndex == 1 ? "chatgpt" : "api"; RefreshProvider();
        };
        accountPicker.SelectionChanged += (_, _) =>
        {
            if (updating || accountPicker.SelectedItem is not ChatGptProfile profile) return;
            CancelLoad(); CancelSignIn();
            if (draft.ChatGptAccount != profile.Id) { draft.ChatGptAccount = profile.Id; draft.ChatGpt = new(); }
            RefreshProvider();
        };
    }
    private void RefreshSignInArea()
    {
        signInArea.Visibility = draft.Provider == "openai" ? Visibility.Visible : Visibility.Collapsed;
        connectionMode.SelectedIndex = draft.OpenAiMode == "chatgpt" ? 1 : 0;
        accountArea.Visibility = UsingChatGpt ? Visibility.Visible : Visibility.Collapsed;
        if (draft.Provider != "openai") return;
        try
        {
            var accounts = ChatGptAuth.Profiles;
            accountPicker.ItemsSource = accounts;
            accountPicker.SelectedItem = accounts.FirstOrDefault(profile => profile.Id == draft.ChatGptAccount);
            bool connected = accountPicker.SelectedItem is ChatGptProfile { Connected: true };
            accountPicker.Visibility = accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            accountState.Text = I18n.T(connected ? ChatGptAuth.Selected && draft.ChatGptAccount == Settings.Current.ChatGptAccount ? "chatGptUsingPlan" : "chatGptReady" : "chatGptChooseAccount");
            signOut.IsEnabled = connected;
        }
        catch { accountState.Text = I18n.T("chatGptStorageError"); }
    }
    private void CancelSignIn()
    {
        signInOperation?.Cancel(); signInOperation = null;
        if (signIn is not null) signIn.IsEnabled = true;
        if (addAccount is not null) addAccount.IsEnabled = true;
        if (cancelSignIn is not null) cancelSignIn.Visibility = Visibility.Collapsed;
    }
    private async Task SignIn(bool newAccount)
    {
        CancelSignIn(); CancelLoad();
        draft.OpenAiMode = "chatgpt"; RefreshProvider();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5)); signInOperation = cancellation;
        signIn.IsEnabled = addAccount.IsEnabled = false; cancelSignIn.Visibility = Visibility.Visible;
        status.Text = I18n.T("chatGptWaiting");
        try
        {
            string? existing = !newAccount && accountPicker.SelectedItem is ChatGptProfile profile ? profile.Id : null;
            var connected = await ChatGptAuth.SignIn(existing, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            draft.ChatGptAccount = connected.Id; draft.ChatGpt = new(); RefreshProvider();
            if (ChatGptAuth.NeedsWelcome(connected.Id))
            {
                ChatGptUi.ShowWelcome(Window.GetWindow(this));
                await ChatGptAuth.MarkWelcomeSeen(connected.Id, cancellation.Token);
            }
            if (!connected.Connected) { status.Text = I18n.T("chatGptConsentNeeded"); return; }
            await LoadModels();
        }
        catch (OperationCanceledException) { if (ReferenceEquals(signInOperation, cancellation)) status.Text = I18n.T("chatGptSignInCancelled"); }
        catch (Exception ex)
        {
            if (ReferenceEquals(signInOperation, cancellation)) status.Text = ex is ChatGptException ? ex.Message : I18n.T("chatGptSignInFailed");
        }
        finally { if (ReferenceEquals(signInOperation, cancellation)) { signInOperation = null; signIn.IsEnabled = addAccount.IsEnabled = true; cancelSignIn.Visibility = Visibility.Collapsed; } }
    }
    private void SaveChatGpt()
    {
        if (!ChatGptAuth.Connected(draft.ChatGptAccount)) throw new ChatGptException("chatGptReconnect");
        if (ModelInput.SelectedItem is not AiModelOption chosen || !AiCatalog.ValidModel("openai", chosen.Id)) throw new InvalidOperationException(I18n.T("aiChooseModel"));
        bool unchanged = draft.ChatGptAccount == Settings.Current.ChatGptAccount && chosen.Id == Settings.Current.ChatGptModel;
        if (!unchanged && (draft.ChatGpt.Fingerprint != ChatGptAuth.Fingerprint(draft.ChatGptAccount) || !draft.ChatGpt.Models.Any(model => model.Id == chosen.Id)))
            throw new InvalidOperationException(I18n.T("aiChooseModel"));
        Settings.Current.OpenAiAuth = "chatgpt"; Settings.Current.AiProvider = "openai";
        Settings.Current.ChatGptAccount = draft.ChatGptAccount; Settings.Current.ChatGptModel = chosen.Id; Settings.Save();
        status.Text = I18n.T("chatGptUsingPlan"); saved();
    }
    private async Task SignOut()
    {
        if (accountPicker.SelectedItem is not ChatGptProfile profile) return;
        CancelLoad(); CancelSignIn(); signOut.IsEnabled = false;
        try
        {
            bool revoked = await signOutProfile(profile.Id, CancellationToken.None);
            draft.ChatGpt = new(); RefreshProvider(); status.Text = I18n.T(revoked ? "chatGptSignedOut" : "chatGptRevocationUnconfirmed");
        }
        catch { status.Text = I18n.T("chatGptStorageError"); }
        finally { signOut.IsEnabled = accountPicker.SelectedItem is ChatGptProfile { Connected: true }; }
    }
}
