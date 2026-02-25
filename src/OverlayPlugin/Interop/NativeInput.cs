using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Playnite.SDK;
using System.Windows.Input;

namespace PlayniteOverlay.Interop;

internal static class NativeInput
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private const uint KEYEVENTF_KEYUP = 0x0002;

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
        var inputs = BuildInputs(modifiers, vk);
        if (inputs.Count == 0)
        {
            logger.Warn($"SendHotkey produced no inputs for '{gesture}'.");
            return;
        }

        logger.Info($"SendHotkey: gesture='{gesture}', vk=0x{vk:X2}, inputCount={inputs.Count}");

        var result = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
        if (result == 0)
        {
            var error = Marshal.GetLastWin32Error();
            logger.Warn($"SendInput failed for '{gesture}'. Win32Error={error}. Trying keybd_event fallback...");

            // Fallback to keybd_event
            SendHotkeyViaKeybdEvent(modifiers, vk);
        }
        else
        {
            logger.Info($"SendHotkey success: sent {result} inputs for '{gesture}'");
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

            // Press and release main key
            keybd_event((byte)vk, 0, 0, 0);
            keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, 0);

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

    private static List<INPUT> BuildInputs(ModifierKeys modifiers, ushort vk)
    {
        var inputs = new List<INPUT>();
        AddModifierInputs(inputs, modifiers, true);
        inputs.Add(CreateKeyInput(vk, false));
        inputs.Add(CreateKeyInput(vk, true));
        AddModifierInputs(inputs, modifiers, false);
        return inputs;
    }

    private static void AddModifierInputs(List<INPUT> inputs, ModifierKeys modifiers, bool keyDown)
    {
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            inputs.Add(CreateKeyInput(0x11, !keyDown));
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            inputs.Add(CreateKeyInput(0x10, !keyDown));
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            inputs.Add(CreateKeyInput(0x12, !keyDown));
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            inputs.Add(CreateKeyInput(0x5B, !keyDown));
        }
    }

    private static INPUT CreateKeyInput(ushort vk, bool keyUp)
    {
        return new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    #region Native Methods

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    #endregion

    #region Native Structures

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
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
