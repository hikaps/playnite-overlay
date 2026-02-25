using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Playnite.SDK;
using System.Windows.Input;

namespace PlayniteOverlay.Interop;

internal static class NativeInput
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int INPUT_KEYBOARD = 1;

    private const int ModifierDelayMs = 10;      // Delay after modifier press/release
    private const int KeyHoldDelayMs = 50;       // How long to hold main key (must be > 25ms for OBS polling)

    public static void SendHotkey(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture))
        {
            logger.Warn("SendHotkey called with empty gesture.");
            return;
        }

        ParseGesture(gesture!, out var modifiers, out var keyToken);
        if (keyToken == null)
        {
            logger.Warn($"SendHotkey could not parse key token from '{gesture}'.");
            return;
        }

        if (KeyFromString(keyToken) is not Key key)
        {
            logger.Warn($"SendHotkey could not resolve key '{keyToken}' from '{gesture}'.");
            return;
        }

        var vk = (ushort)KeyInterop.VirtualKeyFromKey(key);
        logger.Info($"SendHotkey: gesture='{gesture}', vk=0x{vk:X2}");

        SendHotkeyWithDelays(modifiers, vk);
    }

    /// <summary>
    /// Sends hotkey with delays between key events to ensure polling-based
    /// hotkey detectors (like OBS) can detect the key down state.
    /// </summary>
    private static void SendHotkeyWithDelays(ModifierKeys modifiers, ushort vk)
    {
        try
        {
            // Use 64-bit or 32-bit code path based on IntPtr size
            if (IntPtr.Size == 8)
            {
                SendHotkey64(modifiers, vk);
            }
            else
            {
                SendHotkey32(modifiers, vk);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"SendHotkeyWithDelays failed: {ex.Message}");
            // Fallback to keybd_event
            SendHotkeyViaKeybdEvent(modifiers, vk);
        }
    }

    private static void SendHotkey32(ModifierKeys modifiers, ushort vk)
    {
        var inputs = new List<INPUT32>();
        int inputSize = Marshal.SizeOf(typeof(INPUT32));
        const int expectedInputSize = 28;

        if (inputSize != expectedInputSize)
        {
            logger.Warn($"SendHotkey32: INPUT size mismatch (expected {expectedInputSize}, got {inputSize}). Falling back to keybd_event.");
            SendHotkeyViaKeybdEvent(modifiers, vk);
            return;
        }

        // 1. Press modifiers
        AddModifierInputs32(inputs, modifiers, keyDown: true);
        if (inputs.Count > 0)
        {
            uint sent = SendInput32((uint)inputs.Count, inputs.ToArray(), inputSize);
            if (sent == 0)
            {
                logger.Warn($"SendHotkey32: SendInput failed for modifiers, Win32Error={Marshal.GetLastWin32Error()}");
                SendHotkeyViaKeybdEvent(modifiers, vk);
                return;
            }
            Thread.Sleep(ModifierDelayMs);
        }

        // 2. Press main key
        inputs.Clear();
        inputs.Add(CreateKeyInput32(vk, keyUp: false));
        uint sentKeyDown = SendInput32(1, inputs.ToArray(), inputSize);
        if (sentKeyDown == 0)
        {
            logger.Warn($"SendHotkey32: SendInput failed for key down, Win32Error={Marshal.GetLastWin32Error()}");
            SendHotkeyViaKeybdEvent(modifiers, vk);
            return;
        }
        Thread.Sleep(KeyHoldDelayMs);

        // 3. Release main key
        inputs.Clear();
        inputs.Add(CreateKeyInput32(vk, keyUp: true));
        uint sentKeyUp = SendInput32(1, inputs.ToArray(), inputSize);
        if (sentKeyUp == 0)
        {
            logger.Warn($"SendHotkey32: SendInput failed for key up, Win32Error={Marshal.GetLastWin32Error()}");
            SendHotkeyViaKeybdEvent(modifiers, vk);
            return;
        }
        Thread.Sleep(ModifierDelayMs);

        // 4. Release modifiers
        inputs.Clear();
        AddModifierInputs32(inputs, modifiers, keyDown: false);
        if (inputs.Count > 0)
        {
            uint sentModsUp = SendInput32((uint)inputs.Count, inputs.ToArray(), inputSize);
            if (sentModsUp == 0)
            {
                logger.Warn($"SendHotkey32: SendInput failed for modifiers up, Win32Error={Marshal.GetLastWin32Error()}");
                SendHotkeyViaKeybdEvent(modifiers, vk);
                return;
            }
        }
    }

    private static void SendHotkey64(ModifierKeys modifiers, ushort vk)
    {
        var inputs = new List<INPUT64>();
        int inputSize = Marshal.SizeOf(typeof(INPUT64));
        const int expectedInputSize = 40;

        if (inputSize != expectedInputSize)
        {
            logger.Warn($"SendHotkey64: INPUT size mismatch (expected {expectedInputSize}, got {inputSize}). Falling back to keybd_event.");
            SendHotkeyViaKeybdEvent(modifiers, vk);
            return;
        }

        // 1. Press modifiers
        AddModifierInputs64(inputs, modifiers, keyDown: true);
        if (inputs.Count > 0)
        {
            uint sent = SendInput64((uint)inputs.Count, inputs.ToArray(), inputSize);
            if (sent == 0)
            {
                logger.Warn($"SendHotkey64: SendInput failed for modifiers, Win32Error={Marshal.GetLastWin32Error()}");
                SendHotkeyViaKeybdEvent(modifiers, vk);
                return;
            }
            Thread.Sleep(ModifierDelayMs);
        }

        // 2. Press main key
        inputs.Clear();
        inputs.Add(CreateKeyInput64(vk, keyUp: false));
        uint sentKeyDown = SendInput64(1, inputs.ToArray(), inputSize);
        if (sentKeyDown == 0)
        {
            logger.Warn($"SendHotkey64: SendInput failed for key down, Win32Error={Marshal.GetLastWin32Error()}");
            SendHotkeyViaKeybdEvent(modifiers, vk);
            return;
        }
        Thread.Sleep(KeyHoldDelayMs);

        // 3. Release main key
        inputs.Clear();
        inputs.Add(CreateKeyInput64(vk, keyUp: true));
        uint sentKeyUp = SendInput64(1, inputs.ToArray(), inputSize);
        if (sentKeyUp == 0)
        {
            logger.Warn($"SendHotkey64: SendInput failed for key up, Win32Error={Marshal.GetLastWin32Error()}");
            SendHotkeyViaKeybdEvent(modifiers, vk);
            return;
        }
        Thread.Sleep(ModifierDelayMs);

        // 4. Release modifiers
        inputs.Clear();
        AddModifierInputs64(inputs, modifiers, keyDown: false);
        if (inputs.Count > 0)
        {
            uint sentModsUp = SendInput64((uint)inputs.Count, inputs.ToArray(), inputSize);
            if (sentModsUp == 0)
            {
                logger.Warn($"SendHotkey64: SendInput failed for modifiers up, Win32Error={Marshal.GetLastWin32Error()}");
                SendHotkeyViaKeybdEvent(modifiers, vk);
                return;
            }
        }
    }

    private static void SendHotkeyViaKeybdEvent(ModifierKeys modifiers, ushort vk)
    {
        try
        {
            // Press modifiers
            if (modifiers.HasFlag(ModifierKeys.Control))
                keybd_event(0x11, 0, 0, 0);
            if (modifiers.HasFlag(ModifierKeys.Shift))
                keybd_event(0x10, 0, 0, 0);
            if (modifiers.HasFlag(ModifierKeys.Alt))
                keybd_event(0x12, 0, 0, 0);
            if (modifiers.HasFlag(ModifierKeys.Windows))
                keybd_event(0x5B, 0, 0, 0);

            Thread.Sleep(ModifierDelayMs);

            // Press main key
            keybd_event((byte)vk, 0, 0, 0);
            Thread.Sleep(KeyHoldDelayMs);

            // Release main key
            keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, 0);
            Thread.Sleep(ModifierDelayMs);

            // Release modifiers (reverse order)
            if (modifiers.HasFlag(ModifierKeys.Windows))
                keybd_event(0x5B, 0, KEYEVENTF_KEYUP, 0);
            if (modifiers.HasFlag(ModifierKeys.Alt))
                keybd_event(0x12, 0, KEYEVENTF_KEYUP, 0);
            if (modifiers.HasFlag(ModifierKeys.Shift))
                keybd_event(0x10, 0, KEYEVENTF_KEYUP, 0);
            if (modifiers.HasFlag(ModifierKeys.Control))
                keybd_event(0x11, 0, KEYEVENTF_KEYUP, 0);

            logger.Info($"SendHotkey: keybd_event fallback completed for vk=0x{vk:X2}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"keybd_event fallback failed: {ex.Message}");
        }
    }

    private static Key? KeyFromString(string keyToken)
    {
        var converter = new KeyConverter();
        return converter.ConvertFromString(keyToken) is Key key ? key : null;
    }

    private static void ParseGesture(string gesture, out ModifierKeys modifiers, out string? keyToken)
    {
        modifiers = ModifierKeys.None;
        keyToken = null;
        var tokens = gesture.Split('+');
        foreach (var raw in tokens)
        {
            var t = raw.Trim();
            if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || t.Equals("Control", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Control;
            else if (t.Equals("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Alt;
            else if (t.Equals("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Shift;
            else if (t.Equals("Win", StringComparison.OrdinalIgnoreCase) || t.Equals("Windows", StringComparison.OrdinalIgnoreCase)) modifiers |= ModifierKeys.Windows;
            else keyToken = t;
        }
    }

    #region 32-bit Helpers

    private static void AddModifierInputs32(List<INPUT32> inputs, ModifierKeys modifiers, bool keyDown)
    {
        var flags = keyDown ? 0u : KEYEVENTF_KEYUP;

        if (modifiers.HasFlag(ModifierKeys.Control))
            inputs.Add(CreateKeyInput32WithFlags(0x11, flags));
        if (modifiers.HasFlag(ModifierKeys.Shift))
            inputs.Add(CreateKeyInput32WithFlags(0x10, flags));
        if (modifiers.HasFlag(ModifierKeys.Alt))
            inputs.Add(CreateKeyInput32WithFlags(0x12, flags));
        if (modifiers.HasFlag(ModifierKeys.Windows))
            inputs.Add(CreateKeyInput32WithFlags(0x5B, flags));
    }

    private static INPUT32 CreateKeyInput32(ushort vk, bool keyUp)
    {
        return CreateKeyInput32WithFlags(vk, keyUp ? KEYEVENTF_KEYUP : 0u);
    }

    private static INPUT32 CreateKeyInput32WithFlags(ushort vk, uint flags)
    {
        return new INPUT32
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }

    #endregion

    #region 64-bit Helpers

    private static void AddModifierInputs64(List<INPUT64> inputs, ModifierKeys modifiers, bool keyDown)
    {
        var flags = keyDown ? 0u : KEYEVENTF_KEYUP;

        if (modifiers.HasFlag(ModifierKeys.Control))
            inputs.Add(CreateKeyInput64WithFlags(0x11, flags));
        if (modifiers.HasFlag(ModifierKeys.Shift))
            inputs.Add(CreateKeyInput64WithFlags(0x10, flags));
        if (modifiers.HasFlag(ModifierKeys.Alt))
            inputs.Add(CreateKeyInput64WithFlags(0x12, flags));
        if (modifiers.HasFlag(ModifierKeys.Windows))
            inputs.Add(CreateKeyInput64WithFlags(0x5B, flags));
    }

    private static INPUT64 CreateKeyInput64(ushort vk, bool keyUp)
    {
        return CreateKeyInput64WithFlags(vk, keyUp ? KEYEVENTF_KEYUP : 0u);
    }

    private static INPUT64 CreateKeyInput64WithFlags(ushort vk, uint flags)
    {
        return new INPUT64
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = 0,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        };
    }

    #endregion

    #region Native Methods

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SendInput")]
    private static extern uint SendInput32(uint nInputs, INPUT32[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SendInput")]
    private static extern uint SendInput64(uint nInputs, INPUT64[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    #endregion

    #region Native Structures

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    private struct INPUT32
    {
        [FieldOffset(0)]
        public int type;

        [FieldOffset(4)]
        public KEYBDINPUT ki;

        [FieldOffset(4)]
        public MOUSEINPUT mi;

        [FieldOffset(4)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT64
    {
        [FieldOffset(0)]
        public int type;

        [FieldOffset(8)]
        public KEYBDINPUT ki;

        [FieldOffset(8)]
        public MOUSEINPUT mi;

        [FieldOffset(8)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    #endregion
}
