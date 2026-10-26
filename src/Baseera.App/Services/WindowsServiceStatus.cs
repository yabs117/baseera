using System.Runtime.InteropServices;
using Baseera.Core;
using Microsoft.Win32.SafeHandles;

namespace Baseera.App.Services;

internal static class WindowsServiceStatus
{
    private sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    { public ServiceHandle() : base(true) { } protected override bool ReleaseHandle() => CloseServiceHandle(handle); }
    [StructLayout(LayoutKind.Sequential)] private struct Status
    { public uint Type, State, Accepted, Win32Exit, ServiceExit, Checkpoint, Hint, Process, Flags; }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ServiceHandle OpenSCManager(string? machine, string? database, uint access);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryServiceStatusEx(ServiceHandle service, int level, out Status status, int size, out int needed);
    [DllImport("advapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseServiceHandle(IntPtr service);
    internal static IReadOnlyList<ServiceReading> Read(CancellationToken token)
    {
        using var manager = OpenSCManager(null, null, 1); var result = new List<ServiceReading>();
        foreach (string name in WindowsServiceNames.All)
        {
            token.ThrowIfCancellationRequested(); string state = "unknown";
            if (!manager.IsInvalid)
            {
                using var service = OpenService(manager, name, 4);
                if (!service.IsInvalid && QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _) && status.State is >= 1 and <= 7)
                    state = WindowsServiceNames.States[(int)status.State - 1];
            }
            result.Add(new(name, state));
        }
        return result;
    }
    internal static IReadOnlyDictionary<int, string[]> ProcessMap(CancellationToken token)
    {
        using var manager = OpenSCManager(null, null, 1); var result = new Dictionary<int, List<string>>();
        if (manager.IsInvalid) return new Dictionary<int, string[]>();
        foreach (string name in WindowsServiceNames.All)
        {
            token.ThrowIfCancellationRequested(); using var service = OpenService(manager, name, 4);
            if (!service.IsInvalid && QueryServiceStatusEx(service, 0, out var status, Marshal.SizeOf<Status>(), out _) && status.Process is > 0 and <= int.MaxValue)
            { int id = (int)status.Process; if (!result.TryGetValue(id, out var list)) result[id] = list = []; list.Add(name); }
        }
        return result.ToDictionary(p => p.Key, p => p.Value.ToArray());
    }
}
