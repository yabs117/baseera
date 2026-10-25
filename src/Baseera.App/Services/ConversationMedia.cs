using System.Windows.Media.Imaging;

namespace Baseera.App.Services;

internal sealed record ErrorVideo(byte[] Bytes, string Mime, string Name, double Seconds, ErrorImage Frames);

internal static class ConversationMedia
{
    internal const int MaxVideoBytes = 12_000_000;
    internal static async Task<ErrorVideo> Video(string path, CancellationToken token)
    {
        var file = new FileInfo(path);
        string mime = file.Extension.ToLowerInvariant() switch
        { ".mp4" => "video/mp4", ".webm" => "video/webm", ".wmv" => "video/wmv", ".avi" => "video/avi", ".mov" => "video/mov", _ => throw new InvalidDataException(I18n.T("chatVideoUnsupported")) };
        if (!file.Exists || file.Length is < 1 or > MaxVideoBytes) throw new InvalidDataException(I18n.T("chatVideoLimit"));
        var player = new MediaPlayer { Volume = 0, ScrubbingEnabled = true };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        player.MediaOpened += (_, _) => opened.TrySetResult();
        player.MediaFailed += (_, _) => opened.TrySetException(new InvalidDataException(I18n.T("chatVideoUnsupported")));
        try
        {
            player.Open(new Uri(file.FullName)); await opened.Task.WaitAsync(deadline.Token);
            if (!player.NaturalDuration.HasTimeSpan || player.NaturalDuration.TimeSpan.TotalSeconds is <= 0 or > 60 ||
                player.NaturalVideoWidth <= 0 || player.NaturalVideoHeight <= 0 || (long)player.NaturalVideoWidth * player.NaturalVideoHeight > 20_000_000)
                throw new InvalidDataException(I18n.T("chatVideoLimit"));
            double seconds = player.NaturalDuration.TimeSpan.TotalSeconds;
            var frames = new List<byte[]>();
            int width = Math.Min(800, player.NaturalVideoWidth), height = Math.Max(1, width * player.NaturalVideoHeight / player.NaturalVideoWidth);
            if (height > 600) { width = Math.Max(1, width * 600 / height); height = 600; }
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen()) { draw.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height)); draw.DrawVideo(player, new Rect(0, 0, width, height)); }
            for (int i = 0; i < 6; i++)
            {
                deadline.Token.ThrowIfCancellationRequested(); player.Position = TimeSpan.FromSeconds(seconds * i / 6);
                player.Play(); await Task.Delay(160, deadline.Token); player.Pause();
                var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = new MemoryStream(); encoder.Save(stream); frames.Add(stream.ToArray());
            }
            await using var source = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var bounded = new MemoryStream(); var buffer = new byte[65536]; int read;
            while ((read = await source.ReadAsync(buffer, deadline.Token)) > 0)
            {
                if (bounded.Length + read > MaxVideoBytes) throw new InvalidDataException(I18n.T("chatVideoLimit"));
                bounded.Write(buffer, 0, read);
            }
            byte[] video = bounded.ToArray();
            if (video.Length is < 1 or > MaxVideoBytes) throw new InvalidDataException(I18n.T("chatVideoLimit"));
            return new(video, mime, file.Name, seconds, ContactSheet(frames, seconds));
        }
        finally { player.Stop(); player.Close(); }
    }
    internal static ErrorImage ContactSheet(IReadOnlyList<byte[]> frames, double seconds)
    {
        if (frames.Count != 6) throw new InvalidDataException("Six video samples are required.");
        using var sheet = new System.Drawing.Bitmap(1800, 760);
        using var graphics = System.Drawing.Graphics.FromImage(sheet); graphics.Clear(System.Drawing.Color.FromArgb(24, 24, 24));
        using var font = new System.Drawing.Font("Segoe UI", 14);
        for (int i = 0; i < frames.Count; i++)
        {
            using var stream = new MemoryStream(frames[i], false); using var frame = System.Drawing.Image.FromStream(stream);
            double scale = Math.Min(600d / frame.Width, 340d / frame.Height); int w = (int)(frame.Width * scale), h = (int)(frame.Height * scale);
            int x = i % 3 * 600, y = i / 3 * 380;
            graphics.DrawImage(frame, x + (600 - w) / 2, y + (340 - h) / 2, w, h);
            graphics.DrawString($"~{seconds * i / 6:0.0}s — sample {i + 1}/6", font, System.Drawing.Brushes.White, x + 12, y + 345);
        }
        using var output = new MemoryStream(); sheet.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return ErrorAssistant.Prepare(output.ToArray(), I18n.T("chatVideoSamples"));
    }
}
