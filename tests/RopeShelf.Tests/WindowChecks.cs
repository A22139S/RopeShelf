using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using RopeShelf.App;
using RopeShelf.Windows;

internal static class WindowChecks
{
    [DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
    public static int Run(string root)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/RopeShelf;component/Theme.xaml", UriKind.Relative) });
        var results = new List<object>();
        var scenarioRoot = Path.Combine(root, "window-" + Guid.NewGuid().ToString("N"));
        using var controller = new ShelfController(scenarioRoot, true);
        var foreground = Native.GetForegroundWindow();
        var start = controller.StartAsync(true); Pump(() => start.IsCompleted); start.GetAwaiter().GetResult(); Pump(null, 500);
        var window = controller.Window;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { Name = name, Passed = true, Error = "" }); Console.WriteLine("PASS " + name); }
            catch (Exception e) { results.Add(new { Name = name, Passed = false, Error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e.Message); }
        }
        Check("原生浮层：实际可见，显示后未夺走前台焦点", () =>
        {
            if (!Native.IsWindowVisible(window.Handle)) throw new Exception("浮层原生窗口未显示");
            if (Native.GetForegroundWindow() != foreground) throw new Exception($"前台窗口在检查期间改变：before={foreground}, after={Native.GetForegroundWindow()}, shelf={window.Handle}");
        });
        Check("原生浮层：空白命中穿透，卡片命中保留，不激活", () =>
        {
            window.UpdateLayout(); var empty = window.PointToScreen(new Point(window.ActualWidth - 8, 235));
            nint Pack(Point p) => (nint)(((int)p.X & 0xFFFF) | (((int)p.Y & 0xFFFF) << 16));
            if (SendMessage(window.Handle, 0x84, 0, Pack(empty)) != -1) throw new Exception("空白区域未返回HTTRANSPARENT");
            var card = window.PointToScreen(new Point(90, 100));
            if (SendMessage(window.Handle, 0x84, 0, Pack(card)) == -1) throw new Exception("卡片也被穿透");
            if (SendMessage(window.Handle, 0x21, 0, 0) != 3) throw new Exception("鼠标激活策略不正确");
        });
        Check("收起：动画结束后原生窗口隐藏，再展开不会被旧动画隐藏", () =>
        {
            controller.Reveal.Release(); controller.Reveal.Hide(); Pump(null, 650);
            if (Native.IsWindowVisible(window.Handle)) throw new Exception("收起后仍残留原生窗口");
            controller.Reveal.Show(DateTimeOffset.Now, false); controller.Reveal.Hold(); Pump(null, 100);
            window.Tuck(); Pump(null, 30); window.Reveal(false); Pump(null, 350);
            if (!Native.IsWindowVisible(window.Handle)) throw new Exception("旧收起动画隐藏了新窗口");
        });
        Check("索引恢复：演示图片已原子保存并保留五张", () =>
        {
            var file = Path.Combine(scenarioRoot, "items.json");
            var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            if (data.RootElement.GetArrayLength() != 5) throw new Exception("保存卡片数量错误");
        });
        Check("主题：现有卡片和窗口可在深色、浅色、高对比度间切换", () =>
        {
            var frame = (System.Windows.Controls.Border)((CardView)window.Cards.Children[0]).Children[0];
            try
            {
                foreach (var (dark, contrast) in new[] { (true, false), (false, false), (false, true) })
                {
                    ThemePalette.Apply(dark, contrast); Pump(null, 50);
                    if (!ReferenceEquals(frame.Background, Application.Current.Resources["Glass"])) throw new Exception("已有卡片材质未更新");
                    if (!ReferenceEquals(window.Foreground, Application.Current.Resources["Ink"])) throw new Exception("窗口文字未更新");
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(window); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(root, contrast ? "theme-contrast.png" : dark ? "theme-dark.png" : "theme-light.png")); encoder.Save(stream);
                }
            }
            finally { ThemePalette.Apply(false, false); }
        });
        Check("容量和重启恢复：35张外部图片保留最新30张，原图全部保留", () =>
        {
            var directory = Path.Combine(root, "capacity-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            var dataRoot = Path.Combine(directory, "data");
            var paths = new List<string>();
            using (var second = new ShelfController(dataRoot, true))
            {
                var load = second.StartAsync(false); Pump(() => load.IsCompleted); load.GetAwaiter().GetResult();
                for (int i = 0; i < 35; i++)
                {
                    var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null,
                        new byte[] { (byte)(i * 7), (byte)(i * 3), (byte)i, 255 }, 4);
                    var decoded = ImageCodec.FromBitmap(bitmap); var path = Path.Combine(directory, $"image-{i:00}.png");
                    File.WriteAllBytes(path, decoded.Png); paths.Add(path); second.Intake(path, decoded);
                }
                if (second.Items.Count != 30 || second.Items[0].Path != paths[5]) throw new Exception("容量或顺序错误");
            }
            if (paths.Any(p => !File.Exists(p))) throw new Exception("外部原图被删除");
            using var restored = new ShelfController(dataRoot, true);
            var restore = restored.StartAsync(false); Pump(() => restore.IsCompleted, 15000); restore.GetAwaiter().GetResult();
            if (restored.Items.Count != 30 || restored.Window.Cards.Children.Count != 30) throw new Exception("重启恢复不完整");
            if (restored.ThumbnailCacheHits != 30) throw new Exception("重启未使用持久缩略图缓存");
        });
        File.WriteAllText(Path.Combine(root, "window-results.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return results.Any(r => !(bool)r.GetType().GetProperty("Passed")!.GetValue(r)!) ? 1 : 0;
    }
    private static void Pump(Func<bool>? done, int timeout = 5000)
    {
        var frame = new DispatcherFrame(); var until = DateTimeOffset.Now.AddMilliseconds(timeout);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        timer.Tick += (_, _) => { if ((done?.Invoke() ?? false) || DateTimeOffset.Now >= until) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
    }
}
