using Baseera.App.Services;
using System.Security.Cryptography;
using System.Text;

namespace Baseera.App;

internal sealed class AiConnectionDraft
{
    internal string Provider = AiCatalog.Provider;
    internal string OpenAiMode = Settings.Current.OpenAiAuth;
    internal string ChatGptAccount = Settings.Current.ChatGptAccount;
    internal AiServiceDraft ChatGpt = new() { Model = Settings.Current.ChatGptModel };
    internal AiServiceDraft Current => Provider == "openai" && OpenAiMode == "chatgpt" ? ChatGpt : For(Provider);
    internal readonly Dictionary<string, AiServiceDraft> Services = [];
    internal AiServiceDraft For(string provider)
    {
        if (!Services.TryGetValue(provider, out var value)) Services[provider] = value = new() { Model = Settings.GetModel(provider) };
        return value;
    }
}
internal sealed class AiServiceDraft
{
    internal string Key = "", Model = "", Fingerprint = "";
    internal IReadOnlyList<AiModelOption> Models = [];
}

// Build once. Provider changes replace data, never reparent live WPF elements.
internal sealed partial class AiConnectionPanel : StackPanel
{
    internal readonly ComboBox Service = new() { FlowDirection = FlowDirection.LeftToRight, MaxDropDownHeight = 320 };
    internal readonly PasswordBox KeyInput = new() { FlowDirection = FlowDirection.LeftToRight };
    internal readonly ComboBox ModelInput = new() { FlowDirection = FlowDirection.LeftToRight, MaxDropDownHeight = 300 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap }, savedKey = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button load, save, forget;
    private readonly TextBlock keyLabel;
    private readonly AiConnectionDraft draft;
    private readonly Action saved;
    private readonly Func<string, string, CancellationToken, Task<IReadOnlyList<AiModelOption>>> list;
    private CancellationTokenSource? loading;
    private int revision;
    private bool updating;

