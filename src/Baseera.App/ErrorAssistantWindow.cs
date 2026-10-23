using System.Windows.Media.Imaging;
using Baseera.App.Services;
using Clipboard = System.Windows.Clipboard;
using DataFormats = System.Windows.DataFormats;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Baseera.App;

internal sealed class ErrorAssistantWindow : Window
{
    private readonly Func<string, ErrorImage?, string, CancellationToken, Task<ErrorAdvice>> analyze;
    private readonly bool realAnalyzer;
    private readonly Grid frame = new();
    private readonly TextBox problem = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ComboBox windows = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel result = new();
    private readonly StackPanel connection = new();
    private StackPanel planNotice = new();
    private Border connectionBorder = new();
    private readonly TextBlock feedback = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock imageLabel = new() { TextWrapping = TextWrapping.Wrap };
    private readonly System.Windows.Controls.Image preview = new() { MaxHeight = 260, MaxWidth = 690, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock windowCount = new() { TextWrapping = TextWrapping.Wrap };
    private Button analyzeButton = new(), cancelButton = new();
    private Button configureButton = new();
    private ErrorImage? image;
    private CancellationTokenSource? active;
    private AdvicePresenter? presentation;
    private ScrollViewer bodyScroll = null!;
    private bool closed, followResponse, scrollingResponse, scrollQueued;
    private bool showConnection;
    private AiConnectionPanel connectionPanel = null!;

    internal ErrorAssistantWindow(Window owner,
        Func<string, ErrorImage?, string, CancellationToken, Task<ErrorAdvice>>? analyze = null)
    {
        Owner = owner;
        realAnalyzer = analyze is null;
        this.analyze = analyze ?? ((description, picture, language, ct) => ErrorAssistant.Analyze(description, picture, language, ct));
        showConnection = analyze is null && !Configured();
        WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = Math.Min(820, SystemParameters.WorkArea.Width - 28);
        Height = Math.Min(790, SystemParameters.WorkArea.Height - 28);
        MinWidth = Math.Min(600, Width); MinHeight = Math.Min(550, Height);
        FontFamily = Brand.InterfaceFont; FontSize = 13; Icon = Brand.WindowIcon;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        FlowDirection = FlowDirection.LeftToRight;
        Title = I18n.T("errorAssistantTitle");
        SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Ink");
        Content = frame;
        SourceInitialized += (_, _) => WindowAppearance.Apply(this, Settings.Current.Dark);
        Closed += (_, _) => { closed = true; active?.Cancel(); presentation?.Dispose(); };
        Loaded += (_, _) => { if (showConnection) connectionPanel.MissingInput.Focus(); else problem.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.V && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control && HasClipboardImage())
            { PasteClipboard(); e.Handled = true; }
        };
        AllowDrop = true;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length == 1) TryLoadImage(() => ErrorAssistant.PrepareFile(files[0]));
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

