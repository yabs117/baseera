using System.Security.Cryptography;
using Baseera.Core;

namespace Baseera.App.Services;

internal interface ICaseRepository
{
    string HostScope { get; }
    int UnreadableCount => 0;
    bool ListLimited => false;
    Task<RepairCaseDocument> Save(RepairCaseDocument value, int expectedRevision, CancellationToken token);
    Task<IReadOnlyList<RepairCaseDocument>> List(CancellationToken token);
}
internal sealed class CaseRepository(string folder) : ICaseRepository
{
    private string? scope;
    public int UnreadableCount { get; private set; }
    public bool ListLimited { get; private set; }
    private FileStream Lock()
    { Directory.CreateDirectory(folder); return new FileStream(Path.Combine(folder, ".store-lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private string PathFor(Guid id) => Path.Combine(folder, id.ToString("N") + ".case");
    public string HostScope
    {
        get
        {
            if (scope is not null) return scope;
            using var guard = Lock(); string file = Path.Combine(folder, ".scope");
            if (File.Exists(file))
            {
                var info = new FileInfo(file);
                if (info.Length > 4096 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid installation scope.");
                byte[] decoded = Settings.ProtectLocal(File.ReadAllBytes(file), true);
                try { if (decoded.Length != 16) throw new InvalidDataException("Invalid installation scope."); scope = new Guid(decoded).ToString("N"); } finally { CryptographicOperations.ZeroMemory(decoded); }
            }
            else { scope = Guid.NewGuid().ToString("N"); File.WriteAllBytes(file, Settings.ProtectLocal(Guid.Parse(scope).ToByteArray(), false)); }
            return scope;
        }
    }
    private RepairCaseDocument Read(string file)
    {
        var info = new FileInfo(file);
        if (info.Length > RepairCaseCodec.MaxBytes + 4096 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid case file.");
        byte[] plain = Settings.ProtectLocal(File.ReadAllBytes(file), true);
        try { return RepairCaseCodec.Read(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public Task<RepairCaseDocument> Save(RepairCaseDocument value, int expectedRevision, CancellationToken token) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); RepairCaseCodec.Validate(value); using var guard = Lock();
        string target = PathFor(value.Id); int actual = File.Exists(target) ? Read(target).Revision : 0;
        if (actual != expectedRevision) throw new IOException("caseConflict");
        var next = RepairCaseCodec.ForStorage(value with { Revision = actual + 1, UpdatedUtc = DateTimeOffset.UtcNow }, value.StoreEvidence, Environment.UserName, Environment.MachineName);
        byte[] serialized = RepairCaseCodec.Write(next); string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            byte[] encrypted = Settings.ProtectLocal(serialized, false);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(encrypted); stream.Flush(true); }
            token.ThrowIfCancellationRequested(); File.Move(temporary, target, true);
        }
        finally { CryptographicOperations.ZeroMemory(serialized); if (File.Exists(temporary)) File.Delete(temporary); }
        return value with { Revision = next.Revision, UpdatedUtc = next.UpdatedUtc };
    }, token);
    public Task<IReadOnlyList<RepairCaseDocument>> List(CancellationToken token) => Task.Run<IReadOnlyList<RepairCaseDocument>>(() =>
    {
        UnreadableCount = 0; ListLimited = false;
        if (!Directory.Exists(folder)) return []; using var guard = Lock(); var list = new List<RepairCaseDocument>();
        var files = Directory.EnumerateFiles(folder, "*.case").OrderByDescending(File.GetLastWriteTimeUtc).Take(201).ToArray(); ListLimited = files.Length > 200;
        foreach (string file in files.Take(200))
        {
            token.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out var id)) continue;
            try
            {
                var value = Read(file); if (value.Id != id) throw new InvalidDataException("Case identity does not match filename.");
                list.Add(value with { Actions = value.Actions.Select(a => a.Status == "pending" ? a with { Status = "completion-unknown" } : a).ToArray() });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or CryptographicException or System.ComponentModel.Win32Exception or ArgumentException)
            { UnreadableCount++; } // Preserve damaged files; one failure must not hide healthy cases.
        }
        return list.OrderByDescending(v => v.UpdatedUtc).ToArray();
    }, token);
}
internal interface IDiagnosticCollector { Task<CaseEvidence> Capture(DiagnosticOptions options, CancellationToken token); }
internal sealed class DiagnosticCollector(ICaseRepository cases) : IDiagnosticCollector
{
    public async Task<CaseEvidence> Capture(DiagnosticOptions options, CancellationToken token)
    {
        var source = new WindowsDiagnosticSource(); string text = await WindowsDiagnostics.Collect(options, token, source);
        bool complete = !text.Contains("[Source unavailable:") && !text.Contains("[Collection deadline") && !text.Contains("[Data limit") && !text.Contains("[Sample limit") &&
            (!options.ServiceStatus || source.LastServices.Count == WindowsServiceNames.All.Count && source.LastServices.All(s => s.State != "unknown")) &&
            (!options.DeviceHealth || source.LastDevice is { CpuPercent: not null, Memory: not null } d && d.Notices.Count == 0);
        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, cases.HostScope, options, text, source.LastDevice, source.LastServices, complete);
    }
}
internal sealed class ApplicationServices
{
    internal ICaseRepository Cases { get; }
    internal IDiagnosticCollector Diagnostics { get; }
    internal TaskHub Tasks { get; } = new();
    internal ApplicationServices(ICaseRepository? cases = null, IDiagnosticCollector? diagnostics = null)
    { Cases = cases ?? new CaseRepository(Path.Combine(Settings.Folder, "Cases")); Diagnostics = diagnostics ?? new DiagnosticCollector(Cases); }
}
