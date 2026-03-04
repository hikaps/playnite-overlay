using System;
using System.Runtime.InteropServices;

namespace PlayniteOverlay;

/// <summary>
/// SDL2 GameController API bindings for unified controller support.
/// Supports Xbox, PlayStation, Nintendo, and other controllers through a single API.
/// </summary>
internal static class SDL2
{
    private const string SDL2_DLL = "SDL2.dll";
    private static bool isInitialized = false;
    private static bool initFailed = false;

    #region SDL_Init Flags
    public const uint SDL_INIT_JOYSTICK = 0x00000200;
    public const uint SDL_INIT_GAMECONTROLLER = 0x00002000;
    public const uint SDL_INIT_EVENTS = 0x00004000;
    #endregion

    #region SDL_bool
    public const int SDL_FALSE = 0;
    public const int SDL_TRUE = 1;
    #endregion

    #region SDL_GameControllerButton
    public const int SDL_CONTROLLER_BUTTON_INVALID = -1;
    public const int SDL_CONTROLLER_BUTTON_A = 0;
    public const int SDL_CONTROLLER_BUTTON_B = 1;
    public const int SDL_CONTROLLER_BUTTON_X = 2;
    public const int SDL_CONTROLLER_BUTTON_Y = 3;
    public const int SDL_CONTROLLER_BUTTON_BACK = 4;
    public const int SDL_CONTROLLER_BUTTON_GUIDE = 5;
    public const int SDL_CONTROLLER_BUTTON_START = 6;
    public const int SDL_CONTROLLER_BUTTON_LEFTSTICK = 7;
    public const int SDL_CONTROLLER_BUTTON_RIGHTSTICK = 8;
    public const int SDL_CONTROLLER_BUTTON_LEFTSHOULDER = 9;
    public const int SDL_CONTROLLER_BUTTON_RIGHTSHOULDER = 10;
    public const int SDL_CONTROLLER_BUTTON_DPAD_UP = 11;
    public const int SDL_CONTROLLER_BUTTON_DPAD_DOWN = 12;
    public const int SDL_CONTROLLER_BUTTON_DPAD_LEFT = 13;
    public const int SDL_CONTROLLER_BUTTON_DPAD_RIGHT = 14;
    #endregion

    #region SDL_GameControllerAxis
    public const int SDL_CONTROLLER_AXIS_INVALID = -1;
    public const int SDL_CONTROLLER_AXIS_LEFTX = 0;
    public const int SDL_CONTROLLER_AXIS_LEFTY = 1;
    public const int SDL_CONTROLLER_AXIS_RIGHTX = 2;
    public const int SDL_CONTROLLER_AXIS_RIGHTY = 3;
    public const int SDL_CONTROLLER_AXIS_TRIGGERLEFT = 4;
    public const int SDL_CONTROLLER_AXIS_TRIGGERRIGHT = 5;
    #endregion

    #region SDL_Event types
    public const uint SDL_CONTROLLERDEVICEADDED = 0x653;
    public const uint SDL_CONTROLLERDEVICEREMOVED = 0x654;
    #endregion