        var header = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };
        var title = Text(I18n.T("errorAssistantTitle"), 26); title.FontWeight = arabic ? FontWeights.Medium : FontWeights.SemiBold; Add(header, title, 6);
        Add(header, Text(I18n.T("errorAssistantIntro"), 13, true), 0);
        var shortcut = Text(I18n.T("errorShortcut"), 12, true); shortcut.Margin = new Thickness(0, 8, 0, 0); header.Children.Add(shortcut);
        frame.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(28, 0, 28, 20) };
        connection.Children.Clear();
        connection.Visibility = showConnection ? Visibility.Visible : Visibility.Collapsed;
        var connectTitle = Text(I18n.T("errorConnect"), 15); connectTitle.FontWeight = FontWeights.SemiBold; Add(connection, connectTitle, 6);
        Add(connection, Text(I18n.T("errorConnectHelp"), 12, true), 14);
        connectionPanel = new AiConnectionPanel(new AiConnectionDraft(), () =>
        {
            showConnection = false; connectionBorder.Visibility = Visibility.Collapsed;
            configureButton.Visibility = Visibility.Visible; analyzeButton.IsEnabled = true;
            planNotice.Visibility = ChatGptAuth.Selected ? Visibility.Visible : Visibility.Collapsed;
            feedback.Text = I18n.T("errorKeySaved"); problem.Focus();
        }, dialog: true);
        Add(connection, connectionPanel, 4);
        Add(connection, Text(I18n.T("errorKeyLocal"), 12, true), 0);
        connectionBorder = new Border { Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(18), Child = connection,
            Visibility = showConnection ? Visibility.Visible : Visibility.Collapsed };
        Add(body, connectionBorder, 14);
        configureButton = Action("errorConfigure", () => { showConnection = true; connectionBorder.Visibility = Visibility.Visible; configureButton.Visibility = Visibility.Collapsed; connectionPanel.MissingInput.Focus(); });
        configureButton.HorizontalAlignment = HorizontalAlignment.Left; configureButton.Visibility = showConnection ? Visibility.Collapsed : Visibility.Visible; Add(body, configureButton, 14);
        planNotice = ChatGptUi.PlanNotice(); planNotice.Visibility = ChatGptAuth.Selected ? Visibility.Visible : Visibility.Collapsed; Add(body, planNotice, 8);

        Add(body, Text(I18n.T("errorDescription"), 15), 5);
        problem.Height = 130; problem.Tag = I18n.T("errorDescriptionHint"); problem.FlowDirection = FlowDirection.LeftToRight;
        System.Windows.Automation.AutomationProperties.SetAutomationId(problem, "ErrorDescription");
        Add(body, problem, 12);

        var attachment = new StackPanel { Orientation = Orientation.Horizontal };
        Add(attachment, Action("errorPaste", PasteClipboard), 0);
        Add(attachment, Action("errorChooseImage", ChooseImage), 0);
        Add(attachment, Action("errorRemoveImage", () => SetImage(null)), 0);
        Add(body, attachment, 8);
        imageLabel.Text = image is null ? I18n.T("errorNoImage") : image.Label;
        imageLabel.Foreground = Brush("Muted"); Add(body, imageLabel, 8);
        preview.Visibility = image is null ? Visibility.Collapsed : Visibility.Visible;
        Add(body, preview, 12);

        Add(body, Text(I18n.T("errorCaptureWindow"), 13), 5);
        var captureRow = new Grid(); captureRow.ColumnDefinitions.Add(new ColumnDefinition()); captureRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        RefreshWindows(); windows.DisplayMemberPath = "Title"; windows.Tag = I18n.T("errorChooseWindow"); windows.Margin = new Thickness(0, 0, 10, 0);
        windows.DropDownOpened += (_, _) => RefreshWindows();
        System.Windows.Automation.AutomationProperties.SetAutomationId(windows, "ErrorWindowPicker"); captureRow.Children.Add(windows);
        var capture = Action("errorCapture", async () => await CaptureSelected()); capture.Margin = new Thickness(0); Grid.SetColumn(capture, 1); captureRow.Children.Add(capture); Add(body, captureRow, 8);
        windowCount.Foreground = Brush("Muted"); Add(body, windowCount, 5);
        var refresh = Action("refresh", RefreshWindows); refresh.HorizontalAlignment = HorizontalAlignment.Left; Add(body, refresh, 12);
        Add(body, Text(I18n.T("errorPrivacy"), 12, true), 16);

        result.Children.Clear();
        System.Windows.Automation.AutomationProperties.SetAutomationId(result, "ErrorAdviceResult");
        Add(body, result, 0);
        bodyScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
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

        var footer = new DockPanel { Background = Brush("Surface"), LastChildFill = false, Margin = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(28, 16, 28, 10), HorizontalAlignment = HorizontalAlignment.Right };
        cancelButton = Action("cancel", () => active?.Cancel()); cancelButton.IsEnabled = active is not null; Add(actions, cancelButton, 0);
        analyzeButton = Action("errorAnalyze", async () => await Analyze(), true);
        analyzeButton.IsEnabled = active is null && (!realAnalyzer || Configured()); Add(actions, analyzeButton, 0);
        var close = Action("errorClose", Close); Add(actions, close, 0); footer.Children.Add(actions);
        feedback.Foreground = Brush("Muted"); feedback.VerticalAlignment = VerticalAlignment.Center;
        feedback.Margin = new Thickness(28, 16, 0, 10); DockPanel.SetDock(feedback, Dock.Left); footer.Children.Add(feedback);
        var footerBorder = new Border { Child = footer, Background = Brush("Surface"), BorderBrush = Brush("Line"), BorderThickness = new Thickness(0, 1, 0, 0) };
        Grid.SetRow(footerBorder, 2); frame.Children.Add(footerBorder);
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
    private async Task Analyze()
    {
        if (active is not null) return;
        if (!Configured() && realAnalyzer) { showConnection = true; connectionBorder.Visibility = Visibility.Visible; configureButton.Visibility = Visibility.Collapsed; feedback.Text = I18n.T("errorKeyMissing"); connectionPanel.MissingInput.Focus(); return; }
        if (string.IsNullOrWhiteSpace(problem.Text) && image is null) { feedback.Text = I18n.T("errorNeedInput"); return; }
        using var request = new CancellationTokenSource(); active = request;
        analyzeButton.IsEnabled = false; cancelButton.IsEnabled = true;
        feedback.Text = I18n.T("errorAnalyzing"); presentation?.Dispose(); result.Children.Clear();
        var current = new AdvicePresenter(AiCatalog.Provider, AiCatalog.Model, ChatGptAuth.Selected);
        presentation = current; result.Children.Add(current); followResponse = true; FollowResponse();
        current.Changed += FollowResponse;
        current.Completed += () => { if (!closed && ReferenceEquals(presentation, current)) feedback.Text = I18n.T("errorReady"); };
        current.Copied += () => feedback.Text = I18n.T("errorCopied");
        current.CopyFailed += message => feedback.Text = I18n.T("errorClipboardFailed") + " " + message;
        try
        {
            string language = Settings.Current.Language switch { "ar" => "Arabic", "fr" => "French", "es" => "Spanish", _ => "English" };
            var advice = await analyze(problem.Text, image, language, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (closed) return;
            feedback.Text = I18n.T("adviceWriting"); current.Reveal(advice);
        }
        catch (OperationCanceledException)
        {
            if (!closed) { current.Dispose(); result.Children.Clear(); feedback.Text = I18n.T("cancel"); }
        }
        catch (Exception ex)
        {
            if (closed) return;
            current.Dispose(); result.Children.Clear();
            feedback.Text = I18n.T("errorAnalysisFailed") + " " + ex.Message;
            if (ex is ChatGptException { Key: "chatGptUsageLimit" }) ChatGptUi.ShowUsageLimit(this);
        }
        finally
        {
            if (ReferenceEquals(active, request)) active = null;
            if (!closed) { analyzeButton.IsEnabled = true; cancelButton.IsEnabled = false; }
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
