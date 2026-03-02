using System;
using System.Runtime.InteropServices;
using Playnite.SDK;

namespace PlayniteOverlay;

/// <summary>
/// Provides process suspension capabilities using Windows NT API.
/// This is used to freeze game processes while the overlay is open,
/// preventing them from stealing focus.
/// 
/// WARNING: Process suspension may be blocked by anti-cheat systems.
/// Use with caution and only as an opt-in feature.
/// </summary>
internal static class ProcessSuspender
{
    private static readonly ILogger logger = LogManager.GetLogger();

    /// <summary>
    /// Suspends the specified process using NtSuspendProcess.
    /// Note: This may be blocked by anti-cheat systems like Easy Anti-Cheat.
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
            processHandle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                logger.Warn($"[ProcessSuspender] OpenProcess failed for PID {processId}, Win32 error: {error} (0x{error:X8})");
                return false;
            }

            var status = NtSuspendProcess(processHandle);
            if (status != 0)
            {
                // Common error: 0xC0000022 = STATUS_ACCESS_DENIED (anti-cheat blocking)
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
    /// Resumes the specified process using NtResumeProcess.
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
            processHandle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                logger.Warn($"[ProcessSuspender] OpenProcess failed for resume PID {processId}, Win32 error: {error}");
                return false;
            }

            var status = NtResumeProcess(processHandle);
            if (status == 0)
            {
                logger.Info($"[ProcessSuspender] Successfully resumed process {processId}");
            }
            return status == 0;
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
    /// </summary>
    public static bool SafeResumeProcess(int processId)
    {
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
