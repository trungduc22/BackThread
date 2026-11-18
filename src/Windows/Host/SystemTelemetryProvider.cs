using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace BackThread.Host;

public sealed record SystemTelemetrySnapshot(
    long TimestampUnixMs,
    double? CpuUsagePercent,
    long? MemoryUsedMb,
    long? MemoryTotalMb,
    double? MemoryUsedPercent,
    double? BatteryPercent,
    bool? AcOnline,
    string BatteryStatus,
    bool? WifiConnected,
    string? WifiName,
    string BluetoothRadioState,
    int BluetoothConnectedCount,
    bool? BluetoothConnected,
    bool Available,
    string Platform);

public interface ISystemTelemetryProvider
{
    ValueTask<SystemTelemetrySnapshot> SampleAsync(CancellationToken ct);
}

/// <summary>
/// Samples genuine Windows host telemetry (CPU via GetSystemTimes delta, RAM via GlobalMemoryStatusEx,
/// Power via GetSystemPowerStatus, Wi-Fi via Wireless80211 interface, and real Bluetooth radio +
/// Aep.IsConnected device count via WinRT without guessing connected state from radio-on).
/// </summary>
public sealed class SystemTelemetryProvider : ISystemTelemetryProvider
{
    private readonly object cpuGate = new();
    private ulong prevIdle, prevKernel, prevUser;
    private bool hasPrevCpu;

    public async ValueTask<SystemTelemetrySnapshot> SampleAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return SampleLinuxOrUnavailable();

        return await SampleWindowsAsync(ct);
    }

    private static SystemTelemetrySnapshot SampleLinuxOrUnavailable()
    {
        return new SystemTelemetrySnapshot(
            TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CpuUsagePercent: null,
            MemoryUsedMb: null,
            MemoryTotalMb: null,
            MemoryUsedPercent: null,
            BatteryPercent: null,
            AcOnline: null,
            BatteryStatus: "Unavailable",
            WifiConnected: null,
            WifiName: null,
            BluetoothRadioState: "Unavailable",
            BluetoothConnectedCount: 0,
            BluetoothConnected: null,
            Available: false,
            Platform: Environment.OSVersion.Platform.ToString());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<SystemTelemetrySnapshot> SampleWindowsAsync(CancellationToken ct)
    {
        double? cpu = SampleWindowsCpuPercent();
        (long? usedMb, long? totalMb, double? memPct) = SampleWindowsMemory();
        (double? batPct, bool? acOnline, string batStatus) = SampleWindowsBattery();
        (bool? wifiUp, string? wifiName) = SampleWifiInterface();
        (string btRadioState, int btConnectedCount, bool? btConnected) = await SampleWindowsBluetoothAsync(ct);

        return new SystemTelemetrySnapshot(
            TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            CpuUsagePercent: cpu,
            MemoryUsedMb: usedMb,
            MemoryTotalMb: totalMb,
            MemoryUsedPercent: memPct,
            BatteryPercent: batPct,
            AcOnline: acOnline,
            BatteryStatus: batStatus,
            WifiConnected: wifiUp,
            WifiName: wifiName,
            BluetoothRadioState: btRadioState,
            BluetoothConnectedCount: btConnectedCount,
            BluetoothConnected: btConnected,
            Available: true,
            Platform: "Windows");
    }

    private double? SampleWindowsCpuPercent()
    {
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            return null;

        ulong idle = FileTimeToUInt64(idleFt);
        ulong kernel = FileTimeToUInt64(kernelFt);
        ulong user = FileTimeToUInt64(userFt);

        lock (cpuGate)
        {
            if (!hasPrevCpu)
            {
                prevIdle = idle;
                prevKernel = kernel;
                prevUser = user;
                hasPrevCpu = true;
                return null;
            }

            ulong deltaIdle = idle >= prevIdle ? idle - prevIdle : 0;
            ulong deltaKernel = kernel >= prevKernel ? kernel - prevKernel : 0;
            ulong deltaUser = user >= prevUser ? user - prevUser : 0;

            prevIdle = idle;
            prevKernel = kernel;
            prevUser = user;

            ulong total = deltaKernel + deltaUser;
            if (total == 0) return 0.0;
            ulong busy = total > deltaIdle ? total - deltaIdle : 0;
            return Math.Round(Math.Clamp(100.0 * busy / total, 0.0, 100.0), 1);
        }
    }

    private static (long? usedMb, long? totalMb, double? percent) SampleWindowsMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return (null, null, null);

        long totalMb = (long)(status.ullTotalPhys / (1024 * 1024));
        long availMb = (long)(status.ullAvailPhys / (1024 * 1024));
        long usedMb = Math.Max(0, totalMb - availMb);
        double pct = Math.Round(Math.Clamp(100.0 * usedMb / totalMb, 0.0, 100.0), 1);
        return (usedMb, totalMb, pct);
    }

    private static (double? pct, bool? acOnline, string status) SampleWindowsBattery()
    {
        if (!GetSystemPowerStatus(out var pwr))
            return (null, null, "Unknown");

        bool? ac = pwr.ACLineStatus switch { 0 => false, 1 => true, _ => null };
        if ((pwr.BatteryFlag & 128) != 0 || pwr.BatteryLifePercent == 255)
            return (null, ac, ac == true ? "AC" : "No Battery");

        double pct = Math.Clamp(pwr.BatteryLifePercent, (byte)0, (byte)100);
        string status = (pwr.BatteryFlag & 8) != 0
            ? "Charging"
            : ac == true
                ? "Plugged"
                : "Discharging";
        return (pct, ac, status);
    }

    private static (bool? connected, string? name) SampleWifiInterface()
    {
        try
        {
            var ifaces = NetworkInterface.GetAllNetworkInterfaces();
            var upWifi = ifaces.FirstOrDefault(n =>
                n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                && n.OperationalStatus == OperationalStatus.Up);
            if (upWifi != null)
                return (true, upWifi.Name);

            bool hasAnyWifi = ifaces.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
            return hasAnyWifi ? (false, null) : (null, null);
        }
        catch
        {
            return (null, null);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(string radioState, int connectedCount, bool? connected)> SampleWindowsBluetoothAsync(CancellationToken ct)
    {
        string radioState = "Unavailable";
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var btRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
            if (btRadio is null)
                return ("Unavailable", 0, false);

            radioState = btRadio.State switch
            {
                RadioState.On => "On",
                RadioState.Off => "Off",
                RadioState.Disabled => "Disabled",
                _ => "Unknown"
            };
        }
        catch
        {
            radioState = "Unknown";
        }

        if (radioState is "Off" or "Disabled")
            return (radioState, 0, false);

        try
        {
            // Strict Windows PnP Aep.IsConnected query across Bluetooth Classic & BLE protocols.
            // Radio enabled alone MUST NOT report BluetoothConnected = true.
            const string aqsConnectedBt =
                "System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True " +
                "AND (System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" " +
                "OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";
            var devices = await DeviceInformation.FindAllAsync(aqsConnectedBt);
            ct.ThrowIfCancellationRequested();
            int count = devices?.Count ?? 0;
            return (radioState, count, count > 0);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (radioState, 0, null);
        }
    }

    private static ulong FileTimeToUInt64(FILETIME ft) => ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public int dwLowDateTime;
        public int dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
