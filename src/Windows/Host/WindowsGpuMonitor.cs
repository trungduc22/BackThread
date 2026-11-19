using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BackThread.Host;

public sealed record GpuAdapterSnapshot(
    string Name,
    string? Vendor,
    bool Integrated,
    double? UtilizationPercent,
    double? TemperatureCelsius,
    double? VramUsedGb,
    double? VramTotalGb,
    double? SharedUsedGb,
    double? SharedTotalGb,
    double? MemoryClockMhz,
    double? PowerPercent,
    uint? FanRpm,
    string Source);

public interface IGpuMonitor : IDisposable
{
    IReadOnlyList<GpuAdapterSnapshot> Sample();
}

/// <summary>
/// Multi-vendor native Windows GPU monitor (Intel, AMD, NVIDIA) using D3DKMT + PDH English counters
/// with zero external vendor tools. Demand-driven: only samples when requested by active dashboard/viewport.
/// </summary>
public sealed partial class WindowsGpuMonitor : IGpuMonitor
{
    private const double GiB = 1024.0 * 1024 * 1024;
    private const uint PdhMoreData = 0x800007D2;
    private const uint PdhFmtDouble = 0x200;
    private const uint PdhFmtNoCap100 = 0x8000;

    private sealed record AdapterEntry(
        string Luid,
        uint Handle,
        string Name,
        string? Vendor,
        bool Integrated,
        double? DedicatedGb,
        double? SharedGb,
        bool HasFan);

    private readonly Lock gate = new();
    private List<AdapterEntry> adapters = [];
    private long enumeratedAt = -1;
    private nint query, engineCounter, dedicatedCounter, sharedCounter;
    private bool initialized, primed;

