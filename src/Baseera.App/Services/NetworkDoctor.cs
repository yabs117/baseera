using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using Baseera.Core;

namespace Baseera.App.Services;

internal static class NetworkDoctor
{
    internal static async Task<NetworkReading> Measure(string input, CancellationToken token, Func<string, CancellationToken, Task<IPAddress[]>>? resolve = null,
        Func<IPAddress, CancellationToken, Task<long?>>? ping = null)
    {
        string host = NetworkEvidence.Host(input); var clock = Stopwatch.StartNew(); IPAddress[] addresses = []; double? dns = null; string notice = "";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(18));
        try { addresses = await (resolve?.Invoke(host, timeout.Token) ?? Dns.GetHostAddressesAsync(host, timeout.Token)); dns = clock.Elapsed.TotalMilliseconds; }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException) { notice = "hubDnsFailed"; }
        var replies = new List<long>(); int sent = 0;
        if (addresses.FirstOrDefault() is { } target)
            for (int n = 0; n < 6; n++)
            {
                timeout.Token.ThrowIfCancellationRequested(); sent++;
                try
                {
                    long? duration;
                    if (ping is not null) duration = await ping(target, timeout.Token);
                    else { using var probe = new Ping(); var result = await probe.SendPingAsync(target, TimeSpan.FromMilliseconds(1200), new byte[32], null, timeout.Token); duration = result.Status == IPStatus.Success ? result.RoundtripTime : null; }
                    if (duration is >= 0) replies.Add(duration.Value);
                }
                catch (PingException) { notice = "hubIcmpUnknown"; }
                if (n < 5) await Task.Delay(250, timeout.Token);
            }
        string adapter = string.Join(", ", NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet).Take(12).Select(n => DeviceHealth.Label(n.Name) + "=" + n.OperationalStatus));
        var wifi = WifiStatus.Read();
        return new(DateTimeOffset.UtcNow, host, dns, sent, replies, adapter, notice, wifi.Quality, wifi.LinkMbps);
    }
}
