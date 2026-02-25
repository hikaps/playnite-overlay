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
    // Delays for polling-based hotkey detector compatibility (e.g., OBS)
    // OBS polls GetAsyncKeyState every 25ms, so we need delays to ensure
    // the polling thread sees the key down state before key up is processed
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

        // Use SendInput with delays between key events for OBS compatibility
        // OBS polls GetAsyncKeyState every 25ms, so we need delays to ensure
        // the polling thread sees the key down state before key up is processed
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
            var inputs = new List<INPUT>();
            int inputSize = Marshal.SizeOf(typeof(INPUT));

            // 1. Press modifiers
            AddModifierInputs(inputs, modifiers, keyDown: true);
            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), inputSize);
                Thread.Sleep(ModifierDelayMs);
            }

            // 2. Press main key
            inputs.Clear();
            inputs.Add(CreateKeyInput(vk, keyUp: false));
            SendInput(1, inputs.ToArray(), inputSize);

            // 3. Wait long enough for polling detectors to see the key down
            // OBS polls every 25ms, so 50ms ensures at least one poll sees it
            Thread.Sleep(KeyHoldDelayMs);

            // 4. Release main key
            inputs.Clear();
            inputs.Add(CreateKeyInput(vk, keyUp: true));
            SendInput(1, inputs.ToArray(), inputSize);
            Thread.Sleep(ModifierDelayMs);

            // 5. Release modifiers
            inputs.Clear();
            AddModifierInputs(inputs, modifiers, keyDown: false);
            if (inputs.Count > 0)
            {
                SendInput((uint)inputs.Count, inputs.ToArray(), inputSize);
            }

            logger.Info($"SendHotkeyWithDelays: completed for vk=0x{vk:X2}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"SendHotkeyWithDelays failed: {ex.Message}");
            // Fallback to keybd_event
            SendHotkeyViaKeybdEvent(modifiers, vk);
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

    private static void AddModifierInputs(List<INPUT> inputs, ModifierKeys modifiers, bool keyDown)
    {
        // Note: When keyDown=true, we add key-down inputs
        // When keyDown=false, we add key-up inputs
        var flags = keyDown ? 0u : KEYEVENTF_KEYUP;

        if (modifiers.HasFlag(ModifierKeys.Control))
            inputs.Add(CreateKeyInputWithFlags(0x11, flags));
        if (modifiers.HasFlag(ModifierKeys.Shift))
            inputs.Add(CreateKeyInputWithFlags(0x10, flags));
        if (modifiers.HasFlag(ModifierKeys.Alt))
            inputs.Add(CreateKeyInputWithFlags(0x12, flags));
        if (modifiers.HasFlag(ModifierKeys.Windows))
            inputs.Add(CreateKeyInputWithFlags(0x5B, flags));
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return CreateKeyInputWithFlags(vk, keyUp ? KEYEVENTF_KEYUP : 0u);
    }

    private static INPUT CreateKeyInputWithFlags(ushort vk, uint flags)
    {
        return new INPUT
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

    #region Native Methods

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    #endregion

    #region Native Structures (64-bit aligned)

    // On 64-bit Windows, the INPUT struct requires proper alignment:
    // - type (DWORD, 4 bytes) at offset 0
    // - 4 bytes of padding (to align union to 8-byte boundary)
    // - KEYBDINPUT starts at offset 8
    // This matches Microsoft's PowerToys implementation.

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)]
        public int type;

        [FieldOffset(8)]  // Offset 8 for 64-bit alignment (4 bytes type + 4 bytes padding)
        public KEYBDINPUT ki;
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

    #endregion
}
