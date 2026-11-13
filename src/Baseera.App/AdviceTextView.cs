using System.Globalization;
using System.Windows.Documents;
using Baseera.Core;

namespace Baseera.App;

internal sealed class AdviceTextView : System.Windows.Controls.RichTextBox
{
    private readonly Paragraph paragraph = new() { Margin = new Thickness(0) };
    private readonly IReadOnlyList<AdviceInline> segments;
    private readonly IReadOnlyList<AdviceArrangement> arranged;
    private readonly Dictionary<int, double> measured = new();
    internal string FullText { get; }
    internal string VisibleText { get; private set; } = "";

    internal AdviceTextView(IReadOnlyList<AdviceInline> segments, double size = 14, bool bold = false, bool muted = false, bool code = false)
    {
        this.segments = segments; FullText = string.Concat(segments.Select(s => s.Value));
        arranged = code ? Array.Empty<AdviceArrangement>() : AdviceMarkup.Arrange(FullText);
        IsReadOnly = true; IsReadOnlyCaretVisible = false; IsUndoEnabled = false; IsDocumentEnabled = false;
        BorderThickness = new Thickness(0); Padding = new Thickness(0); Margin = new Thickness(0);
        FontSize = size;
        Background = Brushes.Transparent; VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; Cursor = System.Windows.Input.Cursors.IBeam;
        SetResourceReference(ForegroundProperty, muted ? "Muted" : "Ink");
        SetResourceReference(SelectionBrushProperty, "Accent"); SelectionOpacity = .25;
        Document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontSize = size, LineHeight = size * 1.8,
            FontFamily = code ? new FontFamily("Cascadia Mono, Consolas") : Brand.InterfaceFont,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (code) FlowDirection = FlowDirection.LeftToRight;
        Document.FlowDirection = FlowDirection;
        PreviewMouseWheel += (_, e) =>
        {
            DependencyObject? ancestor = VisualTreeHelper.GetParent(this);
            while (ancestor is not null && ancestor is not ScrollViewer) ancestor = VisualTreeHelper.GetParent(ancestor);
            // Skip our template's internal scroller: start from the view's parent.
            if (ancestor is ScrollViewer scroll) { scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta / 3d); e.Handled = true; }
        };
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FlowDirectionProperty && Document is not null)
        {
            Document.FlowDirection = FlowDirection;
            if (arranged.Count > 0 && VisibleText.Length > 0)
            {
                measured.Clear(); int count = VisibleText.Length; VisibleText = ""; RevealTo(count);
            }
        }
    }
    internal void RevealTo(int characters)
    {
        if (characters < 0) characters = 0;
        if (characters > FullText.Length) characters = FullText.Length;
        string visible = FullText[..characters]; if (visible == VisibleText) return;
        if (arranged.Count == 0) RevealPlain(characters);
        else
        {
            Document.Blocks.Clear();
            for (int index = 0; index < arranged.Count; index++)
            {
                var part = arranged[index];
                if (part.Start >= characters) break;
                int take = Math.Min(part.Length, characters - part.Start);
                if (take <= 0) continue;
                Document.Blocks.Add(CreateParagraph(part, index, characters, take));
            }
            if (Document.Blocks.Count == 0) { paragraph.Inlines.Clear(); Document.Blocks.Add(paragraph); }
        }
        VisibleText = visible;
    }

    private void RevealPlain(int characters)
    {
        paragraph.Inlines.Clear(); int remaining = characters;
        foreach (var segment in segments)
        {
            if (remaining <= 0) break;
            int length = Math.Min(remaining, segment.Value.Length); var run = new Run(segment.Value[..length]);
            Apply(run, segment.Kind);
            paragraph.Inlines.Add(run); remaining -= length;
        }
    }

    private Paragraph CreateParagraph(AdviceArrangement part, int index, int characters, int take)
    {
        double bottom = 0;
        if (index + 1 < arranged.Count && arranged[index + 1].Start < characters)
        {
            var next = arranged[index + 1];
            bottom = next.BlankBefore ? 16 : part.List && next.List ? 10 : 12;
        }
        double start = part.List && part.MarkerLength > 0 ? PrefixWidth(part.Start, part.MarkerLength) : 0;
        bool rtl = FlowDirection == FlowDirection.RightToLeft;
        var block = new Paragraph
        {
            Margin = rtl ? new Thickness(0, 0, start, bottom) : new Thickness(start, 0, 0, bottom),
            Padding = new Thickness(0), TextIndent = -start, FlowDirection = FlowDirection,
            LineHeight = FontSize * 1.8
        };
        AppendSlice(block, part.Start, take);
        return block;
    }

    private void AppendSlice(Paragraph block, int start, int length)
    {
        int end = start + length, cursor = 0;
        foreach (var segment in segments)
        {
            int segStart = cursor, segEnd = cursor + segment.Value.Length; cursor = segEnd;
            int from = Math.Max(start, segStart), to = Math.Min(end, segEnd);
            int local = from;
            while (local < to)
            {
                int limit = local;
                while (limit < to && FullText[limit] is not '\n' and not '\r') limit++;
                if (limit > local)
                {
                    var run = new Run(FullText[local..limit]); Apply(run, segment.Kind); block.Inlines.Add(run);
                }
                if (limit >= to) break;
                block.Inlines.Add(new LineBreak());
                local = limit + 1;
                if (local < to && FullText[limit] == '\r' && FullText[local] == '\n') local++;
            }
        }
    }

    private void Apply(Run run, AdviceInlineKind kind)
    {
        if (kind == AdviceInlineKind.Strong) run.FontWeight = FontWeights.SemiBold;
        if (kind == AdviceInlineKind.Code)
        {
            run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
            run.Background = (Brush)Application.Current.FindResource("Subtle");
            run.FlowDirection = FlowDirection.LeftToRight;
        }
    }

    private double PrefixWidth(int start, int length)
    {
        if (length <= 0) return 0;
        if (measured.TryGetValue(start, out double cached)) return cached;
        string prefix = FullText.Substring(start, length);
        double dpi = 1;
        try { var reading = VisualTreeHelper.GetDpi(this).PixelsPerDip; if (reading > 0) dpi = reading; }
        catch (InvalidOperationException) { dpi = 1; }
        var weight = Document?.FontWeight ?? FontWeights.Normal;
        var family = Document?.FontFamily ?? FontFamily;
        var formatted = new FormattedText(prefix, CultureInfo.CurrentUICulture, FlowDirection,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), FontSize, Brushes.Black, dpi);
        double width = formatted.WidthIncludingTrailingWhitespace;
        width = width > 1 ? width : prefix.Length * FontSize * 0.5;
        measured[start] = width; return width;
    }
}
