using System.Text.Json;
using RopeShelf.Core;

namespace RopeShelf.Windows;

public sealed record TransferLease(string Directory, string FilePath);
public sealed record TransferManifest(string FileName, DateTimeOffset CreatedUtc);

/// <summary>Retain successful drag copies for seven days; only journaled, unmodified lease directories are owned.</summary>
public static class TransferStore
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private const string ManifestName = ".lease.json";
    public static TransferLease Create(string root, string original, DateTimeOffset now)
    {
        var directory = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, Path.GetFileName(original));
        if (!FileOwnership.MayRecycle(root, target, true)) throw new IOException("拖放暂存目录不可用");
        File.Copy(original, target);
        File.WriteAllText(Path.Combine(directory, ManifestName), JsonSerializer.Serialize(new TransferManifest(Path.GetFileName(target), now)));
        return new(directory, target);
    }
    public static int Cleanup(string root, DateTimeOffset now, Action<string> log)
    {
        int removed = 0;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (!Guid.TryParseExact(Path.GetFileName(directory), "N", out _)) continue;
            try
            {
                var marker = Path.Combine(directory, ManifestName);
                if (!FileOwnership.MayRecycle(root, marker, true) || !File.Exists(marker) || new FileInfo(marker).Length > 4096) continue;
                var manifest = JsonSerializer.Deserialize<TransferManifest>(File.ReadAllText(marker));
                if (manifest == null || Path.GetFileName(manifest.FileName) != manifest.FileName || string.IsNullOrWhiteSpace(manifest.FileName)
                    || manifest.FileName == ManifestName || now - manifest.CreatedUtc < Retention) continue;
                var lease = new TransferLease(directory, Path.Combine(directory, manifest.FileName));
                if (Remove(root, lease)) removed++;
            }
            catch (Exception e) { log("Transfer cleanup deferred: " + e.Message); }
        }
        return removed;
    }
    public static bool Remove(string root, TransferLease lease)
    {
        var marker = Path.Combine(lease.Directory, ManifestName);
        if (!FileOwnership.MayRecycle(root, marker, true) || !FileOwnership.MayRecycle(root, lease.FilePath, true)) return false;
        if (!Directory.Exists(lease.Directory)) return true;
        var files = Directory.EnumerateFileSystemEntries(lease.Directory).ToArray();
        if (files.Any(p => !string.Equals(p, marker, StringComparison.OrdinalIgnoreCase) && !string.Equals(p, lease.FilePath, StringComparison.OrdinalIgnoreCase))) return false;
        if (File.Exists(lease.FilePath)) File.Delete(lease.FilePath);
        if (File.Exists(marker)) File.Delete(marker);
        Directory.Delete(lease.Directory); return true;
    }
}