    #region SDL_Event structure
    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct SDL_Event
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(0)] public SDL_ControllerDeviceEvent cdevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SDL_ControllerDeviceEvent
    {
        public uint type;
        public uint timestamp;
        public int which;  // joystick index for ADDED, instance ID for REMOVED/REMAPPED
    }
    #endregion

    /// <summary>
    /// Initializes SDL2 GameController subsystem. Must be called before any other SDL2 functions.
    /// </summary>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    public static bool Init()
    {
        if (isInitialized) return true;
        if (initFailed) return false;

        try
        {
            // Initialize joystick and gamecontroller subsystems
            var result = SDL_InitSubSystem(SDL_INIT_JOYSTICK | SDL_INIT_GAMECONTROLLER | SDL_INIT_EVENTS);
            if (result < 0)
            {
                initFailed = true;
                return false;
            }

            // Load controller mappings from the file next to the DLL
            var assemblyLocation = typeof(SDL2).Assembly.Location;
            var assemblyDir = System.IO.Path.GetDirectoryName(assemblyLocation) ?? "";
            var mappingsPath = System.IO.Path.Combine(assemblyDir, "gamecontrollerdb.txt");
            
            if (System.IO.File.Exists(mappingsPath))
            {
                SDL_GameControllerAddMappingsFromFile(mappingsPath);
            }

            isInitialized = true;
            return true;
        }
        catch (Exception)
        {
            initFailed = true;
            return false;
        }
    }

    /// <summary>
    /// Shuts down SDL2. Call when the application exits.
    /// </summary>
    public static void Quit()
    {
        if (!isInitialized) return;
        SDL_QuitSubSystem(SDL_INIT_JOYSTICK | SDL_INIT_GAMECONTROLLER | SDL_INIT_EVENTS);
        isInitialized = false;
    }

    /// <summary>
    /// Gets the number of connected joysticks/controllers.
    /// </summary>
    public static int NumJoysticks()
    {
        return SDL_NumJoysticks();
    }

    /// <summary>
    /// Checks if a joystick index is a game controller (has a mapping).
    /// </summary>
    public static bool IsGameController(int joystickIndex)
    {
        return SDL_IsGameController(joystickIndex) == SDL_TRUE;
    }

    /// <summary>
    /// Opens a game controller by joystick index.
    /// </summary>
    public static IntPtr GameControllerOpen(int joystickIndex)
    {
        return SDL_GameControllerOpen(joystickIndex);
    }

    /// <summary>
    /// Closes a game controller.
    /// </summary>
    public static void GameControllerClose(IntPtr controller)
    {
        if (controller != IntPtr.Zero)
        {
            SDL_GameControllerClose(controller);
        }
    }

    /// <summary>
    /// Gets the instance ID of a controller's joystick.
    /// </summary>
    public static int GameControllerGetJoystickInstanceID(IntPtr controller)
    {
        var joystick = SDL_GameControllerGetJoystick(controller);
        return joystick != IntPtr.Zero ? SDL_JoystickInstanceID(joystick) : -1;
    }

    /// <summary>
    /// Gets the name of a game controller.
    /// </summary>
    public static string? GameControllerName(IntPtr controller)
    {
        var ptr = SDL_GameControllerName(controller);
        return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
    }

    /// <summary>
    /// Gets the current state of a controller button.
    /// </summary>
    /// <returns>1 if pressed, 0 if released, negative on error.</returns>
    public static int GameControllerGetButton(IntPtr controller, int button)
    {
        return SDL_GameControllerGetButton(controller, button);
    }

    /// <summary>
    /// Gets the current state of a controller axis.
    /// </summary>
    /// <returns>Value from -32768 to 32767 (triggers are 0 to 32767).</returns>
    public static short GameControllerGetAxis(IntPtr controller, int axis)
    {
        return SDL_GameControllerGetAxis(controller, axis);
    }

    /// <summary>
    /// Updates the current state of all open controllers.
    /// Call this before checking button/axis states.
    /// </summary>
    public static void GameControllerUpdate()
    {
        SDL_GameControllerUpdate();
    }

    #region Native Imports

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_InitSubSystem(uint flags);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_QuitSubSystem(uint flags);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_NumJoysticks();

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_IsGameController(int joystickIndex);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerOpen(int joystickIndex);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GameControllerClose(IntPtr controller);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerGetJoystick(IntPtr controller);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_JoystickInstanceID(IntPtr joystick);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GameControllerName(IntPtr controller);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GameControllerGetButton(IntPtr controller, int button);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern short SDL_GameControllerGetAxis(IntPtr controller, int axis);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_GameControllerUpdate();

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_GameControllerAddMappingsFromFile([MarshalAs(UnmanagedType.LPStr)] string file);

    [DllImport(SDL2_DLL, CallingConvention = CallingConvention.Cdecl)]
    private static extern int SDL_PollEvent(out SDL_Event _event);

    /// <summary>
    /// Polls for pending events. Returns true if an event was available.
    /// </summary>
    public static bool PollEvent(out SDL_Event _event)
    {
        return SDL_PollEvent(out _event) == 1;
    }

    #endregion
}
