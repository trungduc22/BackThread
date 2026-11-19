using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackThread.Transport;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransportRouteKind
{
    None,
    Usb,
    WifiLan,
    Tailscale,
    Localhost
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceCapabilityTier
{
    Compact,   // e.g. iOS 12 A8 / 1 GB RAM
    Enhanced,  // e.g. iOS 13-15 A9-A11 / 2-3 GB RAM
    Neural     // e.g. iOS 15+ A12+ with Neural Engine
}

public sealed record AdaptiveAudioProfile(int PacketMs, int BytesPerPacket, int PrimeMs, int RingCapacityMs)
{
    public static AdaptiveAudioProfile ForRoute(TransportRouteKind route) => route switch
    {
        TransportRouteKind.WifiLan or TransportRouteKind.Tailscale => new(PacketMs: 20, BytesPerPacket: 3840, PrimeMs: 70, RingCapacityMs: 200),
        _ => new(PacketMs: 10, BytesPerPacket: 1920, PrimeMs: 30, RingCapacityMs: 100)
    };
}

public sealed record PairedDeviceRecord(
    string DeviceId,
    string Name,
    string? IosVersion = null,
    DeviceCapabilityTier CapabilityTier = DeviceCapabilityTier.Compact,
    int? RamMb = null,
    int? CpuCores = null,
    bool HasNeuralEngine = false,
    string? ThermalState = null,
    int? BatteryLevel = null,
    bool? BatteryCharging = null,
    string[]? SupportedJobs = null,
    string[]? SupportedCodecs = null,
    string LastRoute = "Unknown",
    long LastSeenUnixMs = 0,
    bool Revoked = false);

/// <summary>
/// Multi-device registry tracking paired iPhones, runtime capability advertisements, battery/thermal health, and revocations.
/// </summary>
public sealed class DeviceRegistry
{
    private readonly string? persistPath;
    private readonly Lock gate = new();
    private readonly Dictionary<string, PairedDeviceRecord> devices = new(StringComparer.OrdinalIgnoreCase);

    public DeviceRegistry(string? persistPath = null)
    {
        this.persistPath = persistPath;
        if (!string.IsNullOrWhiteSpace(persistPath) && File.Exists(persistPath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<PairedDeviceRecord[]>(File.ReadAllText(persistPath), Message.Json) ?? [];
                foreach (var d in loaded) devices[d.DeviceId] = d;
            }
            catch
            {
                // Ignore corrupt persisted file; rewrite on next update.
            }
        }
    }

    public IReadOnlyList<PairedDeviceRecord> List()
    {
        lock (gate) return devices.Values.OrderByDescending(d => d.LastSeenUnixMs).ToList();
    }

    public PairedDeviceRecord? Get(string deviceId)
    {
        lock (gate) return devices.GetValueOrDefault(deviceId);
    }

    public bool IsRevoked(string deviceId)
    {
        lock (gate) return devices.TryGetValue(deviceId, out var d) && d.Revoked;
    }

    public PairedDeviceRecord RecordSeen(string deviceId, string name, string route)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (gate)
        {
            if (devices.TryGetValue(deviceId, out var existing))
            {
                var updated = existing with
                {
                    Name = string.IsNullOrWhiteSpace(name) ? existing.Name : name,
                    LastRoute = route,
                    LastSeenUnixMs = now,
                    Revoked = false
                };
                devices[deviceId] = updated;
                PersistUnsafe();
                return updated;
            }
            var created = new PairedDeviceRecord(deviceId, string.IsNullOrWhiteSpace(name) ? "iPhone" : name, LastRoute: route, LastSeenUnixMs: now);
            devices[deviceId] = created;
            PersistUnsafe();
            return created;
        }
    }

    public PairedDeviceRecord UpdateFromHello(string deviceId, string fallbackName, TransportRouteKind route, JsonElement helloBody)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? iosVersion = helloBody.TryGetProperty("iosVersion", out var v) ? v.GetString() : null;
        int? ramMb = helloBody.TryGetProperty("ramMb", out var rm) && rm.TryGetInt32(out int r) ? r : null;
        int? cpuCores = helloBody.TryGetProperty("cpuCores", out var cc) && cc.TryGetInt32(out int c) ? c : null;
        bool hasNeural = helloBody.TryGetProperty("hasNeuralEngine", out var ne) && ne.ValueKind == JsonValueKind.True;
        string? thermal = helloBody.TryGetProperty("thermalState", out var th) ? th.GetString() : null;
        int? battery = helloBody.TryGetProperty("batteryLevel", out var bat) && bat.TryGetInt32(out int b) ? Math.Clamp(b, 0, 100) : null;
        bool? charging = helloBody.TryGetProperty("batteryCharging", out var ch) && ch.ValueKind is JsonValueKind.True or JsonValueKind.False ? ch.GetBoolean() : null;
        string? name = helloBody.TryGetProperty("deviceName", out var dn) && !string.IsNullOrWhiteSpace(dn.GetString()) ? dn.GetString() : fallbackName;
        string[]? jobs = ReadStringArray(helloBody, "supportedJobs");
        string[]? codecs = ReadStringArray(helloBody, "supportedCodecs");

        var tier = ClassifyTier(iosVersion, ramMb, hasNeural);

        lock (gate)
        {
            devices.TryGetValue(deviceId, out var prior);
            var updated = (prior ?? new PairedDeviceRecord(deviceId, name ?? "iPhone")) with
            {
                Name = name ?? prior?.Name ?? "iPhone",
                IosVersion = iosVersion ?? prior?.IosVersion,
                CapabilityTier = tier,
                RamMb = ramMb ?? prior?.RamMb,
                CpuCores = cpuCores ?? prior?.CpuCores,
                HasNeuralEngine = hasNeural || (prior?.HasNeuralEngine ?? false),
                ThermalState = thermal ?? prior?.ThermalState,
                BatteryLevel = battery ?? prior?.BatteryLevel,
                BatteryCharging = charging ?? prior?.BatteryCharging,
                SupportedJobs = jobs ?? prior?.SupportedJobs,
                SupportedCodecs = codecs ?? prior?.SupportedCodecs,
                LastRoute = route.ToString(),
                LastSeenUnixMs = now
            };
            devices[deviceId] = updated;
            PersistUnsafe();
            return updated;
        }
    }

    public PairedDeviceRecord? UpdateHealth(string deviceId, string? thermalState, int? batteryLevel, bool? batteryCharging)
    {
        lock (gate)
        {
            if (!devices.TryGetValue(deviceId, out var prior)) return null;
            var updated = prior with
            {
                ThermalState = thermalState ?? prior.ThermalState,
                BatteryLevel = batteryLevel is int lv ? Math.Clamp(lv, 0, 100) : prior.BatteryLevel,
                BatteryCharging = batteryCharging ?? prior.BatteryCharging,
                LastSeenUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            devices[deviceId] = updated;
            return updated;
        }
    }

    public bool Revoke(string deviceId)
    {
        lock (gate)
        {
            if (!devices.TryGetValue(deviceId, out var prior)) return false;
            devices[deviceId] = prior with { Revoked = true };
            PersistUnsafe();
            return true;
        }
    }

    public bool Remove(string deviceId)
    {
        lock (gate)
        {
            bool removed = devices.Remove(deviceId);
            if (removed) PersistUnsafe();
            return removed;
        }
    }

    public static DeviceCapabilityTier ClassifyTier(string? iosVersion, int? ramMb, bool hasNeuralEngine)
    {
        int major = 0;
        if (!string.IsNullOrWhiteSpace(iosVersion))
        {
            string first = iosVersion.Split('.')[0];
            int.TryParse(first, out major);
        }
        if (major >= 15 && (hasNeuralEngine || (ramMb ?? 0) >= 3000))
            return DeviceCapabilityTier.Neural;
        if (major >= 13 || (ramMb ?? 0) >= 1800)
            return DeviceCapabilityTier.Enhanced;
        return DeviceCapabilityTier.Compact;
    }

    private static string[]? ReadStringArray(JsonElement obj, string prop)
    {
        if (!obj.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.Array) return null;
        return el.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray();
    }

    private void PersistUnsafe()
    {
        if (string.IsNullOrWhiteSpace(persistPath)) return;
        try
        {
            string full = Path.GetFullPath(persistPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string tmp = full + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(devices.Values.ToArray(), Message.Json));
            File.Move(tmp, full, overwrite: true);
        }
        catch
        {
            // Keep in-memory state if disk is temporarily read-only.
        }
    }
}

