using System.Runtime.InteropServices;
namespace BackThread.Host;
public sealed record PcMetrics(double? CpuPercent,ulong RamUsedBytes,ulong RamTotalBytes);
public sealed class PcMetricsCounter
{
    private ulong idle,total;private bool primed;
    public PcMetrics Sample()
    {
        var memory=new MemoryStatus{Length=(uint)Marshal.SizeOf<MemoryStatus>()};
        if(!GlobalMemoryStatusEx(ref memory))throw new IOException("Windows RAM sample unavailable");
        double? cpu=null;
        if(GetSystemTimes(out ulong nextIdle,out ulong kernel,out ulong user)){
            ulong nextTotal=kernel+user;
            if(primed&&nextTotal>total&&nextIdle>=idle)cpu=Math.Clamp(100d*(1d-(nextIdle-idle)/(double)(nextTotal-total)),0,100);
            idle=nextIdle;total=nextTotal;primed=true;
        }
        return new(cpu,memory.TotalPhysical-memory.AvailablePhysical,memory.TotalPhysical);
    }
    [StructLayout(LayoutKind.Sequential)]private struct MemoryStatus{public uint Length,Load;public ulong TotalPhysical,AvailablePhysical,TotalPageFile,AvailablePageFile,TotalVirtual,AvailableVirtual,AvailableExtendedVirtual;}
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GlobalMemoryStatusEx(ref MemoryStatus value);
    [DllImport("kernel32.dll",SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]private static extern bool GetSystemTimes(out ulong idle,out ulong kernel,out ulong user);
}
