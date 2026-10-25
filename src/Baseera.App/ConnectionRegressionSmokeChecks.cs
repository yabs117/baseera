using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Baseera.App.Services;

namespace Baseera.App;

internal static class ConnectionRegressionSmokeChecks
{
    private static IEnumerable<DependencyObject> Nodes(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Nodes(child)) yield return nested; }
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Nodes(root).OfType<T>().Single(x => AutomationProperties.GetAutomationId(x) == id);
    private static void Click(DependencyObject root, string id) => Find<Button>(root, id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static async Task Settle(Window owner)
    { await owner.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); await Task.Delay(100); }
    private sealed class Waiting(TaskCompletionSource started) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); }
    }

    internal static async Task CheckReopen(MainWindow owner, ErrorImage image, string output)
    {
        string language = Settings.Current.Language, provider = Settings.Current.AiProvider, model = Settings.Current.GeminiModel, key = Settings.GetKey("gemini");
        bool dark = Settings.Current.Dark;
        try
        {
            Settings.Current.AiProvider = "gemini"; Settings.Current.GeminiModel = "gemini-3.8-flash"; Settings.SetKey("gemini", "fixture-key-not-live");
            foreach (string locale in new[] { "ar", "en", "fr", "es" })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = locale == "ar"; owner.ApplyTheme(); owner.Render();
                var dialog = new ErrorAssistantWindow(owner); dialog.Show(); await Settle(owner);
                Click(dialog, "chatTools"); await Settle(owner);
                var description = Find<TextBox>(dialog, "ErrorDescription"); description.Text = "Keep this error description";
                typeof(ErrorAssistantWindow).GetMethod("SetImage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, [image]);
                var panel = Nodes(dialog).OfType<AiConnectionPanel>().Single();
                Require(!panel.IsVisible && Find<Button>(dialog, "errorConfigure").IsVisible, "Configured assistant should initially hide connection controls.");
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    Click(dialog, "errorConfigure"); await Settle(owner); dialog.UpdateLayout();
                    Require(panel.IsVisible && panel.Service.IsVisible && panel.Service.Items.Count == 20 && ReferenceEquals(panel, Nodes(dialog).OfType<AiConnectionPanel>().Single()),
                        "Reopening connection showed an empty or replaced panel.");
                    var scroll = Find<ScrollViewer>(dialog, "ErrorAssistantScroll");
                    var bounds = panel.Service.TransformToAncestor(scroll).TransformBounds(new Rect(0, 0, panel.Service.ActualWidth, panel.Service.ActualHeight));
                    Require(bounds.Top >= 0 && bounds.Bottom <= scroll.ActualHeight && panel.Service.IsKeyboardFocusWithin,
                        "Changing service did not bring the provider into view and focus it.");
                    foreach (var service in AiProviders.All) { panel.Service.SelectedItem = service; dialog.UpdateLayout(); Require(panel.Service.IsVisible, "Provider switch hid the form."); }
                    panel.Service.SelectedItem = AiProviders.Get("gemini");
                    Click(panel, "errorSaveKey"); await Settle(owner);
                    Require(!panel.IsVisible && Find<Button>(dialog, "errorConfigure").IsVisible && description.Text == "Keep this error description" &&
                        Nodes(dialog).OfType<System.Windows.Controls.Image>().Any(view => view.Source is not null && view.IsVisible),
                        "Saving the connection lost input/image state or failed to close the editor.");
                }
                Click(dialog, "errorConfigure"); await Settle(owner);
                if (locale is "ar" or "en") WindowAppearance.SavePreview(dialog, Path.Combine(output, "connection-reopen-" + locale + ".png"));
                dialog.Close();
            }
        }
        finally
        {
            Settings.Current.Language = language; Settings.Current.AiProvider = provider; Settings.Current.GeminiModel = model; Settings.Current.Dark = dark;
            Settings.SetKey("gemini", key); Settings.Save(); owner.ApplyTheme(); owner.Render(); owner.Activate();
        }
        File.WriteAllText(Path.Combine(output, "connection-reopen-result.txt"), "PASS: configured assistant reopens the real connection panel in four languages; provider visible/in viewport/focused; three save/reopen cycles; all twenty provider switches; description and image preserved. No live AI request.");
    }

    internal static async Task<bool> CheckSignOut(MainWindow owner, string account, string otherAccount, HttpClient authHttp, Action releaseRemote, string output)
    {
        var keys = AiProviders.All.ToDictionary(p => p.Id, p => Settings.GetKey(p.Id));
        var models = AiProviders.All.ToDictionary(p => p.Id, p => Settings.GetModel(p.Id));
        Settings.Current.Language = "ar"; Settings.Current.Dark = true; owner.ApplyTheme(); owner.Render();
        Task<bool>? logout = null;
        var assistant = new ErrorAssistantWindow(owner, signOutProfile: (id, ct) => logout = ChatGptAuth.SignOut(id, ct, authHttp));
        var another = new ErrorAssistantWindow(owner);
        var oauthDraft = new AiConnectionDraft { Provider = "openai", OpenAiMode = "chatgpt", ChatGptAccount = account,
            ChatGpt = new() { Model = Settings.Current.ChatGptModel, Models = [new(Settings.Current.ChatGptModel, "Fixture vision", "OpenAI", "chatGptPlan")] } };
        var apiDraft = new AiConnectionDraft { Provider = "anthropic", OpenAiMode = "api", ChatGptAccount = account };
        apiDraft.For("anthropic").Key = "unsaved-api-draft";
        var observer = new AiConnectionPanel(oauthDraft, () => { });
        AiConnectionPanel oauth = null!;
        var api = new AiConnectionPanel(apiDraft, () => { });
        var contents = new StackPanel(); contents.Children.Add(observer); contents.Children.Add(api);
        var host = new Window { Owner = owner, Width = 680, Height = 720, ShowInTaskbar = false, Content = new ScrollViewer { Content = contents } };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pendingHttp = new HttpClient(new Waiting(started));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<string>? inference = null;
        try
        {
            owner.Navigate("translate"); assistant.Show(); another.Show(); host.Show(); await Settle(owner);
            Click(assistant, "chatTools"); await Settle(owner);
            oauth = Nodes(assistant).OfType<AiConnectionPanel>().Single();
            Click(assistant, "errorConfigure"); await Settle(owner);
            Require(ChatGptAuth.UsingPlan && Find<Button>(oauth, "chatGptSignOut").IsEnabled, "Fixture is not connected before sign-out.");
            Find<TextBox>(assistant, "ErrorDescription").Text = "Preserve this text after sign-out";
            inference = ChatGptAuth.Complete(account, Settings.Current.ChatGptModel, "Fixture", null, "image/jpeg", deadline.Token, pendingHttp);
            await started.Task.WaitAsync(deadline.Token);
            Click(oauth, "chatGptSignOut"); await Settle(owner);
            Require(logout is not null && !logout.IsCompleted, "Remote revocation fixture did not remain pending.");
            Require(!ChatGptAuth.Connected(account) && ChatGptAuth.Connected(otherAccount) && Settings.Current.ChatGptAccount.Length == 0 &&
                Settings.Current.ChatGptModel.Length == 0 && Settings.Current.OpenAiAuth == "chatgpt" && !AiCatalog.Configured(),
                "Sign-out was not global/immediate or silently selected an API-key connection.");
            Require(!ChatGptAuth.Profiles.Any(p => p.Id == account) && !Find<ComboBox>(oauth, "ChatGptAccountPicker").Items.OfType<ChatGptProfile>().Any(p => p.Id == account) &&
                !Find<ComboBox>(observer, "ChatGptAccountPicker").Items.OfType<ChatGptProfile>().Any(p => p.Id == account) &&
                oauthDraft.ChatGptAccount.Length == 0 && oauthDraft.ChatGpt.Model.Length == 0,
                "A removed account remained in storage, a picker or a draft.");
            Require(owner.IsVisible && assistant.IsVisible && another.IsVisible && host.IsVisible, "Signing out closed an application window.");
            Require(Nodes(owner).OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "TranslationProviderSettings"), "Translation retained its connected state.");
            Require(oauth.ModelInput.Items.Count == 0 && !Find<Button>(oauth, "chatGptSignOut").IsEnabled &&
                Nodes(assistant).OfType<AiConnectionPanel>().Single().IsVisible && Nodes(another).OfType<AiConnectionPanel>().Single().IsVisible &&
                !Find<Button>(assistant, "errorAnalyze").IsEnabled && !Find<Button>(another, "errorAnalyze").IsEnabled,
                "An open panel retained models or an enabled analysis after global sign-out.");
            Require(Find<TextBox>(assistant, "ErrorDescription").Text == "Preserve this text after sign-out" && api.KeyInput.Password == "unsaved-api-draft",
                "Sign-out changed error input or an unsaved API-key draft.");
            Require(AiProviders.All.All(p => Settings.GetKey(p.Id) == keys[p.Id] && Settings.GetModel(p.Id) == models[p.Id]), "API credentials or model choices changed.");
            bool canceled = false; try { await inference; } catch (OperationCanceledException) { canceled = true; }
            Require(canceled, "In-flight account inference survived sign-out.");
            Click(oauth, "errorSaveKey"); Require(Settings.Current.ChatGptAccount.Length == 0 && !AiCatalog.Configured(), "A stale model was saved after sign-out.");
            releaseRemote(); bool revoked = await logout!; await Settle(owner);
            Require(revoked && api.KeyInput.Password == "unsaved-api-draft", "Remote revocation failed or touched the independent API draft.");
            var notice = Application.Current.Windows.OfType<Window>().Single(w => w.IsVisible && AutomationProperties.GetAutomationId(w) == "ChatGptSignOutNotice");
            Require(WindowAppearance.HasNativeCaption(notice, requireMinMax: false), "Sign-out notice lost the native caption.");
            await notice.Dispatcher.InvokeAsync(() => notice.UpdateLayout(), System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(200);
            WindowAppearance.SavePreview(notice, Path.Combine(output, "chatgpt-signout-close-ar.png"));
            Click(notice, "CloseAssistantAfterSignOut"); await Settle(owner);
            Require(!assistant.IsVisible && owner.IsVisible && another.IsVisible && host.IsVisible, "Close confirmation did not close only the error assistant.");
            WindowAppearance.SavePreview(another, Path.Combine(output, "chatgpt-global-signout.png"));
            File.WriteAllText(Path.Combine(output, "chatgpt-signout-result.txt"), "PASS: actual assistant Sign out button removes account identity before delayed remote revocation; all pickers and drafts clear the profile; two assistants and translation update; inference canceled; close notice closes only the assistant; main app stays open; API keys/models and unsaved API draft preserved; unrelated registration stays connected. Fake OAuth only.");
            return revoked;
        }
        finally
        {
            releaseRemote(); deadline.Cancel();
            if (logout is not null) await logout;
            host.Close(); assistant.Close(); another.Close(); owner.Activate();
        }
    }

    internal static async Task CheckRemovedProfileCleanup(MainWindow owner, string output)
    {
        string language = Settings.Current.Language; bool dark = Settings.Current.Dark;
        var keys = AiProviders.All.ToDictionary(p => p.Id, p => Settings.GetKey(p.Id));
        byte[] bytes = Settings.ReadProtected("chatgpt-accounts.bin");
        var store = JsonNode.Parse(bytes)!.AsObject(); Array.Clear(bytes);
        string hostId = store["HostId"]!.GetValue<string>();
        store["Accounts"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { ClientId = "legacy-signed-out", Email = "legacy@example.invalid", Name = "Legacy label", AccessToken = "", RefreshToken = "", IdToken = "" }));
        store["Accounts"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { ClientId = "identity-only-fixture", Email = "identity@example.invalid", Name = "Identity permission", AccessToken = "identity-token-not-live", RefreshToken = "", IdToken = "identity-id-not-live", Scopes = new[] { "openid" } }));
        Settings.WriteProtected("chatgpt-accounts.bin", JsonSerializer.SerializeToUtf8Bytes(store));
        Settings.Current.ChatGptAccount = "legacy-signed-out"; Settings.Current.ChatGptModel = "stale-model"; Settings.Save();
        await ChatGptAuth.PruneSignedOutProfiles(CancellationToken.None);
        Require(ChatGptAuth.Profiles.Count == 1 && ChatGptAuth.Profiles.Single().Id == "identity-only-fixture" && Settings.Current.ChatGptAccount.Length == 0,
            "Legacy signed-out cleanup missed an old profile or removed a still-signed-in identity.");
        await ChatGptAuth.SignOut("identity-only-fixture", CancellationToken.None);
        Settings.Load(); await ChatGptAuth.PruneSignedOutProfiles(CancellationToken.None);
        bytes = Settings.ReadProtected("chatgpt-accounts.bin");
        using (var document = JsonDocument.Parse(bytes))
        {
            Require(document.RootElement.GetProperty("Accounts").GetArrayLength() == 0 && document.RootElement.GetProperty("HostId").GetString() == hostId,
                "Removed profile returned after storage reload or installation host changed.");
        }
        Array.Clear(bytes);
        try
        {
            foreach (string locale in new[] { "ar", "en", "fr", "es" })
            {
                Settings.Current.Language = locale; Settings.Current.Dark = locale == "ar"; owner.ApplyTheme(); owner.Render();
                var reopened = new ErrorAssistantWindow(owner); reopened.Show(); await Settle(owner);
                var picker = Find<ComboBox>(reopened, "ChatGptAccountPicker");
                Require(picker.Items.Count == 0 && !picker.IsVisible && !Find<Button>(reopened, "chatGptSignOut").IsVisible &&
                    Find<Button>(reopened, "chatGptContinue").IsVisible, "Reopening the assistant displayed the removed account.");
                var notice = ChatGptUi.CreateSignOutDialog(reopened, revoked: false, reopened.Close); notice.Show(); await Settle(owner);
                Require(Nodes(notice).OfType<TextBlock>().Any(t => t.Text == I18n.T("chatGptCloseAssistantHelp")) &&
                    Nodes(notice).OfType<TextBlock>().Any(t => t.Text == I18n.T("chatGptRevocationUnconfirmed")), "Localized close notice or unconfirmed-revocation explanation missing.");
                if (locale == "en") WindowAppearance.SavePreview(notice, Path.Combine(output, "chatgpt-signout-close-en.png"));
                notice.Close();
                if (locale is "ar" or "en") WindowAppearance.SavePreview(reopened, Path.Combine(output, "chatgpt-removed-" + locale + ".png"));
                reopened.Close();
            }
            Require(AiProviders.All.All(p => Settings.GetKey(p.Id) == keys[p.Id]), "Profile removal touched API credentials.");
        }
        finally { Settings.Current.Language = language; Settings.Current.Dark = dark; owner.ApplyTheme(); owner.Render(); owner.Activate(); }
        File.WriteAllText(Path.Combine(output, "chatgpt-profile-removal-result.txt"), "PASS: legacy signed-out identities pruned; active identity-only consent profile preserved until explicit logout; final profile removed; no account/email/picker/logout button after reload and reopening in four languages; native close notices and revocation-failure explanation; API keys and opaque installation host preserved. No live account.");
    }
}
