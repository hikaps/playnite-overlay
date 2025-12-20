using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Playnite.SDK;

namespace PlayniteOverlay;

/// <summary>
/// Provides methods to suspend and resume processes using Windows NT APIs.
/// This is useful for pausing games while the overlay is open to prevent input bleed.
/// </summary>
internal static class ProcessSuspend
{
    private static readonly ILogger logger = LogManager.GetLogger();

    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtResumeProcess(IntPtr processHandle);

    /// <summary>
    /// Suspends all threads in the specified process.
    /// </summary>
    /// <param name="processId">The process ID to suspend.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public static bool Suspend(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (handle == IntPtr.Zero)
            {
                logger.Debug($"Failed to open process {processId} for suspend");
                return false;
            }

            uint result = NtSuspendProcess(handle);
            if (result == 0)
            {
                logger.Debug($"Successfully suspended process {processId}");
                return true;
            }
            else
            {
                logger.Debug($"NtSuspendProcess returned {result} for process {processId}");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Exception suspending process {processId}");
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
    }

    /// <summary>
    /// Resumes all threads in the specified process.
    /// </summary>
    /// <param name="processId">The process ID to resume.</param>
    /// <returns>True if successful, false otherwise.</returns>
    public static bool Resume(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (handle == IntPtr.Zero)
            {
                logger.Debug($"Failed to open process {processId} for resume");
                return false;
            }

            uint result = NtResumeProcess(handle);
            if (result == 0)
            {
                logger.Debug($"Successfully resumed process {processId}");
                return true;
            }
            else
            {
                logger.Debug($"NtResumeProcess returned {result} for process {processId}");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Exception resuming process {processId}");
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                CloseHandle(handle);
            }
        }
    }

    /// <summary>
    /// Checks if a process is still running.
    /// </summary>
    /// <param name="processId">The process ID to check.</param>
    /// <returns>True if the process exists and is running.</returns>
    public static bool IsProcessRunning(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById(processId);
            bool isRunning = !process.HasExited;
            process.Dispose();
            return isRunning;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            return false;
        }
        catch
        {
            return false;
        }
    }
}
