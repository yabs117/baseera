using Baseera.App.Services;
using System.Windows.Media.Imaging;

namespace Baseera.App;

internal static class SmokeChecks
{
    public static void Run(string output)
    {
        var typeface = new Typeface(Brand.ArabicFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out var arabic) || !arabic.CharacterToGlyphMap.ContainsKey(0x628))
            throw new Exception("Bundled Arabic font did not load.");
        var shaped = new FormattedText("بصيرة", System.Globalization.CultureInfo.GetCultureInfo("ar"), FlowDirection.RightToLeft,
            new Typeface(Brand.InterfaceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 24, Brushes.Black, 1);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawText(shaped, new System.Windows.Point());
        static IEnumerable<string> FontSources(Drawing drawing)
        {
            if (drawing is GlyphRunDrawing run) yield return run.GlyphRun.GlyphTypeface.FontUri.OriginalString;
            if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var source in FontSources(child)) yield return source;
        }
        var runs = FontSources(visual.Drawing).ToArray();
        File.WriteAllText(Path.Combine(output, "brand-font.txt"), string.Join(Environment.NewLine, runs));
        if (runs.Length == 0 || runs.Any(uri => !uri.Contains("Elgharib-Hayyakum-Allah-Taweel-Medium.ttf", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Arabic text fell back to an installed font.");
        var latinSources = new List<string>();
        foreach (var (weight, style) in new[] { (FontWeights.Normal, FontStyles.Normal), (FontWeights.Medium, FontStyles.Normal), (FontWeights.SemiBold, FontStyles.Normal), (FontWeights.Bold, FontStyles.Normal), (FontWeights.Normal, FontStyles.Italic) })
        {
            var latin = new FormattedText("Baseera Settings — Français Español", System.Globalization.CultureInfo.GetCultureInfo("en"), FlowDirection.LeftToRight,
                new Typeface(Brand.InterfaceFont, style, weight, FontStretches.Normal), 24, Brushes.Black, 1);
            var preview = new DrawingVisual(); using (var drawing = preview.RenderOpen()) drawing.DrawText(latin, new System.Windows.Point());
            var sources = FontSources(preview.Drawing).ToArray(); latinSources.AddRange(sources);
            if (sources.Length == 0 || sources.Any(uri => !uri.Contains("NotoSerif-", StringComparison.OrdinalIgnoreCase))) throw new Exception("Latin text fell back from bundled Noto Serif.");
        }
        File.WriteAllLines(Path.Combine(output, "noto-font.txt"), latinSources);
        foreach (var values in I18n.Words.Values) if (values.Length != 4 || values.Any(string.IsNullOrWhiteSpace)) throw new Exception("Missing translation.");
        var source = Path.Combine(output, "sample.png");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] { 0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 0, 0, 0 }, 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(source)) encoder.Save(file);
        foreach (var format in new[] { "PNG", "JPEG", "BMP", "TIFF" })
        {
            var result = ImageConverter.ConvertImage(source, output, format);
            var converted = ImageConverter.Load(result);
            if (converted.PixelWidth != 2 || converted.PixelHeight != 2) throw new Exception("Conversion dimensions changed.");
        }
        var pdf = ImageConverter.UniquePath(output, "sample", ".pdf");
        ImageConverter.ToPdf(new[] { source, source }, pdf);
        var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
        if (!text.StartsWith("%PDF-1.4") || !text.Contains("/Count 2") || !text.EndsWith("%%EOF\n")) throw new Exception("Invalid PDF structure.");
        var blocks = WindowTranslator.ParseBlocks("{\"blocks\":[{\"text\":\"مرحبا\",\"x\":0.1,\"y\":0.1,\"width\":0.5,\"height\":0.1},{\"text\":\"bad\",\"x\":-1,\"y\":0,\"width\":1,\"height\":1}]}");
        if (blocks.Count != 1) throw new Exception("Translation bounds validation failed.");
        Settings.SetKey("baseera-smoke-key-not-a-real-credential");
        if (Settings.GetKey() != "baseera-smoke-key-not-a-real-credential") throw new Exception("DPAPI roundtrip failed.");
        Settings.SetKey("");
    }
}
