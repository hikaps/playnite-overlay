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
    // Guide button - undocumented, only available via XInputGetStateEx (ordinal 100)
    public const ushort XINPUT_GAMEPAD_GUIDE = 0x0400;
    public const ushort XINPUT_GAMEPAD_A = 0x1000;
    public const ushort XINPUT_GAMEPAD_B = 0x2000;
    public const ushort XINPUT_GAMEPAD_X = 0x4000;
    public const ushort XINPUT_GAMEPAD_Y = 0x8000;

    // ========== Standard XInputGetState (Guide button masked out) ==========
    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState1_4(int dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState1_3(int dwUserIndex, out XINPUT_STATE pState);

    [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState9_1_0(int dwUserIndex, out XINPUT_STATE pState);

    // ========== XInputGetStateEx (ordinal 100) - includes Guide button ==========
    // This is an undocumented API exported by ordinal, not by name.
    // We need to load it dynamically using GetProcAddress.

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr ordinal);

    private delegate int XInputGetStateExDelegate(int dwUserIndex, out XINPUT_STATE pState);
    private static XInputGetStateExDelegate? _xInputGetStateEx;
    private static bool _initialized;
    private static bool _guideButtonSupported;

    /// <summary>
    /// Whether the Guide button is supported (XInputGetStateEx ordinal 100 was found).
    /// </summary>
    public static bool GuideButtonSupported
    {
        get
        {
            EnsureInitialized();
            return _guideButtonSupported;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        // Try to load xinput1_4.dll first (Windows 8+), then fall back to xinput1_3.dll
        IntPtr xinputHandle = LoadLibrary("xinput1_4.dll");
        if (xinputHandle == IntPtr.Zero)
        {
            xinputHandle = LoadLibrary("xinput1_3.dll");
        }

        if (xinputHandle == IntPtr.Zero)
        {
            _guideButtonSupported = false;
            return;
        }

        // Load XInputGetStateEx by ordinal 100
        // This undocumented function is the same as XInputGetState but doesn't mask out the Guide button
        IntPtr procAddress = GetProcAddress(xinputHandle, (IntPtr)100);
        if (procAddress != IntPtr.Zero)
        {
            _xInputGetStateEx = Marshal.GetDelegateForFunctionPointer<XInputGetStateExDelegate>(procAddress);
            _guideButtonSupported = true;
        }
        else
        {
            _guideButtonSupported = false;
        }
    }

    /// <summary>
    /// Gets controller state using the standard XInputGetState API.
    /// Note: Guide button is masked out in this API.
    /// </summary>
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

    /// <summary>
    /// Gets controller state using XInputGetStateEx (ordinal 100).
    /// This includes the Guide button in wButtons (bit 0x0400).
    /// Falls back to standard XInputGetState if ordinal 100 is not available.
    /// </summary>
    public static bool TryGetStateEx(int userIndex, out XINPUT_STATE state)
    {
        EnsureInitialized();

        if (_xInputGetStateEx != null)
        {
            try
            {
                if (_xInputGetStateEx(userIndex, out state) == ERROR_SUCCESS)
                    return true;
            }
            catch
            {
                // Fall through to standard API
            }
        }

        // Fall back to standard API (Guide button won't be available)
        return TryGetState(userIndex, out state);
    }

    // ========== Keystroke APIs ==========
    // Note: XInputGetKeystroke does NOT report Guide button events.
    // Use TryGetStateEx and check XINPUT_GAMEPAD_GUIDE bit instead.

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

    // VK_PAD constants for XInputGetKeystroke (NOT for Guide button!)
    public const ushort VK_PAD_A = 0x5800;
    public const ushort VK_PAD_B = 0x5801;
    public const ushort VK_PAD_X = 0x5802;
    public const ushort VK_PAD_Y = 0x5803;
    public const ushort VK_PAD_RSHOULDER = 0x5804;
    public const ushort VK_PAD_LSHOULDER = 0x5805;
    public const ushort VK_PAD_LTRIGGER = 0x5806;
    public const ushort VK_PAD_RTRIGGER = 0x5807;
    public const ushort VK_PAD_DPAD_UP = 0x5810;
    public const ushort VK_PAD_DPAD_DOWN = 0x5811;
    public const ushort VK_PAD_DPAD_LEFT = 0x5812;
    public const ushort VK_PAD_DPAD_RIGHT = 0x5813;
    public const ushort VK_PAD_START = 0x5814;
    public const ushort VK_PAD_BACK = 0x5815;
    public const ushort VK_PAD_LTHUMB_PRESS = 0x5816;
    public const ushort VK_PAD_RTHUMB_PRESS = 0x5817;

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
