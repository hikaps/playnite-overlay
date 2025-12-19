using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PlayniteOverlay.Input;

/// <summary>
/// Blocks keyboard and mouse input from reaching other applications while the overlay is active.
/// Uses low-level hooks (WH_KEYBOARD_LL, WH_MOUSE_LL) to intercept input at the system level,
/// which is necessary to prevent exclusive fullscreen games from receiving input.
/// </summary>
internal sealed class InputBlocker : IDisposable
{
    #region Win32 API

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int HC_ACTION = 0;

    // Keyboard messages
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    // Mouse messages
    private const int WM_MOUSEMOVE = 0x0200;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    #endregion

    private IntPtr keyboardHookHandle = IntPtr.Zero;
    private IntPtr mouseHookHandle = IntPtr.Zero;
    private LowLevelProc? keyboardProc;
    private LowLevelProc? mouseProc;
    private volatile bool disposed;
    private volatile bool blockAllKeyboard = true;
    private volatile bool blockMouse;

    /// <summary>
    /// Called when a key event is intercepted.
    /// Return true to block the key, false to let it pass through.
    /// </summary>
    public Func<uint, bool, bool>? OnKeyEvent { get; set; }

    /// <summary>
    /// Called when a mouse event is intercepted.
    /// Return true to block the event, false to let it pass through.
    /// </summary>
    public Func<int, bool>? OnMouseEvent { get; set; }

    /// <summary>
    /// When true, blocks all keyboard input (default behavior when overlay is active).
    /// When false, uses OnKeyEvent to decide which keys to block.
    /// </summary>
    public bool BlockAllKeyboard
    {
        get => blockAllKeyboard;
        set => blockAllKeyboard = value;
    }

    /// <summary>
    /// When true, blocks mouse clicks from reaching other applications.
    /// Mouse movement is not blocked.
    /// </summary>
    public bool BlockMouse
    {
        get => blockMouse;
        set => blockMouse = value;
    }

    public void Install()
    {
        if (keyboardHookHandle != IntPtr.Zero)
            return;

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = GetModuleHandle(module?.ModuleName);

        // Keep delegate references alive
        keyboardProc = KeyboardHookCallback;
        mouseProc = MouseHookCallback;

        keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, moduleHandle, 0);

        if (BlockMouse)
        {
            mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, moduleHandle, 0);
        }
    }

    public void Uninstall()
    {
        if (keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHookHandle);
            keyboardHookHandle = IntPtr.Zero;
        }

        if (mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(mouseHookHandle);
            mouseHookHandle = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int msg = wParam.ToInt32();
            bool isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

            bool shouldBlock;

            if (BlockAllKeyboard)
            {
                // Block all keyboard input
                shouldBlock = true;
            }
            else if (OnKeyEvent != null)
            {
                // Let callback decide
                shouldBlock = OnKeyEvent(hookStruct.vkCode, isKeyDown);
            }
            else
            {
                shouldBlock = false;
            }

            if (shouldBlock)
            {
                // Return non-zero to block the input
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= HC_ACTION && BlockMouse)
        {
            int msg = wParam.ToInt32();

            // Don't block mouse move - let the overlay receive it
            if (msg != WM_MOUSEMOVE)
            {
                bool shouldBlock = OnMouseEvent?.Invoke(msg) ?? false;

                if (shouldBlock)
                {
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(mouseHookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Uninstall();
    }
}
