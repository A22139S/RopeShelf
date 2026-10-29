using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using RopeShelf.Core;
using RopeShelf.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace RopeShelf.App;

public sealed class ShelfController : IDisposable
{
    private readonly Storage storage;
    private readonly ThumbnailCache thumbnails;
    private readonly ShelfWindow window = new();
    private readonly ShelfSettings settings;
    private readonly List<ShelfItem> items;
    private readonly IntakeHistory history = new(TimeSpan.FromSeconds(6));
    private readonly ClipboardService? clipboard;
    private readonly FolderIntake folders = new();
    private readonly Forms.NotifyIcon tray;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly bool noInput;
    private bool ready, latest, disposed;
    private DateTimeOffset nextDiagnostic;
    private readonly string? diagnosticPath = App.Argument("--diagnostics");
    private long tickCount, tickTicks;
    private string status = "正在启动…";
    public RevealState Reveal { get; } = new();
    public ShelfWindow Window => window;
    internal IReadOnlyList<ShelfItem> Items => items;
    internal int ThumbnailCacheHits => thumbnails.Hits;
    public ShelfController(string? root, bool noInput)
    {
        this.noInput = noInput;
        storage = new Storage(root);
        thumbnails = new ThumbnailCache(Path.Combine(storage.Root, "Thumbnails"), storage.Log);
        settings = storage.Load("settings.json", () => new ShelfSettings()); settings.Capacity = Math.Clamp(settings.Capacity, 1, 100);
        items = storage.Load("items.json", () => new List<ShelfItem>());
        window.Source.AddHook(Hook);
        if (!noInput)
        {
            clipboard = new ClipboardService(window.Source) { Enabled = settings.ClipboardEnabled };
            clipboard.ImageReceived += image => Intake(null, image);
            clipboard.Error += Report;
        }
        folders.ImageReceived += (path, image) => window.Dispatcher.BeginInvoke(() => Intake(path, image));
        folders.Error += message => window.Dispatcher.BeginInvoke(() => Report(message));
        tray = new Forms.NotifyIcon { Text = "绳间 · 截图晾衣绳", Icon = MakeIcon(), Visible = !noInput };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Toggle(); };
        BuildTray();
        if (!noInput && settings.HotkeyKey != 0 && !Native.RegisterHotKey(window.Handle, 1, settings.HotkeyModifiers | 0x4000, settings.HotkeyKey))
            Report("无法注册唤出快捷键，请在托盘设置中更换");
        timer.Tick += (_, _) => Tick();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
    }
    public async Task StartAsync(bool demo)
    {
        foreach (var item in items.ToArray())
        {
            if (!File.Exists(item.Path)) { items.Remove(item); continue; }
            try
            {
                var cached = await Task.Run(() => thumbnails.Get(item));
                if (disposed) return;
                if (!items.Any(i => i.Id == item.Id)) continue;
                if (cached != null) { AddCard(item, cached); continue; }
                var image = await Task.Run(() => ImageCodec.FromFile(item.Path));
                if (disposed) return;
                int index = items.FindIndex(i => i.Id == item.Id);
                if (index < 0) continue;
                var updated = ThumbnailCache.Stamp(items[index] with { Fingerprint = image.Fingerprint, Width = image.Width, Height = image.Height });
                items[index] = updated; thumbnails.Put(updated, image.Thumb); AddCard(updated, image.Thumb);
            }
            catch (Exception e) { Report("恢复图片失败，索引已保留：" + e.Message); }
        }
        if (demo && items.Count == 0)
        {
            for (int i = 0; i < 5; i++)
            {
                var image = CreateDemo(i); Intake(null, image);
            }
        }
        if (!noInput)
        {
            var watchOnly = App.Argument("--watch-only");
            var configured = watchOnly != null ? new[] { watchOnly } : new[] { Native.ScreenshotFolder() }.Concat(settings.WatchFolders);
            folders.Watch(configured);
        }
        EnforceCapacity(); thumbnails.Prune(items); Persist(); ready = true; timer.Start();
        if (status == "正在启动…") Report("已就绪 · 等待新截图");
        if (demo) { Reveal.Show(DateTimeOffset.Now, false); Reveal.Hold(); }
        var output = App.Argument("--render-preview");
        if (output != null)
        {
            Reveal.Show(DateTimeOffset.Now, false); Tick();
            await Task.Delay(1600);
            window.UpdateLayout();
            var target = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(target));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            using (var stream = File.Create(output)) encoder.Save(stream);
            File.WriteAllText(output + ".json", JsonSerializer.Serialize(new { Count = items.Count, Visible = window.IsVisible, Width = window.ActualWidth, Height = window.ActualHeight, Status = status }));
            Application.Current.Shutdown();
        }
    }
    internal void Intake(string? path, DecodedImage image)
    {
        if (disposed) return;
        try
        {
            var now = DateTimeOffset.Now;
            var duplicate = history.Find(image.Fingerprint, now);
            if (duplicate != null && items.FirstOrDefault(i => i.Id == duplicate) is ShelfItem existing)
            {
                if (path != null && existing.Owned)
                {
                    var index = items.IndexOf(existing); items[index] = existing with { ExternalPath = path }; Persist();
                }
                return;
            }
            var actual = path ?? storage.StoreImage(image.Png);
            var item = ThumbnailCache.Stamp(new ShelfItem(Guid.NewGuid(), actual, path == null, now, image.Fingerprint, image.Width, image.Height,
                path == null ? ImageOrigin.Clipboard : ImageOrigin.Folder, path));
            items.Add(item); history.Remember(item); thumbnails.Put(item, image.Thumb); AddCard(item, image.Thumb); bool withinCapacity = EnforceCapacity(); Persist();
            Report(withinCapacity ? $"已挂上 · 共 {items.Count} 张" : $"已挂上 · 共 {items.Count} 张（清理未成功，暂时超出保留数量）"); latest = true;
            if (!Native.IsFullscreen(window.Handle)) Reveal.Show(now, true);
        }
        catch (Exception e) { Report("收图失败：" + e.Message); }
    }
    private void AddCard(ShelfItem item, BitmapSource thumb) => window.Cards.Children.Add(new CardView(item, thumb, this));
    private bool EnforceCapacity()
    {
        while (items.Count > settings.Capacity)
        {
            if (!Remove(items[0])) { Report("最旧图片清理失败，已暂时保留；可稍后手动取下"); return false; }
        }
        return true;
    }
    public bool Remove(ShelfItem item)
    {
        bool recycled = false;
        try
        {
            var current = items.FirstOrDefault(i => i.Id == item.Id); if (current == null) return true;
            storage.Recycle(current); recycled = current.Owned && !File.Exists(current.Path);
            storage.Save("items.json", items.Where(i => i.Id != item.Id).ToList());
            items.Remove(current); history.Forget(item.Id);
            var card = window.Cards.Children.Cast<CardView>().FirstOrDefault(v => v.Item.Id == item.Id);
            if (card != null) window.Cards.Children.Remove(card);
            thumbnails.Prune(items);
            Report($"已取下 · 剩余 {items.Count} 张"); return true;
        }
        catch (Exception e) { Report((recycled ? "图片已回收，索引保存失败，请重试取下：" : "取下失败，文件已保留：") + e.Message); return false; }
    }
    public async Task CopyAsync(ShelfItem item)
    { if (clipboard == null) throw new InvalidOperationException("当前为无输入验证模式，未启用剪贴板"); await clipboard.CopyAsync(item.Path); }
    public void Open(ShelfItem item) => Safe(() => Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }));
    public void RevealFile(ShelfItem item) => Safe(() => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", item.Path }, UseShellExecute = true }));
    public void SaveAs(ShelfItem item)
    {
        Reveal.Hold();
        try
        {
            var dialog = new SaveFileDialog { FileName = Path.GetFileName(item.Path), Filter = "图片文件|*" + Path.GetExtension(item.Path), AddExtension = true };
            if (dialog.ShowDialog() != true) return;
            if (string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(item.Path), StringComparison.OrdinalIgnoreCase)) { Report("图片已在此位置"); return; }
            File.Copy(item.Path, dialog.FileName, true);
            if (new FileInfo(dialog.FileName).Length == new FileInfo(item.Path).Length) Remove(item);
        }
        catch (Exception e) { Report("另存失败：" + e.Message); }
        finally { Reveal.Release(); }
    }
    public void Drag(ShelfItem item)
    {
        Reveal.Hold();
        try
        {
            var result = ShellTransfer.Drag(item.Path, storage);
            if (result.FolderSaved) Remove(item);
            else Report(result.Completed ? "已交给接收应用 · 图片保留在绳上" : "拖放已取消 · 图片保留在绳上");
        }
        catch (Exception e) { Report("拖放失败：" + e.Message); }
        finally { Reveal.Release(); }
    }
    public async void Preview(ShelfItem item)
    {
        Reveal.Hold();
        try
        {
            var bitmap = await Task.Run(() => ImageCodec.ReadFull(item.Path));
            var preview = new Window { Title = "绳间预览 · " + Path.GetFileName(item.Path), Width = 920, Height = 680, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = (Brush)Application.Current.FindResource("Paper"), Content = new Image { Source = bitmap, Stretch = Stretch.Uniform, Margin = new Thickness(16) } };
            preview.SetResourceReference(System.Windows.Window.BackgroundProperty, "Paper");
            preview.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) preview.Close(); };
            preview.Closed += (_, _) => Reveal.Release(); preview.Show();
        }
        catch (Exception e) { Reveal.Release(); Report("预览失败：" + e.Message); }
    }
    private void Tick()
    {
        if (!ready) return;
        var tickStarted = Stopwatch.GetTimestamp(); tickCount++;
        Native.GetCursorPos(out var pointer); var primary = Native.PrimaryMonitor();
        var dpi = window.Source.CompositionTarget.TransformToDevice.M11;
        bool hot = pointer.Y >= primary.Monitor.Top && pointer.Y <= primary.Monitor.Top + Math.Max(2, 3 * dpi)
            && Math.Abs(pointer.X - (primary.Work.Left + primary.Work.Right) / 2) <= 180 * dpi;
        Reveal.Tick(DateTimeOffset.Now, hot, window.PointerInBand(pointer), Native.IsFullscreen(window.Handle), items.Count > 0);
        if (Reveal.Visible && (!window.IsVisible || window.IsTucking)) { window.Reveal(latest); latest = false; }
        else if (!Reveal.Visible && window.IsVisible) window.Tuck();
        else if (Reveal.Visible && latest) { window.Reveal(true); latest = false; }
        tickTicks += Stopwatch.GetTimestamp() - tickStarted;
        if (diagnosticPath is string diagnostic && DateTimeOffset.Now >= nextDiagnostic)
        {
            nextDiagnostic = DateTimeOffset.Now.AddSeconds(2);
            Native.GetWindowRect(window.Handle, out var rect);
            File.WriteAllText(diagnostic, JsonSerializer.Serialize(new { Count = items.Count, State = Reveal.Mode.ToString(), Holds = Reveal.Holds,
                WpfVisible = window.IsVisible, NativeVisible = Native.IsWindowVisible(window.Handle), Hwnd = window.Handle.ToInt64(),
                Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom, Status = status,
                TickCount = tickCount, TickDurationMs = tickTicks * 1000d / Stopwatch.Frequency,
                ThumbnailCacheHits = thumbnails.Hits }));
        }
    }
    private void Toggle()
    {
        if (items.Count == 0) { Report("还没有图片：使用现有截图工具截图，或复制一张图片"); tray.ShowBalloonTip(2500, "绳间", status, Forms.ToolTipIcon.Info); return; }
        if (Reveal.Visible) Reveal.Hide(); else Reveal.Show(DateTimeOffset.Now, false); Tick();
    }
    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    { if (msg == 0x0312 && wParam == 1) { Toggle(); handled = true; } return 0; }
    public void Report(string message)
    {
        if (!window.Dispatcher.CheckAccess()) { window.Dispatcher.BeginInvoke(() => Report(message)); return; }
        status = message; storage.Log(message); tray.Text = ("绳间 · " + message)[..Math.Min(63, message.Length + 5)];
    }
    private void Persist() => storage.Save("items.json", items);
    private void SaveSettings() { storage.Save("settings.json", settings); if (clipboard != null) clipboard.Enabled = settings.ClipboardEnabled; BuildTray(); }
    private void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("展开 / 收起   " + HotkeyText(settings.HotkeyModifiers, settings.HotkeyKey), null, (_, _) => Toggle());
        var copy = new Forms.ToolStripMenuItem("接收复制的图片") { Checked = settings.ClipboardEnabled, CheckOnClick = true };
        copy.CheckedChanged += (_, _) => { settings.ClipboardEnabled = copy.Checked; SaveSettings(); }; menu.Items.Add(copy);
        menu.Items.Add("设置…", null, (_, _) => ShowSettings());
        menu.Items.Add("打开数据目录", null, (_, _) => Safe(() => Process.Start(new ProcessStartInfo(storage.Root) { UseShellExecute = true })));
        menu.Items.Add("使用说明", null, (_, _) => MessageBox.Show("顶部中央停留展开，离开收起。\n单击复制 · 双击打开 · 中键预览\n拖进应用分享，拖进文件夹保存。\n× 取下：自管图片进回收站，外部原图保留。\n\n全屏时继续收图，使用快捷键主动展开。", "绳间 · 使用说明"));
        menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("退出绳间", null, (_, _) => Application.Current.Shutdown());
        var old = tray.ContextMenuStrip; tray.ContextMenuStrip = menu; old?.Dispose();
    }
    private void ShowSettings()
    {
        Reveal.Hold();
        try
        {
            var panel = new StackPanel { Margin = new Thickness(26) };
            panel.Children.Add(new TextBlock { Text = "绳间设置", FontSize = 24, FontWeight = FontWeights.SemiBold });
            panel.Children.Add(new TextBlock { Text = "本地收图，随手取用。", Margin = new Thickness(0, 5, 0, 18) });
            var receive = new CheckBox { Content = "接收剪贴板中复制的图片", IsChecked = settings.ClipboardEnabled }; panel.Children.Add(receive);
            var startup = new CheckBox { Content = "登录 Windows 后启动", IsChecked = StartupEnabled() }; panel.Children.Add(startup);
            panel.Children.Add(new TextBlock { Text = "额外监视目录（每行一个，保存后立即生效）", Margin = new Thickness(0, 14, 0, 6) });
            var directories = new TextBox { Text = string.Join(Environment.NewLine, settings.WatchFolders), Height = 80, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(directories);
            var browse = new Button { Content = "添加文件夹…", HorizontalAlignment = HorizontalAlignment.Left };
            browse.Click += (_, _) => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog() == true) directories.Text += (directories.Text.Length > 0 ? Environment.NewLine : "") + dialog.FolderName; }; panel.Children.Add(browse);
            panel.Children.Add(new TextBlock { Text = "默认目录：" + Native.ScreenshotFolder(), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 12) });
            panel.Children.Add(new TextBlock { Text = "唤出快捷键（点击后按组合键，Delete 清除）" });
            uint mods = settings.HotkeyModifiers, key = settings.HotkeyKey;
            bool shortcutValid = true;
            var shortcut = new TextBox { Text = HotkeyText(mods, key), IsReadOnly = true, Margin = new Thickness(0, 6, 0, 10) };
            shortcut.PreviewKeyDown += (_, e) =>
            {
                e.Handled = true; var actual = e.Key == Key.System ? e.SystemKey : e.Key;
                if (actual == Key.Delete) { key = 0; mods = 0; shortcutValid = true; shortcut.Text = "未设置"; return; }
                if (actual is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
                uint candidateMods = (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? 1u : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 2u : 0) | (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 4u : 0);
                if ((candidateMods & 3) == 0 || Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
                { shortcutValid = false; shortcut.Text = "请包含 Ctrl 或 Alt，且不要使用 Windows 键"; return; }
                mods = candidateMods; key = (uint)KeyInterop.VirtualKeyFromKey(actual); shortcutValid = true; shortcut.Text = HotkeyText(mods, key);
            }; panel.Children.Add(shortcut);
            var current = new TextBlock { Text = "状态：" + status, TextWrapping = TextWrapping.Wrap, FontSize = 12 }; panel.Children.Add(current);
            var save = new Button { Content = "保存设置", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; panel.Children.Add(save);
            var dialogWindow = new Window { Title = "绳间设置", Width = 540, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = (Brush)Application.Current.FindResource("Paper"), Content = panel };
            dialogWindow.SetResourceReference(System.Windows.Window.BackgroundProperty, "Paper");
            save.Click += (_, _) =>
            {
                try
                {
                    if (!shortcutValid) throw new InvalidOperationException("请设置有效组合键，或按 Delete 清除快捷键");
                    var paths = directories.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (paths.Any(p => !Directory.Exists(p))) throw new IOException("请检查目录是否存在");
                    if (paths.Any(p => Path.GetFullPath(p).StartsWith(storage.Root, StringComparison.OrdinalIgnoreCase))) throw new IOException("不能监视绳间自身的数据目录");
                    if (!noInput)
                    {
                        Native.UnregisterHotKey(window.Handle, 1);
                        if (key != 0 && !Native.RegisterHotKey(window.Handle, 1, mods | 0x4000, key))
                        { if (settings.HotkeyKey != 0) Native.RegisterHotKey(window.Handle, 1, settings.HotkeyModifiers | 0x4000, settings.HotkeyKey); throw new InvalidOperationException("快捷键已被占用，请换一个"); }
                    }
                    settings.HotkeyModifiers = mods; settings.HotkeyKey = key;
                    settings.ClipboardEnabled = receive.IsChecked == true; settings.WatchFolders = paths;
                    SetStartup(startup.IsChecked == true); SaveSettings(); folders.Reset(new[] { Native.ScreenshotFolder() }.Concat(paths));
                    dialogWindow.Close();
                }
                catch (Exception e) { current.Text = e.Message; }
            };
            dialogWindow.ShowDialog();
        }
        finally { Reveal.Release(); }
    }
    private static string HotkeyText(uint mods, uint key) => key == 0 ? "未设置" :
        ((mods & 2) != 0 ? "Ctrl+" : "") + ((mods & 1) != 0 ? "Alt+" : "") + ((mods & 4) != 0 ? "Shift+" : "") + KeyInterop.KeyFromVirtualKey((int)key);
    private static bool StartupEnabled()
    { using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return run?.GetValue("RopeShelf") != null; }
    private static void SetStartup(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) run.SetValue("RopeShelf", "\"" + Environment.ProcessPath + "\""); else run.DeleteValue("RopeShelf", false);
    }
    private static Drawing.Icon MakeIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32); using var graphics = Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var cord = new Drawing.Pen(Drawing.Color.FromArgb(184, 159, 125), 2);
        using var glass = new Drawing.SolidBrush(Drawing.Color.FromArgb(36, 123, 121));
        using var paper = new Drawing.SolidBrush(Drawing.Color.White);
        graphics.DrawBezier(cord, 1, 5, 10, 10, 22, 10, 31, 5); graphics.FillRectangle(glass, 7, 10, 19, 17); graphics.FillRectangle(paper, 10, 13, 13, 10); graphics.DrawLine(cord, 16, 6, 16, 13);
        var handle = bitmap.GetHicon(); try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    private void Safe(Action action) { try { action(); } catch (Exception e) { Report(e.Message); } }
    private void OnDisplayChanged(object? sender, EventArgs e) => window.Dispatcher.BeginInvoke(window.Place);
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) window.Dispatcher.BeginInvoke(window.Place); }
    private void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    { if (!disposed) window.Dispatcher.BeginInvoke(ThemePalette.ApplyFromSystem); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer.Stop(); clipboard?.Dispose(); folders.Dispose();
        Native.UnregisterHotKey(window.Handle, 1); SystemEvents.DisplaySettingsChanged -= OnDisplayChanged; SystemEvents.PowerModeChanged -= OnPowerChanged;
        SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
        tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); window.Close();
    }
    private static DecodedImage CreateDemo(int index)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var palettes = new[] { ("#E4EFE9", "#2E6052"), ("#F4E8D6", "#9E643B"), ("#DDE7EF", "#416582"), ("#ECE2ED", "#865C7E"), ("#E7ECE1", "#657A44") };
            var (bg, fg) = palettes[index % palettes.Length]; var paper = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)); var ink = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
            dc.DrawRectangle(paper, null, new Rect(0, 0, 720, 480));
            dc.DrawEllipse(ink, null, new Point(540, 155), 90, 90);
            var text = new FormattedText(new[] { "随手一截", "灵感，晾一会儿", "正在进行", "留住这一刻", "稍后再看" }[index % 5], System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface("Microsoft YaHei UI"), 38, ink, 1);
            dc.DrawText(text, new Point(45, 280));
            dc.DrawRectangle(ink, null, new Rect(45, 345, 200 + index * 24, 5));
        }
        var target = new RenderTargetBitmap(720, 480, 96, 96, PixelFormats.Pbgra32); target.Render(visual); target.Freeze();
        return ImageCodec.FromBitmap(target);
    }
}
