using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace PlayniteOverlay;

internal static class Monitors
{
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public static Rect GetActiveMonitorBoundsInPixels()
    {
        GetCursorPos(out var pt);
        var hm = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hm, ref mi))
        {
            return new Rect(0, 0, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        }
        var width = mi.rcMonitor.Right - mi.rcMonitor.Left;
        var height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        return new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, width, height);
    }

    /// <summary>
    /// Gets the monitor handle for the current foreground window (typically the game).
    /// </summary>
    public static IntPtr GetForegroundWindowMonitorHandle()
    {
        var foreground = GetForegroundWindow();
        return MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>
    /// Gets the monitor bounds for the current foreground window (typically the game).
    /// </summary>
    public static Rect GetForegroundWindowMonitorBounds()
    {
        var foreground = GetForegroundWindow();
        return GetMonitorBoundsForWindow(foreground);
    }

    /// <summary>
    /// Gets the monitor bounds (in pixels) for a specific window handle.
    /// </summary>
    public static Rect GetMonitorBoundsForWindow(IntPtr hwnd)
    {
        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref mi))
        {
            return new Rect(0, 0, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        }

        var width = mi.rcMonitor.Right - mi.rcMonitor.Left;
        var height = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
        return new Rect(mi.rcMonitor.Left, mi.rcMonitor.Top, width, height);
    }

    public static Rect PixelsToDips(Window window, Rect pixelRect)
    {
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget == null)
        {
            // Fallback to 1:1 mapping
            return pixelRect;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new System.Windows.Point(pixelRect.Left, pixelRect.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(pixelRect.Right, pixelRect.Bottom));
        return new Rect(topLeft, bottomRight);
    }
}

