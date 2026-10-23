using System.Net;
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
        var assistant = new ErrorAssistantWindow(owner); var another = new ErrorAssistantWindow(owner);
        var oauthDraft = new AiConnectionDraft { Provider = "openai", OpenAiMode = "chatgpt", ChatGptAccount = account,
            ChatGpt = new() { Model = Settings.Current.ChatGptModel, Models = [new(Settings.Current.ChatGptModel, "Fixture vision", "OpenAI", "chatGptPlan")] } };
        var apiDraft = new AiConnectionDraft { Provider = "anthropic", OpenAiMode = "api", ChatGptAccount = account };
        apiDraft.For("anthropic").Key = "unsaved-api-draft";
        Task<bool>? logout = null;
        var oauth = new AiConnectionPanel(oauthDraft, () => { }, signOutProfile: (id, ct) => logout = ChatGptAuth.SignOut(id, ct, authHttp));
        var api = new AiConnectionPanel(apiDraft, () => { });
        var contents = new StackPanel(); contents.Children.Add(oauth); contents.Children.Add(api);
        var host = new Window { Owner = owner, Width = 680, Height = 720, ShowInTaskbar = false, Content = new ScrollViewer { Content = contents } };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pendingHttp = new HttpClient(new Waiting(started));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<string>? inference = null;
        try
        {
            owner.Navigate("translate"); assistant.Show(); another.Show(); host.Show(); await Settle(owner);
            Require(ChatGptAuth.UsingPlan && Find<Button>(oauth, "chatGptSignOut").IsEnabled, "Fixture is not connected before sign-out.");
            Find<TextBox>(assistant, "ErrorDescription").Text = "Preserve this text after sign-out";
            inference = ChatGptAuth.Complete(account, Settings.Current.ChatGptModel, "Fixture", null, "image/jpeg", deadline.Token, pendingHttp);
            await started.Task.WaitAsync(deadline.Token);
            Click(oauth, "chatGptSignOut"); await Settle(owner);
            Require(logout is not null && !logout.IsCompleted, "Remote revocation fixture did not remain pending.");
            Require(!ChatGptAuth.Connected(account) && ChatGptAuth.Connected(otherAccount) && Settings.Current.ChatGptAccount.Length == 0 &&
                Settings.Current.ChatGptModel.Length == 0 && Settings.Current.OpenAiAuth == "chatgpt" && !AiCatalog.Configured(),
                "Sign-out was not global/immediate or silently selected an API-key connection.");
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
            Click(oauth, "save"); Require(Settings.Current.ChatGptAccount.Length == 0 && !AiCatalog.Configured(), "A stale model was saved after sign-out.");
            releaseRemote(); bool revoked = await logout!; await Settle(owner);
            Require(revoked && api.KeyInput.Password == "unsaved-api-draft", "Remote revocation failed or touched the independent API draft.");
            WindowAppearance.SavePreview(assistant, Path.Combine(output, "chatgpt-global-signout.png"));
            File.WriteAllText(Path.Combine(output, "chatgpt-signout-result.txt"), "PASS: actual Sign out button disconnects before delayed remote revocation; two open assistants, translation and connection panels update globally; running inference canceled; application stays open; all API keys/models and unsaved API draft preserved; another registration remains connected; stale account model cannot be saved. Fake OAuth only.");
            return revoked;
        }
        finally
        {
            releaseRemote(); deadline.Cancel();
            if (logout is not null) await logout;
            host.Close(); assistant.Close(); another.Close(); owner.Activate();
        }
    }
}
