using System.Xml;
using System.Xml.Linq;
using Baseera.App.Services;

namespace Baseera.App;

internal static class AiProviderBadge
{
    private static readonly Dictionary<string, DrawingImage> Marks = new();

    internal static string Name(string provider, bool chatGpt) => provider switch
    {
        "openai" => chatGpt ? "ChatGPT" : "OpenAI",
        "anthropic" => "Claude",
        "gemini" => "Gemini",
        "xai" => "Grok",
        _ => AiProviders.Get(provider).Name
    };

    // These are bundled, pinned monochrome assets. No remote SVG or user path is loaded.
    internal static System.Windows.Controls.Image Mark(string provider, double size = 25)
    {
        if (!AiProviders.Contains(provider)) throw new ArgumentException("Unknown AI service.", nameof(provider));
        string cacheKey = provider + (Settings.Current.Dark ? "-dark" : "-light");
        if (!Marks.TryGetValue(cacheKey, out var image))
        {
            using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Brand/Providers/{provider}.svg")).Stream;
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var svg = XDocument.Load(reader).Root!;
            var group = new DrawingGroup();
            // Keep the original view box, including purposeful whitespace around the mark.
            group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
            foreach (var path in svg.Descendants().Where(element => element.Name.LocalName == "path"))
            {
                string rule = (string?)path.Attribute("fill-rule") ?? (string?)svg.Attribute("fill-rule") ?? "nonzero";
                var geometry = Geometry.Parse((rule == "evenodd" ? "F0 " : "F1 ") + path.Attribute("d")!.Value);
                group.Children.Add(new GeometryDrawing((Brush)Application.Current.FindResource("Ink"), null, geometry));
            }
            image = new DrawingImage(group); image.Freeze(); Marks.Add(cacheKey, image);
        }
        var view = new System.Windows.Controls.Image
        {
            Source = image, Width = size, Height = size, Stretch = Stretch.Uniform,
            FlowDirection = FlowDirection.LeftToRight, SnapsToDevicePixels = true
        };
        System.Windows.Automation.AutomationProperties.SetName(view, Name(provider, false));
        System.Windows.Automation.AutomationProperties.SetAutomationId(view, "AdviceProviderLogo");
        return view;
    }
}
