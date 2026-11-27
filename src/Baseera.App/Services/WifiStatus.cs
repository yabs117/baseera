using System.Runtime.InteropServices;

namespace Baseera.App.Services;

internal static class WifiStatus
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct InterfaceInfo { public Guid Id; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description; public int State; }
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int kind);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
    internal static (double? Quality, double? LinkMbps) Read()
    {
        IntPtr handle = IntPtr.Zero, list = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(2, IntPtr.Zero, out _, out handle) != 0 || WlanEnumInterfaces(handle, IntPtr.Zero, out list) != 0) return (null, null);
            int count = Marshal.ReadInt32(list); if (count is < 0 or > 32) return (null, null);
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<InterfaceInfo>(list + 8 + i * Marshal.SizeOf<InterfaceInfo>()); if (info.State != 1) continue;
                IntPtr data = IntPtr.Zero;
                try
                {
                    // Current-connection query is read only. Win11 location denial stays unavailable.
                    // SSID/BSSID/profile bytes are not copied, retained or displayed.
                    if (WlanQueryInterface(handle, ref info.Id, 7, IntPtr.Zero, out uint size, out data, out _) != 0 || size < 588) continue;
                    int quality = Marshal.ReadInt32(data, 576); uint rate = unchecked((uint)Marshal.ReadInt32(data, 580));
                    return (quality is >= 0 and <= 100 ? quality : null, rate > 0 ? rate / 1000d : null);
                }
                finally { if (data != IntPtr.Zero) WlanFreeMemory(data); }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception) { }
        finally { if (list != IntPtr.Zero) WlanFreeMemory(list); if (handle != IntPtr.Zero) WlanCloseHandle(handle, IntPtr.Zero); }
        return (null, null);
    }
}
