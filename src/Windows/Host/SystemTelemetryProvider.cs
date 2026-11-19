using System.Collections.Concurrent;
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
/// Power via GetSystemPowerStatus, Wi-Fi via Wireless80211 interface, and event-driven WinRT Bluetooth
/// radio + Aep.IsConnected DeviceWatcher with 30s fallback refresh so weak PCs avoid 2s PnP query storms).
/// </summary>
public sealed class SystemTelemetryProvider : ISystemTelemetryProvider, IDisposable
{
    private const string AqsConnectedBt =
        "System.Devices.Aep.IsConnected:=System.StructuredQueryType.Boolean#True " +
        "AND (System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" " +
        "OR System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

    private readonly object cpuGate = new();
    private readonly object btGate = new();
    private ulong prevIdle, prevKernel, prevUser;
    private bool hasPrevCpu;

    // Event-driven Bluetooth state (initialized lazily on Windows)
    private readonly ConcurrentDictionary<string, byte> connectedBtIds = new(StringComparer.OrdinalIgnoreCase);
    private DeviceWatcher? btWatcher;
    private Radio? btRadio;
    private string cachedBtRadioState = "Unknown";
    private bool btWatcherStarted;
    private bool btInitialEnumComplete;
    private long lastBtRefreshTick = -1;
    private long lastWifiRefreshTick = -1;
    private (bool? Connected, string? Name) cachedWifi;

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
        (bool? wifiUp, string? wifiName) = SampleWifiCached();
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

    private (bool? connected, string? name) SampleWifiCached()
    {
        long now = Environment.TickCount64;
        if (lastWifiRefreshTick >= 0 && now - lastWifiRefreshTick < 5000)
            return cachedWifi;

        cachedWifi = SampleWifiInterface();
        lastWifiRefreshTick = now;
        return cachedWifi;
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
    private void EnsureBluetoothWatcherStarted()
    {
        lock (btGate)
        {
            if (btWatcherStarted) return;
            btWatcherStarted = true;
            try
            {
                var watcher = DeviceInformation.CreateWatcher(AqsConnectedBt);
                watcher.Added += (_, info) => connectedBtIds[info.Id] = 1;
                watcher.Removed += (_watcher, update) => connectedBtIds.TryRemove(update.Id, out byte _);
                watcher.EnumerationCompleted += (_, _) => btInitialEnumComplete = true;
                watcher.Stopped += (_, _) =>
                {
                    lock (btGate) { btWatcherStarted = false; }
                };
                watcher.Start();
                btWatcher = watcher;
            }
            catch
            {
                btWatcher = null;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<(string radioState, int connectedCount, bool? connected)> SampleWindowsBluetoothAsync(CancellationToken ct)
    {
        EnsureBluetoothWatcherStarted();
        long now = Environment.TickCount64;
        bool needRadioRefresh = lastBtRefreshTick < 0 || now - lastBtRefreshTick >= 30_000;

        if (needRadioRefresh)
        {
            lastBtRefreshTick = now;
            try
            {
                var radios = await Radio.GetRadiosAsync();
                ct.ThrowIfCancellationRequested();
                var radio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
                lock (btGate)
                {
                    if (btRadio is not null && !ReferenceEquals(btRadio, radio))
                        try { btRadio.StateChanged -= OnBluetoothRadioStateChanged; } catch { }
                    btRadio = radio;
                    if (radio is null)
                    {
                        cachedBtRadioState = "Unavailable";
                    }
                    else
                    {
                        cachedBtRadioState = FormatRadioState(radio.State);
                        try
                        {
                            radio.StateChanged -= OnBluetoothRadioStateChanged;
                            radio.StateChanged += OnBluetoothRadioStateChanged;
                        }
                        catch { }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                lock (btGate) cachedBtRadioState = "Unknown";
            }

            // Fallback one-time PnP sync if watcher failed to start or hasn't finished initial scan yet.
            if (btWatcher is null || !btInitialEnumComplete)
            {
                try
                {
                    var devices = await DeviceInformation.FindAllAsync(AqsConnectedBt);
                    ct.ThrowIfCancellationRequested();
                    connectedBtIds.Clear();
                    if (devices is not null)
                    {
                        foreach (var d in devices)
                            connectedBtIds[d.Id] = 1;
                    }
                    btInitialEnumComplete = true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch { }
            }
        }

        string radioState;
        lock (btGate) radioState = cachedBtRadioState;

        if (radioState is "Unavailable" or "Off" or "Disabled")
            return (radioState, 0, false);

        int count = connectedBtIds.Count;
        return (radioState, count, count > 0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnBluetoothRadioStateChanged(Radio sender, object args)
    {
        lock (btGate) cachedBtRadioState = FormatRadioState(sender.State);
    }

    private static string FormatRadioState(RadioState state) => state switch
    {
        RadioState.On => "On",
        RadioState.Off => "Off",
        RadioState.Disabled => "Disabled",
        _ => "Unknown"
    };

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        DisposeWindowsResources();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisposeWindowsResources()
    {
        lock (btGate)
        {
            if (btWatcher is not null)
            {
                try { if (btWatcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) btWatcher.Stop(); } catch { }
                btWatcher = null;
            }
            if (btRadio is not null)
            {
                try { btRadio.StateChanged -= OnBluetoothRadioStateChanged; } catch { }
                btRadio = null;
            }
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
