using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RopeShelf.Windows;

namespace RopeShelf.App;

public sealed class ShelfWindow : Window
{
    public StackPanel Cards { get; } = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer scroller;
    private readonly Grid root;
    private readonly TranslateTransform slide = new();
    public bool IsTucking { get; private set; }
    public nint Handle { get; private set; }
    public HwndSource Source { get; private set; } = null!;
    public ShelfWindow()
    {
        Title = "绳间 · 截图晾衣绳"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(ForegroundProperty, "Ink");
        bool testWindow = Environment.GetCommandLineArgs().Contains("--test-window");
        AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = testWindow; ShowActivated = false; Topmost = true;
        Height = 246; Width = 1000; Left = 0; Top = 0;
        root = new Grid { Background = null, RenderTransform = slide };
        var cord = new System.Windows.Shapes.Path { Stroke = (Brush)Application.Current.FindResource("Cord"), StrokeThickness = 1.7,
            Data = Geometry.Parse("M 0,20 Q 500,38 1000,20"), Stretch = Stretch.Fill, Height = 12, Margin = new Thickness(0, 17, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        root.Children.Add(cord);
        scroller = new ScrollViewer { Content = Cards, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(24, 12, 24, 0), Background = null, CanContentScroll = false };
        scroller.PreviewMouseWheel += (_, e) => { scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset - e.Delta); e.Handled = true; };
        root.Children.Add(scroller); Content = root;
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle; Source = HwndSource.FromHwnd(Handle);
            var flags = Native.GetWindowLongPtr(Handle, -20).ToInt64(); Native.SetWindowLongPtr(Handle, -20, (nint)(flags | (testWindow ? 0L : 0x08000080L)));
            Source.AddHook(Hook); Place();
        };
        new WindowInteropHelper(this).EnsureHandle();
    }
    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x21) { handled = true; return 3; } // MA_NOACTIVATE
        if (msg is 0x007E or 0x02E0) Dispatcher.BeginInvoke(Place);
        if (msg == 0x0084)
        {
            var point = PointFromScreen(new Point((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF)));
            bool onCard = Cards.Children.Cast<CardView>().Any(c => { var p = c.TranslatePoint(new Point(), this); return new Rect(p, c.RenderSize).Contains(point); });
            if (!onCard) { handled = true; return -1; }
        }
        return 0;
    }
    public void Place()
    {
        var monitor = Native.PrimaryMonitor(); var transform = Source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(monitor.Work.Left, monitor.Monitor.Top));
        var width = transform.Transform(new Vector(monitor.Work.Right - monitor.Work.Left, 0)).X;
        Left = topLeft.X; Top = topLeft.Y; Width = Math.Max(300, width);
    }
    public bool PointerInBand(Native.Point p)
    {
        if (!IsVisible) return false;
        var local = PointFromScreen(new Point(p.X, p.Y));
        return local.X >= 15 && local.X <= ActualWidth - 15 && local.Y >= 0 && local.Y <= 227;
    }
    public void Reveal(bool latest)
    {
        IsTucking = false;
        Place(); Show();
        if (latest) { UpdateLayout(); scroller.ScrollToRightEnd(); }
        slide.BeginAnimation(TranslateTransform.YProperty, null);
        if (SystemParameters.ClientAreaAnimation)
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-230, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        foreach (CardView card in Cards.Children) card.Sway();
    }
    public void Tuck()
    {
        if (IsTucking) return;
        if (!SystemParameters.ClientAreaAnimation)
        { Hide(); foreach (CardView card in Cards.Children) card.StopAnimation(); return; }
        IsTucking = true;
        var animation = new DoubleAnimation(0, -Height, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        animation.Completed += (_, _) =>
        {
            if (!IsTucking) return;
            IsTucking = false; Hide(); slide.BeginAnimation(TranslateTransform.YProperty, null);
            foreach (CardView card in Cards.Children) card.StopAnimation();
        };
        slide.BeginAnimation(TranslateTransform.YProperty, animation);
    }
}
