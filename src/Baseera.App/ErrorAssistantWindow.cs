using System.Windows.Media.Imaging;
using Baseera.App.Services;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Baseera.App;

internal sealed partial class ErrorAssistantWindow : Window
{
    private readonly Func<string, ErrorImage?, string, CancellationToken, Task<ErrorAdvice>> analyze;
    private readonly bool realAnalyzer;
    private readonly Grid frame = new();
    private readonly TextBox problem = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox windows = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel result = new();
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock imageLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly System.Windows.Controls.Image preview = new() { MaxHeight = 260, MaxWidth = 690, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock windowCount = new() { TextWrapping = TextWrapping.Wrap };
    private Button analyzeButton = new(), cancelButton = new();
    private ErrorImage? image;
    private CancellationTokenSource? active;
    private AdvicePresenter? presentation;
    private ScrollViewer bodyScroll = null!;
    private bool closed, followResponse, scrollingResponse, scrollQueued;
    private string responseAccount = "";

    internal ErrorAssistantWindow(Window owner,
        Func<string, ErrorImage?, string, CancellationToken, Task<ErrorAdvice>>? analyze = null,
        Func<Baseera.Core.DiagnosticOptions, CancellationToken, Task<string>>? collectDiagnostics = null,
        Func<string, ErrorImage?, string, string, CancellationToken, Task<ErrorAdvice>>? analyzeDiagnostics = null,
        Func<string, Baseera.Core.RepairApproval?>? approveRepair = null,
        RepairRunner? repairRunner = null,
        Func<ErrorChatRequest, CancellationToken, Task<ErrorAdvice>>? analyzeConversation = null,
        Func<long, bool>? confirmRequests = null, Func<IVoiceRecorder>? recorderFactory = null,
        Func<byte[], string, string, string, CancellationToken, Task<string>>? transcribeAudio = null, Func<bool>? approveAudioUpload = null)
    {
        Owner = owner;
        realAnalyzer = analyze is null && analyzeDiagnostics is null && analyzeConversation is null;
        this.analyzeConversation = analyzeConversation; this.confirmRequests = confirmRequests;
        this.approveAudioUpload = approveAudioUpload;
        this.recorderFactory = recorderFactory ?? (() => new VoiceRecording());
        this.transcribeAudio = transcribeAudio ?? ((audio, provider, key, model, ct) => VoiceTranscription.Transcribe(audio, provider, key, model, ct));
        this.analyze = analyze ?? ((description, picture, language, ct) => ErrorAssistant.Analyze(description, picture, language, ct));
        problem.TextChanged += (_, _) => RefreshSmartControls();
        collectDiagnosticData = collectDiagnostics ?? ((options, ct) => WindowsDiagnostics.Collect(options, ct));
        analyzeDiagnosticData = analyzeDiagnostics; this.approveRepair = approveRepair; this.repairRunner = repairRunner ?? RepairRunner.Shared;
        WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(960, SystemParameters.WorkArea.Width - 28);
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 28);
        MinWidth = Math.Min(600, Width); MinHeight = Math.Min(550, Height);
        FontFamily = Brand.InterfaceFont; FontSize = 13; Icon = Brand.WindowIcon;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        FlowDirection = FlowDirection.LeftToRight;
        Title = I18n.T("errorAssistantTitle");
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        Content = frame;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        ChatGptAuth.SessionSignedOut += OnSessionSignedOut;
        Closing += (_, e) =>
        {
            if (repairRunning && MessageBox.Show(this, I18n.T("smartCloseRunning"), I18n.T("smartApproveTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) e.Cancel = true;
        };
        Closed += (_, _) => { closed = true; ChatGptAuth.SessionSignedOut -= OnSessionSignedOut; active?.Cancel(); diagnosticRequest?.Cancel(); CloseConversation(); };
        Loaded += (_, _) => problem.Focus();
        Activated += (_, _) => { if (!NetworkBusy) RefreshSavedConnection(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.V && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && HasClipboardImage())
            { PasteClipboard(); e.Handled = true; }
        };
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += async (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1)
            {
                if (new[] { ".mp4", ".wmv", ".avi", ".webm", ".mov" }.Contains(Path.GetExtension(files[0]).ToLowerInvariant())) await LoadVideo(files[0]);
                else TryLoadImage(() => ErrorAssistant.PrepareFile(files[0]));
            }
        };
        Render();
    }

    private static bool Configured()
    {
        return AiCatalog.Configured();
    }
    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
    private static TextBlock Text(string content, int size = 13, bool muted = false) => new()
    { Text = content, FontSize = size, Foreground = Brush(muted ? "Muted" : "Ink"), TextWrapping = TextWrapping.Wrap };
    private static Button Action(string label, Action click, bool primary = false)
    {
        var button = new Button { Content = Text(I18n.T(label), 13), MinWidth = 105 };
        if (primary) { button.Background = Brush("Accent"); button.BorderBrush = button.Background; ((TextBlock)button.Content).Foreground = Brush("OnAccent"); }
        System.Windows.Automation.AutomationProperties.SetAutomationId(button, label);
        button.Click += (_, _) => click(); return button;
    }
    private static void Add(StackPanel stack, UIElement child, double bottom = 12)
    { child.SetValue(MarginProperty, new Thickness(0, 0, 0, bottom)); stack.Children.Add(child); }

    private void Render()
    {
        bool arabic = Settings.Current.Language == "ar";
        frame.FlowDirection = arabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        frame.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "Bg");
        frame.Children.Clear(); frame.RowDefinitions.Clear();
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        frame.RowDefinitions.Add(new RowDefinition());
        frame.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(22, 16, 22, 12) };
        var title = Text(I18n.T("errorAssistantTitle"), 22); title.FontWeight = arabic ? FontWeights.Medium : FontWeights.SemiBold; Add(header, title, 4);
        Add(header, Text(I18n.T("assistantConversationIntro"), 13, true), 0);
        var shortcut = Text(I18n.T("errorShortcut"), 12, true); shortcut.Margin = new Thickness(0, 8, 0, 0); header.Children.Add(shortcut);
        frame.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        Add(body, Text(I18n.T("errorCaptureWindow"), 13), 5);
        var captureRow = new Grid(); captureRow.ColumnDefinitions.Add(new ColumnDefinition()); captureRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RefreshWindows(); windows.DisplayMemberPath = "Title"; windows.Tag = I18n.T("errorChooseWindow"); windows.Margin = new Thickness(0, 0, 10, 0);
        windows.DropDownOpened += (_, _) => RefreshWindows();
        System.Windows.Automation.AutomationProperties.SetAutomationId(windows, "ErrorWindowPicker"); captureRow.Children.Add(windows);
        var capture = Action("errorCapture", async () => await CaptureSelected()); capture.Margin = new Thickness(0); Grid.SetColumn(capture, 1); captureRow.Children.Add(capture); Add(body, captureRow, 8);
        windowCount.Foreground = Brush("Muted"); Add(body, windowCount, 5);
        var refresh = Action("refresh", RefreshWindows); refresh.HorizontalAlignment = HorizontalAlignment.Left; Add(body, refresh, 12);
        Add(body, Text(I18n.T("errorPrivacy"), 12, true), 16);
        Add(body, BuildDiagnostics(), 16);

        result.Children.Clear();
        System.Windows.Automation.AutomationProperties.SetAutomationId(result, "ErrorAdviceResult");
        var thread = new StackPanel { Margin = new Thickness(22, 0, 22, 12) };
        assistantTools.Header = I18n.T("chatTools"); assistantTools.Content = body; assistantTools.IsExpanded = false;
        System.Windows.Automation.AutomationProperties.SetAutomationId(assistantTools, "ConversationTools"); Add(thread, assistantTools, 10);
        emptyChat.Children.Add(new System.Windows.Controls.Image { Source = Brand.WindowIcon, Width = 36, Height = 36, Margin = new Thickness(0, 0, 0, 14) });
        emptyChat.Children.Add(Text(I18n.T("chatEmptyTitle"), 20));
        var emptyHint = Text(I18n.T("chatEmptyHelp"), 13, true); emptyHint.Margin = new Thickness(0, 8, 0, 0); emptyHint.MaxWidth = 480; emptyChat.Children.Add(emptyHint);
        Add(thread, emptyChat, 0);
        Add(thread, result, 0); Add(thread, repairActions, 8); Add(thread, repairStatus, 12); Add(thread, BuildSession(), 0);
        bodyScroll = new ScrollViewer { Content = thread, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        System.Windows.Automation.AutomationProperties.SetAutomationId(bodyScroll, "ErrorAssistantScroll");
        bodyScroll.ScrollChanged += (_, e) =>
        {
            if (!scrollingResponse && e.ExtentHeightChange == 0 && e.VerticalChange != 0)
                followResponse = bodyScroll.ScrollableHeight - bodyScroll.VerticalOffset < 44;
        };
        bodyScroll.PreviewMouseWheel += (_, e) => { if (e.Delta > 0) followResponse = false; };
        bodyScroll.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.PageUp or System.Windows.Input.Key.Home or System.Windows.Input.Key.Up) followResponse = false;
        };
        Grid.SetRow(bodyScroll, 1); frame.Children.Add(bodyScroll);

