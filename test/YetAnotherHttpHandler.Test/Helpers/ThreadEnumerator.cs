using System.Runtime.InteropServices;

namespace _YetAnotherHttpHandler.Test.Helpers;

public class ThreadEnumerator
{
    private const uint TH32CS_SNAPTHREAD = 0x00000004;

    [StructLayout(LayoutKind.Sequential)]
    private struct THREADENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ThreadID;
        public uint th32OwnerProcessID;
        public int tpBasePri;
        public int tpDeltaPri;
        public uint dwFlags;
    }

    public class ThreadInfo
    {
        public uint ThreadId { get; set; }
        public string ThreadName { get; set; }

        public ThreadInfo(uint threadId, string threadName)
        {
            ThreadId = threadId;
            ThreadName = threadName;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32First(nint hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool Thread32Next(nint hSnapshot, ref THREADENTRY32 lpte);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetThreadDescription(nint hThread, out nint ppszThreadDescription);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    private const uint THREAD_QUERY_INFORMATION = 0x0040;
    private const uint THREAD_QUERY_LIMITED_INFORMATION = 0x0800;

    /// <summary>
    /// Gets all threads with their names for the specified process ID
    /// </summary>
    /// <param name="processId">Process ID to enumerate threads for</param>
    /// <returns>List of ThreadInfo objects containing thread IDs and names</returns>
    public static IReadOnlyList<ThreadInfo> GetThreadsWithNames(int processId)
    {
        var threads = new List<ThreadInfo>();
        nint snapshotHandle = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);

        if (snapshotHandle == nint.Zero)
        {
            throw new Exception($"Failed to create snapshot. Error: {Marshal.GetLastWin32Error()}");
        }

        try
        {
            var threadEntry = new THREADENTRY32();
            threadEntry.dwSize = (uint)Marshal.SizeOf(typeof(THREADENTRY32));

            if (!Thread32First(snapshotHandle, ref threadEntry))
            {
                throw new Exception($"Failed to get first thread. Error: {Marshal.GetLastWin32Error()}");
            }

            do
            {
                if (threadEntry.th32OwnerProcessID == (uint)processId)
                {
                    string threadName = GetThreadName(threadEntry.th32ThreadID);
                    threads.Add(new ThreadInfo(threadEntry.th32ThreadID, threadName));
                }
            }
            while (Thread32Next(snapshotHandle, ref threadEntry));
        }
        finally
        {
            CloseHandle(snapshotHandle);
        }

        return threads;
    }

    /// <summary>
    /// Gets the name of a thread by its ID
    /// </summary>
    /// <param name="threadId">Thread ID</param>
    /// <returns>Thread name or empty string if no name is set</returns>
    private static string GetThreadName(uint threadId)
    {
        var threadName = string.Empty;
        var threadHandle = OpenThread(THREAD_QUERY_LIMITED_INFORMATION, false, threadId);

        if (threadHandle != nint.Zero)
        {
            try
            {
                nint threadDescriptionPtr;
                var result = GetThreadDescription(threadHandle, out threadDescriptionPtr);

                if (result >= 0 && threadDescriptionPtr != nint.Zero)
                {
                    try
                    {
                        threadName = Marshal.PtrToStringUni(threadDescriptionPtr);
                    }
                    finally
                    {
                        LocalFree(threadDescriptionPtr);
                    }
                }
            }
            finally
            {
                CloseHandle(threadHandle);
            }
        }

        return threadName ?? string.Empty;
    }
}