    internal AiConnectionPanel(AiConnectionDraft draft, Action saved, bool dialog = false,
        Func<string, string, CancellationToken, Task<IReadOnlyList<AiModelOption>>>? list = null)
    {
        this.draft = draft; this.saved = saved;
        this.list = list ?? ((provider, key, ct) => AiCatalog.ListVisionModels(provider, key, ct));
        if (!AiProviders.Contains(draft.Provider)) draft.Provider = "gemini";
        AddLabel("aiService"); Service.ItemsSource = AiProviders.All;
        Service.SelectedItem = AiProviders.Get(draft.Provider); AddControl(Service);
        Id(Service, dialog ? "ErrorAssistantService" : "AiService");
        BuildSignInArea();
        var note = new TextBlock { Text = I18n.T("aiDirectServices"), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        note.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); AddControl(note);
        keyLabel = AddLabel("aiKey"); AddControl(KeyInput);
        Id(KeyInput, dialog ? "ErrorAssistantApiKey" : "AiApiKey");
        System.Windows.Automation.AutomationProperties.SetName(KeyInput, I18n.T("aiKey"));
        savedKey.Text = I18n.T("keySaved"); savedKey.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); AddControl(savedKey);
        forget = Button("forgetKey", Forget); AddControl(forget);
        load = Button("aiLoadModels", async () => await LoadModels()); AddControl(load);
        var modelsNote = new TextBlock { Text = I18n.T("aiModelsNote"), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        modelsNote.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); AddControl(modelsNote);
        AddLabel("model"); AddControl(ModelInput);
        Id(ModelInput, dialog ? "ErrorAssistantModel" : "AiModelPicker");
        System.Windows.Automation.AutomationProperties.SetName(ModelInput, I18n.T("model"));
        save = Button(dialog ? "errorSaveKey" : "save", Save); save.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Accent");
        save.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "Accent");
        ((TextBlock)save.Content).SetResourceReference(TextBlock.ForegroundProperty, "OnAccent"); AddControl(save);
        status.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); AddControl(status); Id(status, "AiConnectionStatus");
        Service.SelectionChanged += (_, _) =>
        {
            if (updating || Service.SelectedItem is not AiProvider selected || selected.Id == draft.Provider) return;
            CancelLoad(); CancelSignIn(); draft.Provider = selected.Id; RefreshProvider();
        };
        KeyInput.PasswordChanged += (_, _) =>
        {
            if (updating) return;
            CancelLoad();
            var state = draft.For(draft.Provider); state.Key = KeyInput.Password; state.Models = []; state.Fingerprint = "";
            updating = true; ModelInput.ItemsSource = Array.Empty<AiModelOption>(); updating = false;
            status.Text = "";
        };
        ModelInput.SelectionChanged += (_, _) =>
        {
            if (!updating) draft.Current.Model = (ModelInput.SelectedItem as AiModelOption)?.Id ?? "";
        };
        Unloaded += (_, _) => { CancelLoad(); CancelSignIn(); };
        RefreshProvider();
    }
    private static void Id(DependencyObject element, string id) => System.Windows.Automation.AutomationProperties.SetAutomationId(element, id);
    private TextBlock AddLabel(string key) { var label = new TextBlock { Text = I18n.T(key), TextWrapping = TextWrapping.Wrap }; AddControl(label); return label; }
    private void AddControl(FrameworkElement control) { control.Margin = new Thickness(0, 0, 0, 10); Children.Add(control); }
    private static Button Button(string key, Action click)
    {
        var label = new TextBlock { Text = I18n.T(key) }; label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 105 };
        Id(button, key); System.Windows.Automation.AutomationProperties.SetName(button, I18n.T(key));
        button.Click += (_, _) => click(); return button;
    }
    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim())));
    private void RefreshProvider()
    {
        updating = true;
        try
        {
            var state = draft.Current;
            KeyInput.Password = state.Key;
            var available = state.Models.ToList();
            string storedModel = UsingChatGpt ? Settings.Current.ChatGptModel : Settings.GetModel(draft.Provider);
            if (state.Models.Count == 0 && storedModel.Length > 0 && storedModel == state.Model)
                available.Add(new(storedModel, storedModel, AiCatalog.Label(draft.Provider), "aiTierVaries"));
            ModelInput.ItemsSource = available;
            ModelInput.SelectedItem = available.FirstOrDefault(option => option.Id == state.Model);
            bool hasKey = File.Exists(Settings.KeyPath(draft.Provider));
            keyLabel.Visibility = KeyInput.Visibility = UsingChatGpt ? Visibility.Collapsed : Visibility.Visible;
            savedKey.Visibility = forget.Visibility = hasKey && !UsingChatGpt ? Visibility.Visible : Visibility.Collapsed;
            RefreshSignInArea();
            status.Text = "";
        }
        finally { updating = false; }
    }
    private void CancelLoad()
    {
        revision++; loading?.Cancel(); loading = null;
        if (load is not null) load.IsEnabled = true;
    }
    internal async Task LoadModels()
    {
        CancelLoad();
        int version = revision; string provider = draft.Provider; bool chatGpt = UsingChatGpt;
        using var cancellation = new CancellationTokenSource(); loading = cancellation;
        try
        {
            string key = chatGpt ? "" : KeyInput.Password.Length > 0 ? KeyInput.Password : Settings.GetKey(provider);
            load.IsEnabled = false; status.Text = I18n.T("aiLoadingModels");
            var models = chatGpt ? await ChatGptAuth.Models(draft.ChatGptAccount, cancellation.Token) : await list(provider, key, cancellation.Token);
            if (version != revision || cancellation.IsCancellationRequested || provider != draft.Provider) return;
            var state = draft.Current; state.Models = models; state.Fingerprint = chatGpt ? ChatGptAuth.Fingerprint(draft.ChatGptAccount) : Fingerprint(key);
            if (!models.Any(option => option.Id == state.Model)) state.Model = "";
            RefreshProvider();
            status.Text = models.Count == 0 ? I18n.T("aiNoVisionModels") : string.Format(I18n.T("aiModelsFound"), models.Count, 1);
        }
        catch (OperationCanceledException) { if (version == revision) status.Text = I18n.T("cancel"); }
        catch (Exception ex) { if (version == revision) status.Text = I18n.T("aiCatalogFailed") + " " + ex.Message; }
        finally { if (version == revision) { loading = null; load.IsEnabled = true; } }
    }
    private void Save()
    {
        try
        {
            if (UsingChatGpt) { SaveChatGpt(); return; }
            var state = draft.For(draft.Provider);
            string key = state.Key.Length > 0 ? state.Key : Settings.GetKey(draft.Provider);
            if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(I18n.T("aiNeedKey"));
            if (ModelInput.SelectedItem is not AiModelOption chosen || !AiCatalog.ValidModel(draft.Provider, chosen.Id))
                throw new InvalidOperationException(I18n.T("aiChooseModel"));
            bool unchanged = state.Key.Length == 0 && chosen.Id == Settings.GetModel(draft.Provider) && File.Exists(Settings.KeyPath(draft.Provider));
            if (!unchanged && (state.Fingerprint != Fingerprint(key) || !state.Models.Any(option => option.Id == chosen.Id)))
                throw new InvalidOperationException(I18n.T("aiRefreshAfterKey"));
            Settings.SetKey(draft.Provider, key); Settings.SetModel(draft.Provider, chosen.Id);
            if (draft.Provider == "openai") Settings.Current.OpenAiAuth = "api";
            Settings.Current.AiProvider = draft.Provider; Settings.Save();
            state.Key = ""; updating = true; KeyInput.Clear(); updating = false;
            RefreshProvider(); status.Text = I18n.T("errorKeySaved"); saved();
        }
        catch (Exception ex) { updating = false; status.Text = I18n.T("errorSaveFailed") + " " + ex.Message; }
    }
    private void Forget()
    {
        try
        {
            CancelLoad(); Settings.SetKey(draft.Provider, "");
            draft.Services[draft.Provider] = new(); RefreshProvider();
        }
        catch (Exception ex) { status.Text = I18n.T("errorSaveFailed") + " " + ex.Message; }
    }
}
