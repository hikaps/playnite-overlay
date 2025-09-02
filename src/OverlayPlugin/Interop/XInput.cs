using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

internal static class XInput
{
    // ========== State APIs ==========
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    public const int ERROR_SUCCESS = 0x0;
    public const ushort XINPUT_GAMEPAD_DPAD_UP = 0x0001;
    public const ushort XINPUT_GAMEPAD_DPAD_DOWN = 0x0002;
    public const ushort XINPUT_GAMEPAD_DPAD_LEFT = 0x0004;
    public const ushort XINPUT_GAMEPAD_DPAD_RIGHT = 0x0008;
    public const ushort XINPUT_GAMEPAD_START = 0x0010;
    public const ushort XINPUT_GAMEPAD_BACK = 0x0020;
    public const ushort XINPUT_GAMEPAD_LEFT_THUMB = 0x0040;
    public const ushort XINPUT_GAMEPAD_RIGHT_THUMB = 0x0080;
    public const ushort XINPUT_GAMEPAD_LEFT_SHOULDER = 0x0100;
    public const ushort XINPUT_GAMEPAD_RIGHT_SHOULDER = 0x0200;
    public const ushort XINPUT_GAMEPAD_A = 0x1000;
    public const ushort XINPUT_GAMEPAD_B = 0x2000;
    public const ushort XINPUT_GAMEPAD_X = 0x4000;
    public const ushort XINPUT_GAMEPAD_Y = 0x8000;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState1_4(int dwUserIndex, out XINPUT_STATE pState);

    // Fallback to older DLLs if needed
    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState1_3(int dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState9_1_0(int dwUserIndex, out XINPUT_STATE pState);

    public static bool TryGetState(int userIndex, out XINPUT_STATE state)
    {
        try
        {
            if (XInputGetState1_4(userIndex, out state) == ERROR_SUCCESS)
                return true;
        }
        catch
        {
            // ignore, try next
        }
        try
        {
            if (XInputGetState1_3(userIndex, out state) == ERROR_SUCCESS)
                return true;
        }
        catch { }
        try
        {
            if (XInputGetState9_1_0(userIndex, out state) == ERROR_SUCCESS)
                return true;
        }
        catch { }

        state = default;
        return false;
    }

    // ========== Keystroke APIs ==========
    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_KEYSTROKE
    {
        public ushort VirtualKey;
        public char Unicode;
        public ushort Flags;
        public byte UserIndex;
        public byte HidCode;
    }

    public const ushort XINPUT_KEYSTROKE_KEYDOWN = 0x0001;
    public const ushort XINPUT_KEYSTROKE_KEYUP = 0x0002;
    public const ushort XINPUT_KEYSTROKE_REPEAT = 0x0004;

    // Common VK_PAD constants subset (values based on widely used headers/community references)
    public const ushort VK_PAD_GUIDE_BUTTON = 0x0400;
    public const ushort VK_PAD_START = 0x5814;
    public const ushort VK_PAD_BACK = 0x5815;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetKeystroke")]
    private static extern int XInputGetKeystroke1_4(int dwUserIndex, int dwReserved, out XINPUT_KEYSTROKE pKeystroke);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetKeystroke")]
    private static extern int XInputGetKeystroke1_3(int dwUserIndex, int dwReserved, out XINPUT_KEYSTROKE pKeystroke);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetKeystroke")]
    private static extern int XInputGetKeystroke9_1_0(int dwUserIndex, int dwReserved, out XINPUT_KEYSTROKE pKeystroke);

    public static bool TryGetKeystroke(int userIndex, out XINPUT_KEYSTROKE stroke)
    {
        try
        {
            if (XInputGetKeystroke1_4(userIndex, 0, out stroke) == ERROR_SUCCESS)
                return true;
        }
        catch { }
        try
        {
            if (XInputGetKeystroke1_3(userIndex, 0, out stroke) == ERROR_SUCCESS)
                return true;
        }
        catch { }
        try
        {
            if (XInputGetKeystroke9_1_0(userIndex, 0, out stroke) == ERROR_SUCCESS)
                return true;
        }
        catch { }
        stroke = default;
        return false;
    }
}
