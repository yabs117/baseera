using System.Windows.Automation;
using Binding = System.Windows.Data.Binding;
using ContextMenu = System.Windows.Controls.ContextMenu;
using PlacementMode = System.Windows.Controls.Primitives.PlacementMode;
using CustomPopupPlacement = System.Windows.Controls.Primitives.CustomPopupPlacement;
using PopupPrimaryAxis = System.Windows.Controls.Primitives.PopupPrimaryAxis;
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
        actions.Children.Add(AssistantMenu("chatMore", "more", PlacementMode.Bottom, "settings",
            ("chatDictation", "microphone", StartDictation), ("chatExport", "cases", ExportConversation),
            ("smartWorkspacePrivacyTitle", "security", ShowAssistantPrivacy),
            ("settings", "settings", ShowConnectionSettings), ("errorClose", "close", Close)));

        var identity = new DockPanel { Margin = new Thickness(0, 0, 10, 0) };
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(7), Background = Brush("Tint"),
            Child = Icons.Create("doctor", 17, "AccentInk"), Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(badge, Dock.Left);
        identity.Children.Add(badge);
        var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Text(I18n.T("smartWorkspaceTitle"), 15);
        title.FontWeight = FontWeights.SemiBold;
        copy.Children.Add(title);
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

    private ContextMenu CreateAssistantPopup(Button target, PlacementMode placement)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = target, Placement = PlacementMode.Custom,
            FlowDirection = frame.FlowDirection, FontFamily = FontFamily, FontSize = 13
        };
        menu.CustomPopupPlacementCallback = (popup, anchor, _) =>
        {
            bool above = placement == PlacementMode.Top;
            bool rtl = frame.FlowDirection == FlowDirection.RightToLeft;
            // WPF supplies the physical right edge as the origin for an RTL anchor.
            double x = rtl ? (above ? -popup.Width : -anchor.Width) : (above ? 0 : anchor.Width - popup.Width);
            double top = -popup.Height - 5, bottom = anchor.Height + 5;
            return
            [
                new(new System.Windows.Point(x, above ? top : bottom), PopupPrimaryAxis.Horizontal),
                new(new System.Windows.Point(x, above ? bottom : top), PopupPrimaryAxis.Horizontal)
            ];
        };
        menu.SetResourceReference(StyleProperty, "AssistantPopup");
        AutomationProperties.SetName(menu, AutomationProperties.GetName(target));
        target.ContextMenu = menu;
        return menu;
    }

    private static MenuItem CreateAssistantMenuItem(string label, string? icon = null)
    {
        var title = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
        var item = new MenuItem { Header = title };
        item.SetResourceReference(StyleProperty, "AssistantPopupItem");
        if (icon is not null) item.Icon = Icons.Create(icon, 16, "Muted");
        AutomationProperties.SetName(item, label);
        return item;
    }

    private static void AddAssistantMenuDivider(ContextMenu menu)
    {
        var divider = new Separator();
        divider.SetResourceReference(StyleProperty, "AssistantPopupDivider");
        menu.Items.Add(divider);
    }

    private Button AssistantMenu(string key, string icon, PlacementMode placement, string splitBefore,
        params (string Label, string Icon, Action Select)[] options)
    {
        var button = CompactAction(key, () => { }, icon);
        var menu = CreateAssistantPopup(button, placement);
        foreach (var option in options)
        {
            if (option.Label == splitBefore) AddAssistantMenuDivider(menu);
            var item = CreateAssistantMenuItem(I18n.T(option.Label), option.Icon);
            AutomationProperties.SetAutomationId(item, option.Label);
            item.Click += (_, _) => option.Select();
            menu.Items.Add(item);
        }
        button.Click += (_, _) => menu.IsOpen = true;
        return button;
    }

    private Border BuildComposer()
    {
        var panel = new StackPanel { Margin = new Thickness(24, 6, 24, 9), MaxWidth = AssistantContentWidth };
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

        var setupHint = Text(I18n.T("smartWorkspaceSetup"), 12, true);
        setupBanner.Child = setupHint;
        setupBanner.Padding = new Thickness(8, 3, 8, 0);
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

        var inputArea = new StackPanel();
        problem.Height = 69;
        problem.Tag = I18n.T("smartWorkspacePlaceholder");
        problem.Padding = new Thickness(7, 3, 7, 4);
        problem.Background = Brushes.Transparent;
        problem.BorderThickness = new Thickness(0);
        problem.ToolTip = I18n.T("chatPlaceholder");
        problem.FlowDirection = frame.FlowDirection;
        AutomationProperties.SetAutomationId(problem, "ErrorDescription");
        AutomationProperties.SetName(problem, I18n.T("smartWorkspacePlaceholder"));
        AutomationProperties.SetHelpText(problem, I18n.T("chatPlaceholder"));
        Add(inputArea, problem, 4);
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
        deliveryActions.Children.Add(BuildModelButton());
        recordButton = CompactAction("chatRecord", ToggleRecording, "microphone");
        deliveryActions.Children.Add(recordButton);
        analyzeButton = CompactAction("chatSend", async () => await Analyze(), "send");
        analyzeButton.SetResourceReference(StyleProperty, "ComposerSendButton");
        analyzeButton.Content = Icons.Create("send", 16, "OnAccent");
        analyzeButton.SetResourceReference(BackgroundProperty, "Accent");
        analyzeButton.SetResourceReference(BorderBrushProperty, "Accent");
        analyzeButton.Margin = new Thickness(0);
        AutomationProperties.SetAutomationId(analyzeButton, "errorAnalyze");
        deliveryActions.Children.Add(analyzeButton);
        var attach = AssistantMenu("chatAttach", "plus", PlacementMode.Top, "smartWorkspaceCapture",
            ("errorChooseImage", "convert", ChooseImage), ("chatChooseVideo", "attachment", async () => await ChooseVideo()),
            ("errorPaste", "activity", PasteClipboard), ("smartWorkspaceCapture", "application", OpenTools),
            ("smartWorkspaceDiagnostics", "health", () => ShowDiagnosticPreview(false)));
        attach.HorizontalAlignment = HorizontalAlignment.Left;
        toolbar.Children.Add(attach);
        inputArea.Children.Add(toolbar);
        var composerStyle = new Style(typeof(Border));
        composerStyle.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("Line")));
        var focused = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
        focused.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("Accent")));
        composerStyle.Triggers.Add(focused);
        Add(panel, new Border
        {
            Child = inputArea, Style = composerStyle, Background = Brush("Surface"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(17), Padding = new Thickness(9)
        }, 6);

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
        var composer = new Border { Child = panel };
        AutomationProperties.SetAutomationId(composer, "AssistantComposer");
        return composer;
    }
}
