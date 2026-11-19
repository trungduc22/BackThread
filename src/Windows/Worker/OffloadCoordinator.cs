using System.Text.Json.Serialization;
using BackThread.Transport;

namespace BackThread.Worker;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PowerProfile
{
    Eco,
    Balanced,
    Performance
}

public sealed record OffloadPolicyConfig
{
    public bool Enabled { get; init; } = true;
    public double AutoPipCpuThresholdPercent { get; init; } = 70.0;
    public int AutoPipSustainSeconds { get; init; } = 12;
    public double PressureCpuThresholdPercent { get; init; } = 70.0;
    public double PressureRamThresholdPercent { get; init; } = 80.0;
    public long MinOffloadBytes { get; init; } = 262_144; // 256 KiB
    public int PauseBelowBatteryPercent { get; init; } = 20;
    public bool AllowOcrOffload { get; init; } = true;
    public bool AllowSpeechOffload { get; init; } = true;
    public bool AllowFeedPollingOffload { get; init; } = true;
}

public sealed record HostLoadMetrics(
    double CpuUsagePercent,
    double MemoryUsedPercent,
    PowerProfile PowerProfile,
    bool VideoPlayingOnPc = false);

public sealed record OffloadCandidate(
    string Kind,
    long PayloadBytes,
    double EstimatedPcComputeMs = 0);

public sealed record OffloadDecision(
    bool ShouldOffload,
    string Target, // "iphone" | "pc"
    string Reason,
    double EstimatedPcMs,
    double EstimatedPhoneTotalMs);

public sealed record OffloadCoordinatorSnapshot(
    bool Enabled,
    PowerProfile PowerProfile,
    bool PhoneEligibleForCompute,
    string PhoneHealthReason,
    string DeviceTier,
    bool AutoPipRecommended,
    long OffloadedJobsCount,
    long KeptLocalJobsCount,
    string[] AdvertisedPhoneJobs);

/// <summary>
/// Cost-benefit and thermal/battery-aware offload coordinator that reduces thread/CPU pressure on weak PCs
/// while avoiding counter-productive offloading on small payloads or hot/low-battery iPhones.
/// </summary>
public sealed class OffloadCoordinator
{
    private readonly Lock gate = new();
    private long offloadedJobs;
    private long keptLocalJobs;
    private long highCpuSinceUnixMs;
    private bool autoPipRecommended;

