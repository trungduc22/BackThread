using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BackThread.Host;

public sealed record ChargeGuardConfig
{
    public bool Enabled { get; init; }
    public int MinPercent { get; init; } = 50;
    public int MaxPercent { get; init; } = 80;
    /// <summary>ha | webhook | action</summary>
    public string Method { get; init; } = "webhook";
    public string? HomeAssistantUrl { get; init; }
    public string? HomeAssistantToken { get; init; }
    public string? HomeAssistantEntity { get; init; }
    public string? WebhookOnUrl { get; init; }
    public string? WebhookOffUrl { get; init; }
    public string? OnActionId { get; init; }
    public string? OffActionId { get; init; }
}

public sealed record PhoneBatteryTelemetry(int Level, bool Charging, string? ThermalState, long ReportedAtUnixMs);

public sealed record ChargeGuardStatus(
    bool Enabled,
    int MinPercent,
    int MaxPercent,
    PhoneBatteryTelemetry? LatestBattery,
    string? LastPowerState,
    long? LastSwitchedUnixMs,
    string? LastError);

/// <summary>
/// Protects a permanently docked iPhone battery by cycling an external smart plug / USB switch
/// between MinPercent and MaxPercent, with a 10-minute silent fail-safe that restores power if the phone stops reporting.
/// </summary>
public sealed class ChargeGuardService : IDisposable
{
    public const long RetryIntervalMs = 10 * 60_000;
    public const long SilentFailsafeMs = 10 * 60_000;

    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly SemaphoreSlim switchGate = new(1, 1);
    private readonly Lock stateGate = new();
    private PhoneBatteryTelemetry? latest;
    private string? lastPowerState; // "on" | "off"
    private long lastSwitchedAt;
    private string? lastError;

    public ChargeGuardService(HttpMessageHandler? handler = null)
    {
        if (handler is not null) { http = new HttpClient(handler, disposeHandler: false); ownsHttp = false; }
        else { http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) }; ownsHttp = true; }
    }

    public void RecordPhoneBattery(int level, bool charging, string? thermalState = null, long? nowUnixMs = null)
    {
        lock (stateGate)
        {
            latest = new PhoneBatteryTelemetry(
                Math.Clamp(level, 0, 100),
                charging,
                thermalState,
                nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    public ChargeGuardStatus GetStatus(ChargeGuardConfig config)
    {
        lock (stateGate)
        {
            return new ChargeGuardStatus(
                config.Enabled,
                config.MinPercent,
                config.MaxPercent,
                latest,
                lastPowerState,
                lastSwitchedAt == 0 ? null : lastSwitchedAt,
                lastError);
        }
    }

    /// <summary>Pure decision function: returns "on", "off", or null.</summary>
    public static string? Decide(ChargeGuardConfig config, PhoneBatteryTelemetry? report, long nowUnixMs, string? lastState, long lastSwitchedUnixMs)
    {
        if (!config.Enabled)
            return lastState == "off" ? "on" : null;

        // Fail-safe: if phone stops reporting for > 10 minutes while plug is off, turn power back ON!
        if (report is null || nowUnixMs - report.ReportedAtUnixMs is < 0 or > SilentFailsafeMs)
            return lastState == "off" ? "on" : null;

        int min = Math.Clamp(config.MinPercent, 15, 90);
        int max = Math.Clamp(config.MaxPercent, min + 5, 100);
        bool retryDue = lastSwitchedUnixMs == 0 || (nowUnixMs - lastSwitchedUnixMs) > RetryIntervalMs;

        if (report.Level >= max && report.Charging && (lastState != "off" || retryDue))
            return "off";
        if (report.Level <= min && !report.Charging && (lastState != "on" || retryDue))
            return "on";

        return null;
    }

    public async Task<string?> EvaluateAndApplyAsync(
        ChargeGuardConfig config,
        Func<string, CancellationToken, Task>? runActionAsync,
        CancellationToken ct,
        long? nowUnixMs = null)
    {
        await switchGate.WaitAsync(ct);
        try
        {
            PhoneBatteryTelemetry? snap;
            string? prior;
            long priorAt;
            lock (stateGate) { snap = latest; prior = lastPowerState; priorAt = lastSwitchedAt; }

            long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string? desired = Decide(config, snap, now, prior, priorAt);
            if (desired is null) return null;

            string? err = await SwitchPowerAsync(config, desired, runActionAsync, ct);
            lock (stateGate)
            {
                if (err is null || desired == "off") lastPowerState = desired;
                lastSwitchedAt = now;
                lastError = err;
            }
            return desired;
        }
        finally
        {
            switchGate.Release();
        }
    }

    private async Task<string?> SwitchPowerAsync(ChargeGuardConfig config, string action, Func<string, CancellationToken, Task>? runActionAsync, CancellationToken ct)
    {
        try
        {
            if (string.Equals(config.Method, "ha", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(config.HomeAssistantUrl) || string.IsNullOrWhiteSpace(config.HomeAssistantEntity))
                    return "Home Assistant URL or switch entity not configured";

                string service = action == "on" ? "turn_on" : "turn_off";
                string endpoint = config.HomeAssistantUrl.TrimEnd('/') + $"/api/services/switch/{service}";
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { entity_id = config.HomeAssistantEntity }), Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrWhiteSpace(config.HomeAssistantToken))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.HomeAssistantToken);
                using var res = await http.SendAsync(req, ct);
                return res.IsSuccessStatusCode ? null : $"Home Assistant returned HTTP {(int)res.StatusCode}";
            }

            if (string.Equals(config.Method, "action", StringComparison.OrdinalIgnoreCase))
            {
                string? actionId = action == "on" ? config.OnActionId : config.OffActionId;
                if (string.IsNullOrWhiteSpace(actionId) || runActionAsync is null)
                    return $"No authorized action configured for power {action}";
                await runActionAsync(actionId, ct);
                return null;
            }

            string? url = action == "on" ? config.WebhookOnUrl : config.WebhookOffUrl;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                return $"Invalid webhook URL for power {action}";
            using var post = new HttpRequestMessage(HttpMethod.Post, uri);
            using var resp = await http.SendAsync(post, ct);
            return resp.IsSuccessStatusCode ? null : $"Webhook returned HTTP {(int)resp.StatusCode}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public void Dispose()
    {
        switchGate.Dispose();
        if (ownsHttp) http.Dispose();
    }
}
