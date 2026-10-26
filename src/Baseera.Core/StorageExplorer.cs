using System.Diagnostics;
using System.Security.Cryptography;

namespace Baseera.Core;

public sealed record StorageFile(string Path, long Bytes, DateTime LastWriteUtc);
public sealed record StorageFolder(string Path, long Bytes);
public sealed record DuplicateSet(string Sha256, long Bytes, IReadOnlyList<StorageFile> Files);
public sealed record StorageReport(string Root, DateTimeOffset Time, long Bytes, int Entries, IReadOnlyList<StorageFolder> Folders,
    IReadOnlyList<StorageFile> Largest, IReadOnlyList<DuplicateSet> Duplicates, IReadOnlyList<string> Notices);
public sealed record StorageLimits(int Entries = 50000, int Seconds = 25, long HashBytes = 1073741824, int HashFiles = 100, long MaxHashFile = 268435456);

public static class LocalFileScope
{
    public static string Root(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Path.IsPathFullyQualified(path) || full.StartsWith(@"\\") || full.Contains('\0') || !Directory.Exists(full)) throw new InvalidDataException("Invalid local folder.");
        string? volume = Path.GetPathRoot(full);
        if (volume is null || new DriveInfo(volume).DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new InvalidDataException("Only local fixed or removable volumes are supported.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked folders are excluded.");
        for (string? parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(full)); parent is not null; parent = Path.GetDirectoryName(parent))
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked ancestor folders are excluded.");
        return Path.TrimEndingDirectorySeparator(full);
    }
    public static bool Contains(string root, string path) => Path.GetFullPath(path).StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static void RequireFile(string root, string path)
    {
        path = Path.GetFullPath(path); root = Root(root);
        if (!Contains(root, path) || !File.Exists(path) || Path.GetFileName(path).Contains(':')) throw new InvalidDataException("File is outside its allowed scope.");
        for (string? current = path; current is not null && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked paths are excluded.");
    }
    public static IReadOnlyList<StorageFile> Walk(string folder, StorageLimits limits, List<string> notices, CancellationToken token)
    {
        string root = Root(folder); var stack = new Stack<string>(); stack.Push(root); var files = new List<StorageFile>(); int entries = 0; var clock = Stopwatch.StartNew();
        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested(); string current = stack.Pop();
            try
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(current))
                {
                    token.ThrowIfCancellationRequested();
                    if (++entries > limits.Entries || clock.Elapsed.TotalSeconds > limits.Seconds) { notices.Add("hubPartial"); return files; }
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { notices.Add("hubLinksSkipped"); continue; }
                        if ((attributes & FileAttributes.Directory) != 0) stack.Push(path);
                        else { var file = new FileInfo(path); if (file.Length >= 0) files.Add(new(path, file.Length, file.LastWriteTimeUtc)); }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notices.Add("hubUnreadable"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { notices.Add("hubUnreadable"); }
        }
        return files;
    }
    public static bool Unchanged(StorageFile file)
    { var current = new FileInfo(file.Path); return current.Exists && current.Length == file.Bytes && current.LastWriteTimeUtc == file.LastWriteUtc; }
    public static async Task<string> Hash(StorageFile file, CancellationToken token)
    {
        if (!Unchanged(file)) throw new IOException("File changed.");
        await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, token);
        if (!Unchanged(file)) throw new IOException("File changed."); return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
public static class StorageExplorer
{
    public static async Task<StorageReport> Inspect(string folder, bool duplicates, CancellationToken token, StorageLimits? supplied = null)
    {
        var limits = supplied ?? new(); var notices = new List<string>(); string root = LocalFileScope.Root(folder);
        var files = LocalFileScope.Walk(root, limits, notices, token); var folders = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase); long bytes = 0;
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested(); bytes = checked(bytes + file.Bytes); string relative = Path.GetRelativePath(root, file.Path); int split = relative.IndexOf(Path.DirectorySeparatorChar);
            string name = split < 0 ? root : Path.Combine(root, relative[..split]); folders[name] = checked(folders.GetValueOrDefault(name) + file.Bytes);
        }
        var hashed = new List<(StorageFile File, string Hash)>(); long budget = 0; int count = 0;
        if (duplicates)
            foreach (var group in files.Where(f => f.Bytes > 0).GroupBy(f => f.Bytes).Where(g => g.Skip(1).Any()).OrderBy(g => g.Key))
                foreach (var file in group)
                {
                    token.ThrowIfCancellationRequested();
                    if (file.Bytes > limits.MaxHashFile || budget + file.Bytes > limits.HashBytes || count >= limits.HashFiles) { notices.Add("hubHashLimit"); continue; }
                    count++; budget += file.Bytes;
                    try { LocalFileScope.RequireFile(root, file.Path); hashed.Add((file, await LocalFileScope.Hash(file, token))); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { notices.Add("hubChangedFile"); }
                }
        var matches = hashed.GroupBy(x => (x.File.Bytes, x.Hash)).Where(g => g.Count() > 1).Select(g => new DuplicateSet(g.Key.Hash, g.Key.Bytes, g.Select(x => x.File).ToArray())).Take(100).ToArray();
        return new(root, DateTimeOffset.UtcNow, bytes, files.Count, folders.Select(x => new StorageFolder(x.Key, x.Value)).OrderByDescending(x => x.Bytes).Take(30).ToArray(), files.OrderByDescending(x => x.Bytes).Take(50).ToArray(), matches, notices.Distinct().ToArray());
    }
}
public sealed class CleanupApproval
{
    private readonly StorageFile[] files; private readonly DateTimeOffset expires; private int consumed;
    private CleanupApproval(StorageFile[] files) { this.files = files; expires = DateTimeOffset.UtcNow.AddMinutes(2); }
    public static IReadOnlyList<StorageFile> Candidates(string root, CancellationToken token)
    {
        var notices = new List<string>(); var allowed = new[] { ".tmp", ".temp", ".log", ".dmp", ".etl" };
        bool Writable(StorageFile file) { try { return (File.GetAttributes(file.Path) & (FileAttributes.System | FileAttributes.ReadOnly)) == 0; } catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } }
        return LocalFileScope.Walk(root, new(10000, 5), notices, token).Where(f => f.LastWriteUtc < DateTime.UtcNow.AddDays(-7) && f.Bytes <= 67108864 && allowed.Contains(Path.GetExtension(f.Path).ToLowerInvariant()) && Writable(f)).Take(200).ToArray();
    }
    public static CleanupApproval Confirm(IEnumerable<StorageFile> selected, bool acknowledged)
    { var files = selected.ToArray(); if (!acknowledged || files.Length is < 1 or > 200 || files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length) throw new InvalidOperationException("Explicit bounded selection required."); return new(files); }
    public IReadOnlyList<StorageFile> Consume(string allowedRoot)
    {
        if (Interlocked.Exchange(ref consumed, 1) != 0 || DateTimeOffset.UtcNow > expires) throw new InvalidOperationException("Expired or consumed cleanup approval.");
        foreach (var file in files)
        {
            LocalFileScope.RequireFile(allowedRoot, file.Path);
            if (!LocalFileScope.Unchanged(file) || file.LastWriteUtc >= DateTime.UtcNow.AddDays(-7) || file.Bytes > 67108864 || !new[] { ".tmp", ".temp", ".log", ".dmp", ".etl" }.Contains(Path.GetExtension(file.Path).ToLowerInvariant()) || (File.GetAttributes(file.Path) & (FileAttributes.System | FileAttributes.ReadOnly)) != 0) throw new IOException("Cleanup preview changed or is not allowed.");
            using var check = File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        return files;
    }
}
