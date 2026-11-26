using System.Windows.Automation;
using Baseera.App.Services;
using ContextMenu = System.Windows.Controls.ContextMenu;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;

namespace Baseera.App;

internal sealed partial class ErrorAssistantWindow
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<AiModelOption>>> listAssistantModels;
    private Button modelButton = null!;
    private ContextMenu modelMenu = null!;
    private CancellationTokenSource? modelRequest;
    private IReadOnlyList<AiModelOption> availableModels = [];
    private string modelRecipient = "", defaultModel = "", selectedModel = "", modelStatus = "";
    private string AssistantModel => selectedModel.Length > 0 ? selectedModel : AiCatalog.Model;

    private bool SyncAssistantModel(string recipient)
    {
        string saved = AiCatalog.Model;
        if (recipient == modelRecipient && saved == defaultModel) return false;
        modelRequest?.Cancel();
        modelRequest = null;
        modelRecipient = recipient;
        defaultModel = saved;
        selectedModel = modelStatus = "";
        availableModels = [];
        InvalidateDiagnosticConsent();
        return true;
    }

    private Button BuildModelButton()
    {
        modelButton = CompactAction("assistantChooseModel", OpenModelMenu);
        modelButton.MaxWidth = 225;
        modelButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        modelButton.Background = Brush("Subtle");
        modelMenu = CreateAssistantPopup(modelButton, PlacementMode.Top);
        modelMenu.Closed += (_, _) => modelRequest?.Cancel();
        modelMenu.Opened += (_, _) => BuildModelOptions();
        AutomationProperties.SetAutomationId(modelButton, "AssistantModel");
        return modelButton;
    }

    private void RefreshModelButton()
    {
        bool configured = Configured();
        string label = configured ? AssistantModel : I18n.T("assistantAddModel");
        var content = new DockPanel { LastChildFill = true };
        var glyph = Icons.Create(configured ? "chevronDown" : "plus", 13, "AccentInk");
        glyph.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(glyph, Dock.Right);
        content.Children.Add(glyph);
        var text = Text(label, 12);
        text.TextWrapping = TextWrapping.NoWrap;
        text.TextTrimming = TextTrimming.CharacterEllipsis;
        text.VerticalAlignment = VerticalAlignment.Center;
        if (configured) text.FlowDirection = FlowDirection.LeftToRight;
        content.Children.Add(text);
        modelButton.Content = content;
        string accessible = configured ? I18n.T("assistantChooseModel") + " · " + label : label;
        modelButton.ToolTip = configured ? AiProviderBadge.Name(AiCatalog.Provider, ChatGptAuth.Selected) + " · " + label : label;
        AutomationProperties.SetName(modelButton, accessible);
    }

    private void OpenModelMenu()
    {
        if (NetworkBusy || recorder?.Recording == true) return;
        if (!Configured()) { ShowConnectionSettings(); return; }
        modelMenu.IsOpen = true;
    }

    private void BuildModelOptions()
    {
        SyncAssistantModel(DiagnosticRecipient);
        RefreshModelButton();
        modelMenu.Items.Clear();
        if (!Configured())
        {
            AddModelSettingsItem();
            return;
        }

        string current = AssistantModel;
        AddModelOption(availableModels.FirstOrDefault(model => model.Id == current) ?? new(current, current, "", ""));
        AddAssistantMenuDivider(modelMenu);
        if (modelStatus.Length > 0)
        {
            var status = CreateAssistantMenuItem(I18n.T(modelStatus));
            status.IsEnabled = false;
            modelMenu.Items.Add(status);
        }
        var load = CreateAssistantMenuItem(I18n.T("aiLoadModels"), "updates");
        load.StaysOpenOnClick = true;
        load.IsEnabled = modelRequest is null;
        AutomationProperties.SetAutomationId(load, "AssistantLoadModels");
        load.Click += async (_, _) => await LoadAssistantModels();
        modelMenu.Items.Add(load);
        AddModelSettingsItem();

        var alternatives = availableModels.Where(model => model.Id != current);
        if (current != defaultModel && !availableModels.Any(model => model.Id == defaultModel))
            alternatives = alternatives.Prepend(new(defaultModel, defaultModel, "", ""));
        var options = alternatives.ToArray();
        if (options.Length == 0) return;
        AddAssistantMenuDivider(modelMenu);
        foreach (var option in options) AddModelOption(option);
    }

    private void AddModelOption(AiModelOption option)
    {
        var item = CreateAssistantMenuItem(option.Name, "cpu");
        if (item.Header is TextBlock label)
        {
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            if (option.Name == option.Id)
            {
                label.FlowDirection = FlowDirection.LeftToRight;
                label.TextAlignment = frame.FlowDirection == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left;
            }
        }
        item.ToolTip = option.Tier.Length > 0 ? option.Id + " · " + I18n.T(option.Tier) : option.Id;
        item.IsCheckable = true;
        item.IsChecked = option.Id == AssistantModel;
        AutomationProperties.SetAutomationId(item, "AssistantModel-" + option.Id);
        string recipient = modelRecipient, baseline = defaultModel;
        item.Click += (_, _) =>
        {
            if (NetworkBusy || recorder?.Recording == true || !Configured() ||
                recipient != DiagnosticRecipient || baseline != AiCatalog.Model || option.Id == AssistantModel) return;
            selectedModel = option.Id;
            InvalidateDiagnosticConsent();
            RefreshModelButton();
        };
        modelMenu.Items.Add(item);
    }

    private void AddModelSettingsItem()
    {
        var settings = CreateAssistantMenuItem(I18n.T("assistantAddModel"), "plus");
        AutomationProperties.SetAutomationId(settings, "AssistantAddModel");
        settings.Click += (_, _) => ShowConnectionSettings();
        modelMenu.Items.Add(settings);
    }

    private static Task<IReadOnlyList<AiModelOption>> LoadAvailableAssistantModels(CancellationToken token) =>
        ChatGptAuth.Selected ? ChatGptAuth.Models(Settings.Current.ChatGptAccount, token) :
            AiCatalog.ListVisionModels(AiCatalog.Provider, Settings.GetKey(AiCatalog.Provider), token);

    private async Task LoadAssistantModels()
    {
        if (modelRequest is not null || !Configured()) return;
        string recipient = modelRecipient, baseline = defaultModel;
        using var request = new CancellationTokenSource();
        modelRequest = request;
        modelStatus = "aiLoadingModels";
        BuildModelOptions();
        try
        {
            var options = await listAssistantModels(request.Token);
            if (request.IsCancellationRequested || closed || recipient != DiagnosticRecipient || baseline != AiCatalog.Model) return;
            availableModels = options.Where(model => AiCatalog.ValidModel(AiCatalog.Provider, model.Id)).DistinctBy(model => model.Id).ToArray();
            modelStatus = availableModels.Count == 0 ? "aiNoVisionModels" : "";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (ReferenceEquals(modelRequest, request)) modelStatus = "";
        }
        catch (Exception)
        {
            if (ReferenceEquals(modelRequest, request)) modelStatus = request.IsCancellationRequested ? "" : "aiCatalogFailed";
        }
        finally
        {
            if (ReferenceEquals(modelRequest, request))
            {
                modelRequest = null;
                if (!closed && modelMenu.IsOpen) BuildModelOptions();
            }
        }
    }
}
