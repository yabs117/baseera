using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using Baseera.App.Services;
using Baseera.Core;

namespace Baseera.App;

internal sealed class AdvicePresenter : Border, IDisposable
{
    private sealed record Part(FrameworkElement Container, FrameworkElement Block, AdviceTextView Text, string Value, int[] Elements, Button? CopyCode);
    private readonly StackPanel content = new();
    private readonly TextBlock state = new();
    private readonly StackPanel activity = new() { Orientation = Orientation.Horizontal };
    private readonly List<System.Windows.Shapes.Ellipse> dots = [];
    private readonly Button revealAll = new(), copy = new();
    private readonly List<Part> parts = [];
    private readonly DispatcherTimer timer;
    private readonly Stopwatch elapsed = new(), requestClock = Stopwatch.StartNew();
    private readonly Action<string> writeClipboard;
    private int current, revealed, total;
    private double rate;
    private bool disposed, waiting = true, readerPaused;
    private string completeText = "";
    internal bool IsTyping { get; private set; }
    internal event Action? Completed;
    internal event Action? Changed;
    internal event Action? Copied;
    internal event Action<string>? CopyFailed;

    internal AdvicePresenter(string provider, string model, bool chatGpt, string description = "", ErrorImage? image = null, bool sharedDiagnostics = false, Action<string>? clipboardWriter = null)
    {
        writeClipboard = clipboardWriter ?? System.Windows.Clipboard.SetText;
        Padding = new Thickness(0); Background = Brushes.Transparent; BorderThickness = new Thickness(0);
        AutomationProperties.SetAutomationId(this, "AdvicePresentation");
        var layout = new StackPanel(); Child = layout;
        if (description.Length > 0 || image is not null || sharedDiagnostics)
            layout.Children.Add(UserMessage(description, image, sharedDiagnostics));

        var response = new StackPanel { Margin = new Thickness(0, 24, 0, 0) }; layout.Children.Add(response);
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(8), Child = AiProviderBadge.Mark(provider, 23), VerticalAlignment = VerticalAlignment.Top };
        mark.SetResourceReference(BackgroundProperty, "Subtle"); header.Children.Add(mark);
        var identity = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var providerName = Text(AiProviderBadge.Name(provider, chatGpt), 14); providerName.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetAutomationId(providerName, "AdviceProviderName"); identity.Children.Add(providerName);
        var modelName = Text(model, 11, true); modelName.FlowDirection = FlowDirection.LeftToRight;
        modelName.TextAlignment = Settings.Current.Language == "ar" ? TextAlignment.Right : TextAlignment.Left;
        modelName.TextTrimming = TextTrimming.CharacterEllipsis; modelName.TextWrapping = TextWrapping.NoWrap; modelName.ToolTip = model;
        AutomationProperties.SetAutomationId(modelName, "AdviceModelName"); identity.Children.Add(modelName);
        Grid.SetColumn(identity, 1); header.Children.Add(identity);
        revealAll.Content = I18n.T("adviceRevealAll"); revealAll.Visibility = Visibility.Collapsed;
        revealAll.VerticalAlignment = VerticalAlignment.Center; revealAll.Margin = new Thickness(0); revealAll.MinWidth = 0;
        revealAll.MinHeight = 28; revealAll.Padding = new Thickness(10, 4, 10, 4); revealAll.FontSize = 11;
        AutomationProperties.SetAutomationId(revealAll, "AdviceRevealAll"); revealAll.Click += (_, _) => Finish();
        Grid.SetColumn(revealAll, 2); header.Children.Add(revealAll); response.Children.Add(header);

