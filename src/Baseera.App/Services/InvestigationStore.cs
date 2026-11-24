using System.Security.Cryptography;
using System.Text.Json;

namespace Baseera.App.Services;

internal static class InvestigationStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static string FileFor(string kind)
    { if (!new[] { "network", "performance", "timeline", "search" }.Contains(kind)) throw new ArgumentException("Unknown local history."); return Path.Combine(Settings.Folder, "Investigations", kind + ".data"); }
    internal static async Task Save<T>(string kind, T value, CancellationToken token)
    {
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(value);
        try
        {
            if (plain.Length > 16777216) throw new InvalidDataException("Local index/history limit.");
            await Gate.WaitAsync(token);
            try
            {
                string path = FileFor(kind), folder = Path.GetDirectoryName(path)!;
                Directory.CreateDirectory(folder);
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 ||
                    File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked local history is excluded.");
                string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    byte[] encrypted = Settings.ProtectLocal(plain, false);
                    await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { await stream.WriteAsync(encrypted, token); stream.Flush(true); }
                    token.ThrowIfCancellationRequested(); File.Move(temp, path, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            finally { Gate.Release(); }
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    internal static async Task<T?> Load<T>(string kind, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            string path = FileFor(kind); if (!File.Exists(path)) return default; var file = new FileInfo(path);
            if ((File.GetAttributes(Path.GetDirectoryName(path)!) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked local history is excluded.");
            if (file.Length > 16781312 || (file.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Invalid local history.");
            byte[] plain = Settings.ProtectLocal(await File.ReadAllBytesAsync(path, token), true);
            try { return JsonSerializer.Deserialize<T>(plain, new JsonSerializerOptions { MaxDepth = 32 }); }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
        finally { Gate.Release(); }
    }
}
