using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Playnite.SDK;
using System.Windows.Input;

namespace PlayniteOverlay.Interop;

internal static class NativeInput
{
    private static readonly ILogger logger = LogManager.GetLogger();

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

        var result = SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
        if (result == 0)
        {
            var error = Marshal.GetLastWin32Error();
            logger.Warn($"SendHotkey failed for '{gesture}'. Win32Error={error}.");
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
            inputs.Add(CreateKeyInput(0x11, keyDown));
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            inputs.Add(CreateKeyInput(0x10, keyDown));
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            inputs.Add(CreateKeyInput(0x12, keyDown));
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            inputs.Add(CreateKeyInput(0x5B, keyDown));
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
                    dwFlags = keyUp ? 0x0002u : 0u,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
}