        double gutter = 44;
        Thickness Indent(double top) => Settings.Current.Language == "ar" ? new Thickness(0, top, gutter, 0) : new Thickness(gutter, top, 0, 0);
        activity.Margin = Indent(10); activity.VerticalAlignment = VerticalAlignment.Center;
        for (int i = 0; i < 3; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, Margin = new Thickness(0, 0, 4, 0), Opacity = .4, VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Muted"); dots.Add(dot); activity.Children.Add(dot);
        }
        state.FontSize = 11; state.Text = I18n.T("adviceWaiting");
        state.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetAutomationId(state, "AdviceState"); AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite);
        activity.Children.Add(state); response.Children.Add(activity);
        content.Margin = Indent(16); content.Visibility = Visibility.Collapsed; response.Children.Add(content);

        var copyLabel = new StackPanel { Orientation = Orientation.Horizontal };
        var copyIcon = new System.Windows.Shapes.Path { Data = Geometry.Parse("M7,3 L15,3 L15,13 L7,13 Z M4,6 L4,16 L12,16"), StrokeThickness = 1.2, Width = 14, Height = 16, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0) };
        copyIcon.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted"); copyLabel.Children.Add(copyIcon);
        copyLabel.Children.Add(Text(I18n.T("errorCopyAdvice"), 11, true)); copy.Content = copyLabel;
        copy.HorizontalAlignment = Settings.Current.Language == "ar" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        copy.Margin = Indent(12); copy.Padding = new Thickness(6, 4, 6, 4); copy.MinHeight = 28;
        copy.BorderThickness = new Thickness(0); copy.Background = Brushes.Transparent; copy.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(copy, "errorCopyAdvice"); AutomationProperties.SetName(copy, I18n.T("errorCopyAdvice"));
        copy.Click += (_, _) => CopyText(completeText); response.Children.Add(copy);
        timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += Tick;
        if (SystemParameters.ClientAreaAnimation) timer.Start();
        Unloaded += (_, _) => Dispose();
    }

    private FrameworkElement UserMessage(string description, ErrorImage? image, bool sharedDiagnostics)
    {
        var body = new StackPanel(); var label = Text(I18n.T("adviceYou"), 11, true); label.Margin = new Thickness(0, 0, 0, 6); body.Children.Add(label);
        var text = new AdviceTextView([new(description)], 13);
        text.RevealTo(description.Length); AutomationProperties.SetAutomationId(text, "AdviceUserText");
        if (description.Length > 700 || description.Count(c => c == '\n') > 5)
        {
            var expand = new Expander { Header = I18n.T("adviceSubmittedMessage"), Content = text, IsExpanded = false };
            AutomationProperties.SetAutomationId(expand, "AdviceUserExpander");
            body.Children.Add(expand);
        }
        else if (description.Length > 0) body.Children.Add(text);
        if (image is not null)
        {
            using var stream = new MemoryStream(image.Bytes, false);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 320; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
            var thumbnail = new System.Windows.Controls.Image { Source = bitmap, MaxWidth = 240, MaxHeight = 140, Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
            AutomationProperties.SetAutomationId(thumbnail, "AdviceUserImage"); AutomationProperties.SetName(thumbnail, I18n.T("adviceImageAttached")); body.Children.Add(thumbnail);
        }
        if (sharedDiagnostics)
        {
            var attached = Text(I18n.T("adviceDiagnosticsAttached"), 11, true); attached.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(attached);
        }
        var bubble = new Border { Child = body, Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(12, 12, 4, 12),
            HorizontalAlignment = Settings.Current.Language == "ar" ? HorizontalAlignment.Left : HorizontalAlignment.Right, MaxWidth = 560, MinWidth = 130 };
        bubble.SetResourceReference(BackgroundProperty, "Subtle"); bubble.SetResourceReference(BorderBrushProperty, "Line"); bubble.BorderThickness = new Thickness(1);
        AutomationProperties.SetAutomationId(bubble, "AdviceUserMessage"); return bubble;
    }

    private static TextBlock Text(string value, int size = 13, bool muted = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.6 };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink"); return text;
    }

    internal void Reveal(ErrorAdvice advice, bool? animate = null)
    {
        if (disposed) return;
        waiting = false; readerPaused = false; requestClock.Stop(); timer.Stop(); elapsed.Reset(); current = revealed = total = 0;
        parts.Clear(); content.Children.Clear(); copy.Visibility = Visibility.Collapsed; content.Visibility = Visibility.Visible;
        var locale = advice.Locale ?? AdviceLocale.Interface;
        content.FlowDirection = locale.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        content.Language = System.Windows.Markup.XmlLanguage.GetLanguage(locale.Language);
        var greeting = Add(locale.Greeting, 14, muted: true, bottom: 14);
        AutomationProperties.SetAutomationId(greeting, "AdviceGreeting");
        var summary = Add(advice.Summary, 19, bold: true, bottom: 20); AutomationProperties.SetAutomationId(summary, "AdviceSummary");
        Add(locale.CauseTitle, 13, bold: true, bottom: 5); Add(advice.Cause, bottom: 20);
        Add(locale.StepsTitle, 13, bold: true, bottom: 10);
        for (int i = 0; i < advice.Steps.Count; i++) Add(advice.Steps[i], number: (i + 1).ToString(CultureInfo.InvariantCulture), bottom: 12);
        if (advice.Checks.Count > 0)
        {
            Add(locale.ChecksTitle, 13, bold: true, bottom: 8);
            foreach (string check in advice.Checks) Add(check, number: "·", bottom: 8);
        }
        Add(locale.ConfidenceTitle + ": " + advice.Confidence, 12, muted: true, bottom: 0);
        completeText = locale.Greeting + "\n\n" + advice.Summary + "\n\n" + locale.CauseTitle + "\n" + advice.Cause + "\n\n" +
            locale.StepsTitle + "\n" + string.Join("\n", advice.Steps.Select((step, i) => $"{i + 1}. {step}")) +
            (advice.Checks.Count == 0 ? "" : "\n\n" + locale.ChecksTitle + "\n" + string.Join("\n", advice.Checks.Select(check => "• " + check))) +
            "\n\n" + locale.ConfidenceTitle + ": " + advice.Confidence;
        IsTyping = true; state.Text = I18n.T("adviceWriting"); revealAll.Visibility = Visibility.Visible;
        foreach (var dot in dots) dot.Visibility = Visibility.Visible;
        rate = Math.Max(52, total / 32d);
        if (!(animate ?? SystemParameters.ClientAreaAnimation)) { Finish(); return; }
        elapsed.Start(); timer.Start();
    }

    private AdviceTextView Add(string value, int size = 14, bool bold = false, bool muted = false, string? number = null, double bottom = 8)
    {
        var section = new StackPanel(); FrameworkElement container = section;
        if (number is not null)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var index = Text(number, 12, true); index.Margin = new Thickness(0, 3, 0, 0); index.HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(index); Grid.SetColumn(section, 1); row.Children.Add(section); container = row;
        }
        container.Margin = new Thickness(0, 0, 0, bottom); container.Visibility = Visibility.Collapsed; content.Children.Add(container);
        AdviceTextView? first = null;
        foreach (var block in AdviceMarkup.Parse(value))
        {
            var view = new AdviceTextView(block.Inlines, block.Code ? 12 : size, bold, muted, block.Code); first ??= view;
            FrameworkElement blockHost = view;
            Button? copyCode = null;
            if (block.Code)
            {
                var code = new DockPanel(); var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
                copyCode = new Button { Content = I18n.T("adviceCopyCode"), FontSize = 11, MinHeight = 26, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
                AutomationProperties.SetAutomationId(copyCode, "AdviceCopyCode"); AutomationProperties.SetName(copyCode, I18n.T("adviceCopyCode"));
                copyCode.Click += (_, _) => CopyText(block.Text); DockPanel.SetDock(copyCode, Dock.Right); header.Children.Add(copyCode);
                header.Children.Add(Text(block.Language.Length > 0 ? block.Language : I18n.T("adviceCode"), 11, true));
                DockPanel.SetDock(header, Dock.Top); code.Children.Add(header); code.Children.Add(view);
                var surface = new Border { Child = code, Padding = new Thickness(12), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 6, 0, 6), FlowDirection = FlowDirection.LeftToRight };
                surface.SetResourceReference(BackgroundProperty, "Subtle"); surface.SetResourceReference(BorderBrushProperty, "Line"); blockHost = surface;
            }
            blockHost.Visibility = Visibility.Collapsed; section.Children.Add(blockHost);
            int[] elements = StringInfo.ParseCombiningCharacters(block.Text); total += elements.Length;
            parts.Add(new(container, blockHost, view, block.Text, elements, copyCode));
        }
        return first ?? throw new InvalidDataException("Empty response paragraph.");
    }

    private void CopyText(string text)
    {
        try { writeClipboard(text); Copied?.Invoke(); }
        catch (Exception ex) { CopyFailed?.Invoke(ex.Message); }
    }
    private void Tick(object? sender, EventArgs args)
    {
        if (disposed) return;
        double seconds = waiting ? requestClock.Elapsed.TotalSeconds : elapsed.Elapsed.TotalSeconds;
        for (int i = 0; i < dots.Count; i++) dots[i].Opacity = .25 + .6 * Math.Max(0, Math.Sin(seconds * 5 - i * .9));
        if (waiting || !IsTyping) return;
        if (parts.Any(p => p.Text.IsKeyboardFocusWithin && !p.Text.Selection.IsEmpty))
        {
            if (!readerPaused) { elapsed.Stop(); readerPaused = true; state.Text = I18n.T("adviceSelectionPaused"); }
            return;
        }
        if (readerPaused) { readerPaused = false; elapsed.Start(); state.Text = I18n.T("adviceWriting"); }
        int budget = Math.Min(total, (int)(elapsed.Elapsed.TotalSeconds * rate)); bool changed = false;
        while (current < parts.Count && budget >= revealed)
        {
            var part = parts[current]; int count = Math.Min(part.Elements.Length, budget - revealed);
            if (count == 0 && part.Elements.Length > 0) break;
            int end = count == part.Elements.Length ? part.Value.Length : part.Elements[count];
            if (end > part.Text.VisibleText.Length || part.Elements.Length == 0)
            { part.Container.Visibility = Visibility.Visible; part.Block.Visibility = Visibility.Visible; part.Text.RevealTo(end); changed = true; }
            if (part.CopyCode is not null) part.CopyCode.IsEnabled = count == part.Elements.Length;
            if (count < part.Elements.Length) break;
            revealed += part.Elements.Length; current++;
        }
        if (changed) Changed?.Invoke();
        if (current == parts.Count) Finish();
    }

    internal void Finish()
    {
        if (disposed || !IsTyping) return;
        timer.Stop(); elapsed.Stop();
        foreach (var part in parts)
        { part.Container.Visibility = Visibility.Visible; part.Block.Visibility = Visibility.Visible; part.Text.RevealTo(part.Value.Length); if (part.CopyCode is not null) part.CopyCode.IsEnabled = true; }
        IsTyping = false; revealAll.Visibility = Visibility.Collapsed; copy.Visibility = Visibility.Visible;
        foreach (var dot in dots) dot.Visibility = Visibility.Collapsed;
        state.Text = I18n.T("adviceComplete") + " · " + string.Format(I18n.T("adviceResponseTime"), requestClock.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture));
        Changed?.Invoke(); Completed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); timer.Tick -= Tick; elapsed.Stop(); requestClock.Stop(); IsTyping = false;
    }
}
