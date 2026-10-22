using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Baseera.App;

internal static class WindowAppearance
{
    internal static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        int enabled = dark ? 1 : 0;
        // Windows owns the caption, hit targets, system menu, resizing and snap layouts.
        _ = DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    internal static bool HasNativeCaption(Window window)
    {
        long style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -16).ToInt64();
        const long required = 0x00C00000 | 0x00080000 | 0x00020000 | 0x00010000;
        return (style & required) == required;
    }
    internal static bool IsInputEnabled(Window window) => IsWindowEnabled(new WindowInteropHelper(window).Handle);

    internal static void SavePreview(Window window, string path)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("Cannot read preview window bounds.");
        using var bitmap = new System.Drawing.Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try { if (!PrintWindow(handle, dc, 2)) throw new InvalidOperationException("Cannot render native window preview."); }
            finally { graphics.ReleaseHdc(dc); }
        }
        if (DwmGetWindowAttribute(handle, 9, out var visible, Marshal.SizeOf<NativeRect>()) == 0)
        {
            int left = Math.Clamp(visible.Left - bounds.Left, 0, bitmap.Width - 1), top = Math.Clamp(visible.Top - bounds.Top, 0, bitmap.Height - 1);
            int right = Math.Clamp(visible.Right - bounds.Left, left + 1, bitmap.Width), bottom = Math.Clamp(visible.Bottom - bounds.Top, top + 1, bitmap.Height);
            using var cropped = bitmap.Clone(new System.Drawing.Rectangle(left, top, right - left, bottom - top), bitmap.PixelFormat);
            cropped.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        else bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
