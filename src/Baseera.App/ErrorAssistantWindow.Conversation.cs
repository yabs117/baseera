using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Threading;
using Baseera.App.Services;
using Baseera.Core;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace Baseera.App;

internal sealed record ErrorChatRequest(string Text, ErrorImage? Image, ErrorVideo? Video, string Language,
    string Diagnostics, IReadOnlyList<ConversationExchange> History, string AttachmentContext, string LocalRepairs = "");

internal sealed partial class ErrorAssistantWindow
{
    private readonly ConversationLedger conversation = new();
    private readonly List<AdvicePresenter> turns = [];
    private readonly TextBlock savedConnection = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Border setupBanner = new();
    private readonly TextBlock chatUsage = new(), voiceStatus = new(), videoStatus = new();
    private readonly StackPanel voicePanel = new(), videoPanel = new();
    private readonly Expander assistantTools = new();
    private readonly Border checkpoint = new();
    private readonly StackPanel emptyChat = new() { Margin = new Thickness(24, 65, 24, 24), HorizontalAlignment = HorizontalAlignment.Center };
    private Button recordButton = new(), transcribeButton = new(), voicePlay = new(), voiceDiscard = new(), newChat = new();
    private readonly Func<ErrorChatRequest, CancellationToken, Task<ErrorAdvice>>? analyzeConversation;
    private readonly Func<long, bool>? confirmRequests;
    private readonly Func<IVoiceRecorder> recorderFactory;
    private readonly Func<byte[], string, string, string, CancellationToken, Task<string>> transcribeAudio;
    private readonly Func<bool>? approveAudioUpload;
    private IVoiceRecorder? recorder;
    private byte[]? recording;
    private bool recordingTranscribed;
    private MemoryStream? playbackStream;
    private System.Media.SoundPlayer? playback;
    private readonly Stopwatch recordingClock = new();
    private readonly DispatcherTimer recordingTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? mediaRequest, voiceRequest;
    private ErrorVideo? video;
    private string composerRecipient = "";
    private bool NetworkBusy => active is not null || diagnosticRequest is not null || mediaRequest is not null || voiceRequest is not null || repairRunning;
    private Border BuildComposer()
    {
        var panel = new StackPanel { Margin = new Thickness(22, 12, 22, 12) };
        var caution = new StackPanel(); Add(caution, Text(I18n.T("chatCheckpointHelp"), 12), 8);
        var choices = new WrapPanel(); choices.Children.Add(Action("chatContinue", () => { conversation.Continue(); RefreshSmartControls(); }));
        choices.Children.Add(Action("chatPause", () => { feedback.Text = I18n.T("chatPaused"); problem.Focus(); })); Add(caution, choices, 0);
        checkpoint.Child = caution; checkpoint.Padding = new Thickness(12); checkpoint.CornerRadius = new CornerRadius(5);
        checkpoint.BorderBrush = Brush("Accent"); checkpoint.BorderThickness = new Thickness(1); checkpoint.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(checkpoint, "ChatCheckpoint"); Add(panel, checkpoint, 8);

        var top = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        savedConnection.Foreground = Brush("Muted"); top.Children.Add(savedConnection);
        var setup = new StackPanel(); Add(setup, Text(I18n.T("aiSetupRequired"), 12), 6);
        setup.Children.Add(Action("aiOpenSettings", () => ShowConnectionSettings()));
        setupBanner.Child = setup; setupBanner.Padding = new Thickness(12); setupBanner.BorderBrush = Brush("Line"); setupBanner.BorderThickness = new Thickness(1); setupBanner.CornerRadius = new CornerRadius(5);
        System.Windows.Automation.AutomationProperties.SetAutomationId(setupBanner, "AssistantSetupNotice"); Add(panel, setupBanner, 8);
        var utilities = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var attach = Action("chatAttach", () => { }); attach.MinWidth = 0;
        var menu = new ContextMenu { Background = Brush("Surface"), Foreground = Brush("Ink"), BorderBrush = Brush("Line") };
        void Option(string key, Action action) { var item = new MenuItem { Header = I18n.T(key), Foreground = Brush("Ink") }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Option("errorChooseImage", ChooseImage); Option("chatChooseVideo", async () => await ChooseVideo()); Option("errorPaste", PasteClipboard);
        Option("errorCaptureWindow", OpenTools); Option("smartDiagnostics", OpenTools);
        attach.ContextMenu = menu; attach.Click += (_, _) => { menu.PlacementTarget = attach; menu.IsOpen = true; }; utilities.Children.Add(attach);
        recordButton = Action("chatRecord", ToggleRecording); recordButton.MinWidth = 0; utilities.Children.Add(recordButton);
        var tools = Action("chatTools", OpenTools); tools.MinWidth = 0; utilities.Children.Add(tools);
        newChat = Action("chatNew", () => ClearConversation(true)); newChat.MinWidth = 0; utilities.Children.Add(newChat);
        var more = Action("chatMore", () => { }); more.MinWidth = 0; utilities.Children.Add(more);
        var moreMenu = new ContextMenu { Background = Brush("Surface"), Foreground = Brush("Ink") };
        void More(string key, Action action) { var item = new MenuItem { Header = I18n.T(key), Foreground = Brush("Ink") }; item.Click += (_, _) => action(); moreMenu.Items.Add(item); }
        More("chatDictation", StartDictation); More("chatExport", ExportConversation); More("settings", () => ShowConnectionSettings()); More("errorClose", Close);
        more.ContextMenu = moreMenu; more.Click += (_, _) => { moreMenu.PlacementTarget = more; moreMenu.IsOpen = true; };
        Add(panel, top, 4);

        imageLabel.Foreground = Brush("Muted"); imageLabel.FontSize = 11; imageLabel.Text = I18n.T("errorNoImage");
        Add(panel, imageLabel, 2); preview.MaxHeight = 80; preview.MaxWidth = 240; preview.Visibility = Visibility.Collapsed; Add(panel, preview, 3);
        var removeImage = Action("errorRemoveImage", () => SetImage(null)); removeImage.MinWidth = 0; utilities.Children.Add(removeImage);
        removeImage.SetBinding(VisibilityProperty, new System.Windows.Data.Binding("Visibility") { Source = preview });
        videoStatus.FontSize = 11; videoStatus.Foreground = Brush("Muted"); videoStatus.TextWrapping = TextWrapping.Wrap;
        Add(videoPanel, videoStatus, 4); videoPanel.Children.Add(Action("chatRemoveVideo", () => { video = null; UpdateMediaStatus(); RefreshSmartControls(); }));
        videoPanel.Visibility = Visibility.Collapsed; Add(panel, videoPanel, 4);
        voiceStatus.FontSize = 11; voiceStatus.Foreground = Brush("Muted"); voiceStatus.TextWrapping = TextWrapping.Wrap; Add(voicePanel, voiceStatus, 4);
        var voiceButtons = new WrapPanel();
        transcribeButton = Action("chatTranscribe", async () => await TranscribeRecording()); voiceButtons.Children.Add(transcribeButton);
        voicePlay = Action("chatPlayRecording", PlayRecording); voiceButtons.Children.Add(voicePlay);
        voiceDiscard = Action("chatDiscardRecording", DiscardRecording); voiceButtons.Children.Add(voiceDiscard);
        Add(voicePanel, voiceButtons, 0); voicePanel.Visibility = Visibility.Collapsed; Add(panel, voicePanel, 4);

        problem.Height = 76; problem.MinHeight = 54; problem.Tag = I18n.T("chatPlaceholder"); problem.Margin = new Thickness(0, 0, 0, 8);
        problem.FlowDirection = Settings.Current.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        AutomationProperties.SetAutomationId(problem, "ErrorDescription"); panel.Children.Add(problem);
        problem.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
            { e.Handled = true; await Analyze(); }
        };
        analyzeButton = Action("errorAnalyze", async () => await Analyze(), true); analyzeButton.MinWidth = 40; analyzeButton.ToolTip = I18n.T("chatSend");
        analyzeButton.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse("M7,13 L7,2 M2,7 L7,2 L12,7"), Stroke = Brush("OnAccent"), StrokeThickness = 1.8, Width = 14, Height = 14, Stretch = Stretch.Uniform };
        AutomationProperties.SetName(analyzeButton, I18n.T("chatSend")); utilities.Children.Add(analyzeButton);
        cancelButton = Action("cancel", () => { active?.Cancel(); diagnosticRequest?.Cancel(); mediaRequest?.Cancel(); voiceRequest?.Cancel(); }); cancelButton.MinWidth = 0; utilities.Children.Add(cancelButton);
        Add(panel, utilities, 0);
        chatUsage.FontSize = 10; chatUsage.Foreground = Brush("Muted"); chatUsage.TextWrapping = TextWrapping.Wrap; Add(panel, chatUsage, 3);
        feedback.FontSize = 11; feedback.Foreground = Brush("Muted"); feedback.MaxHeight = 35; feedback.TextTrimming = TextTrimming.CharacterEllipsis;
        feedback.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("Text") { Source = feedback }); Add(panel, feedback, 0);
        var privacy = Text(I18n.T("chatContextPrivacy"), 10, true); privacy.ToolTip = I18n.T("chatContextPrivacy"); privacy.MaxHeight = 28; privacy.TextTrimming = TextTrimming.CharacterEllipsis; Add(panel, privacy, 0);
        RefreshSavedConnection(true);
        recordingTimer.Tick += (_, _) =>
        {
            if (recorder?.Recording != true) return;
            voiceStatus.Text = string.Format(I18n.T("chatRecording"), (int)recordingClock.Elapsed.TotalSeconds);
            if (recordingClock.Elapsed.TotalSeconds >= VoiceRecording.MaxSeconds) ToggleRecording();
        };
        return new Border { Child = new ScrollViewer { Content = panel, MaxHeight = Math.Max(280, Height * .5), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 0) };
    }
    private void OpenTools() { assistantTools.IsExpanded = true; assistantTools.BringIntoView(); }
    internal void FocusComposer() => problem.Focus();
    internal void RefreshSavedConnection(bool acknowledged = false)
    {
        if (closed) return;
        if (acknowledged || composerRecipient.Length == 0) composerRecipient = DiagnosticRecipient;
        savedConnection.Text = Configured() ? AiProviderBadge.Name(AiCatalog.Provider, ChatGptAuth.Selected) + " · " + AiCatalog.Model : I18n.T("aiConnectionNotConfigured");
        UpdateMediaStatus();
        if (recording is not null && recorder?.Recording != true)
            voiceStatus.Text = string.Format(I18n.T("chatRecordingReady"), recordingClock.Elapsed.TotalSeconds.ToString("0.0")) + "\n" + I18n.T(VoiceTranscription.Supported(AiCatalog.Provider, ChatGptAuth.Selected) ? "chatAudioPrivacy" : "chatAudioUnsupported");
        RefreshSmartControls();
    }
    private bool AllowRequest()
    {
        if (!conversation.CanUse(DiagnosticRecipient))
        {
            if (MessageBox.Show(this, I18n.T("chatRecipientChange"), I18n.T("chatNew"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return false;
            ClearConversation(false, preserveDraft: true);
        }
        conversation.Bind(DiagnosticRecipient);
        if (!conversation.NeedsConfirmation) return true;
        bool allow = confirmRequests?.Invoke(conversation.Requests) ?? MessageBox.Show(this, I18n.T("chatCheckpointHelp"), I18n.T("chatUsageTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (allow) conversation.Continue(); RefreshSmartControls(); return allow;
    }
    private void ClearConversation(bool confirm, bool preserveDraft = false)
    {
        if (NetworkBusy || recorder?.Recording == true) return;
        if (confirm && turns.Count > 0 && MessageBox.Show(this, I18n.T("chatClearConfirm"), I18n.T("chatNew"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        foreach (var turn in turns) turn.Dispose(); turns.Clear(); result.Children.Clear(); presentation = null; conversation.Clear();
        repairActions.Children.Clear(); repairSession.Clear(); repairStatus.Text = ""; RenderSession(); ClearDiagnostics();
        if (!preserveDraft) { problem.Clear(); SetImage(null); video = null; DiscardRecording(); }
        RefreshSmartControls(); problem.Focus();
    }
    private void RefreshConversationControls(bool idle)
    {
        setupBanner.Visibility = realAnalyzer && !Configured() ? Visibility.Visible : Visibility.Collapsed;
        cancelButton.Visibility = cancelButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        emptyChat.Visibility = turns.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        newChat.IsEnabled = idle; recordButton.IsEnabled = active is null && diagnosticRequest is null && mediaRequest is null && voiceRequest is null && !repairRunning;
        recordButton.Content = Text(I18n.T(recorder?.Recording == true ? "chatStopRecording" : "chatRecord"), 13);
        transcribeButton.IsEnabled = idle && recording is not null && VoiceTranscription.Supported(AiCatalog.Provider, ChatGptAuth.Selected);
        voicePlay.IsEnabled = idle && recording is not null; voiceDiscard.IsEnabled = voiceRequest is null;
        checkpoint.Visibility = conversation.NeedsConfirmation ? Visibility.Visible : Visibility.Collapsed;
        chatUsage.Text = string.Format(I18n.T("chatUsage"), conversation.Requests, conversation.Context().Count, conversation.Completed > ConversationLedger.RetainedExchanges ? I18n.T("chatOlderOmitted") : "");
    }
    private void UpdateMediaStatus()
    {
        videoPanel.Visibility = video is null ? Visibility.Collapsed : Visibility.Visible;
        if (video is not null) videoStatus.Text = video.Name + " · " + video.Seconds.ToString("0.0") + "s\n" + I18n.T(AiCatalog.Provider == "gemini" ? "chatVideoNative" : "chatVideoFrames");
    }
    private async Task ChooseVideo()
    {
        if (NetworkBusy || recorder?.Recording == true) return;
        var dialog = new OpenFileDialog { Filter = "Video (*.mp4;*.webm;*.wmv;*.avi;*.mov)|*.mp4;*.webm;*.wmv;*.avi;*.mov", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await LoadVideo(dialog.FileName);
    }
    private async Task LoadVideo(string path)
    {
        if (mediaRequest is not null) return;
        using var request = new CancellationTokenSource(); mediaRequest = request; RefreshSmartControls(); feedback.Text = I18n.T("chatVideoPreparing");
        try { var prepared = await ConversationMedia.Video(path, request.Token); if (!closed) { SetImage(null); video = prepared; UpdateMediaStatus(); feedback.Text = I18n.T("chatVideoReady"); } }
        catch (OperationCanceledException) { if (!closed) feedback.Text = I18n.T("cancel"); }
        catch (Exception ex) { if (!closed) feedback.Text = I18n.T("chatVideoUnsupported") + " " + ex.GetType().Name; }
        finally { mediaRequest = null; if (!closed) RefreshSmartControls(); }
    }
    private void ToggleRecording()
    {
        if (active is not null || diagnosticRequest is not null || mediaRequest is not null || voiceRequest is not null || repairRunning) return;
        try
        {
            if (recorder?.Recording == true)
            {
                recording = recorder.Stop(); recorder.Dispose(); recorder = null; recordingTimer.Stop(); recordingClock.Stop();
                voiceStatus.Text = string.Format(I18n.T("chatRecordingReady"), recordingClock.Elapsed.TotalSeconds.ToString("0.0")) + "\n" + I18n.T(VoiceTranscription.Supported(AiCatalog.Provider, ChatGptAuth.Selected) ? "chatAudioPrivacy" : "chatAudioUnsupported");
            }
            else
            {
                DiscardRecording(); recorder = recorderFactory(); recorder.Start(); recordingClock.Restart(); recordingTimer.Start();
                voicePanel.Visibility = Visibility.Visible; voiceStatus.Text = string.Format(I18n.T("chatRecording"), 0);
            }
        }
        catch (Exception ex) { recorder?.Dispose(); recorder = null; recordingTimer.Stop(); feedback.Text = I18n.T("chatMicFailed") + " " + ex.GetType().Name; }
        RefreshSmartControls();
    }
    private void DiscardRecording()
    {
        voiceRequest?.Cancel(); recorder?.Dispose(); recorder = null; recordingTimer.Stop(); recordingClock.Reset();
        playback?.Stop(); playback?.Dispose(); playback = null; playbackStream?.Dispose(); playbackStream = null; recording = null; recordingTranscribed = false; voicePanel.Visibility = Visibility.Collapsed;
    }
    private void PlayRecording()
    {
        if (recording is null || recorder?.Recording == true) return;
        try { playback?.Dispose(); playbackStream?.Dispose(); playbackStream = new MemoryStream(recording, false); playback = new System.Media.SoundPlayer(playbackStream); playback.Play(); }
        catch (Exception ex) { feedback.Text = I18n.T("error") + " " + ex.GetType().Name; }
    }
    private async Task TranscribeRecording()
    {
        if (NetworkBusy || recorder?.Recording == true || recording is null || !VoiceTranscription.Supported(AiCatalog.Provider, ChatGptAuth.Selected)) return;
        bool consent = approveAudioUpload?.Invoke() ?? MessageBox.Show(this, I18n.T("chatAudioPrivacy") + "\n" + AiProviderBadge.Name(AiCatalog.Provider, false) + " · " + VoiceTranscription.Model(AiCatalog.Provider, AiCatalog.Model), I18n.T("chatTranscribe"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        if (!consent || !AllowRequest()) return;
        using var request = new CancellationTokenSource(); voiceRequest = request;
        string recipient = DiagnosticRecipient, provider = AiCatalog.Provider, model = AiCatalog.Model;
        byte[] audio = recording; RefreshSmartControls();
        try
        {
            string key = Settings.GetKey(provider); if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(I18n.T("needsKey"));
            conversation.BeginRequest(); feedback.Text = I18n.T("chatTranscribing");
            string transcript = await transcribeAudio(audio, provider, key, model, request.Token);
            if (closed || request.IsCancellationRequested || recipient != DiagnosticRecipient) return;
            string merged = (problem.Text + "\n" + transcript).Trim(); if (merged.Length > 12000) throw new InvalidDataException(I18n.T("errorTextSize"));
            problem.Text = merged; recordingTranscribed = true; feedback.Text = I18n.T("chatTranscriptReady");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!closed) feedback.Text = I18n.T("chatAudioFailed") + " " + ex.Message; }
        finally { voiceRequest = null; if (!closed) RefreshSmartControls(); }
    }
    private void StartDictation()
    {
        if (recorder?.Recording == true) return;
        problem.Focus(); feedback.Text = I18n.T("chatDictationHelp");
        keybd_event(0x5B, 0, 0, UIntPtr.Zero); keybd_event(0x48, 0, 0, UIntPtr.Zero);
        keybd_event(0x48, 0, 2, UIntPtr.Zero); keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    private void ExportConversation()
    {
        if (conversation.Exchanges.Count == 0) { feedback.Text = I18n.T("chatNothingToExport"); return; }
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "Baseera-conversation.json", DefaultExt = ".json", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string Clean(string text) => DiagnosticPrivacy.Sanitize(text, Environment.UserName, Environment.MachineName);
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(new { format = "baseera-conversation-v1", requests = conversation.Requests,
                notice = "Recent 20 text exchanges only. No raw diagnostic logs, recordings, video or image bytes, stored credentials or account identifiers. Redaction is best effort; review before sharing.",
                exchanges = conversation.Exchanges.Select(x => new { user = Clean(x.User), assistant = Clean(x.Assistant), attachment = Clean(x.Attachment), x.Time }) }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            feedback.Text = I18n.T("smartReportSaved");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { feedback.Text = I18n.T("error") + " " + ex.GetType().Name; }
    }
    private void CloseConversation()
    {
        foreach (var turn in turns) turn.Dispose(); conversation.Clear(); mediaRequest?.Cancel(); voiceRequest?.Cancel();
        DiscardRecording(); video = null;
    }
    private void RestoreChatDraft(string text, ErrorImage? picture, ErrorVideo? clip)
    {
        if (problem.Text.Length > 0 || image is not null || video is not null) return;
        problem.Text = text;
        if (clip is not null) { video = clip; UpdateMediaStatus(); }
        else SetImage(picture);
    }
    private static string AdviceHistory(ErrorAdvice advice) => advice.Message ?? string.Join("\n", new[] { advice.Summary, advice.Cause }.Concat(advice.Steps).Concat(advice.Checks).Append(advice.Confidence));
}
