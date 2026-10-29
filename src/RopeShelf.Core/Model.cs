namespace RopeShelf.Core;

public enum ImageOrigin { Clipboard, Folder }
public sealed record ShelfItem(Guid Id, string Path, bool Owned, DateTimeOffset Added,
    string Fingerprint, int Width, int Height, ImageOrigin Origin, string? ExternalPath = null,
    long SourceFileLength = -1, long SourceModifiedUtcTicks = 0);

public sealed class ShelfSettings
{
    public bool ClipboardEnabled { get; set; } = true;
    public int Capacity { get; set; } = 30;
    public List<string> WatchFolders { get; set; } = [];
    public uint HotkeyModifiers { get; set; } = 3; // Alt + Control
    public uint HotkeyKey { get; set; } = 0x54;
}

/// <summary>Only a short time window deduplicates; reusing an old picture is intentional.</summary>
public sealed class IntakeHistory(TimeSpan window)
{
    private readonly Dictionary<string, (DateTimeOffset At, Guid Id)> recent = [];
    public Guid? Find(string fingerprint, DateTimeOffset now)
    {
        foreach (var key in recent.Where(p => now - p.Value.At > window).Select(p => p.Key).ToArray())
            recent.Remove(key);
        return recent.TryGetValue(fingerprint, out var value) ? value.Id : null;
    }
    public void Remember(ShelfItem item) => recent[item.Fingerprint] = (item.Added, item.Id);
    public void Forget(Guid id)
    {
        foreach (var key in recent.Where(p => p.Value.Id == id).Select(p => p.Key).ToArray()) recent.Remove(key);
    }
}

public static class FileOwnership
{
    public static bool MayRecycle(string inbox, string path, bool owned)
    {
        if (!owned) return false;
        var root = System.IO.Path.GetFullPath(inbox).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        var full = System.IO.Path.GetFullPath(path);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        for (var current = new FileInfo(full) as FileSystemInfo; current != null; current = current switch
        { FileInfo f => f.Directory, DirectoryInfo d => d.Parent, _ => null })
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (string.Equals(current.FullName.TrimEnd(System.IO.Path.DirectorySeparatorChar),
                root.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
