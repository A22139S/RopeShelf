using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RopeShelf.App;
using RopeShelf.Core;
using RopeShelf.Windows;

internal static class LargeImageChecks
{
    private const int Width = 3840, Height = 2160, Count = 30;
    public static int Prepare(string root)
    {
        var fixture = Path.Combine(root, "large-images");
        Directory.CreateDirectory(fixture);
        var store = new Storage(Path.Combine(fixture, "data"));
        var pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int p = (y * Width + x) * 4;
                pixels[p] = (byte)(x / 16); pixels[p + 1] = (byte)(y / 9);
                pixels[p + 2] = (byte)((x / 120 + y / 120) % 2 * 180); pixels[p + 3] = 255;
            }
        var items = new List<ShelfItem>();
        for (int i = 0; i < Count; i++)
        {
            pixels[0] = (byte)i;
            var path = Path.Combine(fixture, $"synthetic-4k-{i:00}.png");
            var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(path)) encoder.Save(file);
            items.Add(new ShelfItem(Guid.NewGuid(), path, false, DateTimeOffset.Now, "fixture-" + i, Width, Height, ImageOrigin.Folder, path));
            if (i % 5 == 4) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        }
        store.Save("items.json", items);
        Console.WriteLine("Prepared 30 synthetic 3840x2160 PNG files; no user images used.");
        return 0;
    }
    public static int Run(string root)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/RopeShelf;component/Theme.xaml", UriKind.Relative) });
        var dataRoot = Path.Combine(root, "large-images", "data");
        var samples = new List<object>();
        bool passed = true;
        foreach (var phase in new[] { "first-restore", "cached-restore" })
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            using var process = Process.GetCurrentProcess(); process.Refresh();
            long before = process.PrivateMemorySize64;
            using var controller = new ShelfController(dataRoot, true);
            var watch = Stopwatch.StartNew();
            var task = controller.StartAsync(false);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (task.IsCompleted || watch.Elapsed.TotalSeconds > 120) frame.Continue = false; };
            timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
            if (!task.IsCompleted) throw new TimeoutException("30张4K图恢复超过120秒");
            task.GetAwaiter().GetResult(); watch.Stop(); process.Refresh();
            bool valid = controller.Items.Count == Count && controller.Window.Cards.Children.Count == Count
                && controller.Items.All(i => i.Width == Width && i.Height == Height && File.Exists(i.Path))
                && (phase != "cached-restore" || controller.ThumbnailCacheHits == Count);
            passed &= valid;
            samples.Add(new { Phase = phase, Passed = valid, Images = controller.Items.Count, Width, Height,
                ElapsedMs = watch.Elapsed.TotalMilliseconds, CacheHits = controller.ThumbnailCacheHits,
                PrivateBeforeMiB = before / 1048576d, PrivateAfterMiB = process.PrivateMemorySize64 / 1048576d,
                WorkingSetMiB = process.WorkingSet64 / 1048576d, ProcessPeakWorkingSetMiB = process.PeakWorkingSet64 / 1048576d });
        }
        var result = new { Passed = passed, Timestamp = DateTimeOffset.Now, Samples = samples,
            Limitation = "Synthetic compressible PNGs, hidden cards, same-process second restore; does not verify animation FPS or other image formats." };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "large-image-results.json"), json); Console.WriteLine(json);
        return passed ? 0 : 1;
    }
}
