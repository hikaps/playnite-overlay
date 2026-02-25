using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Playnite.SDK;
using System.Windows.Input;

namespace PlayniteOverlay.Interop;

internal static class NativeInput
{
    private static readonly ILogger logger = LogManager.GetLogger();
    
    // MapVirtualKey mapping types
    private const uint MAPVK_VK_TO_VSC = 0;
    
    // Keyboard input flags
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    // Extended keys that need KEYEVENTF_EXTENDEDKEY flag
    private static readonly HashSet<ushort> ExtendedKeys = new()
    {
        0x11, // VK_CONTROL
        0x12, // VK_MENU (Alt)
        0x10, // VK_SHIFT (for right shift)
        0x21, // VK_PRIOR (Page Up)
        0x22, // VK_NEXT (Page Down)
        0x23, // VK_END
        0x24, // VK_HOME
        0x25, // VK_LEFT
        0x26, // VK_UP
        0x27, // VK_RIGHT
        0x28, // VK_DOWN
        0x2D, // VK_INSERT
        0x2E, // VK_DELETE
        0x5B, // VK_LWIN
        0x5C, // VK_RWIN
        0x6A, // VK_MULTIPLY
        0x6B, // VK_ADD
        0x6D, // VK_SUBTRACT
        0x6E, // VK_DECIMAL
        0x6F, // VK_DIVIDE
        0x90, // VK_NUMLOCK
    };

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
        var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        var inputs = BuildInputs(modifiers, vk, scan);
        if (inputs.Count == 0)
        {
            logger.Warn($"SendHotkey produced no inputs for '{gesture}'.");
            return;
        }

        logger.Info($"SendHotkey: gesture='{gesture}', vk=0x{vk:X2}, scan=0x{scan:X2}, inputCount={inputs.Count}");

        // Try SendInput first
        var result = SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
        if (result == 0)
        {
            var error = Marshal.GetLastWin32Error();
            logger.Warn($"SendInput failed for '{gesture}'. Win32Error={error}. Trying keybd_event fallback...");
            
            // Fallback to keybd_event
            SendHotkeyViaKeybdEvent(modifiers, vk, scan);
        }
        else
        {
            logger.Info($"SendHotkey success: sent {result} inputs for '{gesture}'");
        }
    }

    private static void SendHotkeyViaKeybdEvent(ModifierKeys modifiers, ushort vk, ushort scan)
    {
        try
        {
            var isExtended = IsExtendedKey(vk);
            var flagsDown = KEYEVENTF_SCANCODE | (isExtended ? KEYEVENTF_EXTENDEDKEY : 0);
            var flagsUp = flagsDown | KEYEVENTF_KEYUP;

            // Press modifiers
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                var modVk = (byte)0x11;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsDown | KEYEVENTF_EXTENDEDKEY, 0);
            }
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                var modVk = (byte)0x10;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsDown, 0);
            }
            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                var modVk = (byte)0x12;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsDown | KEYEVENTF_EXTENDEDKEY, 0);
            }
            if (modifiers.HasFlag(ModifierKeys.Windows))
            {
                var modVk = (byte)0x5B;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsDown | KEYEVENTF_EXTENDEDKEY, 0);
            }

            // Press and release main key
            keybd_event((byte)vk, (byte)scan, flagsDown, 0);
            keybd_event((byte)vk, (byte)scan, flagsUp, 0);

            // Release modifiers (reverse order)
            if (modifiers.HasFlag(ModifierKeys.Windows))
            {
                var modVk = (byte)0x5B;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsUp | KEYEVENTF_EXTENDEDKEY, 0);
            }
            if (modifiers.HasFlag(ModifierKeys.Alt))
            {
                var modVk = (byte)0x12;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsUp | KEYEVENTF_EXTENDEDKEY, 0);
            }
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                var modVk = (byte)0x10;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsUp, 0);
            }
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                var modVk = (byte)0x11;
                var modScan = (byte)MapVirtualKey(modVk, MAPVK_VK_TO_VSC);
                keybd_event(modVk, modScan, flagsUp | KEYEVENTF_EXTENDEDKEY, 0);
            }

            logger.Info($"SendHotkey: keybd_event fallback completed for vk=0x{vk:X2}");
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"keybd_event fallback failed: {ex.Message}");
        }
    }

    private static bool IsExtendedKey(ushort vk)
    {
        return ExtendedKeys.Contains(vk);
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

    private static List<INPUT> BuildInputs(ModifierKeys modifiers, ushort vk, ushort scan)
    {
        var inputs = new List<INPUT>();
        var isExtended = IsExtendedKey(vk);
        
        // Press modifiers
        AddModifierInputs(inputs, modifiers, true);
        
        // Press and release main key with KEYEVENTF_SCANCODE (critical for games/OBS)
        inputs.Add(CreateKeyInput(vk, scan, false, isExtended));
        inputs.Add(CreateKeyInput(vk, scan, true, isExtended));
        
        // Release modifiers
        AddModifierInputs(inputs, modifiers, false);
        
        return inputs;
    }

    private static void AddModifierInputs(List<INPUT> inputs, ModifierKeys modifiers, bool keyDown)
    {
        // All modifier keys are extended keys
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            var vk = (ushort)0x11;
            var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            inputs.Add(CreateKeyInput(vk, scan, !keyDown, true));
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            var vk = (ushort)0x10;
            var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            inputs.Add(CreateKeyInput(vk, scan, !keyDown, false));
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            var vk = (ushort)0x12;
            var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            inputs.Add(CreateKeyInput(vk, scan, !keyDown, true));
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            var vk = (ushort)0x5B;
            var scan = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            inputs.Add(CreateKeyInput(vk, scan, !keyDown, true));
        }
    }

    private static INPUT CreateKeyInput(ushort vk, ushort scan, bool keyUp, bool isExtended)
    {
        uint flags = KEYEVENTF_SCANCODE; // Always use scancode - critical for games
        if (isExtended) flags |= KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= KEYEVENTF_KEYUP;

        return new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    #region Native Methods

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

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

        public static readonly int Size = Marshal.SizeOf(typeof(INPUT));
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
        [FieldOffset(0)]
        public KEYBDINPUT ki;
        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
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
