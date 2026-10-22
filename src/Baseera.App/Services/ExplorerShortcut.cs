using System.Runtime.InteropServices;
using System.Text;

namespace Baseera.App.Services;

/// <summary>Opt-in Ctrl + right-click over an Explorer window's selected images.</summary>
public sealed class ExplorerShortcut : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    private readonly HookProc callback;
    private IntPtr hook;
    private bool pending;
    public ExplorerShortcut(Action<List<string>> onSelection)
    {
        callback = (code, message, data) =>
        {
            if (code >= 0 && message == (IntPtr)0x204 && (GetAsyncKeyState(0x11) & 0x8000) != 0 && !pending)
            {
                var hwnd = GetForegroundWindow(); var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() is "CabinetWClass" or "ExploreWClass")
                {
                    pending = true;
                    Application.Current.Dispatcher.BeginInvoke(() =>
                    {
                        try
                        {
                            var files = SelectedImages(hwnd);
                            if (files.Count > 0) onSelection(files);
                        }
                        catch (COMException) { }
                        finally { pending = false; }
                    });
                    // Keep Explorer's normal behavior if no file was selected; the app opens only for selected images.
                }
            }
            return CallNextHookEx(hook, code, message, data);
        };
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private static List<string> SelectedImages(IntPtr hwnd)
    {
        var result = new List<string>();
        var type = Type.GetTypeFromProgID("Shell.Application"); if (type is null) return result;
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic windows = shell.Windows();
            try
            {
                foreach (dynamic window in windows)
                {
                    try
                    {
                        if ((long)window.HWND != hwnd.ToInt64()) continue;
                        dynamic selected = window.Document.SelectedItems();
                        try
                        {
                            foreach (dynamic item in selected)
                            {
                                try
                                {
                                    string path = item.Path;
                                    if (File.Exists(path) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant())) result.Add(path);
                                }
                                finally { Marshal.FinalReleaseComObject(item); }
                            }
                        }
                        finally { Marshal.FinalReleaseComObject(selected); }
                    }
                    finally { Marshal.FinalReleaseComObject(window); }
                }
            }
            finally { Marshal.FinalReleaseComObject(windows); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return result;
    }
    public void Dispose() { if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
