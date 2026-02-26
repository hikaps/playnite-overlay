using System;
using System.Collections.Generic;
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
    
    // Track suspended threads for proper resumption
    private static readonly Dictionary<int, List<int>> suspendedThreads = new Dictionary<int, List<int>>();

    /// <summary>
    /// Suspends all threads in the specified process.
    /// First tries process-level suspension, falls back to thread-level if that fails.
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

        // Try process-level suspension first (faster, cleaner)
        if (TrySuspendProcessLevel(processId))
        {
            return true;
        }

        // Fall back to thread-level suspension (may work when process-level is blocked)
        logger.Info($"[ProcessSuspender] Process-level suspension failed, trying thread-level for PID {processId}");
        return TrySuspendThreadLevel(processId);
    }

    /// <summary>
    /// Attempts process-level suspension using NtSuspendProcess.
    /// </summary>
    private static bool TrySuspendProcessLevel(int processId)
    {
        IntPtr processHandle = IntPtr.Zero;
        try
        {
            processHandle = OpenProcess(PROCESS_SUSPEND_RESUME, false, processId);
            if (processHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                logger.Debug($"[ProcessSuspender] OpenProcess (process-level) failed for PID {processId}, Win32 error: {error} (0x{error:X8})");
                return false;
            }

            var status = NtSuspendProcess(processHandle);
            if (status != 0)
            {
                logger.Debug($"[ProcessSuspender] NtSuspendProcess failed for PID {processId}, NTSTATUS: 0x{status:X8}");
                return false;
            }
            
            logger.Info($"[ProcessSuspender] Process-level suspension succeeded for PID {processId}");
            return true;
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"[ProcessSuspender] Exception in TrySuspendProcessLevel for PID {processId}");
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
    /// Attempts thread-level suspension by enumerating and suspending each thread.
    /// This may work when process-level suspension is blocked by anti-cheat.
    /// </summary>
    private static bool TrySuspendThreadLevel(int processId)
    {
        var threads = GetProcessThreads(processId);
        if (threads.Count == 0)
        {
            logger.Warn($"[ProcessSuspender] No threads found for PID {processId}");
            return false;
        }

        logger.Debug($"[ProcessSuspender] Found {threads.Count} threads for PID {processId}");

        var suspendedThreadIds = new List<int>();
        int successCount = 0;

        foreach (var threadId in threads)
        {
            if (SuspendThread(threadId))
            {
                suspendedThreadIds.Add(threadId);
                successCount++;
            }
        }

        if (successCount == 0)
        {
            logger.Warn($"[ProcessSuspender] Failed to suspend any threads for PID {processId}");
            return false;
        }

        // Track suspended threads for resumption
        lock (suspendedThreads)
        {
            suspendedThreads[processId] = suspendedThreadIds;
        }

        logger.Info($"[ProcessSuspender] Thread-level suspension succeeded for PID {processId} ({successCount}/{threads.Count} threads)");
        return true;
    }

    /// <summary>
    /// Gets all thread IDs for a process.
    /// </summary>
    private static List<int> GetProcessThreads(int processId)
    {
        var threads = new List<int>();
        IntPtr snapshot = IntPtr.Zero;

        try
        {
            snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
            if (snapshot == IntPtr.Zero || snapshot == (IntPtr)(-1))
            {
                logger.Warn($"[ProcessSuspender] CreateToolhelp32Snapshot failed");
                return threads;
            }

            var threadEntry = new THREADENTRY32();
            threadEntry.dwSize = (uint)Marshal.SizeOf(typeof(THREADENTRY32));

            if (!Thread32First(snapshot, ref threadEntry))
            {
                return threads;
            }

            do
            {
                if (threadEntry.th32OwnerProcessID == processId)
                {
                    threads.Add((int)threadEntry.th32ThreadID);
                }
            } while (Thread32Next(snapshot, ref threadEntry));
        }
        finally
        {
            if (snapshot != IntPtr.Zero && snapshot != (IntPtr)(-1))
            {
                CloseHandle(snapshot);
            }
        }

        return threads;
    }

    /// <summary>
    /// Suspends a single thread.
    /// </summary>
    private static bool SuspendThread(int threadId)
    {
        IntPtr threadHandle = IntPtr.Zero;
        try
        {
            threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, threadId);
            if (threadHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                // Don't log every thread failure - too noisy
                return false;
            }

            var status = NtSuspendThread(threadHandle, out _);
            return status == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (threadHandle != IntPtr.Zero)
            {
                CloseHandle(threadHandle);
            }
        }
    }

    /// <summary>
    /// Resumes a single thread.
    /// </summary>
    private static bool ResumeThread(int threadId)
    {
        IntPtr threadHandle = IntPtr.Zero;
        try
        {
            threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, threadId);
            if (threadHandle == IntPtr.Zero)
            {
                return false;
            }

            var status = NtResumeThread(threadHandle, out _);
            return status == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (threadHandle != IntPtr.Zero)
            {
                CloseHandle(threadHandle);
            }
        }
    }

    /// <summary>
    /// Resumes all threads in the specified process.
    /// </summary>
    /// <param name="processId">The process ID to resume</param>
    /// <returns>True if resumption succeeded, false otherwise</returns>
    public static bool ResumeProcess(int processId)
    {
        if (processId <= 0)
        {
            return false;
        }

        // Check if we have tracked suspended threads
        List<int>? threadsToResume = null;
        lock (suspendedThreads)
        {
            if (suspendedThreads.TryGetValue(processId, out threadsToResume))
            {
                suspendedThreads.Remove(processId);
            }
        }

        if (threadsToResume != null && threadsToResume.Count > 0)
        {
            // Resume tracked threads
            int successCount = 0;
            foreach (var threadId in threadsToResume)
            {
                if (ResumeThread(threadId))
                {
                    successCount++;
                }
            }
            
            logger.Info($"[ProcessSuspender] Thread-level resume for PID {processId} ({successCount}/{threadsToResume.Count} threads)");
            return successCount > 0;
        }

        // Try process-level resume
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
                logger.Info($"[ProcessSuspender] Process-level resume succeeded for PID {processId}");
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

    // Process access rights
    private const uint PROCESS_SUSPEND_RESUME = 0x0800;

    // Thread access rights
    private const uint THREAD_SUSPEND_RESUME = 0x0002;

    // Snapshot flags
    private const uint TH32CS_SNAPTHREAD = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct THREADENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ThreadID;
        public uint th32OwnerProcessID;
        public uint tpBasePri;
        public uint tpDeltaPri;
        public uint dwFlags;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtSuspendProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtResumeProcess(IntPtr processHandle);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtSuspendThread(IntPtr threadHandle, out uint previousSuspendCount);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern uint NtResumeThread(IntPtr threadHandle, out uint previousSuspendCount);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, int dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);

    #endregion
}
