using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PlayniteOverlay;

internal sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly int id;
    private HwndSource? source;
    private HwndSourceHook? hook;
    private Action? onHotkey;

    public HotkeyManager(int id = 0xBEEF)
    {
        this.id = id;
    }

    public bool Register(string gesture, Action onHotkey)
    {
        Unregister();

        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        if (Application.Current?.MainWindow == null)
        {
            return false;
        }

        var tokens = gesture.Split('+');
        uint mods = 0;
        string? keyToken = null;
        foreach (var raw in tokens)
        {
            var t = raw.Trim();
            if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || t.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
            else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;
            else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
            else if (t.Equals("Win", StringComparison.OrdinalIgnoreCase) || t.Equals("Windows", StringComparison.OrdinalIgnoreCase)) mods |= MOD_WIN;
            else keyToken = t; // assume last meaningful token is key
        }

        if (keyToken == null)
        {
            return false;
        }

        var kc = new KeyConverter();
        if (kc.ConvertFromString(keyToken) is not Key key)
        {
            return false;
        }
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);

        var helper = new WindowInteropHelper(Application.Current.MainWindow);
        if (helper.Handle == IntPtr.Zero)
        {
            return false;
        }
        source = HwndSource.FromHwnd(helper.Handle);
        if (source == null)
        {
            return false;
        }

        this.onHotkey = onHotkey;
        hook = new HwndSourceHook(WndProc);
        source.AddHook(hook);

        if (!RegisterHotKey(helper.Handle, id, mods, vk))
        {
            // Cleanup hook if registration fails
            source.RemoveHook(hook);
            hook = null;
            onHotkey = null;
            return false;
        }

        return true;
    }

    public void Unregister()
    {
        if (source != null && hook != null)
        {
            var hwnd = source.Handle;
            try { UnregisterHotKey(hwnd, id); } catch { /* ignore */ }
            source.RemoveHook(hook);
        }
        hook = null;
        source = null;
        onHotkey = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == id)
        {
            onHotkey?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
    }
}