        var footerBorder = BuildComposer();
        Grid.SetRow(footerBorder, 2); frame.Children.Add(footerBorder);
        RefreshSmartControls();
    }

    private void ShowConnectionSettings()
    {
        if (NetworkBusy || recorder?.Recording == true) { feedback.Text = I18n.T("aiFinishBeforeSettings"); return; }
        InvalidateDiagnosticConsent();
        presentation?.Finish();
        if (Owner is MainWindow main) { Hide(); main.OpenAiSettings(this); }
    }

    private void OnSessionSignedOut(string account)
    {
        if (!Dispatcher.CheckAccess()) { _ = Dispatcher.InvokeAsync(() => OnSessionSignedOut(account)); return; }
        if (closed) return;
        InvalidateDiagnosticConsent();
        RefreshSavedConnection();
        if (responseAccount == account) { active?.Cancel(); presentation?.Finish(); }
        if (conversation.Recipient == "openai|chatgpt|" + account)
        {
            voiceRequest?.Cancel();
            foreach (var turn in turns) turn.Dispose(); turns.Clear(); result.Children.Clear(); presentation = null;
            conversation.Clear(); repairActions.Children.Clear(); repairSession.Clear(); ClearDiagnostics(); RenderSession();
        }
        if (realAnalyzer && !Configured())
        {
            analyzeButton.IsEnabled = false; feedback.Text = I18n.T("chatGptSignedOut"); RefreshSmartControls();
        }
    }

    private void PasteClipboard()
    {
        try
        {
            bool found = false;
            if (Clipboard.ContainsImage())
            {
                var bitmap = Clipboard.GetImage();
                if (bitmap is not null)
                {
                    if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 40_000_000) throw new InvalidDataException(I18n.T("errorImageSize"));
                    using var stream = new MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream);
                    SetImage(ErrorAssistant.Prepare(stream.ToArray(), I18n.T("errorClipboardImage"))); found = true;
                }
            }
            if (!found && Clipboard.GetDataObject()?.GetDataPresent("PNG") == true)
            {
                var png = Clipboard.GetData("PNG");
                byte[]? bytes = png switch { MemoryStream stream => stream.ToArray(), byte[] data => data, _ => null };
                if (bytes is not null) { SetImage(ErrorAssistant.Prepare(bytes, I18n.T("errorClipboardImage"))); found = true; }
            }
            if (!found && Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                string? path = files.Cast<string>().FirstOrDefault(file => new[] { ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase));
                if (path is not null) { SetImage(ErrorAssistant.PrepareFile(path)); found = true; }
            }
            if (!found && Clipboard.ContainsText()) { problem.Text = Clipboard.GetText().Trim(); found = problem.Text.Length > 0; }
            feedback.Text = found ? I18n.T("errorPasted") : I18n.T("errorEmptyClipboard");
        }
        catch (Exception ex) { feedback.Text = I18n.T("errorClipboardFailed") + " " + ex.Message; }
    }
    private static bool HasClipboardImage()
    {
        try { return Clipboard.ContainsImage() || Clipboard.ContainsFileDropList() || Clipboard.GetDataObject()?.GetDataPresent("PNG") == true; }
        catch { return false; }
    }
    private void RefreshWindows()
    {
        var selected = (windows.SelectedItem as WindowChoice)?.Handle;
        var ownHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var available = WindowTranslator.ListWindows(includeOwnProcess: true, includeMinimized: true, exclude: ownHandle);
        windows.ItemsSource = available;
        windows.SelectedItem = available.FirstOrDefault(item => item.Handle == selected);
        windowCount.Text = string.Format(I18n.T("errorWindowCount"), available.Count);
    }
    private void ChooseImage()
    {
        var dialog = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) == true) TryLoadImage(() => ErrorAssistant.PrepareFile(dialog.FileName));
    }
    private void TryLoadImage(Func<ErrorImage> load)
    {
        try { SetImage(load()); feedback.Text = I18n.T("errorImageReady"); }
        catch (Exception ex) { feedback.Text = I18n.T("errorImageFailed") + " " + ex.Message; }
    }
    private void SetImage(ErrorImage? value)
    {
        image = value;
        if (value is not null) { video = null; UpdateMediaStatus(); }
        RefreshSmartControls();
        imageLabel.Text = value?.Label ?? I18n.T("errorNoImage");
        preview.Source = null; preview.Visibility = value is null ? Visibility.Collapsed : Visibility.Visible;
        if (value is null) return;
        using var stream = new MemoryStream(value.Bytes, false);
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); preview.Source = bitmap;
        _ = Dispatcher.InvokeAsync(() => preview.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private async Task CaptureSelected()
    {
        if (windows.SelectedItem is not WindowChoice choice) { feedback.Text = I18n.T("errorChooseWindow"); return; }
        try
        {
            feedback.Text = I18n.T("errorCapturing");
            var ownerHandle = new System.Windows.Interop.WindowInteropHelper(Owner).Handle;
            if (choice.Handle == ownerHandle) SetImage(CaptureOwner(choice.Title));
            else
            {
                var capture = await WindowTranslator.CaptureWindow(choice.Handle, CancellationToken.None, includeOwnProcess: true);
                SetImage(ErrorAssistant.Prepare(capture.Png, choice.Title));
            }
            Activate(); feedback.Text = I18n.T("errorImageReady");
        }
        catch (Exception ex) { Activate(); feedback.Text = I18n.T("errorImageFailed") + " " + ex.Message; }
    }
    private ErrorImage CaptureOwner(string title)
    {
        Owner.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(Owner);
        int width = (int)Math.Ceiling(Owner.ActualWidth * dpi.DpiScaleX);
        int height = (int)Math.Ceiling(Owner.ActualHeight * dpi.DpiScaleY);
        if (width < 1 || height < 1 || (long)width * height > 40_000_000) throw new InvalidDataException(I18n.T("errorImageSize"));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(Owner);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output);
        return ErrorAssistant.Prepare(output.ToArray(), title);
    }
    private async Task Analyze(bool withoutDiagnostics = false)
    {
        if (NetworkBusy || recorder?.Recording == true) return;
        if (composerRecipient != DiagnosticRecipient)
        { RefreshSavedConnection(true); InvalidateDiagnosticConsent(); feedback.Text = I18n.T("chatConnectionChanged"); return; }
        if (!Configured() && realAnalyzer) { ShowConnectionSettings(); feedback.Text = I18n.T("errorKeyMissing"); return; }
        if (problem.Text.Length > 12000) { feedback.Text = I18n.T("errorTextSize"); return; }
        if (recording is not null && !recordingTranscribed) { feedback.Text = I18n.T("chatReviewAudio"); return; }
        if (withoutDiagnostics) { diagnosticGateActive = false; InvalidateDiagnosticConsent(); }
        else if (PauseForDiagnosticReview()) return;
        if (string.IsNullOrWhiteSpace(problem.Text) && image is null && video is null && (diagnosticShare.IsChecked != true || string.IsNullOrWhiteSpace(diagnosticPreview.Text))) { feedback.Text = I18n.T("errorNeedInput"); return; }
        if (diagnosticShare.IsChecked == true && diagnosticRecipient != DiagnosticRecipient) { InvalidateDiagnosticConsent(); feedback.Text = I18n.T("smartReviewRecipient"); return; }
        if (!AllowRequest()) return;
        string diagnostics;
        try { diagnostics = withoutDiagnostics ? "" : ApprovedDiagnostics(); }
        catch (InvalidOperationException ex) { feedback.Text = ex.Message; return; }
        string submittedDescription = problem.Text, submittedProvider = AiCatalog.Provider, submittedModel = AiCatalog.Model;
        ErrorVideo? submittedVideo = video;
        ErrorImage? submittedImage = image ?? submittedVideo?.Frames;
        string mediaContext = submittedVideo is null ? submittedImage is null ? "" : "A user-selected image is attached to this turn only." :
            submittedProvider == "gemini" ? $"Original video with audio is attached to this turn only; duration {submittedVideo.Seconds:0.0}s." :
            $"Only six evenly sampled video frames over {submittedVideo.Seconds:0.0}s are attached. No audio or continuous motion is available; do not claim you inspected unsampled moments.";
        var history = conversation.Context(); conversation.BeginRequest();
        string localRepairs = string.Join("\n", repairSession.Activity.TakeLast(5).Select(x => $"{x.ActionId}: {x.Status}; exit={x.ExitCode}; UTC {x.Time:O}")
            .Concat(repairSession.Observations.TakeLast(2).Select(x => $"User-reported outcome, not independently verified: {x.Outcome}; UTC {x.Time:O}")));
        using var request = new CancellationTokenSource(); active = request;
        diagnosticGateActive = false; diagnosticRequestHasData = previousRequestHadDiagnostics = diagnostics.Length > 0;
        analyzeButton.IsEnabled = false; cancelButton.IsEnabled = true;
        presentation?.Finish(); feedback.Text = I18n.T("errorAnalyzing");
        repairActions.Children.Clear(); repairStatus.Text = ""; repairSession.Clear(); sessionDetails.IsExpanded = false; reportDiagnostics.IsChecked = false; RenderSession(); RefreshSmartControls();
        responseAccount = ChatGptAuth.Selected ? Settings.Current.ChatGptAccount : "";
        var current = new AdvicePresenter(submittedProvider, submittedModel, ChatGptAuth.Selected, submittedDescription, submittedImage, diagnostics.Length > 0,
            showGreeting: conversation.Completed == 0);
        presentation = current; result.Children.Add(current); turns.Add(current);
        problem.Clear(); SetImage(null); video = null; UpdateMediaStatus(); DiscardRecording();
        assistantTools.IsExpanded = false; followResponse = true; FollowResponse();
        current.Changed += FollowResponse;
        current.Completed += () => { if (!closed && ReferenceEquals(presentation, current)) { feedback.Text = I18n.T("errorReady"); RefreshSmartControls(); } };
        current.Copied += () => feedback.Text = I18n.T("errorCopied");
        current.CopyFailed += message => feedback.Text = I18n.T("errorClipboardFailed") + " " + message;
        try
        {
            string language = Settings.Current.Language switch { "ar" => "Arabic", "fr" => "French", "es" => "Spanish", _ => "English" };
            var chatRequest = new ErrorChatRequest(submittedDescription, submittedImage, submittedVideo, language, diagnostics, history, mediaContext, localRepairs);
            var advice = analyzeConversation is not null ? await analyzeConversation(chatRequest, request.Token) :
                analyzeDiagnosticData is not null ? await analyzeDiagnosticData(submittedDescription, submittedImage, language, diagnostics, request.Token) :
                realAnalyzer ? await ErrorAssistant.Analyze(submittedDescription, submittedProvider == "gemini" && submittedVideo is not null ? null : submittedImage, language, request.Token, approvedDiagnostics: diagnostics,
                    conversation: history, video: submittedProvider == "gemini" ? submittedVideo : null, attachmentContext: mediaContext, localRepairs: localRepairs) :
                await analyze(submittedDescription, submittedImage, language, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (closed) return;
            analyzedDescription = submittedDescription; analyzedImage = submittedImage;
            conversation.Add(submittedDescription, AdviceHistory(advice), mediaContext, contextAllowed: diagnostics.Length == 0);
            if (turns.Count > Baseera.Core.ConversationLedger.RetainedExchanges) { turns[0].Dispose(); result.Children.Remove(turns[0]); turns.RemoveAt(0); }
            repairSession.SetAnalysis(submittedDescription, submittedImage is not null, diagnostics, submittedProvider, submittedModel, advice); RenderSession();
            feedback.Text = I18n.T("adviceWriting"); current.Reveal(advice); ShowRepairActions(advice);
        }
        catch (OperationCanceledException)
        {
            if (!closed)
            {
                current.Dispose(); result.Children.Remove(current); turns.Remove(current);
                if (responseAccount.Length == 0 || ChatGptAuth.Connected(responseAccount)) RestoreChatDraft(submittedDescription, submittedImage, submittedVideo);
                feedback.Text = I18n.T(responseAccount.Length > 0 && !ChatGptAuth.Connected(responseAccount) ? "chatGptSignedOut" : "cancel");
            }
        }
        catch (Exception ex)
        {
            if (closed) return;
            current.Dispose(); result.Children.Remove(current); turns.Remove(current);
            if (responseAccount.Length == 0 || ChatGptAuth.Connected(responseAccount)) RestoreChatDraft(submittedDescription, submittedImage, submittedVideo);
            feedback.Text = I18n.T("errorAnalysisFailed") + " " + ex.Message;
            if (ex is ChatGptException { Key: "chatGptUsageLimit" }) ChatGptUi.ShowUsageLimit(this);
        }
        finally
        {
            if (ReferenceEquals(active, request)) { active = null; diagnosticRequestHasData = false; }
            if (!closed) RefreshSmartControls();
        }
    }

    private void FollowResponse()
    {
        if (!followResponse || closed || scrollQueued) return;
        scrollQueued = true;
        _ = Dispatcher.InvokeAsync(() =>
        {
            scrollQueued = false;
            if (closed || !followResponse) return;
            scrollingResponse = true;
            try { bodyScroll.UpdateLayout(); bodyScroll.ScrollToEnd(); }
            finally { scrollingResponse = false; }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
}
