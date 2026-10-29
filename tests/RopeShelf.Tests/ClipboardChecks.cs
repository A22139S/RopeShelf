using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RopeShelf.Windows;

internal static class ClipboardChecks
{
    public static int Run(string root)
    {
        var results = new List<object>();
        var previous = Clipboard.GetDataObject();
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var source = new HwndSource(new HwndSourceParameters("RopeShelf Clipboard Integration") { Width = 1, Height = 1, WindowStyle = 0 });
        using var service = new ClipboardService(source);
        int count = 0; string? hash = null; string? error = null;
        service.ImageReceived += image => { count++; hash = image.Fingerprint; };
        service.Error += message => error = message;
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null,
            new byte[] { 10,20,30,255, 40,50,60,255, 70,80,90,255, 100,110,120,255 }, 8);
        bitmap.Freeze(); var image = ImageCodec.FromBitmap(bitmap);
        var path = Path.Combine(root, "clipboard-fixture.png"); File.WriteAllBytes(path, image.Png);
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { Name = name, Passed = true, Error = "" }); Console.WriteLine("PASS " + name); }
            catch (Exception e) { results.Add(new { Name = name, Passed = false, Error = e.ToString() }); Console.WriteLine("FAIL " + name + ": " + e); }
        }
        void Send(bool text = false, bool files = false, string? privacy = null)
        {
            var data = new DataObject(); data.SetData("PNG", new MemoryStream(image.Png));
            if (text) data.SetText("RopeShelf generated test text");
            if (files) data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
            if (privacy != null) data.SetData(privacy, new MemoryStream([1]));
            Clipboard.SetDataObject(data, true);
        }
        try
        {
            Check("真实剪贴板通知：接收PNG并校验全像素指纹", () =>
            { Send(); Pump(() => count >= 1); if (count != 1 || hash != image.Fingerprint) throw new Exception(error ?? "图片未正确到达"); });
            Check("真实剪贴板：混合文本、文件列表和隐私标记不收图", () =>
            {
                var before = count; Send(text: true); Pump(null, 250); Send(files: true); Pump(null, 250);
                Send(privacy: "ExcludeClipboardContentFromMonitorProcessing"); Pump(null, 250);
                if (count != before) throw new Exception("过滤失效");
            });
            Check("真实剪贴板：自身复制不回流，原图尺寸保持", () =>
            {
                var before = count; var copy = service.CopyAsync(path); Pump(() => copy.IsCompleted); copy.GetAwaiter().GetResult(); Pump(null, 300);
                if (count != before) throw new Exception("自身复制产生回流");
                var actual = Clipboard.GetImage(); if (actual == null || actual.PixelWidth != 2 || actual.PixelHeight != 2) throw new Exception("原图尺寸错误");
            });
            Check("真实剪贴板：暂停后不收图，恢复后可接收", () =>
            {
                var before = count; service.Enabled = false; Send(); Pump(null, 250); if (count != before) throw new Exception("暂停失效");
                service.Enabled = true; Send(); Pump(() => count > before); if (count != before + 1) throw new Exception(error ?? "恢复失败");
            });
        }
        finally
        {
            service.Enabled = false;
            if (previous != null) Clipboard.SetDataObject(previous, true); else Clipboard.Clear();
        }
        File.WriteAllText(Path.Combine(root, "clipboard-results.json"), System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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
