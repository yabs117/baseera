using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Baseera.Core;

namespace Baseera.App.Services;

internal static class PackagedAppIcons
{
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width, Height; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string name, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    internal static bool ValidId(string id) => id.Length <= 512 && id.Contains('!') && Regex.IsMatch(id, @"^[A-Za-z0-9_.!\-]+$");
    internal static List<InstalledAppEntry> Read()
    {
        List<InstalledAppEntry> result = [];
        // Shell automation belongs in an STA, separate from the UI and registry worker.
        var thread = new Thread(() =>
        {
            object? shell = null, folder = null, items = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                folder = ((dynamic)shell!).Namespace("shell:AppsFolder"); if (folder is null) return;
                items = ((dynamic)folder).Items(); int count = Math.Min((int)((dynamic)items).Count, 2048);
                for (int i = 0; i < count; i++)
                {
                    object? item = null;
                    try
                    {
                        item = ((dynamic)items).Item(i); string id = (string?)((dynamic)item!).ExtendedProperty("System.AppUserModel.ID") ?? "";
                        if (!ValidId(id)) continue;
                        string name = (string?)((dynamic)item).Name ?? ""; int split = id.IndexOf('_');
                        if (name.Length > 0 && split > 0) result.Add(new(name, "", "", "", "", id[..split], "", id));
                    }
                    catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                    finally { Release(item); }
                }
            }
            catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or TypeLoadException or ArgumentException) { }
            finally { Release(items); Release(folder); Release(shell); }
        }) { IsBackground = true, Name = "Baseera local app identities" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return thread.Join(TimeSpan.FromSeconds(5)) ? result : [];
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    internal static ImageSource? Load(string id)
    {
        if (!ValidId(id)) return null;
        lock (Cache)
        {
            if (Cache.TryGetValue(id, out var cached)) return cached;
            IShellItemImageFactory? factory = null; IntPtr bitmap = IntPtr.Zero; ImageSource? result = null;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName("shell:AppsFolder\\" + id, IntPtr.Zero, ref iid, out factory) == 0 &&
                    factory.GetImage(new Size { Width = 64, Height = 64 }, 0x4 | 0x1, out bitmap) == 0 && bitmap != IntPtr.Zero)
                { var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); result = image; }
            }
            catch (Exception ex) when (ex is COMException or ArgumentException) { }
            finally { if (bitmap != IntPtr.Zero) DeleteObject(bitmap); Release(factory); }
            if (Cache.Count >= 256) Cache.Clear(); Cache[id] = result; return result;
        }
    }
}
