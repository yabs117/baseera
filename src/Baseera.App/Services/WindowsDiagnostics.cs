using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using Baseera.Core;

namespace Baseera.App.Services;

internal interface IWindowsDiagnosticSource
{
    string SystemSummary();
    IEnumerable<string> Events(string channel, int hours, CancellationToken token);
}

internal sealed class WindowsDiagnosticSource : IWindowsDiagnosticSource
{
    public string SystemSummary()
    {
        long free = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!).AvailableFreeSpace / (1024 * 1024);
        return $"Windows version/build: {Environment.OSVersion.Version}\nOS architecture: {RuntimeInformation.OSArchitecture}\nLogical processors: {Environment.ProcessorCount}\nSystem drive free MiB: {free}\nUptime hours: {TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours:0.0}";
    }
    public IEnumerable<string> Events(string channel, int hours, CancellationToken token)
    {
        if (channel is not ("Application" or "System") || hours is < 1 or > 72) throw new ArgumentException("Invalid event query.");
        var query = new EventLogQuery(channel, PathType.LogName,
            $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {hours * 3600000L}]]]" ) { ReverseDirection = true };
        using var reader = new EventLogReader(query) { BatchSize = 8 };
        using var cancel = token.Register(reader.CancelReading);
        for (int i = 0; i < 15; i++)
        {
            token.ThrowIfCancellationRequested();
            using var item = reader.ReadEvent(TimeSpan.FromMilliseconds(400));
            if (item is null) yield break;
            string message;
            try { message = item.FormatDescription() ?? "Message unavailable."; }
            catch (EventLogException) { message = "Message unavailable; event ID and provider retained."; }
            if (message.Length > 1600) message = message[..(char.IsHighSurrogate(message[1599]) ? 1599 : 1600)] + " [truncated]";
            yield return $"{item.TimeCreated?.ToUniversalTime():O} | {channel} | {item.ProviderName} | ID {item.Id} | Level {item.Level}\n{message}";
        }
    }
}

internal static class WindowsDiagnostics
{
    internal static async Task<string> Collect(DiagnosticOptions options, CancellationToken token, IWindowsDiagnosticSource? source = null)
    {
        options.Validate(); source ??= new WindowsDiagnosticSource();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        return await Task.Run(() =>
        {
            var text = new StringBuilder("Baseera Smart Repair — local read-only diagnostics\n");
            text.AppendLine($"Collected UTC: {DateTimeOffset.UtcNow:O}; lookback: {options.Hours} h.");
            text.AppendLine("Scope: selected sources only; at most 15 critical/error events per channel. This is not a complete system audit. Redaction is best effort; review the text before sharing.\n");
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
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { text.AppendLine("[Collection deadline reached; source incomplete.]"); }
                catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or IOException or System.ComponentModel.Win32Exception)
                { text.AppendLine("[Source unavailable: " + ex.GetType().Name + "; no elevation was requested.]"); }
                text.AppendLine();
            }
            if (options.SystemInfo) Read("System summary", () => new[] { source.SystemSummary() });
            if (options.ApplicationEvents) Read("Application critical/errors", () => source.Events("Application", options.Hours, timeout.Token));
            if (options.SystemEvents) Read("System critical/errors", () => source.Events("System", options.Hours, timeout.Token));
            token.ThrowIfCancellationRequested();
            return DiagnosticPrivacy.Sanitize(text.ToString(), Environment.UserName, Environment.MachineName);
        }, token);
    }
}
