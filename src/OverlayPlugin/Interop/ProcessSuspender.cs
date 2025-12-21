using System;
using System.Runtime.InteropServices;
using Playnite.SDK;

namespace PlayniteOverlay.Interop;

/// <summary>
/// Provides process suspension and resumption using Windows NT APIs.
/// Used to pause game processes while the overlay is active, preventing
/// them from receiving controller input.
/// </summary>
internal static class ProcessSuspender
{
    private static readonly ILogger logger = LogManager.GetLogger();

    // NT status code for success
    private const int STATUS_SUCCESS = 0;

    // Process access rights
    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = false)]
    private static extern int NtResumeProcess(IntPtr processHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// Suspends all threads of a process.
    /// </summary>
    /// <param name="processId">The process ID to suspend</param>
    /// <returns>True if suspension succeeded, false otherwise</returns>
    public static bool SuspendProcess(int processId)
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
                logger.Debug($"Failed to open process {processId} for suspension (access denied or process exited)");
                return false;
            }

            int status = NtSuspendProcess(processHandle);
            if (status == STATUS_SUCCESS)
            {
                logger.Info($"Suspended process {processId}");
                return true;
            }
            else
            {
                logger.Debug($"NtSuspendProcess failed for process {processId} with status 0x{status:X8}");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Exception while suspending process {processId}");
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
    /// Resumes all threads of a suspended process.
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
                logger.Debug($"Failed to open process {processId} for resumption (access denied or process exited)");
                return false;
            }

            int status = NtResumeProcess(processHandle);
            if (status == STATUS_SUCCESS)
            {
                logger.Info($"Resumed process {processId}");
                return true;
            }
            else
            {
                logger.Debug($"NtResumeProcess failed for process {processId} with status 0x{status:X8}");
                return false;
            }
        }
        catch (Exception ex)
        {
            logger.Debug(ex, $"Exception while resuming process {processId}");
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
}
