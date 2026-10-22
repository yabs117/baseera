using System.Security.Cryptography;

namespace Baseera.Core;

// A backup is retained until the restarted app acknowledges that its window loaded.
public static class UpdateTransaction
{
    public static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void Verify(string file, string hash, long size)
    {
        if (new FileInfo(file).Length != size || !string.Equals(Hash(file), hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Update integrity check failed.");
    }

    public static void Replace(string payload, string target, string backup, string oldHash, string newHash, long size)
    {
        if (Path.GetPathRoot(payload) != Path.GetPathRoot(target) || File.Exists(backup)) throw new IOException("Invalid update staging location.");
        Verify(payload, newHash, size);
        if (Hash(target) != oldHash) throw new IOException("The running application changed. Check for updates again.");
        File.Replace(payload, target, backup);
    }

    public static void Rollback(string target, string backup, string newHash)
    {
        if (Hash(target) != newHash) throw new IOException("The application changed after installation; the backup has been retained.");
        File.Replace(backup, target, null);
    }
}
