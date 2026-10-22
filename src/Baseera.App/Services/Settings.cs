using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Baseera.App.Services;

public sealed class Preferences
{
    public string Language { get; set; } = "ar";
    public bool Dark { get; set; }
    public bool Onboarded { get; set; }
    public bool ScanEnabled { get; set; }
    public bool TranslationEnabled { get; set; }
    public bool UpdatesEnabled { get; set; }
    public bool ConvertEnabled { get; set; }
    public bool StartWithWindows { get; set; }
    public bool ScanAtStartup { get; set; }
    public string ScanPath { get; set; } = "";
    public bool ExplorerMenu { get; set; }
    public string GeminiModel { get; set; } = "";
    public string TargetLanguage { get; set; } = "Arabic";
    public string ReleasePage { get; set; } = "";
    public List<string> AutoUpdateIds { get; set; } = [];
    public Dictionary<string, string> ManualSources { get; set; } = [];
}

public static class Settings
{
    public static string Folder { get; } = Environment.GetEnvironmentVariable("BASEERA_DATA_DIR") ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Baseera");
    public static Preferences Current { get; private set; } = new();
    public static void Load()
    {
        var file = Path.Combine(Folder, "settings.json");
        if (File.Exists(file))
        {
            try { Current = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file)) ?? new(); }
            catch (JsonException) { Current = new(); }
        }
        if (!new[] { "ar", "en", "fr", "es" }.Contains(Current.Language)) Current.Language = "ar";
    }
    public static void Save()
    {
        Directory.CreateDirectory(Folder);
        var temp = Path.Combine(Folder, "settings.tmp");
        File.WriteAllText(temp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path.Combine(Folder, "settings.json"), true);
    }
    public static void SetKey(string key)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, "secrets.bin");
        if (string.IsNullOrWhiteSpace(key)) { if (File.Exists(path)) File.Delete(path); return; }
        File.WriteAllBytes(path, Protect(Encoding.UTF8.GetBytes(key.Trim()), false));
    }
    public static string GetKey()
    {
        var path = Path.Combine(Folder, "secrets.bin");
        return File.Exists(path) ? Encoding.UTF8.GetString(Protect(File.ReadAllBytes(path), true)) : "";
    }
    private static byte[] Protect(byte[] bytes, bool decrypt)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Marshal.Copy(bytes, 0, input.Data, bytes.Length);
        Blob output = default;
        try
        {
            bool ok = decrypt ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) :
                CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); Array.Clear(bytes); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    public static void ApplyWindowsIntegration()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (Current.StartWithWindows) run.SetValue("Baseera", $"\"{exe}\" --startup"); else run.DeleteValue("Baseera", false);
        const string menuPath = @"Software\Classes\SystemFileAssociations\image\shell\Baseera.Convert";
        if (Current.ConvertEnabled && Current.ExplorerMenu)
        {
            using var menu = Registry.CurrentUser.CreateSubKey(menuPath);
            menu.SetValue("", "Baseera — " + I18n.T("convert"));
            menu.SetValue("Icon", exe);
            menu.SetValue("MultiSelectModel", "Single");
            using var command = menu.CreateSubKey("command");
            command.SetValue("", $"\"{exe}\" --convert \"%1\"");
        }
        else Registry.CurrentUser.DeleteSubKeyTree(menuPath, false);
    }
}
