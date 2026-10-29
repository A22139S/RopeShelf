using System.Windows.Media.Imaging;
using RopeShelf.Core;

namespace RopeShelf.Windows;

/// <summary>Disposable derived data. A cache failure must never prevent intake or touch an original.</summary>
public sealed class ThumbnailCache
{
    private readonly string root;
    private readonly Action<string> log;
    private readonly bool available;
    public int Hits { get; private set; }
    public ThumbnailCache(string root, Action<string> log)
    {
        this.root = root; this.log = log;
        try { Directory.CreateDirectory(root); available = true; }
        catch (Exception e) { log("Thumbnail cache disabled: " + e.Message); }
    }
    private static bool ValidKey(string key) => key.Length == 64 && key.All(Uri.IsHexDigit);
    public static ShelfItem Stamp(ShelfItem item)
    {
        var file = new FileInfo(item.Path);
        return item with { SourceFileLength = file.Length, SourceModifiedUtcTicks = file.LastWriteTimeUtc.Ticks };
    }
    public BitmapSource? Get(ShelfItem item)
    {
        if (!available || !ValidKey(item.Fingerprint) || item.SourceFileLength < 0) return null;
        try
        {
            var original = new FileInfo(item.Path);
            if (!original.Exists || original.Length != item.SourceFileLength || original.LastWriteTimeUtc.Ticks != item.SourceModifiedUtcTicks) return null;
            var path = Path.Combine(root, item.Fingerprint + ".png");
            if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024 || !FileOwnership.MayRecycle(root, path, true)) return null;
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth > 480 || frame.PixelHeight > 480 || frame.PixelWidth <= 0 || frame.PixelHeight <= 0) return null;
            // Copy into a detached bitmap; decoder metadata is dispatcher-bound even on frozen frames.
            var image = ImageCodec.ReadFull(path); Hits++; return image;
        }
        catch (Exception e) { log("Thumbnail cache miss: " + e.Message); return null; }
    }
    public void Put(ShelfItem item, BitmapSource thumbnail)
    {
        if (!available || !ValidKey(item.Fingerprint) || thumbnail.PixelWidth > 480 || thumbnail.PixelHeight > 480) return;
        try
        {
            var target = Path.Combine(root, item.Fingerprint + ".png");
            if (!FileOwnership.MayRecycle(root, target, true)) return;
            var temporary = target + ".tmp";
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(thumbnail));
            using (var stream = File.Create(temporary)) encoder.Save(stream);
            File.Move(temporary, target, true);
        }
        catch (Exception e) { log("Thumbnail cache write skipped: " + e.Message); }
    }
    public void Prune(IEnumerable<ShelfItem> items)
    {
        if (!available) return;
        try
        {
        var retained = items.Select(i => i.Fingerprint).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            if (!ValidKey(key) || retained.Contains(key) || !FileOwnership.MayRecycle(root, path, true)) continue;
            try { File.Delete(path); } catch (Exception e) { log("Thumbnail cache cleanup deferred: " + e.Message); }
        }
        }
        catch (Exception e) { log("Thumbnail cache enumeration skipped: " + e.Message); }
    }
}
