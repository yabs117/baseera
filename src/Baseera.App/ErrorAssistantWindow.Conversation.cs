using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal sealed record ErrorChatRequest(string Text, ErrorImage? Image, ErrorVideo? Video, string Language,
    string Diagnostics, IReadOnlyList<ConversationExchange> History, string AttachmentContext, string LocalRepairs = "");

internal sealed partial class ErrorAssistantWindow
{
    private readonly ConversationLedger conversation = new();
    private readonly List<AdvicePresenter> turns = [];
    private readonly TextBlock savedConnection = new() { FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Border setupBanner = new();
    private readonly TextBlock voiceStatus = new(), videoStatus = new();
    private readonly StackPanel voicePanel = new(), videoPanel = new();
    private readonly Expander assistantTools = new();
    private readonly Border checkpoint = new();
    private readonly StackPanel emptyChat = new() { Margin = new Thickness(20, 54, 20, 24), HorizontalAlignment = HorizontalAlignment.Center };
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
        previousRequestHadDiagnostics = diagnosticRequestHasData = diagnosticGateActive = false;
        foreach (var turn in turns) turn.Dispose(); turns.Clear(); result.Children.Clear(); presentation = null; conversation.Clear();
        repairActions.Children.Clear(); repairSession.Clear(); repairStatus.Text = ""; RenderSession(); ClearDiagnostics();
        if (!preserveDraft) { problem.Clear(); SetImage(null); video = null; DiscardRecording(); }
        if (!preserveDraft) caseWorkflow.Reset();
        RefreshSmartControls(); problem.Focus();
    }
    private void RefreshConversationControls(bool idle)
    {
        setupBanner.Visibility = realAnalyzer && !Configured() ? Visibility.Visible : Visibility.Collapsed;
        cancelButton.Visibility = cancelButton.IsEnabled ? Visibility.Visible : Visibility.Collapsed;
        assistantTools.Visibility = assistantTools.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        emptyChat.Visibility = turns.Count == 0 && caseWorkflow.Current is null && !assistantTools.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        newChat.IsEnabled = idle; recordButton.IsEnabled = active is null && diagnosticRequest is null && mediaRequest is null && voiceRequest is null && !repairRunning;
        bool recordingNow = recorder?.Recording == true;
        string label = I18n.T(recordingNow ? "chatStopRecording" : "chatRecord");
        recordButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(recordButton, label);
        recordButton.Background = recordingNow ? Brush("Tint") : Brushes.Transparent;
        transcribeButton.IsEnabled = idle && recording is not null && VoiceTranscription.Supported(AiCatalog.Provider, ChatGptAuth.Selected);
        voicePlay.IsEnabled = idle && recording is not null; voiceDiscard.IsEnabled = voiceRequest is null;
        checkpoint.Visibility = conversation.NeedsConfirmation ? Visibility.Visible : Visibility.Collapsed;
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
        using var tracked = services.Tasks.Begin("chatTranscribe", true);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(tracked.Token); voiceRequest = request;
        string recipient = DiagnosticRecipient, provider = AiCatalog.Provider, model = AiCatalog.Model;
        byte[] audio = recording; RefreshSmartControls();
        try
        {
            string key = Settings.GetKey(provider); if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(I18n.T("needsKey"));
            conversation.BeginRequest(); feedback.Text = I18n.T("chatTranscribing");
            string transcript = await transcribeAudio(audio, provider, key, model, request.Token);
            if (closed || request.IsCancellationRequested || recipient != DiagnosticRecipient) return;
            string merged = (problem.Text + "\n" + transcript).Trim(); if (merged.Length > 12000) throw new InvalidDataException(I18n.T("errorTextSize"));
            problem.Text = merged; recordingTranscribed = true; feedback.Text = I18n.T("chatTranscriptReady"); tracked.Complete();
        }
        catch (OperationCanceledException) { tracked.Cancelled(); }
        catch (Exception ex) { tracked.Failed(); if (!closed) feedback.Text = I18n.T("chatAudioFailed") + " " + ex.Message; }
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
