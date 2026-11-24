using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using Baseera.Core;

namespace Baseera.App.Services;

internal interface IWindowsDiagnosticSource
{
    string SystemSummary();
    string DeviceHealth(CancellationToken token) => "[Device health source unavailable.]";
    string Services(CancellationToken token) => "[Service status source unavailable.]";
    string Drivers(CancellationToken token) => "[Source unavailable: driver inventory.]";
    IEnumerable<string> Events(string channel, int hours, string matchText, CancellationToken token);
}

internal sealed class WindowsDiagnosticSource : IWindowsDiagnosticSource
{
    internal DeviceSnapshot? LastDevice { get; private set; }
    internal IReadOnlyList<ServiceReading> LastServices { get; private set; } = [];
    public string DeviceHealth(CancellationToken token) { LastDevice = DeviceMonitor.Collect(true, token).GetAwaiter().GetResult(); return DeviceMonitor.DiagnosticText(LastDevice); }
    public string Services(CancellationToken token)
    { LastServices = WindowsServiceStatus.Read(token); return "Fixed service status sample; stopped/trigger-start is not proof of a fault.\n" + string.Join("\n", LastServices.Select(s => s.Name + ": " + s.State)); }
    public string Drivers(CancellationToken token)
    { var report = ProbeRunner.Read<InvestigationReport>("drivers", token).GetAwaiter().GetResult(); return (report.Limits.Any(l => l is "hubUnavailable" or "hubPartial") ? "[Source unavailable: driver inventory partial.]\n" : "") + string.Join("\n", report.Facts.Select(f => f.Label + ": " + f.Value + " " + f.Detail)) + "\n" + string.Join("; ", report.Limits); }
    public string SystemSummary()
    {
        long free = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace / (1024 * 1024);
        return $"Windows version/build: {Environment.OSVersion.Version}\nOS architecture: {RuntimeInformation.OSArchitecture}\nLogical processors: {Environment.ProcessorCount}\nSystem drive free MiB: {free}\nUptime hours: {TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours:0.0}";
    }
    public IEnumerable<string> Events(string channel, int hours, string matchText, CancellationToken token)
    {
        if (channel is not ("Application" or "System") || hours is < 1 or > 72) throw new ArgumentException("Invalid event query.");
        var query = new EventLogQuery(channel, PathType.LogName,
            $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {hours * 3600000L}]]]" ) { ReverseDirection = true };
        using var reader = new EventLogReader(query) { BatchSize = 8 };
        using var cancel = token.Register(reader.CancelReading);
        matchText = matchText.Trim(); int matched = 0;
        for (int i = 0; i < (matchText.Length == 0 ? 15 : 200) && matched < 15; i++)
        {
            token.ThrowIfCancellationRequested();
            using var item = reader.ReadEvent(TimeSpan.FromMilliseconds(400));
            if (item is null) yield break;
            string message;
            try { message = item.FormatDescription() ?? "Message unavailable."; }
            catch (EventLogException) { message = "Message unavailable; event ID and provider retained."; }
            if (message.Length > 1600) message = message[..(char.IsHighSurrogate(message[1599]) ? 1599 : 1600)] + " [truncated]";
            string record = $"{item.TimeCreated?.ToUniversalTime():O} | {channel} | {item.ProviderName} | ID {item.Id} | Level {item.Level}\n{message}";
            if (!Matches(record, matchText)) continue;
            matched++; yield return record;
        }
    }
    internal static bool Matches(string record, string matchText) => record.Contains(matchText.Trim(), StringComparison.OrdinalIgnoreCase);
}

internal static class WindowsDiagnostics
{
    internal static async Task<string> Collect(DiagnosticOptions options, CancellationToken token, IWindowsDiagnosticSource? source = null)
    {
        options.Validate(); source ??= new WindowsDiagnosticSource();
        if (options.ExternalReport) throw new InvalidDataException("Refresh this report in its original investigation center.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await Task.Run(() =>
        {
            var text = new StringBuilder("Baseera Smart Repair — local read-only diagnostics\n");
            text.AppendLine($"Collected UTC: {DateTimeOffset.UtcNow:O}; lookback: {options.Hours} h.");
            text.AppendLine("Scope: selected sources only; at most 15 critical/error events per channel. This is not a complete system audit. Redaction is best effort; review the text before sharing.\n");
            if (!string.IsNullOrWhiteSpace(options.MatchText))
                text.AppendLine("Literal text filter: " + System.Text.Json.JsonSerializer.Serialize(options.MatchText.Trim()) + "; searches at most 200 recent critical/error entries per channel within the time range and collection deadline. Matches cover event headers and the first 1600 message characters only. Older/nonmatching events are omitted.\n");
            void Read(string label, Func<IEnumerable<string>> read)
            {
                text.AppendLine("--- " + label + " ---");
                token.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested) { text.AppendLine("[Collection deadline reached; source not read.]"); return; }
                try
                {
                    int count = 0;
                    foreach (var line in read().Take(15))
                    {
                        timeout.Token.ThrowIfCancellationRequested();
                        if (text.Length + line.Length > DiagnosticPrivacy.MaxCharacters - 500) { text.AppendLine("[Data limit reached; remaining data omitted.]"); break; }
                        text.AppendLine(line); count++;
                    }
                    if (count == 0) text.AppendLine("[No matching events returned. This does not prove the absence of a problem.]");
                    if (count == 15) text.AppendLine("[Sample limit reached; additional matching events may exist.]");
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { text.AppendLine("[Collection deadline reached; source incomplete.]"); }
                catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
                { text.AppendLine("[Source unavailable: " + ex.GetType().Name + "; no elevation was requested.]"); }
                text.AppendLine();
            }
            if (options.SystemInfo) Read("System summary", () => new[] { source.SystemSummary() });
            if (options.DeviceHealth) Read("Device health and performance snapshot", () => new[] { source.DeviceHealth(timeout.Token) });
            if (options.ServiceStatus) Read("Selected Windows service states", () => new[] { source.Services(timeout.Token) });
            if (options.Drivers) Read("Driver/PnP inventory", () => new[] { source.Drivers(timeout.Token) });
            if (options.ApplicationEvents) Read("Application critical/errors", () => source.Events("Application", options.Hours, options.MatchText, timeout.Token));
            if (options.SystemEvents) Read("System critical/errors", () => source.Events("System", options.Hours, options.MatchText, timeout.Token));
            token.ThrowIfCancellationRequested();
            return DiagnosticPrivacy.Sanitize(text.ToString(), Environment.UserName, Environment.MachineName);
        }, token);
    }
}