public sealed record ActiveRouteSnapshot(
    TransportRouteKind Route,
    string? RemoteAddress,
    string? ActiveDeviceId,
    double? RttMilliseconds,
    AdaptiveAudioProfile AudioProfile,
    long FailoverCount,
    long ConnectedSinceUnixMs);

/// <summary>
/// Tracks the active transport route (USB / Wi-Fi LAN / Tailscale / Localhost), RTT, failover counters,
/// and computes adaptive audio framing automatically per route.
/// </summary>
public sealed class ActiveRouteManager
{
    private readonly Lock gate = new();
    private TransportRouteKind route = TransportRouteKind.None;
    private string? remoteAddress;
    private string? activeDeviceId;
    private double? rttMs;
    private long failoverCount;
    private long connectedSince;

    public event Action<ActiveRouteSnapshot>? RouteChanged;

    public ActiveRouteSnapshot Snapshot
    {
        get
        {
            lock (gate)
            {
                return new ActiveRouteSnapshot(
                    route,
                    remoteAddress,
                    activeDeviceId,
                    rttMs,
                    AdaptiveAudioProfile.ForRoute(route),
                    failoverCount,
                    connectedSince);
            }
        }
    }

    public void SetActive(TransportRouteKind newRoute, string? remote, string? deviceId = null)
    {
        ActiveRouteSnapshot snap;
        lock (gate)
        {
            if (route != TransportRouteKind.None && newRoute != TransportRouteKind.None && route != newRoute)
                failoverCount++;
            route = newRoute;
            remoteAddress = remote;
            activeDeviceId = deviceId ?? activeDeviceId;
            connectedSince = newRoute == TransportRouteKind.None ? 0 : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (newRoute == TransportRouteKind.None) rttMs = null;
            snap = new ActiveRouteSnapshot(route, remoteAddress, activeDeviceId, rttMs, AdaptiveAudioProfile.ForRoute(route), failoverCount, connectedSince);
        }
        RouteChanged?.Invoke(snap);
    }

    public void RecordRtt(double milliseconds)
    {
        lock (gate) rttMs = Math.Round(Math.Max(0, milliseconds), 2);
    }

    public void Clear() => SetActive(TransportRouteKind.None, null, null);
}