    public IReadOnlyList<GpuAdapterSnapshot> Sample()
    {
        if (!OperatingSystem.IsWindows())
            return [];
        return SampleWindows();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private IReadOnlyList<GpuAdapterSnapshot> SampleWindows()
    {
        lock (gate)
        {
            if (!initialized)
            {
                initialized = true;
                if (PdhOpenQueryW(null, 0, out query) == 0)
                {
                    if (PdhAddEnglishCounterW(query, @"\GPU Engine(*)\Utilization Percentage", 0, out engineCounter) != 0) engineCounter = 0;
                    if (PdhAddEnglishCounterW(query, @"\GPU Adapter Memory(*)\Dedicated Usage", 0, out dedicatedCounter) != 0) dedicatedCounter = 0;
                    if (PdhAddEnglishCounterW(query, @"\GPU Adapter Memory(*)\Shared Usage", 0, out sharedCounter) != 0) sharedCounter = 0;
                }
            }

            if (enumeratedAt < 0 || Environment.TickCount64 - enumeratedAt > 300_000)
                EnumerateAdapters();

            Dictionary<string, double> util = [], dedicated = [], shared = [];
            if (query != 0 && PdhCollectQueryData(query) == 0 && primed)
            {
                var engines = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (var (inst, value) in ReadCounterArray(engineCounter))
                {
                    if (InstanceRegex().Match(inst) is { Success: true } m)
                    {
                        string key = FormatLuid(m) + "|" + m.Groups[3].Value + "|" + m.Groups[4].Value;
                        engines[key] = engines.GetValueOrDefault(key) + value;
                    }
                }
                foreach (var (key, value) in engines)
                {
                    string luid = key[..key.IndexOf('|')];
                    util[luid] = Math.Max(util.GetValueOrDefault(luid), Math.Min(100.0, value));
                }
                foreach (var (inst, value) in ReadCounterArray(dedicatedCounter))
                    if (InstanceRegex().Match(inst) is { Success: true } m)
                        dedicated[FormatLuid(m)] = dedicated.GetValueOrDefault(FormatLuid(m)) + value;

                foreach (var (inst, value) in ReadCounterArray(sharedCounter))
                    if (InstanceRegex().Match(inst) is { Success: true } m)
                        shared[FormatLuid(m)] = shared.GetValueOrDefault(FormatLuid(m)) + value;
            }
            primed = true;

            var list = new List<GpuAdapterSnapshot>(adapters.Count);
            foreach (var a in adapters)
            {
                var perf = QueryPerfData(a.Handle);
                list.Add(new GpuAdapterSnapshot(
                    Name: a.Name,
                    Vendor: a.Vendor,
                    Integrated: a.Integrated,
                    UtilizationPercent: util.TryGetValue(a.Luid, out var u) ? Math.Round(u, 1) : null,
                    TemperatureCelsius: perf?.Temp,
                    VramUsedGb: dedicated.TryGetValue(a.Luid, out var d) ? Math.Round(d / GiB, 2) : null,
                    VramTotalGb: a.DedicatedGb,
                    SharedUsedGb: shared.TryGetValue(a.Luid, out var s) ? Math.Round(s / GiB, 2) : null,
                    SharedTotalGb: a.SharedGb,
                    MemoryClockMhz: perf?.MemClockMhz,
                    PowerPercent: perf?.PowerPct,
                    FanRpm: a.HasFan ? perf?.FanRpm : null,
                    Source: "d3dkmt+pdh"));
            }
            return list;
        }
    }

    private void EnumerateAdapters()
    {
        enumeratedAt = Environment.TickCount64;
        CloseAdapters();

        var buffer = new AdapterInfo[32];
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        uint count;
        try
        {
            var e = new EnumAdapters2 { NumAdapters = (uint)buffer.Length, pAdapters = pin.AddrOfPinnedObject() };
            if (D3DKMTEnumAdapters2(ref e) != 0) return;
            count = Math.Min(e.NumAdapters, (uint)buffer.Length);
        }
        finally { pin.Free(); }

        for (int i = 0; i < count; i++)
        {
            var a = buffer[i];
            byte[] typeBuf = new byte[4];
            QueryAdapter(a.hAdapter, 15 /* ADAPTERTYPE */, typeBuf);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(typeBuf);

            byte[] idsBuf = new byte[28];
            QueryAdapter(a.hAdapter, 31 /* PHYSICALADAPTERDEVICEIDS */, idsBuf);
            uint vendorId = BinaryPrimitives.ReadUInt32LittleEndian(idsBuf.AsSpan(4));

            bool software = (type & (1u << 2)) != 0;
            bool indirect = (type & (1u << 6)) != 0;
            bool renders = (type & 1u) != 0;
            if (software || indirect || !renders || vendorId == 0x1414)
            {
                CloseAdapter(a.hAdapter);
                continue;
            }

            string name = "GPU";
            byte[] reg = new byte[4 * 260 * 2];
            if (QueryAdapter(a.hAdapter, 8 /* ADAPTERREGISTRYINFO */, reg) == 0)
            {
                var chars = MemoryMarshal.Cast<byte, char>(reg.AsSpan(0, 260 * 2));
                int end = chars.IndexOf('\0');
                string parsed = new string(end < 0 ? chars : chars[..end]).Trim();
                if (parsed.Length > 0) name = parsed;
            }

            byte[] seg = new byte[24];
            bool segOk = QueryAdapter(a.hAdapter, 3 /* GETSEGMENTSIZE */, seg) == 0;
            ulong dedicatedBytes = BinaryPrimitives.ReadUInt64LittleEndian(seg);
            ulong sharedBytes = BinaryPrimitives.ReadUInt64LittleEndian(seg.AsSpan(16));

            byte[] caps = new byte[40];
            bool capsOk = QueryAdapter(a.hAdapter, 63 /* ADAPTERPERFDATA_CAPS */, caps) == 0;
            uint maxFan = BinaryPrimitives.ReadUInt32LittleEndian(caps.AsSpan(24));
            bool hybridIntegrated = (type & (1u << 5)) != 0;

            adapters.Add(new AdapterEntry(
                Luid: $"{(uint)a.LuidHigh:x8}_{a.LuidLow:x8}",
                Handle: a.hAdapter,
                Name: name,
                Vendor: ResolveVendor(vendorId, name),
                Integrated: hybridIntegrated || LooksIntegrated(name),
                DedicatedGb: segOk && dedicatedBytes > 0 ? Math.Round(dedicatedBytes / GiB, 2) : null,
                SharedGb: segOk && sharedBytes > 0 ? Math.Round(sharedBytes / GiB, 2) : null,
                HasFan: capsOk && maxFan > 0));
        }
    }

    private sealed record PerfReading(double? Temp, double? MemClockMhz, double? PowerPct, uint? FanRpm);

    private static PerfReading? QueryPerfData(uint handle)
    {
        byte[] buf = new byte[64];
        if (QueryAdapter(handle, 62 /* ADAPTERPERFDATA */, buf) != 0) return null;
        ulong memHz = BinaryPrimitives.ReadUInt64LittleEndian(buf.AsSpan(8));
        uint fan = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(48));
        uint power = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(52));
        uint temp = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(56));
        return new PerfReading(
            Temp: temp is > 0 and < 2000 ? Math.Round(temp / 10.0, 1) : null,
            MemClockMhz: memHz > 0 ? Math.Round(memHz / 1e6) : null,
            PowerPct: power is > 0 and <= 2000 ? Math.Round(power / 10.0, 1) : null,
            FanRpm: fan);
    }

    private static string? ResolveVendor(uint vendorId, string name) => vendorId switch
    {
        0x10DE => "NVIDIA",
        0x1002 or 0x1022 => "AMD",
        0x8086 => "Intel",
        0x5143 => "Qualcomm",
        _ => name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA"
            : name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ? "AMD"
            : name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : null
    };

    private static bool LooksIntegrated(string name)
        => name.Contains("UHD", StringComparison.OrdinalIgnoreCase)
            || name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Iris", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Vega", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase);

    private static string FormatLuid(Match m) => m.Groups[1].Value.ToLowerInvariant() + "_" + m.Groups[2].Value.ToLowerInvariant();

    private static List<(string Instance, double Value)> ReadCounterArray(nint counter)
    {
        var list = new List<(string, double)>();
        if (counter == 0) return list;
        uint size = 0, count = 0;
        if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, ref count, 0) != PdhMoreData || size == 0)
            return list;
        nint buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PdhFmtDouble | PdhFmtNoCap100, ref size, ref count, buf) != 0)
                return list;
            int stride = Marshal.SizeOf<FmtItem>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<FmtItem>(buf + i * stride);
                if (item.CStatus is not (0 or 1) || item.Name == 0) continue;
                list.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
        return list;
    }

    private static uint QueryAdapter(uint handle, int type, byte[] data)
    {
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var q = new QueryAdapterInfo { hAdapter = handle, Type = type, pPrivateDriverData = pin.AddrOfPinnedObject(), PrivateDriverDataSize = (uint)data.Length };
            return D3DKMTQueryAdapterInfo(ref q);
        }
        finally { pin.Free(); }
    }

    private static void CloseAdapter(uint handle)
    {
        uint h = handle;
        D3DKMTCloseAdapter(ref h);
    }

    private void CloseAdapters()
    {
        foreach (var a in adapters) CloseAdapter(a.Handle);
        adapters = [];
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (OperatingSystem.IsWindows())
            {
                CloseAdapters();
                if (query != 0) PdhCloseQuery(query);
            }
            query = 0;
        }
    }

    [GeneratedRegex(@"luid_0x([0-9a-f]{8})_0x([0-9a-f]{8})_phys_(\d+)(?:_eng_(\d+))?", RegexOptions.IgnoreCase)]
    private static partial Regex InstanceRegex();

    [StructLayout(LayoutKind.Sequential)] private struct AdapterInfo { public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint NumOfSources; public int PrecisePresentRegionsPreferred; }
    [StructLayout(LayoutKind.Sequential)] private struct EnumAdapters2 { public uint NumAdapters; public nint pAdapters; }
    [StructLayout(LayoutKind.Sequential)] private struct QueryAdapterInfo { public uint hAdapter; public int Type; public nint pPrivateDriverData; public uint PrivateDriverDataSize; }
    [StructLayout(LayoutKind.Sequential)] private struct FmtItem { public nint Name; public uint CStatus; public double Value; }

    [DllImport("gdi32.dll")] private static extern uint D3DKMTEnumAdapters2(ref EnumAdapters2 e);
    [DllImport("gdi32.dll")] private static extern uint D3DKMTQueryAdapterInfo(ref QueryAdapterInfo q);
    [DllImport("gdi32.dll")] private static extern uint D3DKMTCloseAdapter(ref uint handle);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQueryW(string? source, nint userData, out nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounterW(nint query, string path, nint userData, out nint counter);
    [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(nint query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint bufferSize, ref uint itemCount, nint buffer);
    [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(nint query);
}