    public OffloadDecision Evaluate(
        OffloadCandidate candidate,
        HostLoadMetrics host,
        PairedDeviceRecord? device,
        ActiveRouteSnapshot route,
        OffloadPolicyConfig policy)
    {
        if (!policy.Enabled)
            return Record(false, "pc", "Offloading disabled by policy", candidate.EstimatedPcComputeMs, 0);

        if (route.Route == TransportRouteKind.None || device is null)
            return Record(false, "pc", "No connected companion iPhone", candidate.EstimatedPcComputeMs, 0);

        var (healthy, healthReason) = CheckPhoneHealth(device, policy);
        if (!healthy)
            return Record(false, "pc", healthReason, candidate.EstimatedPcComputeMs, 0);

        // Capability check per job kind
        if (candidate.Kind == "ocr.extract")
        {
            if (!policy.AllowOcrOffload) return Record(false, "pc", "OCR offload disabled", 0, 0);
            if (!SupportsJob(device, "ocr.extract")) return Record(false, "pc", "Connected iPhone does not advertise iOS 15 Vision OCR", 0, 0);
            // Vision Neural Engine OCR almost always beats weak-PC CPU OCR dramatically.
            return Record(true, "iphone", "iOS 15 Vision Neural Engine OCR saves ~100% PC CPU", 1800, EstimateTransferMs(candidate.PayloadBytes, route) + 350);
        }

        if (candidate.Kind == "speech.transcribe")
        {
            if (!policy.AllowSpeechOffload) return Record(false, "pc", "Speech offload disabled", 0, 0);
            if (!SupportsJob(device, "speech.transcribe")) return Record(false, "pc", "Connected iPhone does not advertise on-device Speech", 0, 0);
            return Record(true, "iphone", "iOS 15 on-device Speech offloads dictation completely", 1500, EstimateTransferMs(candidate.PayloadBytes, route) + 250);
        }

        if (candidate.Kind == "feed.poll")
        {
            if (!policy.AllowFeedPollingOffload) return Record(false, "pc", "Feed polling offload disabled", 0, 0);
            if (route.Route is TransportRouteKind.WifiLan or TransportRouteKind.Tailscale)
                return Record(true, "iphone", "iPhone polls public feeds directly over Wi-Fi, eliminating PC timers/sockets", 25, 10);
            return Record(false, "pc", "USB-only phone uses PC SafeFeedProxy instead of direct polling", 25, 40);
        }

        if (!SupportsJob(device, candidate.Kind) && candidate.Kind is not ("sha256" or "verify" or "metadata" or "text" or "compress" or "decompress" or "log.scan"))
            return Record(false, "pc", $"Connected iPhone does not support {candidate.Kind}", 0, 0);

        bool pcUnderPressure = host.CpuUsagePercent >= policy.PressureCpuThresholdPercent
            || host.MemoryUsedPercent >= policy.PressureRamThresholdPercent
            || host.PowerProfile == PowerProfile.Eco;

        if (candidate.PayloadBytes < policy.MinOffloadBytes && !pcUnderPressure)
            return Record(false, "pc", $"Payload ({candidate.PayloadBytes} B) below min offload threshold on idle PC", 5, EstimateTransferMs(candidate.PayloadBytes, route) + 15);

        double pcMs = candidate.EstimatedPcComputeMs > 0
            ? candidate.EstimatedPcComputeMs
            : EstimatePcComputeMs(candidate.Kind, candidate.PayloadBytes, host.CpuUsagePercent);
        double transferMs = EstimateTransferMs(candidate.PayloadBytes, route);
        double phoneComputeMs = EstimatePhoneComputeMs(candidate.Kind, candidate.PayloadBytes, device.CapabilityTier);
        double totalPhoneMs = transferMs + phoneComputeMs;

        // Under high PC pressure / Eco mode, offload whenever phone finishes within a reasonable factor (2x)
        // because freeing weak-PC CPU/threads is worth slight wall-clock tolerance.
        if (pcUnderPressure)
        {
            if (totalPhoneMs <= pcMs * 2.5 || pcMs >= 50)
                return Record(true, "iphone", $"PC pressure/Eco offload ({host.CpuUsagePercent:F0}% CPU, {device.CapabilityTier} iPhone)", pcMs, totalPhoneMs);
        }

        if (pcMs > totalPhoneMs)
            return Record(true, "iphone", $"Faster on {device.CapabilityTier} iPhone ({totalPhoneMs:F0}ms vs {pcMs:F0}ms PC)", pcMs, totalPhoneMs);

        return Record(false, "pc", $"Faster locally on PC ({pcMs:F0}ms vs {totalPhoneMs:F0}ms via {route.Route})", pcMs, totalPhoneMs);
    }

    public bool UpdateAutoPipAdvisor(HostLoadMetrics host, OffloadPolicyConfig policy, long? nowUnixMs = null)
    {
        long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (gate)
        {
            bool condition = policy.Enabled && host.VideoPlayingOnPc && host.CpuUsagePercent >= policy.AutoPipCpuThresholdPercent;
            if (!condition)
            {
                highCpuSinceUnixMs = 0;
                autoPipRecommended = false;
                return false;
            }
            if (highCpuSinceUnixMs == 0)
                highCpuSinceUnixMs = now;
            autoPipRecommended = (now - highCpuSinceUnixMs) >= policy.AutoPipSustainSeconds * 1000L;
            return autoPipRecommended;
        }
    }

