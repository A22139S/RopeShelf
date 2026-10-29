using System.Runtime.InteropServices;

namespace RopeShelf.Windows;

public static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MonitorInfo
    { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int maxCount);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(in Guid id, uint flags, nint token, out nint path);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);

    public static string ScreenshotFolder()
    {
        var id = new Guid("b7bede81-df94-4682-a7d8-57a52620b86f");
        var hr = SHGetKnownFolderPath(id, 0x4000, 0, out var pointer); // DONT_VERIFY, never create folders
        if (hr >= 0) { try { return Marshal.PtrToStringUni(pointer)!; } finally { Marshal.FreeCoTaskMem(pointer); } }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }
    public static MonitorInfo PrimaryMonitor()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(MonitorFromPoint(new Point(), 1), ref info);
        return info;
    }
    private static nint lastForeground;
    private static long nextFullscreenCheck;
    private static bool lastFullscreen;
    public static bool IsFullscreen(nint own)
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0 || hwnd == own) return false;
        var now = Environment.TickCount64;
        if (hwnd == lastForeground && now < nextFullscreenCheck) return lastFullscreen;
        lastForeground = hwnd; nextFullscreenCheck = now + 300;
        return lastFullscreen = CalculateFullscreen(hwnd);
    }
    private static bool CalculateFullscreen(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info) || !GetWindowRect(hwnd, out var r)) return false;
        if (!(r.Left <= info.Monitor.Left && r.Top <= info.Monitor.Top && r.Right >= info.Monitor.Right && r.Bottom >= info.Monitor.Bottom)) return false;
        var name = new System.Text.StringBuilder(128); GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
    public static bool CursorOverExplorer()
    {
        GetCursorPos(out var p);
        GetWindowThreadProcessId(GetAncestor(WindowFromPoint(p), 2), out var pid);
        try { using var process = System.Diagnostics.Process.GetProcessById((int)pid); return process.ProcessName == "explorer"; } catch { return false; }
    }
}
