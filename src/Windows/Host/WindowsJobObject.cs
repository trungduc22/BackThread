using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BackThread.Host;

/// <summary>
/// Windows Job Object configured with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE so any spawned helper process
/// terminates cleanly with BackThread instead of leaking zombie processes on weak PCs.
/// Safe no-op on non-Windows platforms.
/// </summary>
public static class WindowsJobObject
{
    private static readonly Lazy<nint> JobHandle = new(CreateJob);

    public static void Track(Process? process)
    {
        if (process is null || !OperatingSystem.IsWindows()) return;
        try
        {
            nint job = JobHandle.Value;
            if (job != 0) AssignProcessToJobObject(job, process.Handle);
        }
        catch
        {
            // Process may already have exited or be restricted by nested job policy on older Windows.
        }
    }

    private static nint CreateJob()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        nint job = CreateJobObjectW(0, null);
        if (job == 0) return 0;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0x2000 // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        nint ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, fDeleteOld: false);
            return SetInformationJobObject(job, 9 /* JobObjectExtendedLimitInformation */, ptr, (uint)size) ? job : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateJobObjectW(nint attributes, string? name);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint job, int infoClass, nint info, uint length);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint job, nint process);
}
