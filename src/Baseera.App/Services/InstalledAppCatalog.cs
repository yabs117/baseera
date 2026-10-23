using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Baseera.Core;
using Microsoft.Win32;

namespace Baseera.App.Services;

internal sealed record AppListItem(InstalledPackage Package, string Name, string Publisher, ImageSource? Icon);

internal static class InstalledAppCatalog
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object IconGate = new();

    internal static List<InstalledAppEntry> ReadInstalledApps()
    {
        var result = new List<InstalledAppEntry>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", false);
                if (uninstall is null) continue;
                foreach (string name in uninstall.GetSubKeyNames().Take(10000))
                {
                    try
                    {
                        using var entry = uninstall.OpenSubKey(name, false); if (entry is null) continue;
                        string Text(string key) => entry.GetValue(key) as string ?? "";
                        string display = Text("DisplayName"); if (string.IsNullOrWhiteSpace(display) || entry.GetValue("SystemComponent") is int value && value == 1) continue;
                        result.Add(new(display, Text("DisplayVersion"), Text("Publisher"), Text("DisplayIcon"), Text("InstallLocation"), Text("WinGetPackageIdentifier"), name));
                    }
                    catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
                }
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException) { }
        }
        result.AddRange(PackagedAppIcons.Read()); return result;
    }
    internal static List<AppListItem> Decorate(IEnumerable<InstalledPackage> packages, IReadOnlyList<InstalledAppEntry> entries, CancellationToken ct)
    {
        var result = new List<AppListItem>();
        foreach (var package in packages)
        {
            ct.ThrowIfCancellationRequested();
            var installed = InstalledAppIdentity.Match(package.Id, package.Version, entries);
            ImageSource? icon = null;
            if (installed is not null)
                try { icon = FindIcon(installed, package.Id); }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or COMException or SecurityException) { }
            result.Add(new(package, installed?.Name ?? package.Id, installed?.Publisher ?? "", icon));
        }
        return result.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Package.Id).ToList();
    }
    private static ImageSource? FindIcon(InstalledAppEntry app, string id)
    {
        if (app.AppUserModelId.Length > 0) return PackagedAppIcons.Load(app.AppUserModelId);
        var direct = InstalledAppIdentity.ParseIconLocation(app.DisplayIcon);
        if (direct is { } location && ReadIcon(location.Path, location.Index) is { } icon) return icon;
        string folder = Environment.ExpandEnvironmentVariables(app.InstallLocation.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\") || folder.StartsWith("//") || !Directory.Exists(folder)) return null;
        try
        {
            string last = id.Split('.').Last();
            if (last.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && ReadIcon(Path.Combine(folder, last + ".exe"), 0) is { } exact) return exact;
            var candidates = Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly).Take(33)
                .Where(p => !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileNameWithoutExtension(p), "unins|setup|update|crash|helper|service|maint", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray();
            return candidates.Length == 1 ? ReadIcon(candidates[0], 0) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or SecurityException) { return null; }
    }
    internal static ImageSource? ReadIcon(string path, int index = 0)
    {
        if (InstalledAppIdentity.ParseIconLocation('"' + path + "\"," + index) is null) return null;
        try
        {
            var file = new FileInfo(path); if (!file.Exists || file.Length > 512L * 1024 * 1024) return null;
            string key = path + "|" + index + "|" + file.Length + "|" + file.LastWriteTimeUtc.Ticks;
            lock (IconGate)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;
                ImageSource? result = null;
                if (new[] { ".png", ".bmp" }.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                {
                    if (file.Length <= 8 * 1024 * 1024)
                    {
                        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 64;
                        using var stream = File.OpenRead(path); bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); result = bitmap;
                    }
                }
                else
                {
                    IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
                    try
                    {
                        if (SHDefExtractIcon(path, index, 0, out large, out small, 64 | (16 << 16)) == 0 && large != IntPtr.Zero)
                        { var bitmap = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); bitmap.Freeze(); result = bitmap; }
                    }
                    finally { if (large != IntPtr.Zero) DestroyIcon(large); if (small != IntPtr.Zero && small != large) DestroyIcon(small); }
                }
                if (Cache.Count >= 512) Cache.Clear(); Cache[key] = result; return result;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException or System.IO.FileFormatException or SecurityException) { return null; }
    }
}
