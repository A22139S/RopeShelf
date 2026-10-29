using System.Text.Json;
using RopeShelf.Core;

namespace RopeShelf.Windows;

public sealed class Storage
{
    public string Root { get; }
    public string Inbox => Path.Combine(Root, "Inbox");
    public string Transfers => Path.Combine(Root, "Transfers");
    public string Logs => Path.Combine(Root, "logs");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public Storage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RopeShelf");
        Directory.CreateDirectory(Inbox); Directory.CreateDirectory(Transfers); Directory.CreateDirectory(Logs);
        TransferStore.Cleanup(Transfers, DateTimeOffset.UtcNow, Log);
        foreach (var backup in Directory.EnumerateFiles(Inbox, "*.recycle-backup"))
        {
            var original = backup[..^".recycle-backup".Length];
            if (!FileOwnership.MayRecycle(Inbox, backup, true) || !FileOwnership.MayRecycle(Inbox, original, true)) continue;
            // A crash may have interrupted confirmation. Restore a recoverable copy; never silently discard it.
            if (!File.Exists(original)) { File.Move(backup, original); Log("Recovered interrupted recycle transaction"); }
            else
            {
                var recovery = Path.Combine(Root, "Recovered"); Directory.CreateDirectory(recovery);
                var target = Path.Combine(recovery, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(original));
                if (!FileOwnership.MayRecycle(Root, target, true)) { Log("Recovery destination is unsafe; backup preserved"); continue; }
                File.Move(backup, target);
                Log("Preserved interrupted recycle backup in Recovered");
            }
        }
    }
    public void Log(string message)
    {
        try { File.AppendAllText(Path.Combine(Logs, $"{DateTime.Today:yyyy-MM-dd}.log"), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}"); } catch { }
    }
    public T Load<T>(string name, Func<T> fallback)
    {
        var path = Path.Combine(Root, name);
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? fallback() : fallback(); }
        catch (Exception e) { Log($"Cannot load {name}: {e.Message}. Original preserved."); throw new InvalidDataException($"{name} 无法读取；原文件已保留，请检查数据目录。", e); }
    }
    public void Save<T>(string name, T value)
    {
        var path = Path.Combine(Root, name); var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(file, value, Json); file.Flush(true); }
        File.Move(temp, path, true);
    }
    public string StoreImage(byte[] png)
    {
        var path = Path.Combine(Inbox, $"截图-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png");
        var temp = path + ".tmp"; File.WriteAllBytes(temp, png); File.Move(temp, path); return path;
    }
    public void Recycle(ShelfItem item)
    {
        if (!item.Owned || !File.Exists(item.Path)) return;
        if (!FileOwnership.MayRecycle(Inbox, item.Path, item.Owned)) throw new IOException("文件归属校验失败，已保留文件");
        SafeRecycle.File(item.Path, Log);
    }
}
