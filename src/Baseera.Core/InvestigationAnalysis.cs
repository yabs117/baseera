using System.Globalization;
using System.Net;

namespace Baseera.Core;

public sealed record InvestigationFact(string Label, string Value, string Detail = "");
public sealed record InvestigationReport(string Kind, DateTimeOffset Time, IReadOnlyList<InvestigationFact> Facts, IReadOnlyList<string> Limits);
public sealed record ProcessActivity(int Id, long Started, string Name, double? Cpu, long Memory, double? IoBytes);
public sealed record PerformancePoint(DateTimeOffset Time, double? Cpu, double? Memory, double? DiskBytes, double? DiskLatencyMs, IReadOnlyList<ProcessActivity> Processes);
public sealed record Coincidence(string Kind, string Process, int Samples, double? Correlation);
public static class PerformanceEvidence
{
    public static void ValidateHistory(IReadOnlyList<PerformancePoint> points)
    {
        if (points is null || points.Count > 120) throw new InvalidDataException("Invalid performance history.");
        bool Number(double? n, bool percentage = false) => n is null || double.IsFinite(n.Value) && n >= 0 && (!percentage || n <= 100);
        foreach (var p in points)
            if (p is null || !Number(p.Cpu, true) || !Number(p.Memory, true) || !Number(p.DiskBytes) || !Number(p.DiskLatencyMs) || p.Processes is null || p.Processes.Count > 16 || p.Processes.Any(s => s is null || s.Name is null || s.Name.Length > 120 || s.Id < 0 || s.Memory < 0 || !Number(s.Cpu, true) || !Number(s.IoBytes))) throw new InvalidDataException("Invalid performance sample.");
    }
    public static double? Correlation(IReadOnlyList<(double X, double Y)> pairs)
    {
        if (pairs.Count < 4 || pairs.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y))) return null;
        double x = pairs.Average(p => p.X), y = pairs.Average(p => p.Y), a = 0, b = 0, c = 0;
        foreach (var p in pairs) { a += (p.X - x) * (p.Y - y); b += Math.Pow(p.X - x, 2); c += Math.Pow(p.Y - y, 2); }
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(a) || !double.IsFinite(b) || !double.IsFinite(c) || b <= 0 || c <= 0) return null;
        double scale = b * c;
        if (!double.IsFinite(scale) || scale <= 0) return null;
        double value = a / Math.Sqrt(scale);
        return double.IsFinite(value) ? Math.Clamp(value, -1, 1) : null;
    }
    public static IReadOnlyList<Coincidence> Analyze(IReadOnlyList<PerformancePoint> source)
    {
        var points = source.TakeLast(120).ToArray(); ValidateHistory(points); var findings = new List<Coincidence>();
        for (int i = points.Length - 1; i > 0; i--)
            if (points[i].Time - points[i - 1].Time > TimeSpan.FromSeconds(60) || points[i].Time <= points[i - 1].Time) { points = points[i..]; break; }
        foreach (var identity in points.SelectMany(p => p.Processes).Select(p => (p.Id, p.Started, p.Name)).Distinct().Take(1024))
        {
            var samples = points.Select(p => (Point: p, Process: p.Processes.FirstOrDefault(s => s.Id == identity.Id && s.Started == identity.Started))).Where(x => x.Process is not null).ToArray();
            int cpu = samples.Count(x => x.Point.Cpu >= 85 && x.Process!.Cpu >= 25);
            if (cpu >= 3) findings.Add(new("hubCpuCoincidence", DeviceHealth.Label(identity.Name), cpu, null));
            var pairs = samples.Where(x => x.Point.DiskBytes is not null && x.Process!.IoBytes is not null).Select(x => (x.Point.DiskBytes!.Value, x.Process!.IoBytes!.Value)).ToArray();
            var correlation = Correlation(pairs);
            if (correlation >= .6 && samples.Count(x => x.Point.DiskLatencyMs >= 20 && x.Process!.IoBytes > 1048576) >= 3) findings.Add(new("hubIoCoincidence", DeviceHealth.Label(identity.Name), pairs.Length, correlation));
        }
        return findings.Take(12).ToArray();
    }
}
public sealed record NetworkReading(DateTimeOffset Time, string Host, double? DnsMs, int Sent, IReadOnlyList<long> Replies, string AdapterState, string Notice, double? WifiQuality = null, double? WifiLinkMbps = null);
public static class NetworkEvidence
{
    public static string Host(string input)
    {
        input = input.Trim();
        if (IPAddress.TryParse(input, out var ip)) return ip.ToString();
        if (input.Length is < 1 or > 253 || input.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || "/\\:@?#%".Contains(c))) throw new ArgumentException("Enter a hostname or IP, not a URL.");
        string ascii = new IdnMapping().GetAscii(input);
        if (Uri.CheckHostName(ascii) != UriHostNameType.Dns) throw new ArgumentException("Invalid hostname."); return ascii;
    }
    public static void Validate(NetworkReading r)
    {
        if (r is null || r.Host is null || r.AdapterState is null || r.AdapterState.Length > 4096 || r.Notice is null || r.Notice.Length > 100 || r.Replies is null || r.Sent is < 0 or > 6 || r.Replies.Count > r.Sent || r.Replies.Any(n => n is < 0 or > 60000) || r.DnsMs is { } dns && (!double.IsFinite(dns) || dns < 0 || dns > 60000)) throw new InvalidDataException("Invalid network history.");
        _ = Host(r.Host);
        if (r.WifiQuality is { } quality && (!double.IsFinite(quality) || quality is < 0 or > 100) || r.WifiLinkMbps is { } rate && (!double.IsFinite(rate) || rate < 0 || rate > 100000)) throw new InvalidDataException("Invalid Wi-Fi reading.");
    }
    public static double? Loss(NetworkReading r) => r.Replies is null || r.Sent <= 0 || r.Replies.Count > r.Sent ? null : 100d * (r.Sent - r.Replies.Count) / r.Sent;
    public static IReadOnlyList<string> Interpret(NetworkReading r)
    {
        Validate(r);
        var keys = new List<string>();
        if (IPAddress.TryParse(r.Host, out _)) keys.Add("hubDnsNotRequired");
        else if (r.DnsMs is null) keys.Add("hubDnsFailed");
        if (r.Replies.Count == 0) keys.Add("hubIcmpUnknown"); else { if (Loss(r) > 0) keys.Add("hubPacketLoss"); if (r.Replies.Average() > 150) keys.Add("hubLatencyHigh"); }
        keys.Add("hubNetworkLimits"); return keys;
    }
}
