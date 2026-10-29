using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RopeShelf.Core;
using RopeShelf.Windows;

internal static class Program
{
    private static readonly List<object> Results = [];
    [STAThread]
    public static int Main(string[] args)
    {
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/tests");
        Directory.CreateDirectory(root);
        if (args.Contains("--large-prepare")) return LargeImageChecks.Prepare(root);
        if (args.Contains("--large-check")) return LargeImageChecks.Run(root);
        if (args.Contains("--parent-recycle")) return ParentRecycleProbe.Run(root);
        if (args.Contains("--clipboard")) return ClipboardChecks.Run(root);
        if (args.Contains("--window")) return WindowChecks.Run(root);
        if (args.Contains("--recycle"))
        {
            var storage = new Storage(Path.Combine(root, "recycle-" + Guid.NewGuid().ToString("N")));
            var fixture = storage.StoreImage(ImageCodec.FromBitmap(Fixture()).Png);
            var item = new ShelfItem(Guid.NewGuid(), fixture, true, DateTimeOffset.Now, "test", 2, 2, ImageOrigin.Clipboard);
            bool success; string error = "";
            try { storage.Recycle(item); success = !File.Exists(fixture); }
            catch (Exception e) { success = false; error = e.Message; }
            bool preserved = File.Exists(fixture);
            bool bytesMatch = preserved && File.ReadAllBytes(fixture).SequenceEqual(ImageCodec.FromBitmap(Fixture()).Png);
            var evidence = new { Passed = success, SourcePreserved = preserved, SourceBytesMatch = bytesMatch, Error = error };
            File.WriteAllText(Path.Combine(root, "recycle-results.json"), JsonSerializer.Serialize(evidence));
            Console.WriteLine(JsonSerializer.Serialize(evidence)); return success ? 0 : 1;
        }
        Test("展开：悬停不足300ms不展开，到时展开", () =>
        {
            var state = new RevealState(); var t = DateTimeOffset.Now;
            state.Tick(t, true, false, false, true); Equal(RevealMode.HoverPending, state.Mode);
            state.Tick(t.AddMilliseconds(299), true, false, false, true); Assert(!state.Visible);
            state.Tick(t.AddMilliseconds(301), true, false, false, true); Assert(state.Visible);
        });
        Test("全屏：自动展示受抑制，明确唤出允许", () =>
        {
            var state = new RevealState(); var t = DateTimeOffset.Now; state.Show(t, true);
            state.Tick(t, false, false, true, true); Assert(!state.Visible);
            state.Show(t, false); state.Tick(t, false, false, true, true); Assert(state.Visible);
        });
        Test("全屏：悬停展开不算快捷键主动唤出", () =>
        {
            var state = new RevealState(); var t = DateTimeOffset.Now;
            state.Tick(t, true, false, false, true); state.Tick(t.AddMilliseconds(301), true, false, false, true); Assert(state.Visible);
            state.Tick(t.AddMilliseconds(400), false, false, true, true); Assert(!state.Visible);
        });
        Test("新图：不覆盖已明确唤出的展开状态", () =>
        {
            var state = new RevealState(); var t = DateTimeOffset.Now; state.Show(t, false);
            state.Show(t.AddMilliseconds(100), true); Equal(RevealMode.Revealed, state.Mode);
            state.Tick(t.AddMilliseconds(200), false, true, true, true); Assert(state.Visible);
        });
        Test("快捷键收起：指针仍在热区时不立即重新展开", () =>
        {
            var state = new RevealState(); var t = DateTimeOffset.Now; state.Show(t, false); state.Hide();
            state.Tick(t, true, false, false, true); state.Tick(t.AddMilliseconds(400), true, false, false, true); Assert(!state.Visible);
            state.Tick(t.AddMilliseconds(500), false, false, false, true);
            state.Tick(t.AddMilliseconds(600), true, false, false, true); state.Tick(t.AddMilliseconds(901), true, false, false, true); Assert(state.Visible);
        });
        Test("菜单/拖放：多个保持引用均释放前不收起", () =>
        {
            var state = new RevealState(); var t = DateTimeOffset.Now; state.Show(t, true); state.Hold(); state.Hold();
            state.Tick(t.AddSeconds(10), false, false, true, true); Assert(state.Visible);
            state.Release(); state.Tick(t.AddSeconds(11), false, false, true, true); Assert(state.Visible);
            state.Release(); state.Tick(t.AddSeconds(12), false, false, true, true); Assert(!state.Visible);
            state.Release(); Equal(0, state.Holds);
        });
        Test("新图展示1.5s，移入后接管，离开350ms后隐藏", () =>
        {
            var state = new RevealState(); var t = DateTimeOffset.Now; state.Show(t, true);
            state.Tick(t.AddMilliseconds(1499), false, false, false, true); Assert(state.Visible);
            state.Tick(t.AddMilliseconds(1501), false, false, false, true); Assert(!state.Visible);
            state.Show(t, true); state.Tick(t.AddMilliseconds(100), false, true, false, true); Equal(RevealMode.Revealed, state.Mode);
            state.Tick(t.AddSeconds(3), false, false, false, true); Assert(state.Visible);
            state.Tick(t.AddMilliseconds(3351), false, false, false, true); Assert(!state.Visible);
        });
        Test("去重：窗口内合并，过期或移除后允许重收", () =>
        {
            var t = DateTimeOffset.Now; var item = new ShelfItem(Guid.NewGuid(), "x", true, t, "hash", 1, 1, ImageOrigin.Clipboard);
            var history = new IntakeHistory(TimeSpan.FromSeconds(6)); history.Remember(item);
            Equal(item.Id, history.Find("hash", t.AddSeconds(5))!.Value); Assert(history.Find("hash", t.AddSeconds(7)) == null);
            history.Remember(item); history.Forget(item.Id); Assert(history.Find("hash", t) == null);
        });
        Test("文件归属：拒绝外部路径、相邻前缀和路径穿越", () =>
        {
            var inbox = Path.Combine(root, "ownership", "Inbox"); Directory.CreateDirectory(inbox);
            var own = Path.Combine(inbox, "one.png"); File.WriteAllText(own, "fixture");
            Assert(FileOwnership.MayRecycle(inbox, own, true)); Assert(!FileOwnership.MayRecycle(inbox, own, false));
            Assert(!FileOwnership.MayRecycle(inbox, Path.Combine(inbox, "..", "outside.png"), true));
            Assert(!FileOwnership.MayRecycle(inbox, inbox + "Backup/test.png", true));
        });
        Test("回收站：仅接受回收站目标证据，外部文件不执行删除", () =>
        {
            Assert(!SafeRecycle.WasRecycled(0, 0)); Assert(!SafeRecycle.WasRecycled(-1, 1)); Assert(SafeRecycle.WasRecycled(0, 1));
            var store = new Storage(Path.Combine(root, "external-guard"));
            var external = Path.Combine(store.Root, "external.txt"); File.WriteAllText(external, "keep");
            store.Recycle(new ShelfItem(Guid.NewGuid(), external, false, DateTimeOffset.Now, "x", 1, 1, ImageOrigin.Folder));
            Equal("keep", File.ReadAllText(external));
        });
        Test("图像：PNG/BMP编码差异仍得到相同像素指纹", () =>
        {
            var bitmap = Fixture(); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            var bmp = new BmpBitmapEncoder(); bmp.Frames.Add(BitmapFrame.Create(bitmap));
            using var a = new MemoryStream(); using var b = new MemoryStream(); png.Save(a); bmp.Save(b);
            Equal(ImageCodec.FromBytes(a.ToArray()).Fingerprint, ImageCodec.FromBytes(b.ToArray()).Fingerprint);
        });
        Test("文件收图：不重编码文件，跨来源指纹与缩略图保持一致", () =>
        {
            var image = ImageCodec.FromBitmap(Fixture()); var path = Path.Combine(root, "path-source.png"); File.WriteAllBytes(path, image.Png);
            var file = ImageCodec.FromFile(path); Equal(0, file.Png.Length); Equal(image.Fingerprint, file.Fingerprint);
            Equal(image.Thumb.PixelWidth, file.Thumb.PixelWidth); Assert(file.Thumb.IsFrozen); Assert(File.ReadAllBytes(path).SequenceEqual(image.Png));
        });
        Test("图像：透明PNG保持透明，DIB零alpha修复", () =>
        {
            var raw = new byte[] { 30, 60, 90, 0 };
            var bitmap = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, raw, 4);
            var data = ImageCodec.FromBitmap(bitmap); var decoded = ImageCodec.FromBytes(data.Png);
            var px = new byte[4]; new FormatConvertedBitmap(decoded.Thumb, PixelFormats.Bgra32, null, 0).CopyPixels(px, 4, 0); Equal((byte)0, px[3]);
            var repaired = ClipboardService.RepairOpaqueDib(bitmap); repaired.CopyPixels(px, 4, 0); Equal((byte)255, px[3]); Equal((byte)30, px[0]);
        });
        Test("图像：坏数据被拒绝，缩略图有界且可跨线程", () =>
        {
            bool failed = false; try { ImageCodec.FromBytes([1, 2, 3]); } catch { failed = true; } Assert(failed);
            var bitmap = new WriteableBitmap(1800, 1200, 96, 96, PixelFormats.Bgr32, null); var image = ImageCodec.FromBitmap(bitmap);
            Assert(image.Thumb.PixelWidth <= 480 && image.Thumb.IsFrozen); Equal(1800, image.Width);
        });
        Test("存储：原子替换和读取损坏时保留原文件", () =>
        {
            var store = new Storage(Path.Combine(root, "storage")); store.Save("settings.json", new ShelfSettings { Capacity = 30 });
            Equal(30, store.Load("settings.json", () => new ShelfSettings()).Capacity);
            store.Save("settings.json", new ShelfSettings { Capacity = 20 }); Equal(20, store.Load("settings.json", () => new ShelfSettings()).Capacity);
            var path = Path.Combine(store.Root, "broken.json"); File.WriteAllText(path, "{broken");
            bool failed = false; try { store.Load("broken.json", () => new ShelfSettings()); } catch (InvalidDataException) { failed = true; }
            Assert(failed); Equal("{broken", File.ReadAllText(path));
        });
        Test("回收中断恢复：缺失原图从副本恢复，已有原图不覆盖", () =>
        {
            var directory = Path.Combine(root, "recovery-" + Guid.NewGuid().ToString("N")); var inbox = Path.Combine(directory, "Inbox"); Directory.CreateDirectory(inbox);
            var missing = Path.Combine(inbox, "missing.png"); File.WriteAllText(missing + ".recycle-backup", "recover");
            var existing = Path.Combine(inbox, "existing.png"); File.WriteAllText(existing, "current"); File.WriteAllText(existing + ".recycle-backup", "older");
            _ = new Storage(directory); Equal("recover", File.ReadAllText(missing)); Equal("current", File.ReadAllText(existing));
            var recovered = Directory.GetFiles(Path.Combine(directory, "Recovered")); Equal(1, recovered.Length); Equal("older", File.ReadAllText(recovered[0]));
        });
        Test("缩略图缓存：命中、源文件改变失效、坏缓存回退和路径保护", () =>
        {
            var directory = Path.Combine(root, "thumbnail-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var original = Path.Combine(directory, "source.png"); var image = ImageCodec.FromBitmap(Fixture()); File.WriteAllBytes(original, image.Png);
            var item = ThumbnailCache.Stamp(new ShelfItem(Guid.NewGuid(), original, false, DateTimeOffset.Now, image.Fingerprint, 2, 2, ImageOrigin.Folder));
            var cacheRoot = Path.Combine(directory, "Thumbnails"); var cache = new ThumbnailCache(cacheRoot, _ => { });
            cache.Put(item, image.Thumb); Assert(cache.Get(item) is { IsFrozen: true });
            File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddMinutes(1)); Assert(cache.Get(item) == null);
            item = ThumbnailCache.Stamp(item); File.WriteAllText(Path.Combine(cacheRoot, item.Fingerprint + ".png"), "bad"); Assert(cache.Get(item) == null);
            Assert(cache.Get(item with { Fingerprint = "../escape" }) == null);
            cache.Put(item, image.Thumb); cache.Prune([]); Assert(File.Exists(original)); Equal(0, Directory.GetFiles(cacheRoot, "*.png").Length);
        });
        Test("缩略图缓存不可用时退化运行，不修改原有文件", () =>
        {
            var blocked = Path.Combine(root, "cache-blocked-" + Guid.NewGuid().ToString("N")); File.WriteAllText(blocked, "keep");
            var cache = new ThumbnailCache(blocked, _ => { }); var image = ImageCodec.FromBitmap(Fixture());
            var item = new ShelfItem(Guid.NewGuid(), blocked, false, DateTimeOffset.Now, image.Fingerprint, 2, 2, ImageOrigin.Folder);
            cache.Put(item, image.Thumb); Assert(cache.Get(item) == null); cache.Prune([]); Equal("keep", File.ReadAllText(blocked));
        });
        Test("拖放暂存：七天内保留，过期只清理有记录副本，原图不受影响", () =>
        {
            var directory = Path.Combine(root, "transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var original = Path.Combine(directory, "source.txt"); File.WriteAllText(original, "original");
            var transfers = Path.Combine(directory, "Transfers"); Directory.CreateDirectory(transfers); var now = DateTimeOffset.UtcNow;
            var recent = TransferStore.Create(transfers, original, now); var expired = TransferStore.Create(transfers, original, now.AddDays(-8));
            var unknown = Path.Combine(transfers, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(unknown); File.WriteAllText(Path.Combine(unknown, "keep.txt"), "keep");
            Equal(1, TransferStore.Cleanup(transfers, now, _ => { })); Assert(File.Exists(recent.FilePath)); Assert(!Directory.Exists(expired.Directory));
            Assert(File.Exists(Path.Combine(unknown, "keep.txt"))); Equal("original", File.ReadAllText(original));
        });
        Test("目录：忽略旧图、延迟写入后接收、重复通知只一次", () => FolderTest(root).GetAwaiter().GetResult());
        Test("目录：启动时不存在的截图目录稍后创建仍能接收", () => MissingFolderTest(root).GetAwaiter().GetResult());
        var json = JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "results.json"), json);
        return Results.Any(r => !(bool)r.GetType().GetProperty("Passed")!.GetValue(r)!) ? 1 : 0;
    }
    private static async Task FolderTest(string root)
    {
        var folder = Path.Combine(root, "watch-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var image = ImageCodec.FromBitmap(Fixture());
        var old = Path.Combine(folder, "old.png"); File.WriteAllBytes(old, image.Png); File.SetCreationTimeUtc(old, DateTime.UtcNow.AddMinutes(-1));
        using var intake = new FolderIntake(); int count = 0; var received = new TaskCompletionSource();
        intake.ImageReceived += (_, _) => { Interlocked.Increment(ref count); received.TrySetResult(); };
        intake.Watch([folder]); await Task.Delay(300); Equal(0, count);
        var next = Path.Combine(folder, "new.png");
        await using (var stream = new FileStream(next, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        { await stream.WriteAsync(image.Png.AsMemory(0, 20)); await stream.FlushAsync(); await Task.Delay(350); await stream.WriteAsync(image.Png.AsMemory(20)); }
        await received.Task.WaitAsync(TimeSpan.FromSeconds(8));
        File.SetLastWriteTimeUtc(next, DateTime.UtcNow); await Task.Delay(600); Equal(1, count);
    }
    private static async Task MissingFolderTest(string root)
    {
        var folder = Path.Combine(root, "missing-" + Guid.NewGuid().ToString("N"));
        using var intake = new FolderIntake(); var received = new TaskCompletionSource();
        intake.ImageReceived += (_, _) => received.TrySetResult(); intake.Watch([folder]);
        Directory.CreateDirectory(folder); var image = ImageCodec.FromBitmap(Fixture());
        var arrived = Path.Combine(folder, "arrived.png");
        File.WriteAllBytes(arrived, image.Png);
        // Files introduced in a newly created directory are new arrivals even if metadata is older.
        File.SetCreationTimeUtc(arrived, DateTime.UtcNow.AddMinutes(-1));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(7));
    }
    private static BitmapSource Fixture()
    {
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null,
            new byte[] { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 }, 8);
        bitmap.Freeze(); return bitmap;
    }
    private static void Test(string name, Action action)
    { try { action(); Results.Add(new { Name = name, Passed = true, Error = "" }); Console.WriteLine("PASS " + name); } catch (Exception e) { Results.Add(new { Name = name, Passed = false, Error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); } }
    private static void Assert(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
}
