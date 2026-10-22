using Baseera.App.Services;
using System.Windows.Media.Imaging;

namespace Baseera.App;

internal static class SmokeChecks
{
    public static void Run(string output)
    {
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
