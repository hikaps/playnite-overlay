using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Playnite.SDK;
using System.Windows.Input;

namespace PlayniteOverlay.Interop;

internal static class NativeInput
{
    private static readonly ILogger logger = LogManager.GetLogger();
    
    private const uint MAPVK_VK_TO_VSC = 0;

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

        logger.Info($"SendHotkey: gesture='{gesture}', modifiers={modifiers}, vk=0x{vk:X2}, scan=0x{scan:X2}, inputCount={inputs.Count}");

        var result = SendInput((uint)inputs.Count, inputs.ToArray(), INPUT.Size);
        if (result == 0)
        {
            var error = Marshal.GetLastWin32Error();
            logger.Warn($"SendHotkey failed for '{gesture}'. Win32Error={error}.");
        }
        else
        {
            logger.Info($"SendHotkey success: sent {result} inputs for '{gesture}'");
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

    private static List<INPUT> BuildInputs(ModifierKeys modifiers, ushort vk, ushort scan)
    {
        var inputs = new List<INPUT>();
        AddModifierInputs(inputs, modifiers, vk => (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC), true);
        inputs.Add(CreateKeyInput(vk, scan, false));
        inputs.Add(CreateKeyInput(vk, scan, true));
        AddModifierInputs(inputs, modifiers, vk => (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC), false);
        return inputs;
    }

    private static void AddModifierInputs(List<INPUT> inputs, ModifierKeys modifiers, Func<ushort, ushort> getScan, bool keyDown)
    {
        var keyUp = !keyDown;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            var vk = (ushort)0x11;
            inputs.Add(CreateKeyInput(vk, getScan(vk), keyUp));
        }
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            var vk = (ushort)0x10;
            inputs.Add(CreateKeyInput(vk, getScan(vk), keyUp));
        }
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            var vk = (ushort)0x12;
            inputs.Add(CreateKeyInput(vk, getScan(vk), keyUp));
        }
        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            var vk = (ushort)0x5B;
            inputs.Add(CreateKeyInput(vk, getScan(vk), keyUp));
        }
    }

    private static INPUT CreateKeyInput(ushort vk, ushort scan, bool keyUp)
    {
        return new INPUT
        {
            type = 1,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scan,
                    dwFlags = keyUp ? 0x0002u : 0u,
                    time = 0,
                    dwExtraInfo = UIntPtr.Zero
                }
            }
        };
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

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