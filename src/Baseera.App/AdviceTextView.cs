using System.Windows.Documents;
using Baseera.Core;

namespace Baseera.App;

internal sealed class AdviceTextView : System.Windows.Controls.RichTextBox
{
    private readonly Paragraph paragraph = new() { Margin = new Thickness(0) };
    private readonly IReadOnlyList<AdviceInline> segments;
    internal string FullText { get; }
    internal string VisibleText { get; private set; } = "";

    internal AdviceTextView(IReadOnlyList<AdviceInline> segments, double size = 14, bool bold = false, bool muted = false, bool code = false)
    {
        this.segments = segments; FullText = string.Concat(segments.Select(s => s.Value));
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
        if (e.Property == FlowDirectionProperty && Document is not null) Document.FlowDirection = FlowDirection;
    }
    internal void RevealTo(int characters)
    {
        string visible = FullText[..characters]; if (visible == VisibleText) return;
        paragraph.Inlines.Clear(); int remaining = characters;
        foreach (var segment in segments)
        {
            if (remaining <= 0) break;
            int length = Math.Min(remaining, segment.Value.Length); var run = new Run(segment.Value[..length]);
            if (segment.Kind == AdviceInlineKind.Strong) run.FontWeight = FontWeights.SemiBold;
            if (segment.Kind == AdviceInlineKind.Code)
            {
                run.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                run.Background = (Brush)Application.Current.FindResource("Subtle");
                run.FlowDirection = FlowDirection.LeftToRight;
            }
            paragraph.Inlines.Add(run); remaining -= length;
        }
        VisibleText = visible;
    }
}
