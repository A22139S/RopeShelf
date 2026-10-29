using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RopeShelf.Core;
using RopeShelf.Windows;

namespace RopeShelf.App;

public sealed class CardView : Grid
{
    public ShelfItem Item { get; }
    private readonly ShelfController controller;
    private readonly RotateTransform tilt = new();
    private readonly ScaleTransform scale = new(1, 1);
    private readonly TextBlock label;
    private DispatcherTimer? clickTimer;
    private Point? down;
    private bool dragged;
    private bool copying;
    public CardView(ShelfItem item, BitmapSource thumb, ShelfController controller)
    {
        Item = item; this.controller = controller;
        Width = 192; Height = 200; Margin = new Thickness(10, 0, 10, 12);
        Background = Brushes.Transparent; RenderTransformOrigin = new Point(.5, .08);
        var transforms = new TransformGroup(); transforms.Children.Add(scale); transforms.Children.Add(tilt); RenderTransform = transforms;
        var frame = new Border { Margin = new Thickness(0, 21, 0, 8), CornerRadius = new CornerRadius(12), Padding = new Thickness(7),
            Background = (Brush)FindResource("Glass"), BorderBrush = (Brush)FindResource("Highlight"), BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 3, Opacity = .22 } };
        frame.SetResourceReference(Border.BackgroundProperty, "Glass"); frame.SetResourceReference(Border.BorderBrushProperty, "Highlight");
        var content = new Grid(); content.RowDefinitions.Add(new RowDefinition()); content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) });
        var image = new Image { Source = thumb, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var imageFrame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = image };
        imageFrame.SetResourceReference(Border.BackgroundProperty, "Paper"); content.Children.Add(imageFrame);
        label = new TextBlock { Text = $"{item.Added.LocalDateTime:HH:mm}  ·  {item.Width} × {item.Height}", FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)FindResource("Ink") };
        TextOptions.SetTextRenderingMode(label, TextRenderingMode.Grayscale);
        label.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        Grid.SetRow(label, 1); content.Children.Add(label); frame.Child = content; Children.Add(frame);
        var peg = new Border { Width = 11, Height = 32, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, Background = (Brush)FindResource("Wood"), BorderBrush = (Brush)FindResource("Cord"), BorderThickness = new Thickness(.7),
            Child = new Border { Height = 3, Margin = new Thickness(1, 10, 1, 0), VerticalAlignment = VerticalAlignment.Top, Background = (Brush)FindResource("Cord") }, IsHitTestVisible = false };
        Children.Add(peg);
        var close = new Button { Content = "×", FontSize = 16, Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(3, 24, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = (Brush)FindResource("Paper"),
            Foreground = (Brush)FindResource("Ink"), BorderThickness = new Thickness(0), Opacity = 0, ToolTip = item.Owned ? "取下并移到回收站" : "取下（保留原图）" };
        close.SetResourceReference(Button.BackgroundProperty, "Paper"); close.SetResourceReference(Button.ForegroundProperty, "Ink");
        close.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true;
        close.PreviewMouseLeftButtonUp += (_, e) => { clickTimer?.Stop(); controller.Remove(item); e.Handled = true; };
        Children.Add(close);
        MouseEnter += (_, _) => { close.Opacity = 1; scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(140))); scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.025, TimeSpan.FromMilliseconds(140))); Sway(1.8); };
        MouseLeave += (_, _) => { close.Opacity = 0; scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))); scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180))); };
        ToolTip = $"{Path.GetFileName(item.Path)}\n单击复制 · 双击打开 · 中键预览 · 拖动分享";
        var menu = new ContextMenu();
        AddMenu(menu, "复制原图", () => _ = Copy()); AddMenu(menu, "打开", () => controller.Open(item)); AddMenu(menu, "大图预览", () => controller.Preview(item));
        menu.Items.Add(new Separator()); AddMenu(menu, "另存为…", () => controller.SaveAs(item)); AddMenu(menu, "打开所在文件夹", () => controller.RevealFile(item));
        menu.Items.Add(new Separator()); AddMenu(menu, item.Owned ? "取下并移到回收站" : "取下（保留原图）", () => controller.Remove(item));
        menu.Opened += (_, _) => controller.Reveal.Hold(); menu.Closed += (_, _) => controller.Reveal.Release(); ContextMenu = menu;
        MouseLeftButtonDown += OnDown; MouseMove += OnMove; MouseLeftButtonUp += OnUp;
        MouseDown += (_, e) => { if (e.ChangedButton == MouseButton.Middle) { controller.Preview(item); e.Handled = true; } };
        Unloaded += (_, _) => { clickTimer?.Stop(); StopAnimation(); };
    }
    private static void AddMenu(ContextMenu menu, string text, Action action)
    { var entry = new MenuItem { Header = text }; entry.Click += (_, _) => action(); menu.Items.Add(entry); }
    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { clickTimer?.Stop(); down = null; ReleaseMouseCapture(); controller.Open(Item); e.Handled = true; return; }
        down = e.GetPosition(this); dragged = false; CaptureMouse(); e.Handled = true;
    }
    private void OnMove(object sender, MouseEventArgs e)
    {
        if (down is not Point start || e.LeftButton != MouseButtonState.Pressed || dragged) return;
        var now = e.GetPosition(this);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragged = true; down = null; clickTimer?.Stop(); ReleaseMouseCapture(); controller.Drag(Item);
    }
    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture();
        if (down != null && !dragged)
        {
            clickTimer?.Stop(); clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Native.GetDoubleClickTime()) };
            clickTimer.Tick += (_, _) => { clickTimer.Stop(); _ = Copy(); }; clickTimer.Start();
        }
        down = null; e.Handled = true;
    }
    private async Task Copy()
    {
        if (copying) return; copying = true; label.Text = "正在复制…";
        try { await controller.CopyAsync(Item); label.Text = "已复制 ✓"; await Task.Delay(1400); }
        catch (Exception e) { controller.Report("复制失败：" + e.Message); }
        finally { label.Text = $"{Item.Added.LocalDateTime:HH:mm}  ·  {Item.Width} × {Item.Height}"; copying = false; }
    }
    public void Sway(double amplitude = 4)
    {
        if (!SystemParameters.ClientAreaAnimation) { StopAnimation(); return; }
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(1300), FillBehavior = FillBehavior.Stop };
        foreach (var (ms, angle) in new[] { (0, -amplitude), (230, amplitude * .65), (490, -amplitude * .35), (760, amplitude * .15), (1100, 0d) })
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new KeySpline(.3, 0, .2, 1)));
        tilt.BeginAnimation(RotateTransform.AngleProperty, animation);
    }
    public void StopAnimation()
    { tilt.BeginAnimation(RotateTransform.AngleProperty, null); scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); }
}
