namespace Baseera.App;

// A generated silent MJPEG AVI, used only by smoke checks. No downloaded media.
internal static class VideoFixture
{
    internal static void Write(string path)
    {
        using var frame = new System.Drawing.Bitmap(320, 160);
        using var graphics = System.Drawing.Graphics.FromImage(frame); graphics.Clear(System.Drawing.Color.CornflowerBlue);
        using var font = new System.Drawing.Font("Arial", 18); graphics.DrawString("Fixture error 0x80070005", font, System.Drawing.Brushes.White, 8, 50);
        using var image = new MemoryStream(); frame.Save(image, System.Drawing.Imaging.ImageFormat.Jpeg); byte[] jpeg = image.ToArray();
        using var file = File.Create(path); using var writer = new BinaryWriter(file);
        void Tag(string text) => writer.Write(System.Text.Encoding.ASCII.GetBytes(text));
        long Begin(string tag) { Tag(tag); long size = file.Position; writer.Write(0); return size; }
        void End(long size) { long end = file.Position; file.Position = size; writer.Write((int)(end - size - 4)); file.Position = end; if ((end & 1) != 0) writer.Write((byte)0); }
        long riff = Begin("RIFF"); Tag("AVI "); long hdrl = Begin("LIST"); Tag("hdrl");
        long avih = Begin("avih"); writer.Write(100000); writer.Write(0); writer.Write(0); writer.Write(0x10); writer.Write(20); writer.Write(0); writer.Write(1); writer.Write(jpeg.Length); writer.Write(320); writer.Write(160); writer.Write(new byte[16]); End(avih);
        long strl = Begin("LIST"); Tag("strl"); long strh = Begin("strh"); Tag("vids"); Tag("MJPG"); writer.Write(0); writer.Write((short)0); writer.Write((short)0); writer.Write(0); writer.Write(1); writer.Write(10); writer.Write(0); writer.Write(20); writer.Write(jpeg.Length); writer.Write(uint.MaxValue); writer.Write(0); writer.Write((short)0); writer.Write((short)0); writer.Write((short)320); writer.Write((short)160); End(strh);
        long strf = Begin("strf"); writer.Write(40); writer.Write(320); writer.Write(160); writer.Write((short)1); writer.Write((short)24); Tag("MJPG"); writer.Write(jpeg.Length); writer.Write(new byte[16]); End(strf); End(strl); End(hdrl);
        long movi = Begin("LIST"); Tag("movi"); long origin = file.Position - 4; var offsets = new List<int>();
        for (int i = 0; i < 20; i++) { offsets.Add((int)(file.Position - origin)); long chunk = Begin("00dc"); writer.Write(jpeg); End(chunk); }
        End(movi); long index = Begin("idx1"); foreach (int offset in offsets) { Tag("00dc"); writer.Write(0x10); writer.Write(offset); writer.Write(jpeg.Length); } End(index); End(riff);
    }
}
