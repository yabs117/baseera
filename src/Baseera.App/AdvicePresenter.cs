using System.Diagnostics;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Threading;
using Baseera.App.Services;

namespace Baseera.App;

internal sealed class AdvicePresenter : Border, IDisposable
{
    private sealed record Part(FrameworkElement Container, TextBlock Text, string Value, int[] Elements);
    private readonly StackPanel content = new();
    private readonly TextBlock state = new();
    private readonly Button revealAll = new(), copy = new();
    private readonly List<Part> parts = [];
    private readonly DispatcherTimer timer;
    private readonly Stopwatch elapsed = new();
    private int current, revealed, total;
    private double rate;
    private bool disposed;
    private string completeText = "";
    internal bool IsTyping { get; private set; }
    internal event Action? Completed;
    internal event Action? Changed;
    internal event Action? Copied;
    internal event Action<string>? CopyFailed;

    internal AdvicePresenter(string provider, string model, bool chatGpt)
    {
        Padding = new Thickness(20); CornerRadius = new CornerRadius(6);
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(BorderBrushProperty, "Line");
        BorderThickness = new Thickness(1); MinHeight = 104;
        AutomationProperties.SetAutomationId(this, "AdvicePresentation");
        var layout = new StackPanel(); Child = layout;
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var mark = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), Child = AiProviderBadge.Mark(provider), VerticalAlignment = VerticalAlignment.Top };
        mark.SetResourceReference(BorderBrushProperty, "Line"); header.Children.Add(mark);
        var identity = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var providerName = Text(AiProviderBadge.Name(provider, chatGpt), 15); providerName.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetAutomationId(providerName, "AdviceProviderName"); identity.Children.Add(providerName);
        var modelName = Text(model, 11, true); modelName.FlowDirection = FlowDirection.LeftToRight;
        modelName.TextAlignment = Settings.Current.Language == "ar" ? TextAlignment.Right : TextAlignment.Left;
        modelName.TextTrimming = TextTrimming.CharacterEllipsis; modelName.TextWrapping = TextWrapping.NoWrap; modelName.ToolTip = model;
        AutomationProperties.SetAutomationId(modelName, "AdviceModelName"); identity.Children.Add(modelName);
        Grid.SetColumn(identity, 1); header.Children.Add(identity);
        revealAll.Content = I18n.T("adviceRevealAll"); revealAll.Visibility = Visibility.Collapsed;
        revealAll.VerticalAlignment = VerticalAlignment.Center; revealAll.Margin = new Thickness(0); revealAll.MinHeight = 30;
        AutomationProperties.SetAutomationId(revealAll, "AdviceRevealAll"); revealAll.Click += (_, _) => Finish();
        Grid.SetColumn(revealAll, 2); header.Children.Add(revealAll); layout.Children.Add(header);
        state.FontSize = 11; state.Margin = new Thickness(0, 10, 0, 0); state.Text = I18n.T("errorAnalyzing");
        state.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetAutomationId(state, "AdviceState"); AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite);
        layout.Children.Add(state);
        content.Margin = new Thickness(0, 16, 0, 0); content.Visibility = Visibility.Collapsed; layout.Children.Add(content);
        copy.Content = I18n.T("errorCopyAdvice"); copy.HorizontalAlignment = HorizontalAlignment.Left;
        copy.Margin = new Thickness(0, 16, 0, 0); copy.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(copy, "errorCopyAdvice");
        copy.Click += (_, _) =>
        {
            try { System.Windows.Clipboard.SetText(completeText); Copied?.Invoke(); }
            catch (Exception ex) { CopyFailed?.Invoke(ex.Message); }
        };
        layout.Children.Add(copy);
        timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += Tick;
        Unloaded += (_, _) => Dispose();
    }

    private static TextBlock Text(string value, int size = 13, bool muted = false)
    {
        var text = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.65 };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "Muted" : "Ink"); return text;
    }

    internal void Reveal(ErrorAdvice advice, bool? animate = null)
    {
        if (disposed) return;
        timer.Stop(); elapsed.Reset(); current = revealed = total = 0; parts.Clear(); content.Children.Clear();
        copy.Visibility = Visibility.Collapsed; content.Visibility = Visibility.Visible;
        var locale = advice.Locale ?? AdviceLocale.Interface;
        content.FlowDirection = locale.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        content.Language = System.Windows.Markup.XmlLanguage.GetLanguage(locale.Language);
        var greeting = Add(locale.Greeting, 14, bottom: 14);
        AutomationProperties.SetAutomationId(greeting, "AdviceGreeting");
        Add(advice.Summary, 18, bold: true, bottom: 16);
        Add(locale.CauseTitle, 14, bold: true, bottom: 4);
        Add(advice.Cause, bottom: 18);
        Add(locale.StepsTitle, 14, bold: true, bottom: 8);
        for (int i = 0; i < advice.Steps.Count; i++) Add(advice.Steps[i], number: (i + 1).ToString(CultureInfo.InvariantCulture), bottom: 10);
        if (advice.Checks.Count > 0)
        {
            Add(locale.ChecksTitle, 14, bold: true, bottom: 8);
            foreach (string check in advice.Checks) Add(check, number: "·", bottom: 8);
        }
        Add(locale.ConfidenceTitle + ": " + advice.Confidence, 12, muted: true, bottom: 0);
        completeText = locale.Greeting + "\n\n" + advice.Summary + "\n\n" + locale.CauseTitle + "\n" + advice.Cause + "\n\n" +
            locale.StepsTitle + "\n" + string.Join("\n", advice.Steps.Select((step, i) => $"{i + 1}. {step}")) +
            (advice.Checks.Count == 0 ? "" : "\n\n" + locale.ChecksTitle + "\n" + string.Join("\n", advice.Checks.Select(check => "• " + check))) +
            "\n\n" + locale.ConfidenceTitle + ": " + advice.Confidence;
        IsTyping = true; state.Text = I18n.T("adviceWriting"); revealAll.Visibility = Visibility.Visible;
        // Text elements preserve Arabic marks, combining accents and joined emoji. No UTF-16 slicing.
        // Ordinary answers run at 52 elements/sec; long answers take at most 32 seconds.
        rate = Math.Max(52, total / 32d);
        if (!(animate ?? SystemParameters.ClientAreaAnimation)) { Finish(); return; }
        elapsed.Start(); timer.Start();
    }

    private TextBlock Add(string value, int size = 13, bool bold = false, bool muted = false, string? number = null, double bottom = 8)
    {
        var text = Text("", size, muted); if (bold) text.FontWeight = FontWeights.SemiBold;
        FrameworkElement container = text;
        if (number is not null)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) }); row.ColumnDefinitions.Add(new ColumnDefinition());
            var index = Text(number, 12, true); index.HorizontalAlignment = HorizontalAlignment.Center;
            row.Children.Add(index); Grid.SetColumn(text, 1); row.Children.Add(text); container = row;
        }
        container.Margin = new Thickness(0, 0, 0, bottom); container.Visibility = Visibility.Collapsed; content.Children.Add(container);
        var elements = StringInfo.ParseCombiningCharacters(value); total += elements.Length;
        parts.Add(new(container, text, value, elements));
        return text;
    }

    private void Tick(object? sender, EventArgs args)
    {
        if (disposed || !IsTyping) return;
        int budget = Math.Min(total, (int)(elapsed.Elapsed.TotalSeconds * rate));
        bool changed = false;
        while (current < parts.Count && budget >= revealed)
        {
            var part = parts[current]; int count = Math.Min(part.Elements.Length, budget - revealed);
            if (count == 0 && part.Elements.Length > 0) break;
            int end = count == part.Elements.Length ? part.Value.Length : part.Elements[count];
            if (end > part.Text.Text.Length || part.Elements.Length == 0)
            { part.Container.Visibility = Visibility.Visible; part.Text.Text = part.Value[..end]; changed = true; }
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
        foreach (var part in parts) { part.Container.Visibility = Visibility.Visible; part.Text.Text = part.Value; }
        IsTyping = false; revealAll.Visibility = Visibility.Collapsed; copy.Visibility = Visibility.Visible;
        state.Text = I18n.T("adviceComplete"); Changed?.Invoke(); Completed?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); timer.Tick -= Tick; elapsed.Stop(); IsTyping = false;
    }
}
