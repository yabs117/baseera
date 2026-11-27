using System.Windows.Automation;
using Binding = System.Windows.Data.Binding;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MessageBoxOptions = System.Windows.MessageBoxOptions;
using MenuItem = System.Windows.Controls.MenuItem;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal sealed partial class ErrorAssistantWindow
{
    private const double AssistantContentWidth = 780;

    private static Button CompactAction(string key, Action action, string? icon = null)
    {
        var button = Action(key, action);
        button.MinWidth = button.MinHeight = 34;
        button.Padding = new Thickness(icon is null ? 10 : 7, 5, icon is null ? 10 : 7, 5);
        button.Margin = new Thickness(0, 0, 5, 0);
        if (icon is not null)
        {
            button.Content = Icons.Create(icon, 17);
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
        }
        button.ToolTip = I18n.T(key);
        AutomationProperties.SetName(button, I18n.T(key));
        return button;
    }

    private static void ConfigureDisclosure(Expander disclosure, string label, UIElement body, string id)
    {
        disclosure.SetResourceReference(StyleProperty, "DisclosureSection");
        disclosure.Background = Brush("Surface");
        disclosure.BorderBrush = Brush("Line");
        disclosure.Padding = new Thickness(12, 8, 12, 8);
        disclosure.Header = Text(I18n.T(label), 12);
        disclosure.Content = body;
        AutomationProperties.SetName(disclosure, I18n.T(label));
        AutomationProperties.SetAutomationId(disclosure, id);
    }

    private Border BuildAssistantHeader()
    {
        var row = new DockPanel { Margin = new Thickness(18, 9, 18, 9) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);
        var tools = CompactAction("smartWorkspaceDiagnostics", () =>
        {
            if (assistantTools.IsExpanded) assistantTools.IsExpanded = false;
            else OpenTools();
        });
        AutomationProperties.SetAutomationId(tools, "chatTools");
        AutomationProperties.SetHelpText(tools, I18n.T("chatTools"));
        actions.Children.Add(tools);
        newChat = CompactAction("chatNew", () => ClearConversation(true), "plus");
        actions.Children.Add(newChat);
        actions.Children.Add(AssistantMenu("chatMore", "more", ("chatDictation", StartDictation), ("chatExport", ExportConversation),
            ("smartWorkspacePrivacyTitle", ShowAssistantPrivacy),
            ("settings", () => ShowConnectionSettings()), ("errorClose", Close)));

        var identity = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(7), Background = Brush("Tint"),
            Child = Icons.Create("doctor", 17, "AccentInk"), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(badge, Dock.Left);
        identity.Children.Add(badge);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(I18n.T("smartWorkspaceTitle"), 15);
        title.FontWeight = FontWeights.SemiBold;
        copy.Children.Add(title);
        savedConnection.Foreground = Brush("Muted");
        savedConnection.TextWrapping = TextWrapping.NoWrap;
        savedConnection.TextTrimming = TextTrimming.CharacterEllipsis;
        savedConnection.SetBinding(ToolTipProperty, new Binding(nameof(TextBlock.Text)) { Source = savedConnection });
        copy.Children.Add(savedConnection);
        identity.Children.Add(copy);
        row.Children.Add(identity);
        return new Border { Child = row, Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1) };
    }

    private void ShowAssistantPrivacy()
    {
        string usage = string.Format(I18n.T("chatUsage"), conversation.Requests, conversation.Context().Count,
            conversation.Completed > ConversationLedger.RetainedExchanges ? I18n.T("chatOlderOmitted") : "");
        string message = string.Join("\n\n", I18n.T("chatContextPrivacy"), I18n.T("errorPrivacy"), usage);
        MessageBox.Show(this, message, I18n.T("smartWorkspacePrivacyTitle"), MessageBoxButton.OK, MessageBoxImage.Information,
            MessageBoxResult.OK, frame.FlowDirection == FlowDirection.RightToLeft ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None);
    }

    private Button AssistantMenu(string key, string icon, params (string Label, Action Select)[] options)
    {
        var button = CompactAction(key, () => { }, icon);
        var menu = new ContextMenu { Background = Brush("Surface"), Foreground = Brush("Ink"),
            BorderBrush = Brush("Line"), FlowDirection = frame.FlowDirection };
        foreach (var option in options)
        {
            var item = new MenuItem { Header = I18n.T(option.Label), Foreground = Brush("Ink") };
            item.Click += (_, _) => option.Select();
            menu.Items.Add(item);
        }
        button.ContextMenu = menu;
        button.Click += (_, _) => { menu.PlacementTarget = button; menu.IsOpen = true; };
        return button;
    }

    private Border BuildComposer()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 8, 24, 10), MaxWidth = AssistantContentWidth };
        var context = new StackPanel();
        var contextScroll = new ScrollViewer { Content = context, MaxHeight = 155, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        panel.Children.Add(contextScroll);

        var choices = new WrapPanel();
        choices.Children.Add(CompactAction("chatContinue", () => { conversation.Continue(); RefreshSmartControls(); }));
        choices.Children.Add(CompactAction("chatPause", () => { feedback.Text = I18n.T("chatPaused"); problem.Focus(); }));
        var caution = new StackPanel();
        Add(caution, Text(I18n.T("chatCheckpointHelp"), 12), 6);
        caution.Children.Add(choices);
        checkpoint.Child = caution;
        checkpoint.Padding = new Thickness(8);
        checkpoint.CornerRadius = new CornerRadius(6);
        checkpoint.BorderBrush = Brush("Accent");
        checkpoint.BorderThickness = new Thickness(1);
        checkpoint.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(checkpoint, "ChatCheckpoint");
        Add(context, checkpoint, 6);

        var setup = new DockPanel();
        var settings = CompactAction("aiOpenSettings", () => ShowConnectionSettings());
        DockPanel.SetDock(settings, Dock.Right);
        setup.Children.Add(settings);
        var setupHint = Text(I18n.T("smartWorkspaceSetup"), 12, true);
        setupHint.VerticalAlignment = VerticalAlignment.Center;
        setup.Children.Add(setupHint);
        setupBanner.Child = setup;
        setupBanner.Padding = new Thickness(8);
        setupBanner.BorderBrush = Brush("Line");
        setupBanner.BorderThickness = new Thickness(1);
        setupBanner.CornerRadius = new CornerRadius(6);
        AutomationProperties.SetAutomationId(setupBanner, "AssistantSetupNotice");
        Add(context, setupBanner, 6);
        Add(context, BuildDiagnosticDelivery(), 6);

        var imageAttachment = new DockPanel();
        imageLabel.Foreground = Brush("Muted");
        imageLabel.FontSize = 11;
        imageLabel.VerticalAlignment = VerticalAlignment.Center;
        var removeImage = CompactAction("errorRemoveImage", () => SetImage(null), "close");
        DockPanel.SetDock(removeImage, Dock.Right);
        imageAttachment.Children.Add(removeImage);
        preview.MaxHeight = 64;
        preview.MaxWidth = 110;
        preview.Margin = new Thickness(0, 0, 10, 0);
        preview.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(preview, Dock.Left);
        imageAttachment.Children.Add(preview);
        imageAttachment.Children.Add(imageLabel);
        imageAttachment.SetBinding(VisibilityProperty, new Binding(nameof(Visibility)) { Source = preview });
        Add(context, imageAttachment, 6);

        videoStatus.FontSize = 11; videoStatus.Foreground = Brush("Muted"); videoStatus.TextWrapping = TextWrapping.Wrap;
        Add(videoPanel, videoStatus, 4);
        videoPanel.Children.Add(CompactAction("chatRemoveVideo", () => { video = null; UpdateMediaStatus(); RefreshSmartControls(); }));
        videoPanel.Visibility = Visibility.Collapsed;
        Add(context, videoPanel, 6);
        voiceStatus.FontSize = 11; voiceStatus.Foreground = Brush("Muted"); voiceStatus.TextWrapping = TextWrapping.Wrap;
        Add(voicePanel, voiceStatus, 4);
        var voiceButtons = new WrapPanel();
        transcribeButton = CompactAction("chatTranscribe", async () => await TranscribeRecording()); voiceButtons.Children.Add(transcribeButton);
        voicePlay = CompactAction("chatPlayRecording", PlayRecording); voiceButtons.Children.Add(voicePlay);
        voiceDiscard = CompactAction("chatDiscardRecording", DiscardRecording); voiceButtons.Children.Add(voiceDiscard);
        voicePanel.Children.Add(voiceButtons);
        voicePanel.Visibility = Visibility.Collapsed;
        Add(context, voicePanel, 6);

        problem.Height = 68;
        problem.Tag = I18n.T("smartWorkspacePlaceholder");
        problem.Padding = new Thickness(11, 9, 11, 9);
        problem.ToolTip = I18n.T("chatPlaceholder");
        problem.FlowDirection = frame.FlowDirection;
        AutomationProperties.SetAutomationId(problem, "ErrorDescription");
        AutomationProperties.SetName(problem, I18n.T("smartWorkspacePlaceholder"));
        AutomationProperties.SetHelpText(problem, I18n.T("chatPlaceholder"));
        Add(panel, problem, 5);
        problem.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
            { e.Handled = true; await Analyze(); }
        };

        var toolbar = new DockPanel();
        var deliveryActions = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(deliveryActions, Dock.Right);
        toolbar.Children.Add(deliveryActions);
        cancelButton = CompactAction("cancel", () => { active?.Cancel(); diagnosticRequest?.Cancel(); mediaRequest?.Cancel(); voiceRequest?.Cancel(); });
        deliveryActions.Children.Add(cancelButton);
        analyzeButton = CompactAction("chatSend", async () => await Analyze());
        analyzeButton.Background = Brush("Accent");
        analyzeButton.BorderBrush = analyzeButton.Background;
        var sendLabel = new StackPanel { Orientation = Orientation.Horizontal };
        var sendIcon = Icons.Create("send", 15, "OnAccent");
        sendIcon.Margin = new Thickness(0, 0, 5, 0);
        sendLabel.Children.Add(sendIcon);
        var sendText = Text(I18n.T("chatSend"), 12);
        sendText.Foreground = Brush("OnAccent");
        sendLabel.Children.Add(sendText);
        analyzeButton.Content = sendLabel;
        analyzeButton.Margin = new Thickness(0);
        AutomationProperties.SetAutomationId(analyzeButton, "errorAnalyze");
        deliveryActions.Children.Add(analyzeButton);
        var utilities = new StackPanel { Orientation = Orientation.Horizontal };
        utilities.Children.Add(AssistantMenu("chatAttach", "attachment", ("errorChooseImage", ChooseImage),
            ("chatChooseVideo", async () => await ChooseVideo()), ("errorPaste", PasteClipboard),
            ("smartWorkspaceCapture", OpenTools), ("smartWorkspaceDiagnostics", () => ShowDiagnosticPreview(false))));
        recordButton = CompactAction("chatRecord", ToggleRecording, "microphone");
        utilities.Children.Add(recordButton);
        toolbar.Children.Add(utilities);
        Add(panel, toolbar, 5);

        feedback.FontSize = 11; feedback.Foreground = Brush("Muted"); feedback.MaxHeight = 35; feedback.TextTrimming = TextTrimming.CharacterEllipsis;
        feedback.SetBinding(ToolTipProperty, new Binding(nameof(TextBlock.Text)) { Source = feedback });
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        var feedbackStyle = new Style(typeof(TextBlock));
        var noFeedback = new Trigger { Property = TextBlock.TextProperty, Value = "" };
        noFeedback.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        feedbackStyle.Triggers.Add(noFeedback);
        feedback.Style = feedbackStyle;
        panel.Children.Add(feedback);
        var privacy = Text(I18n.T("smartWorkspacePrivacy"), 11, true);
        privacy.ToolTip = I18n.T("chatContextPrivacy");
        AutomationProperties.SetHelpText(privacy, I18n.T("chatContextPrivacy"));
        panel.Children.Add(privacy);

        RefreshSavedConnection(true);
        recordingTimer.Tick += (_, _) =>
        {
            if (recorder?.Recording != true) return;
            voiceStatus.Text = string.Format(I18n.T("chatRecording"), (int)recordingClock.Elapsed.TotalSeconds);
            if (recordingClock.Elapsed.TotalSeconds >= VoiceRecording.MaxSeconds) ToggleRecording();
        };
        var composer = new Border { Child = panel, Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 0) };
        AutomationProperties.SetAutomationId(composer, "AssistantComposer");
        return composer;
    }
}
