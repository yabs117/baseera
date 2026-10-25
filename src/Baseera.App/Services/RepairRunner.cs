using System.ComponentModel;
using System.Diagnostics;
using Baseera.Core;

namespace Baseera.App.Services;

internal sealed record RepairRunResult(string ActionId, int? ExitCode, bool Started, DateTimeOffset Finished);

internal sealed class RepairRunner(Func<ProcessStartInfo, Task<int>>? launcher = null)
{
    internal static RepairRunner Shared { get; } = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    internal static ProcessStartInfo StartInfo(RepairAction action)
    {
        // Reload canonical catalog values, never use caller/model supplied arguments or PATH.
        action = RepairCatalog.Get(action.Id);
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), action.Executable))
        { UseShellExecute = true, Verb = action.Administrator ? "runas" : "open", WindowStyle = ProcessWindowStyle.Normal, WorkingDirectory = Environment.SystemDirectory };
        foreach (string arg in action.Arguments) start.ArgumentList.Add(arg);
        return start;
    }
    internal async Task<RepairRunResult> Execute(RepairApproval approval, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!await gate.WaitAsync(0, token)) throw new InvalidOperationException(I18n.T("smartRepairBusy"));
        try
        {
            var action = approval.Consume(); token.ThrowIfCancellationRequested();
            var start = StartInfo(action);
            try
            {
                int exit = await (launcher ?? Launch)(start);
                return new(action.Id, exit, true, DateTimeOffset.UtcNow);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new(action.Id, null, false, DateTimeOffset.UtcNow); }
        }
        finally { gate.Release(); }
    }
    private static async Task<int> Launch(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new IOException("Windows could not start the approved repair tool.");
        // After launch the native tool owns completion. Do not kill SFC/DISM on
        // window close or cancellation: interruption could leave servicing incomplete.
        await process.WaitForExitAsync(); return process.ExitCode;
    }
}