    public static (bool Eligible, string Reason) CheckPhoneHealth(PairedDeviceRecord? device, OffloadPolicyConfig policy)
    {
        if (device is null) return (false, "No iPhone connected");
        if (string.Equals(device.ThermalState, "serious", StringComparison.OrdinalIgnoreCase)
            || string.Equals(device.ThermalState, "critical", StringComparison.OrdinalIgnoreCase))
            return (false, $"Paused: iPhone thermal state is {device.ThermalState}");

        if (device.BatteryLevel is int lvl && lvl < policy.PauseBelowBatteryPercent && device.BatteryCharging != true)
            return (false, $"Paused: iPhone battery at {lvl}% (unplugged)");

        return (true, "Healthy");
    }

    public OffloadCoordinatorSnapshot Snapshot(OffloadPolicyConfig policy, PowerProfile profile, PairedDeviceRecord? device)
    {
        var (eligible, reason) = CheckPhoneHealth(device, policy);
        lock (gate)
        {
            return new OffloadCoordinatorSnapshot(
                Enabled: policy.Enabled,
                PowerProfile: profile,
                PhoneEligibleForCompute: eligible,
                PhoneHealthReason: reason,
                DeviceTier: device?.CapabilityTier.ToString() ?? "None",
                AutoPipRecommended: autoPipRecommended,
                OffloadedJobsCount: offloadedJobs,
                KeptLocalJobsCount: keptLocalJobs,
                AdvertisedPhoneJobs: device?.SupportedJobs ?? []);
        }
    }

    private OffloadDecision Record(bool offload, string target, string reason, double pcMs, double phoneMs)
    {
        if (offload) Interlocked.Increment(ref offloadedJobs);
        else Interlocked.Increment(ref keptLocalJobs);
        return new OffloadDecision(offload, target, reason, Math.Round(pcMs, 1), Math.Round(phoneMs, 1));
    }

    private static bool SupportsJob(PairedDeviceRecord device, string kind)
        => device.SupportedJobs is not null && device.SupportedJobs.Contains(kind, StringComparer.OrdinalIgnoreCase);

    private static double EstimateTransferMs(long bytes, ActiveRouteSnapshot route)
    {
        double mbps = route.Route switch
        {
            TransportRouteKind.Usb or TransportRouteKind.Localhost => 25.0, // ~25 MB/s over USB2 Lightning
            TransportRouteKind.WifiLan => 8.0,
            TransportRouteKind.Tailscale => 3.5,
            _ => 1.0
        };
        double rtt = route.RttMilliseconds ?? (route.Route == TransportRouteKind.Usb ? 2.0 : 12.0);
        return rtt + (bytes / (mbps * 1024.0 * 1024.0)) * 1000.0;
    }

    private static double EstimatePcComputeMs(string kind, long bytes, double cpuPercent)
    {
        double pressureFactor = 1.0 + Math.Clamp(cpuPercent / 100.0, 0, 1) * 2.5;
        double mb = Math.Max(0.01, bytes / (1024.0 * 1024.0));
        double baseMsPerMb = kind switch
        {
            "compress" => 45.0,
            "decompress" => 18.0,
            "image.resize" => 80.0,
            "pdf.extract" => 65.0,
            "log.scan" => 22.0,
            _ => 12.0
        };
        return mb * baseMsPerMb * pressureFactor;
    }

    private static double EstimatePhoneComputeMs(string kind, long bytes, DeviceCapabilityTier tier)
    {
        double tierFactor = tier switch
        {
            DeviceCapabilityTier.Neural => 0.45,
            DeviceCapabilityTier.Enhanced => 0.85,
            _ => 1.8
        };
        double mb = Math.Max(0.01, bytes / (1024.0 * 1024.0));
        double baseMsPerMb = kind switch
        {
            "compress" => 25.0,
            "decompress" => 12.0,
            "image.resize" => 35.0,
            "pdf.extract" => 40.0,
            "log.scan" => 15.0,
            _ => 10.0
        };
        return mb * baseMsPerMb * tierFactor;
    }
}
