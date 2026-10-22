using System.Globalization;
using System.Text;
using System.Windows.Media.Imaging;

namespace Baseera.App.Services;

public static class ImageConverter
{
    public static BitmapSource Load(string path)
    {
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Image exceeds 64 MB.");
        using var input = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        if ((long)frame.PixelWidth * frame.PixelHeight > 40_000_000) throw new InvalidDataException("Image exceeds 40 megapixels.");
        frame.Freeze(); return frame;
    }
    private static BitmapSource Flatten(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var area = new Rect(0, 0, source.PixelWidth, source.PixelHeight);
            dc.DrawRectangle(Brushes.White, null, area); dc.DrawImage(source, area);
        }
        var bitmap = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    public static string UniquePath(string folder, string stem, string extension)
    {
        string path = Path.Combine(folder, stem + extension);
        for (int i = 1; File.Exists(path); i++) path = Path.Combine(folder, stem + " (" + i + ")" + extension);
        return path;
    }
    public static string ConvertImage(string source, string folder, string format, int quality = 90)
    {
        BitmapEncoder encoder = format switch
        {
            "PNG" => new PngBitmapEncoder(), "JPEG" => new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) },
            "BMP" => new BmpBitmapEncoder(), "TIFF" => new TiffBitmapEncoder(), _ => throw new ArgumentException("Unsupported output format.")
        };
        var bitmap = Load(source);
        encoder.Frames.Add(BitmapFrame.Create(format == "JPEG" ? Flatten(bitmap) : bitmap));
        var output = UniquePath(folder, Path.GetFileNameWithoutExtension(source), format == "JPEG" ? ".jpg" : "." + format.ToLowerInvariant());
        using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        encoder.Save(stream); return output;
    }
    public static string ToPdf(IReadOnlyList<string> sources, string output, int quality = 90)
    {
        if (sources.Count is 0 or > 100) throw new ArgumentException("Choose between 1 and 100 images.");
        if (File.Exists(output)) throw new IOException("Output already exists; choose a new name.");
        string temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                var offsets = new List<long> { 0 };
                void Write(string s) => stream.Write(Encoding.ASCII.GetBytes(s));
                void Object(int id, string body) { offsets.Add(stream.Position); Write($"{id} 0 obj\n{body}\nendobj\n"); }
                Write("%PDF-1.4\n%Baseera\n");
                Object(1, "<< /Type /Catalog /Pages 2 0 R >>");
                Object(2, $"<< /Type /Pages /Count {sources.Count} /Kids [" + string.Join(" ", Enumerable.Range(0, sources.Count).Select(i => $"{3 + i * 3} 0 R")) + "] >>");
                for (int i = 0; i < sources.Count; i++)
                {
                    var image = Flatten(Load(sources[i]));
                    var jpeg = new JpegBitmapEncoder { QualityLevel = Math.Clamp(quality, 1, 100) };
                    jpeg.Frames.Add(BitmapFrame.Create(image)); using var bytes = new MemoryStream(); jpeg.Save(bytes);
                    double scale = Math.Min(595.0 / image.PixelWidth, 842.0 / image.PixelHeight);
                    string w = (image.PixelWidth * scale).ToString("0.###", CultureInfo.InvariantCulture);
                    string h = (image.PixelHeight * scale).ToString("0.###", CultureInfo.InvariantCulture);
                    int page = 3 + i * 3, img = page + 1, content = page + 2;
                    Object(page, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {w} {h}] /Resources << /XObject << /Im0 {img} 0 R >> >> /Contents {content} 0 R >>");
                    offsets.Add(stream.Position);
                    Write($"{img} 0 obj\n<< /Type /XObject /Subtype /Image /Width {image.PixelWidth} /Height {image.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {bytes.Length} >>\nstream\n");
                    bytes.Position = 0; bytes.CopyTo(stream); Write("\nendstream\nendobj\n");
                    string drawing = $"q\n{w} 0 0 {h} 0 0 cm\n/Im0 Do\nQ\n";
                    Object(content, $"<< /Length {Encoding.ASCII.GetByteCount(drawing)} >>\nstream\n{drawing}endstream");
                }
                long xref = stream.Position;
                Write($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
                foreach (long offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                Write($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            }
            File.Move(temp, output); return output;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
