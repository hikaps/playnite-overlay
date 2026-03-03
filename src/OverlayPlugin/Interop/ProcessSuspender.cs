using System;
using System.Diagnostics;
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

        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            var status = NtSuspendProcess(process.Handle);
            if (status != 0)
            {
                logger.Warn($"[ProcessSuspender] NtSuspendProcess failed for PID {processId}, NTSTATUS: 0x{status:X8}");
                return false;
            }
            
            logger.Info($"[ProcessSuspender] Successfully suspended process {processId}");
            return true;
        }
        catch (ArgumentException)
        {
            logger.Warn($"[ProcessSuspender] Process {processId} not found or already exited");
            return false;
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[ProcessSuspender] Exception in SuspendProcess for PID {processId}");
            return false;
        }
        finally
        {
            process?.Dispose();
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

        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            var status = NtResumeProcess(process.Handle);
            if (status == 0)
            {
                logger.Info($"[ProcessSuspender] Successfully resumed process {processId}");
            }
            return status == 0;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            logger.Warn($"[ProcessSuspender] Process {processId} not found or already exited");
            return false;
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[ProcessSuspender] Exception in ResumeProcess for PID {processId}");
            return false;
        }
        finally
        {
            process?.Dispose();
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

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtResumeProcess(IntPtr processHandle);

    #endregion
}
