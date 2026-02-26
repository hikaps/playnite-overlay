using System;
using System.Runtime.InteropServices;
using Playnite.SDK;

namespace PlayniteOverlay;

/// <summary>
/// Provides process suspension capabilities using Windows NT API.
/// This is used to freeze game processes while the overlay is open,
/// preventing them from stealing focus.
/// 
/// WARNING: Process suspension may trigger anti-cheat detection in some games.
/// Use with caution and only as an opt-in feature.
/// </summary>
internal static class ProcessSuspender
{
    private static readonly ILogger logger = LogManager.GetLogger();

    /// <summary>
    /// Suspends all threads in the specified process using NtSuspendProcess.
    /// </summary>
    /// <param name="processId">The process ID to suspend</param>
    /// <returns>True if suspension succeeded, false otherwise</returns>
    public static bool SuspendProcess(int processId)
    {
        if (processId <= 0)
        {
            logger.Debug($"[ProcessSuspender] Invalid process ID: {processId}");
            return false;
        }

        IntPtr processHandle = IntPtr.Zero;
        try
        {
            // Open process with PROCESS_SUSPEND_RESUME access right
            processHandle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                logger.Warn($"[ProcessSuspender] OpenProcess failed for PID {processId}, Win32 error: {error} (0x{error:X8})");
                return false;
            }

            logger.Debug($"[ProcessSuspender] Got process handle for PID {processId}, attempting suspend...");
            
            // Suspend the process
            var status = NtSuspendProcess(processHandle);
            if (status != 0)
            {
                logger.Warn($"[ProcessSuspender] NtSuspendProcess failed for PID {processId}, NTSTATUS: 0x{status:X8}");
                return false;
            }
            
            logger.Info($"[ProcessSuspender] Successfully suspended process {processId}");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[ProcessSuspender] Exception in SuspendProcess for PID {processId}");
            return false;
        }
        finally
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }

    /// <summary>
    /// Resumes all threads in the specified process using NtResumeProcess.
    /// </summary>
    /// <param name="processId">The process ID to resume</param>
    /// <returns>True if resumption succeeded, false otherwise</returns>
    public static bool ResumeProcess(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        IntPtr processHandle = IntPtr.Zero;
        try
        {
            // Open process with PROCESS_SUSPEND_RESUME access right
            processHandle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                logger.Warn($"[ProcessSuspender] OpenProcess failed for resume PID {processId}, Win32 error: {error}");
                return false;
            }

            // Resume the process
            var status = NtResumeProcess(processHandle);
            if (status == 0)
            {
                logger.Info($"[ProcessSuspender] Successfully resumed process {processId}");
            }
            return status == 0; // STATUS_SUCCESS
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[ProcessSuspender] Exception in ResumeProcess for PID {processId}");
            return false;
        }
        finally
        {
            if (processHandle != IntPtr.Zero)
            {
                CloseHandle(processHandle);
            }
        }
    }

    /// <summary>
    /// Safely resumes a process, handling the case where it might not be suspended.
    /// This is safe to call even if the process was never suspended.
    /// </summary>
    /// <param name="processId">The process ID to resume</param>
    /// <returns>True if resumption succeeded or process wasn't suspended</returns>
    public static bool SafeResumeProcess(int processId)
    {
        // NtResumeProcess is safe to call on non-suspended processes
        // It will simply return STATUS_SUCCESS without side effects
        return ResumeProcess(processId);
    }

    #region P/Invoke

    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    #endregion
}
