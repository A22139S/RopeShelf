using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RopeShelf.Windows;

namespace RopeShelf.DesktopChecks;

/// <summary>Interactive generated-file drag fixture; separate from the product and user data.</summary>
internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        var root = Path.Combine(AppContext.BaseDirectory, "BenchData");
        var storage = new Storage(root);
        var image = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null,
            new byte[] { 100,140,30,255, 30,90,170,255, 190,110,60,255, 120,50,130,255 }, 8);
        image.Freeze(); var decoded = ImageCodec.FromBitmap(image);
        var source = Path.Combine(root, "source.png"); File.WriteAllBytes(source, decoded.Png);
        var window = new Window { Title = "绳间 · 拖放验证台", Width = 760, Height = 540, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FontFamily = new FontFamily("Microsoft YaHei UI"), Background = new SolidColorBrush(Color.FromRgb(242, 246, 246)) };
        var panel = new StackPanel { Margin = new Thickness(28) }; window.Content = panel;
        panel.Children.Add(new TextBlock { Text = "拖放验证台", FontSize = 25, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "拖动左侧生成图片到右侧接收区，或拖进外部文件夹 / 图片应用。", Margin = new Thickness(0, 8, 0, 20) });
        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
        var card = new Border { Background = new SolidColorBrush(Color.FromRgb(215, 231, 229)), BorderBrush = new SolidColorBrush(Color.FromRgb(36, 123, 121)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 16, 0), Height = 180,
            Child = new Image { Source = image, Width = 150, Height = 130, Stretch = Stretch.Fill }, Cursor = Cursors.Hand };
        row.Children.Add(card);
        var receive = new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(110, 140, 140)), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12), Height = 180, AllowDrop = true,
            Child = new TextBlock { Text = "本地接收区\n仅复制到 BenchData/Received", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } };
        Grid.SetColumn(receive, 1); row.Children.Add(receive); panel.Children.Add(row);
        var status = new TextBlock { Text = "等待拖放。测试原图会保留。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0) }; panel.Children.Add(status);
        Point? start = null;
        card.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(card); card.CaptureMouse(); e.Handled = true; };
        card.MouseLeftButtonUp += (_, _) => { start = null; card.ReleaseMouseCapture(); };
        card.MouseMove += (_, e) =>
        {
            if (start is not Point down || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(card);
            if (Math.Abs(p.X - down.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - down.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            start = null; card.ReleaseMouseCapture();
            try
            {
                var result = ShellTransfer.Drag(source, storage);
                var evidence = new { result.Completed, result.FolderSaved, result.Evidence, SourceUnchanged = File.ReadAllBytes(source).SequenceEqual(decoded.Png) };
                File.WriteAllText(Path.Combine(root, "drag-result.json"), JsonSerializer.Serialize(evidence));
                status.Text = JsonSerializer.Serialize(evidence);
            }
            catch (Exception error) { status.Text = error.ToString(); File.WriteAllText(Path.Combine(root, "drag-error.txt"), error.ToString()); }
        };
        receive.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        receive.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length != 1) return;
            var targetRoot = Path.Combine(root, "Received"); Directory.CreateDirectory(targetRoot);
            var target = Path.Combine(targetRoot, Guid.NewGuid().ToString("N") + ".png"); File.Copy(paths[0], target);
            e.Effects = DragDropEffects.Copy; e.Handled = true;
            File.WriteAllText(Path.Combine(root, "receive-result.json"), JsonSerializer.Serialize(new { Copied = true, BytesMatch = File.ReadAllBytes(target).SequenceEqual(decoded.Png) }));
        };
        app.Run(window);
    }
}
