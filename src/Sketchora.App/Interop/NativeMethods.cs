using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Sketchora.App.Interop;

internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const long WsExTransparent = 0x20;
    internal const long WsExToolWindow = 0x80;
    internal const long WsExNoActivate = 0x08000000;
    internal const int WmHotkey = 0x0312;
    internal const int WmDisplayChange = 0x007E;
    internal const int WmDpiChanged = 0x02E0;
    internal const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000;
    private const uint SwpNoActivate = 0x10, SwpFrameChanged = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint rasterOperation);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out Rect rect);
    internal static void ConstrainToolbar(nint hwnd)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetWindowRect(hwnd, out var window) || !GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return;
        var x = Math.Clamp(window.Left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - window.Width));
        var y = Math.Clamp(window.Top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - window.Height));
        if (x != window.Left || y != window.Top)
            SetWindowPos(hwnd, 0, x, y, 0, 0, 0x1 | 0x4 | SwpNoActivate);
    }
    internal static Rect DesktopBounds()
    {
        var left = GetSystemMetrics(76); var top = GetSystemMetrics(77);
        return new Rect { Left = left, Top = top, Right = left + GetSystemMetrics(78), Bottom = top + GetSystemMetrics(79) };
    }

    internal static Rect PrimaryDisplayBounds()
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        // The Windows primary monitor always includes the desktop origin.
        if (!GetMonitorInfo(MonitorFromPoint(default, 2), ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return info.Monitor;
    }

    internal static void PositionOverlay(nint handle, Rect bounds)
    {
        if (!SetWindowPos(handle, new nint(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                SwpNoActivate | SwpFrameChanged))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    internal static void SetPassThrough(nint handle, bool enabled)
    {
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        style |= WsExToolWindow | WsExNoActivate;
        style = enabled ? style | WsExTransparent : style & ~WsExTransparent;
        Marshal.SetLastPInvokeError(0);
        var previous = SetWindowLongPtr(handle, GwlExStyle, new nint(style));
        if (previous == 0 && Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
}
