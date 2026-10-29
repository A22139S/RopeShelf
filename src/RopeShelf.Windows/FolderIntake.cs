using System.Collections.Concurrent;

namespace RopeShelf.Windows;

public sealed class FolderIntake : IDisposable
{
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly ConcurrentDictionary<string, byte> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> known = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource cancellation = new();
    private readonly DateTime launched = DateTime.UtcNow;
    private readonly HashSet<string> desired = new(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new();
    private readonly Timer retryTimer;
    public FolderIntake() { retryTimer = new Timer(_ => RetryMissing(), null, 2000, 2000); }
    public event Action<string, DecodedImage>? ImageReceived;
    public event Action<string>? Error;
    public void Reset(IEnumerable<string> folders)
    { lock (gate) { foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); desired.Clear(); Watch(folders); } }
    private void RetryMissing()
    {
        lock (gate)
        {
            if (cancellation.IsCancellationRequested) return;
            var active = watchers.Select(w => w.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Watch(desired.Where(p => !active.Contains(p) && Directory.Exists(p)).ToArray());
        }
    }
    public void Watch(IEnumerable<string> folders)
    {
        lock (gate)
        {
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            desired.Add(folder);
            try
            {
                if (!Directory.Exists(folder)) { Error?.Invoke("监视目录不存在：" + folder); continue; }
                var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 32 * 1024 };
                watcher.Created += (_, e) => Queue(e.FullPath);
                watcher.Changed += (_, e) => Queue(e.FullPath);
                watcher.Renamed += (_, e) => Queue(e.FullPath);
                watcher.Deleted += (sender, e) => known.TryRemove(e.FullPath, out _);
                watcher.Error += (_, _) => { Error?.Invoke("目录通知中断，正在补扫：" + folder); Scan(folder); };
                watchers.Add(watcher); watcher.EnableRaisingEvents = true;
                Scan(folder);
            }
            catch (Exception e) { Error?.Invoke("无法监视目录：" + e.Message); }
        }
        }
    }
    private void Scan(string folder)
    {
        try { foreach (var path in Directory.EnumerateFiles(folder).Take(20000)) Queue(path); }
        catch (Exception e) { Error?.Invoke("无法读取监视目录：" + e.Message); }
    }
    private void Queue(string path)
    {
        if (cancellation.IsCancellationRequested || !ImageCodec.Extensions.Contains(Path.GetExtension(path)) || known.ContainsKey(path)) return;
        try { if (File.Exists(path) && File.GetCreationTimeUtc(path) < launched) { known.TryAdd(path, 0); return; } } catch { }
        if (pending.TryAdd(path, 0)) _ = ReadAsync(path);
    }
    private async Task ReadAsync(string path)
    {
        try
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    var before = new FileInfo(path); var size = before.Length; var modified = before.LastWriteTimeUtc;
                    if (size > ImageCodec.MaxBytes) throw new InvalidDataException("图片超过 64 MB");
                    await Task.Delay(180, cancellation.Token);
                    var after = new FileInfo(path);
                    if (size != after.Length || modified != after.LastWriteTimeUtc) { await Task.Delay(200, cancellation.Token); continue; }
                    var decoded = await Task.Run(() => ImageCodec.FromFile(path), cancellation.Token);
                    if (!cancellation.IsCancellationRequested && known.TryAdd(path, 0)) ImageReceived?.Invoke(path, decoded);
                    return;
                }
                catch (InvalidDataException e) { Error?.Invoke(Path.GetFileName(path) + "：" + e.Message); return; }
                catch (Exception) when (attempt < 9 && !cancellation.IsCancellationRequested) { await Task.Delay(150 * (attempt + 1), cancellation.Token); }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Error?.Invoke("图片未接收：" + e.Message); }
        finally { pending.TryRemove(path, out _); }
    }
    public void Dispose() { cancellation.Cancel(); retryTimer.Dispose(); lock (gate) { foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear(); } }
}
