using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace RopeShelf.App;

public partial class App : Application
{
    private ShelfController? controller;
    private Mutex? instance;
    public static string? Argument(string name)
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemePalette.ApplyFromSystem();
        var root = Argument("--data-root");
        var key = root == null ? "Local\\RopeShelf" : "Local\\RopeShelf-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root)))[..12];
        instance = new Mutex(true, key, out var first);
        if (!first) { MessageBox.Show("绳间已经在托盘运行，请从托盘展开。", "绳间"); Shutdown(); return; }
        DispatcherUnhandledException += OnUnhandled;
        try
        {
            controller = new ShelfController(root, e.Args.Contains("--no-input"));
            _ = StartControllerAsync(e.Args.Contains("--demo"));
        }
        catch (Exception error) { MessageBox.Show(error.Message, "绳间启动失败", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    private async Task StartControllerAsync(bool demo)
    {
        try { await controller!.StartAsync(demo); }
        catch (Exception error) { controller?.Report("启动未完成：" + error.Message); MessageBox.Show(error.ToString(), "绳间初始化失败"); Shutdown(1); }
    }
    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    { controller?.Report("操作失败：" + e.Exception.Message); e.Handled = true; }
    protected override void OnExit(ExitEventArgs e)
    { controller?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
