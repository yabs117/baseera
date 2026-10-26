using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Text.Json;
using Baseera.Core;

namespace Baseera.App.Services;

internal static class FileTrust
{
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative { public uint Size; public IntPtr Path, Handle, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData
    { public uint Size; public IntPtr Callback, Client; public uint Ui, Revocation, Choice; public IntPtr File; public uint State; public IntPtr StateData, Url; public uint Flags, Context; public IntPtr Signature; }
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll")] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll")] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counter, uint counterSigner);
    [StructLayout(LayoutKind.Sequential)] private struct Signer { public uint Size; public System.Runtime.InteropServices.ComTypes.FILETIME Time; public uint Count; public IntPtr Certificates; }
    [StructLayout(LayoutKind.Sequential)] private struct ProviderCertificate { public uint Size; public IntPtr Context; }
    [StructLayout(LayoutKind.Sequential)] private struct CertificateContext { public uint Encoding; public IntPtr Bytes; public uint Length; }
    internal static async Task<InvestigationReport> Inspect(string path, Action? beforeVerification = null)
    {
        LocalFileScope.RequireFile(Path.GetDirectoryName(Path.GetFullPath(path))!, path); var file = new System.IO.FileInfo(path);
        if (file.Length > 1073741824) throw new InvalidDataException("Signature inspection file limit.");
        var snapshot = new StorageFile(file.FullName, file.Length, file.LastWriteTimeUtc);
        // Keep the same file open without write/delete sharing through both the
        // digest and native trust check so the two results identify one file.
        await using var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (locked.Length > 1073741824) throw new InvalidDataException("Signature inspection file limit.");
        string hash = Convert.ToHexString(await SHA256.HashDataAsync(locked)).ToLowerInvariant();
        beforeVerification?.Invoke(); // Deterministic in-process mutation test; no CLI callback.
        locked.Position = 0;
        IntPtr name = Marshal.StringToCoTaskMemUni(path), native = IntPtr.Zero; int code; string publisher = "";
        try
        {
            var info = new FileInfoNative { Size = (uint)Marshal.SizeOf<FileInfoNative>(), Path = name, Handle = locked.SafeFileHandle.DangerousGetHandle() }; native = Marshal.AllocCoTaskMem(Marshal.SizeOf<FileInfoNative>()); Marshal.StructureToPtr(info, native, false);
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = native, State = 1, Flags = 0x1000 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try
            {
                code = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
                try
                {
                    IntPtr provider = WTHelperProvDataFromStateData(data.StateData), signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                    if (signer != IntPtr.Zero)
                    {
                        var chain = Marshal.PtrToStructure<Signer>(signer);
                        if (chain.Count > 0 && chain.Certificates != IntPtr.Zero)
                        {
                            var item = Marshal.PtrToStructure<ProviderCertificate>(chain.Certificates); var context = Marshal.PtrToStructure<CertificateContext>(item.Context);
                            if (context.Length is > 0 and <= 1048576) { var encoded = new byte[context.Length]; Marshal.Copy(context.Bytes, encoded, 0, encoded.Length); using var cert = X509CertificateLoader.LoadCertificate(encoded); publisher = DeviceHealth.Label(cert.Subject); }
                        }
                    }
                }
                catch { }
            }
            finally { data.State = 2; WinVerifyTrust(IntPtr.Zero, ref action, ref data); }
        }
        finally { Marshal.FreeCoTaskMem(name); if (native != IntPtr.Zero) Marshal.FreeCoTaskMem(native); }
        if (!LocalFileScope.Unchanged(snapshot)) throw new IOException("File changed during verification.");
        var facts = new List<InvestigationFact> { new("hubSha256", hash), new("hubAuthenticode", code == 0 ? "state:hubSignatureValid" : unchecked((uint)code) == 0x800B0100 ? "state:hubNotSigned" : "state:hubUntrusted", "0x" + unchecked((uint)code).ToString("X8")) };
        if (publisher.Length > 0) facts.Add(new("hubPublisher", publisher));
        return new("security", DateTimeOffset.UtcNow, facts, ["hubSignatureLimits"]);
    }
    internal static async Task<InvestigationReport> Reputation(string hash, string key, CancellationToken token, HttpClient? transport = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-f0-9]{64}$") || key.Length is < 1 or > 500) throw new InvalidDataException("Reputation request invalid.");
        using var own = transport is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) } : null;
        var http = transport ?? own!; using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.virustotal.com/api/v3/files/" + hash); request.Headers.Add("x-apikey", key);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return new("security", DateTimeOffset.UtcNow, [new("hubReputation", "state:hubHashUnknown")], ["hubReputationLimits"]);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1048576) throw new IOException("Reputation unavailable; check provider limits/key.");
        await using var stream = await response.Content.ReadAsStreamAsync(token); using var memory = new MemoryStream(); byte[] buffer = new byte[8192];
        while (true) { int count = await stream.ReadAsync(buffer, token); if (count == 0) break; if (memory.Length + count > 1048576) throw new InvalidDataException("Reputation response limit."); memory.Write(buffer, 0, count); }
        using var json = JsonDocument.Parse(memory.ToArray(), new JsonDocumentOptions { MaxDepth = 32 }); var attr = json.RootElement.GetProperty("data").GetProperty("attributes");
        var stats = attr.GetProperty("last_analysis_stats"); int total = 0, flagged = 0;
        foreach (var s in stats.EnumerateObject()) { int n = s.Value.GetInt32(); if (n is < 0 or > 10000) throw new InvalidDataException("Invalid reputation counts."); total += n; if (s.Name is "malicious" or "suspicious") flagged += n; }
        var facts = new List<InvestigationFact> { new("hubReputation", flagged + " / " + total), new("hubSha256", hash) };
        if (attr.TryGetProperty("last_analysis_date", out var date)) facts.Add(new("hubReputationDate", DateTimeOffset.FromUnixTimeSeconds(date.GetInt64()).ToString("u", CultureInfo.InvariantCulture)));
        return new("security", DateTimeOffset.UtcNow, facts, ["hubReputationLimits"]);
    }
}
