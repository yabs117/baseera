using System.Windows.Automation;
using Baseera.App.Services;

namespace Baseera.App;

internal sealed partial class ErrorAssistantWindow
{
    private readonly TextBlock diagnosticDeliveryStatus = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private Button collectForMessage = null!, reviewForMessage = null!, sendWithoutDiagnostics = null!;
    private bool diagnosticGateActive, diagnosticRequestHasData, previousRequestHadDiagnostics;
    private bool DiagnosticSourceSelected => diagnosticSystem.IsChecked == true || diagnosticApplication.IsChecked == true || diagnosticEvents.IsChecked == true || diagnosticHealth.IsChecked == true || diagnosticServices.IsChecked == true;
    private bool HasDiagnosticPreview => !string.IsNullOrWhiteSpace(diagnosticPreview.Text);
    private bool DiagnosticsReady => HasDiagnosticPreview && diagnosticShare.IsChecked == true && diagnosticRecipient == DiagnosticRecipient;
    private bool NeedsDiagnosticReview => (DiagnosticSourceSelected || HasDiagnosticPreview || diagnosticShare.IsChecked == true) && !DiagnosticsReady;

    private Border BuildDiagnosticDelivery()
    {
        var body = new StackPanel();
        diagnosticDeliveryStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetAutomationId(diagnosticDeliveryStatus, "DiagnosticDeliveryStatus");
        AutomationProperties.SetLiveSetting(diagnosticDeliveryStatus, AutomationLiveSetting.Polite); body.Children.Add(diagnosticDeliveryStatus);
        var actions = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
        collectForMessage = Action("diagnosticCollectForMessage", async () =>
        {
            if (NetworkBusy || recorder?.Recording == true) return;
            ShowDiagnosticPreview(false); await CollectDiagnostics();
        });
        AutomationProperties.SetAutomationId(collectForMessage, "DiagnosticCollectForMessage"); actions.Children.Add(collectForMessage);
        reviewForMessage = Action("diagnosticReviewForMessage", () => ShowDiagnosticPreview(HasDiagnosticPreview));
        AutomationProperties.SetAutomationId(reviewForMessage, "DiagnosticReviewForMessage"); actions.Children.Add(reviewForMessage);
        sendWithoutDiagnostics = Action("diagnosticSendWithout", async () =>
        {
            if (diagnosticGateActive && NeedsDiagnosticReview) await Analyze(withoutDiagnostics: true);
        });
        AutomationProperties.SetAutomationId(sendWithoutDiagnostics, "DiagnosticSendWithout"); actions.Children.Add(sendWithoutDiagnostics);
        foreach (var button in actions.Children.OfType<Button>()) { button.MinWidth = 0; button.FontSize = 11; button.Padding = new Thickness(9, 4, 9, 4); }
        body.Children.Add(actions);
        var box = new Border { Child = body, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(9, 7, 9, 7) };
        box.SetResourceReference(Border.BorderBrushProperty, "Line"); AutomationProperties.SetAutomationId(box, "DiagnosticDelivery");
        RefreshDiagnosticDelivery(); return box;
    }
    private void ShowDiagnosticPreview(bool focusConsent)
    {
        if (closed) return;
        assistantTools.IsExpanded = true;
        foreach (var expander in FindDiagnosticExpanders(assistantTools)) expander.IsExpanded = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (closed) return;
            if (focusConsent && HasDiagnosticPreview) { diagnosticShare.BringIntoView(); diagnosticShare.Focus(); }
            else { diagnosticHealth.BringIntoView(); diagnosticHealth.Focus(); }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void RefreshDiagnosticDelivery()
    {
        if (collectForMessage is null) return;
        bool busy = NetworkBusy || recorder?.Recording == true;
        bool hasPreview = HasDiagnosticPreview, selected = DiagnosticSourceSelected;
        if (!NeedsDiagnosticReview) diagnosticGateActive = false;
        string key = diagnosticRequestHasData && active is { IsCancellationRequested: false } ? "diagnosticInRequest" :
            DiagnosticsReady ? "diagnosticReadyForMessage" :
            hasPreview ? previousRequestHadDiagnostics ? "diagnosticApprovalConsumed" : "diagnosticPreviewNotAttached" :
            selected ? "diagnosticSourcesNotCollected" : "diagnosticNoneAttached";
        diagnosticDeliveryStatus.Text = string.Format(I18n.T(key), AiProviderBadge.Name(AiCatalog.Provider, ChatGptAuth.Selected));
        diagnosticDeliveryStatus.SetResourceReference(TextBlock.ForegroundProperty, DiagnosticsReady ? "Accent" : "Muted");
        collectForMessage.Visibility = selected && !hasPreview ? Visibility.Visible : Visibility.Collapsed;
        reviewForMessage.Visibility = hasPreview ? Visibility.Visible : Visibility.Collapsed;
        sendWithoutDiagnostics.Visibility = diagnosticGateActive && NeedsDiagnosticReview ? Visibility.Visible : Visibility.Collapsed;
        collectForMessage.IsEnabled = reviewForMessage.IsEnabled = sendWithoutDiagnostics.IsEnabled = !busy;
        diagnosticShare.IsEnabled = !busy && hasPreview;
    }
    private bool PauseForDiagnosticReview()
    {
        if (!NeedsDiagnosticReview) return false;
        diagnosticGateActive = true; RefreshDiagnosticDelivery();
        feedback.Text = ""; // The live attachment status is the single source of truth.
        diagnosticDeliveryStatus.BringIntoView(); return true;
    }
}
