using System;
using System.Collections.Generic;
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
    private static readonly object suspendedLock = new object();
    private static readonly HashSet<int> suspendedProcessIds = new HashSet<int>();

    /// <summary>
    /// Gets a snapshot of currently suspended process IDs.
    /// Used for crash recovery.
    /// </summary>
    public static IReadOnlyCollection<int> SuspendedProcessIds
    {
        get
        {
            lock (suspendedLock)
            {
                return new List<int>(suspendedProcessIds).AsReadOnly();
            }
        }
    }

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
            
            // Track for crash recovery
            lock (suspendedLock)
            {
                suspendedProcessIds.Add(processId);
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
                // Remove from tracking
                lock (suspendedLock)
                {
                    suspendedProcessIds.Remove(processId);
                }
                logger.Info($"[ProcessSuspender] Successfully resumed process {processId}");
            }
            return status == 0;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist - remove from tracking
            lock (suspendedLock)
            {
                suspendedProcessIds.Remove(processId);
            }
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
    /// Safely resumes a process with retry logic.
    /// If the first resume attempt fails, it will retry up to 3 times
    /// with a small delay between attempts.
    /// </summary>
    /// <param name="processId">The process ID to resume</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
    /// <returns>True if resumption succeeded, false otherwise</returns>
    public static bool SafeResumeProcess(int processId, int maxRetries = 3)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (ResumeProcess(processId))
            {
                return true;
            }
            
            if (attempt < maxRetries)
            {
                logger.Warn($"[ProcessSuspender] Resume attempt {attempt} failed for PID {processId}, retrying...");
                System.Threading.Thread.Sleep(100 * attempt); // Increasing delay
            }
        }
        
        logger.Error($"[ProcessSuspender] All {maxRetries} resume attempts failed for PID {processId}");
        return false;
    }

    /// <summary>
    /// Resumes all tracked suspended processes. Used for crash recovery.
    /// Should be called during plugin disposal to ensure no processes
    /// are left frozen if Playnite crashes or closes unexpectedly.
    /// </summary>
    /// <returns>Number of successfully resumed processes</returns>
    public static int ResumeAllSuspendedProcesses()
    {
        List<int> toResume;
        lock (suspendedLock)
        {
            toResume = new List<int>(suspendedProcessIds);
        }

        int resumed = 0;
        foreach (var pid in toResume)
        {
            logger.Info($"[ProcessSuspender] Crash recovery: resuming process {pid}");
            if (SafeResumeProcess(pid))
            {
                resumed++;
            }
        }

        if (toResume.Count > 0)
        {
            logger.Info($"[ProcessSuspender] Crash recovery complete: resumed {resumed}/{toResume.Count} processes");
        }

        return resumed;
    }

    /// <summary>
    /// Clears tracking for a process ID without actually resuming it.
    /// Use only when the process is known to have exited.
    /// </summary>
    public static void ClearTracking(int processId)
    {
        lock (suspendedLock)
        {
            suspendedProcessIds.Remove(processId);
        }
    }

    #region P/Invoke

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtResumeProcess(IntPtr processHandle);

    #endregion
}
