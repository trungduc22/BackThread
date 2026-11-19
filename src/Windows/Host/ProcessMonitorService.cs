using System.Diagnostics;

namespace BackThread.Host;

public sealed record ProcessSummary(int Pid, string Name, double MemoryMb, double CpuSeconds, int ThreadCount);

/// <summary>
/// Demand-driven top-process snapshotter. Never runs background polling threads;
/// only snapshots processes when explicitly requested by the iPhone or PC App, with a 3-second cache.
/// </summary>
public sealed class ProcessMonitorService
{
    private readonly Lock gate = new();
    private long cachedAt;
    private IReadOnlyList<ProcessSummary> cached = [];

    public IReadOnlyList<ProcessSummary> SampleTop(int limit = 10)
    {
        limit = Math.Clamp(limit, 1, 32);
        lock (gate)
        {
            if (cached.Count > 0 && Environment.TickCount64 - cachedAt < 3000)
                return cached.Take(limit).ToList();

            var list = new List<ProcessSummary>(64);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    double memMb = Math.Round(p.WorkingSet64 / (1024.0 * 1024.0), 1);
                    double cpuSec = 0;
                    int threads = 0;
                    try { cpuSec = Math.Round(p.TotalProcessorTime.TotalSeconds, 1); } catch { }
                    try { threads = p.Threads.Count; } catch { }
                    list.Add(new ProcessSummary(p.Id, p.ProcessName, memMb, cpuSec, threads));
                }
                catch
                {
                    // System/protected process exited or denied access.
                }
                finally
                {
                    p.Dispose();
                }
            }

            cached = list
                .OrderByDescending(x => x.MemoryMb)
                .ThenByDescending(x => x.CpuSeconds)
                .Take(32)
                .ToList();
            cachedAt = Environment.TickCount64;
            return cached.Take(limit).ToList();
        }
    }
}
