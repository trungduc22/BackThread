using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackThread.Desktop;
using BackThread.Host;
using BackThread.Media;
using BackThread.Transport;
using BackThread.Worker;
using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Communication.Ipc;
using NativeShellKit.Security;

namespace BackThread.Daemon;

/// <summary>
/// Exposes <see cref="HostRuntime"/> to the on-demand WinUI dashboard over the NativeShellKit
/// HMAC-authenticated Named Pipe (protocol 1.0). Every operation is explicitly allowlisted and
/// every payload field is validated; there is no generic "invoke"/"exec" operation.
/// </summary>
public sealed class DaemonIpcHost : IAsyncDisposable
{
    /// <summary>Raw JSON response budget; the signed envelope (with JSON string escaping) must stay under 64 KiB.</summary>
    public const int MaxResponseJsonBytes = 40 * 1024;

    public static readonly JsonSerializerOptions Json = CreateJson();

    private readonly NamedPipeIpcServer server;
    private readonly IpcAuthManager auth;
    private readonly HostRuntime runtime;
    private readonly DaemonLog log;
    private readonly Action onShutdownRequested;
    private readonly DateTimeOffset startedUtc = DateTimeOffset.UtcNow;

    public DaemonIpcHost(
        AppIdentity identity,
        IpcAuthManager auth,
        HostRuntime runtime,
        DaemonLog log,
        Action onShutdownRequested,
        string? pipeNameOverride = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        this.log = log ?? throw new ArgumentNullException(nameof(log));
        this.onShutdownRequested = onShutdownRequested ?? throw new ArgumentNullException(nameof(onShutdownRequested));
        server = new NamedPipeIpcServer(identity, auth, pipeNameOverride);
        RegisterOperations();
    }

    public string PipeName => server.PipeName;
    public IReadOnlyCollection<string> AllowedOperations => server.AllowedOperations;

    public Task StartAsync(CancellationToken ct = default) => server.StartAsync(ct);
    public Task StopAsync(CancellationToken ct = default) => server.StopAsync(ct);
    public ValueTask DisposeAsync() => server.DisposeAsync();

    private void RegisterOperations()
    {
        Register(DaemonOperations.StatusGet, (_, _) => Done(runtime.Status));
        Register(DaemonOperations.TelemetryGet, (_, _) => Done(runtime.LatestTelemetry));
        Register(DaemonOperations.LanStatus, (_, _) => Done(runtime.LanStatus));
        Register(DaemonOperations.LanPairStart, (_, _) => Done(StartPairing()));
        Register(DaemonOperations.LanDevicesList, (_, _) => Done(new { devices = runtime.ListDevices() }));
        Register(DaemonOperations.LanDevicesRevoke, (p, _) =>
        {
            string id = RequireString(p, "deviceId", 128);
            return Done(new { deviceId = id, revoked = runtime.RevokeDevice(id) });
        });
        Register(DaemonOperations.LanDevicesRotate, (p, _) =>
        {
            string id = RequireString(p, "deviceId", 128);
            return Done(new { device = runtime.RotateDeviceKey(id) });
        });
        Register(DaemonOperations.LanDevicesDisconnect, (p, _) =>
        {
            string? id = OptionalString(p, "deviceId", 128);
            return Done(new { disconnected = runtime.DisconnectDevice(id) });
        });
        Register(DaemonOperations.LanDevicesPermissions, (p, _) =>
        {
            string id = RequireString(p, "deviceId", 128);
            bool? allowTouchpad = OptionalBool(p, "allowTouchpad");
            bool? allowDrawingPad = OptionalBool(p, "allowDrawingPad");
            return Done(new { device = runtime.SetDeviceInputPermissions(id, allowTouchpad, allowDrawingPad) });
        });
        Register(DaemonOperations.InputStatus, (_, _) => Done(runtime.Input.GetMetrics()));
        Register(DaemonOperations.InputEmergencyStop, (_, _) =>
        {
            runtime.Input.EmergencyDisconnect();
            return Done(new { emergencyStopped = true, metrics = runtime.Input.GetMetrics() });
        });

        Register(DaemonOperations.AudioState, (_, _) => Done(runtime.CurrentAudioControlState()));
        Register(DaemonOperations.AudioModeSet, async (p, ct) =>
        {
            var mode = RequireEnum<AudioOutputMode>(p, "mode");
            return await runtime.SetAudioOutputModeAsync(mode, ct).ConfigureAwait(false);
        });
        Register(DaemonOperations.AudioVolumeSet, async (p, ct) =>
        {
            float value = RequireUnitFloat(p, "value");
            string? layer = OptionalString(p, "layer", 16);
            if (layer is not (null or "auto" or "pc" or "app" or "player"))
                throw new InvalidDataException("layer must be auto, pc, app, or player");
            return await runtime.SetVolumeAsync(value, layer, ct).ConfigureAwait(false);
        });
        Register(DaemonOperations.AudioMixerList, (_, _) => Done(new { sessions = runtime.ListPerAppAudio() }));
        Register(DaemonOperations.AudioMixerSet, (p, _) =>
        {
            int pid = RequireInt(p, "processId", 1, int.MaxValue);
            float? volume = p.TryGetProperty("volume", out var v) && v.ValueKind == JsonValueKind.Number ? RequireUnitFloat(p, "volume") : null;
            bool? muted = OptionalBool(p, "muted");
            var updated = runtime.SetPerAppAudio(pid, volume, muted) ?? throw new InvalidDataException("Audio session not found");
            return Done(updated);
        });

        Register(DaemonOperations.MediaStatus, (_, _) => Done(runtime.Router.CurrentState));
        Register(DaemonOperations.MediaSubmit, async (p, ct) =>
        {
            string url = RequireString(p, "url", 4096);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new InvalidDataException("Only absolute http(s) URLs can be handed off");
            return await runtime.SubmitMediaAsync(new MediaSubmissionRequest(Url: uri.ToString()), ct).ConfigureAwait(false);
        });
        Register(DaemonOperations.MediaCommand, async (p, ct) =>
        {
            string command = RequireString(p, "command", 16).ToLowerInvariant();
            if (!DaemonOperations.AllowedMediaCommands.Contains(command))
                throw new InvalidDataException("Unsupported media command");
            double seconds = p.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? Math.Clamp(s.GetDouble(), 0, 86_400) : 0;
            string? target = OptionalString(p, "target", 16);
            if (target is not (null or "windows" or "iphone"))
                throw new InvalidDataException("target must be windows or iphone");
            return await runtime.MediaCommandAsync(command, seconds, null, null, target, ct).ConfigureAwait(false);
        });
        Register(DaemonOperations.MediaReturn, async (p, ct) =>
            await runtime.ReturnToWindowsAsync(OptionalBool(p, "resumeWindows") ?? true, ct).ConfigureAwait(false));

        Register(DaemonOperations.OffloadStatus, (_, _) => Done(runtime.GetOffloadSnapshot()));
        Register(DaemonOperations.OffloadEvaluate, (p, _) =>
        {
            string kind = RequireString(p, "kind", 32);
            long bytes = p.TryGetProperty("payloadBytes", out var b) && b.TryGetInt64(out long lb) ? Math.Clamp(lb, 0, 1L << 34) : 0;
            double pcMs = p.TryGetProperty("estimatedPcComputeMs", out var e) && e.ValueKind == JsonValueKind.Number ? Math.Clamp(e.GetDouble(), 0, 3_600_000) : 0;
            return Done(runtime.EvaluateOffload(new OffloadCandidate(kind, bytes, pcMs)));
        });
        Register(DaemonOperations.PowerProfileSet, (p, _) =>
            Done(new { powerProfile = runtime.SetPowerProfile(RequireEnum<PowerProfile>(p, "profile")) }));

        Register(DaemonOperations.GpuSample, (_, _) => Done(new { gpus = runtime.SampleGpu() }));
        Register(DaemonOperations.ProcessesSample, (p, _) =>
        {
            int limit = p.TryGetProperty("limit", out var l) && l.TryGetInt32(out int li) ? Math.Clamp(li, 1, 25) : 10;
            return Done(new { processes = runtime.SampleProcesses(limit) });
        });
        Register(DaemonOperations.DevSample, async (_, ct) => await runtime.SampleDevSessionsAsync(ct).ConfigureAwait(false));

        Register(DaemonOperations.RulesStatus, (_, _) => Done(runtime.GetRulesStatus()));
        Register(DaemonOperations.RulesPause, (p, _) =>
        {
            int minutes = p.TryGetProperty("minutes", out var m) && m.TryGetInt32(out int mi) ? Math.Clamp(mi, 0, 24 * 60) : 60;
            runtime.PauseRules(minutes);
            return Done(runtime.GetRulesStatus());
        });
        Register(DaemonOperations.ChargeGuardStatus, (_, _) => Done(runtime.GetChargeGuardStatus()));
        Register(DaemonOperations.NotificationsStatus, (_, _) => Done(new
        {
            status = runtime.NotificationAccessStatus(),
            diagnostics = runtime.NotificationDiagnostics()
        }));

        Register(DaemonOperations.DiagnosticsSnapshot, (_, _) => Done(CaptureDiagnostics()));
        Register(DaemonOperations.LogsTail, (p, _) =>
        {
            int count = p.TryGetProperty("count", out var c) && c.TryGetInt32(out int ci) ? Math.Clamp(ci, 1, 200) : 50;
            return Done(new { lines = log.Tail(count) });
        });
        Register(DaemonOperations.DaemonShutdown, (_, _) =>
        {
            log.Write("Shutdown requested by dashboard");
            // Reply first, then stop the daemon so the dashboard receives a signed OK.
            _ = Task.Run(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false);
                onShutdownRequested();
            });
            return Done(new { stopping = true });
        });
    }

    private object StartPairing()
    {
        var session = runtime.StartQrPairing();
        byte[] png = QrPairingService.RenderPng(session.PairingUri, pixelsPerModule: 5);
        // SvgMarkup is intentionally omitted: the PNG is ~10x smaller and keeps the signed envelope far below 64 KiB.
        return new
        {
            attempt = session.Attempt,
            pairingUri = session.PairingUri,
            humanCheckCode = session.HumanCheckCode,
            fingerprint = session.Fingerprint,
            pcId = session.PcId,
            pcName = session.PcName,
            port = session.Port,
            hosts = session.Hosts,
            expiresAtUnixMs = session.ExpiresAtUnixMs,
            qrPngBase64 = Convert.ToBase64String(png)
        };
    }

    private object CaptureDiagnostics()
    {
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        return new
        {
            processId = Environment.ProcessId,
            version = DaemonContract.Version,
            startedUtc,
            uptimeSeconds = Math.Round((DateTimeOffset.UtcNow - startedUtc).TotalSeconds, 1),
            workingSetBytes = proc.WorkingSet64,
            privateMemoryBytes = proc.PrivateMemorySize64,
            gcTotalMemoryBytes = GC.GetTotalMemory(false),
            totalProcessorSeconds = Math.Round(proc.TotalProcessorTime.TotalSeconds, 3),
            threadCount = proc.Threads.Count,
            pipeName = server.PipeName,
            allowedOperations = server.AllowedOperations.Count,
            os = Environment.OSVersion.VersionString,
            runtime = Environment.Version.ToString()
        };
    }

    private void Register(string operation, Func<JsonElement, CancellationToken, Task<object?>> handler)
    {
        if (!DaemonOperations.All.Contains(operation))
            throw new InvalidOperationException("Operation is not part of the shared allowlist: " + operation);

        server.RegisterAllowedHandler(operation, async (request, ct) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(
                    string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson,
                    new JsonDocumentOptions { MaxDepth = 8 });
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return Error("INVALID_PAYLOAD", "Payload must be a JSON object");

                object? result = await handler(doc.RootElement, ct).ConfigureAwait(false);
                string json = JsonSerializer.Serialize(result, Json);
                if (Encoding.UTF8.GetByteCount(json) > MaxResponseJsonBytes)
                    return Error("RESPONSE_TOO_LARGE", $"{operation} response exceeds the IPC budget");
                return auth.CreateSignedEnvelope("OK", json);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or JsonException or FormatException)
            {
                return Error("INVALID_REQUEST", ex.Message);
            }
            catch (Exception ex)
            {
                log.Write($"IPC {operation} failed: {ex.GetType().Name}: {ex.Message}");
                return Error("OPERATION_FAILED", ex.Message);
            }
        });
    }

    private IpcEnvelope Error(string code, string message) =>
        auth.CreateSignedEnvelope("ERROR", JsonSerializer.Serialize(new { error = code, message }, Json));

    private static Task<object?> Done(object? value) => Task.FromResult(value);

    private static string RequireString(JsonElement p, string name, int maxLength)
    {
        if (!p.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"'{name}' is required");
        string value = el.GetString()!.Trim();
        if (value.Length == 0 || value.Length > maxLength)
            throw new InvalidDataException($"'{name}' must be 1..{maxLength} characters");
        return value;
    }

    private static string? OptionalString(JsonElement p, string name, int maxLength)
    {
        if (!p.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        if (el.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"'{name}' must be a string");
        string value = el.GetString()!.Trim();
        if (value.Length > maxLength)
            throw new InvalidDataException($"'{name}' is too long");
        return value.Length == 0 ? null : value.ToLowerInvariant();
    }

    private static bool? OptionalBool(JsonElement p, string name) =>
        p.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False ? el.GetBoolean() : null;

    private static int RequireInt(JsonElement p, string name, int min, int max)
    {
        if (!p.TryGetProperty(name, out var el) || !el.TryGetInt32(out int value) || value < min || value > max)
            throw new InvalidDataException($"'{name}' must be an integer in [{min}, {max}]");
        return value;
    }

    private static float RequireUnitFloat(JsonElement p, string name)
    {
        if (!p.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"'{name}' must be a number");
        double value = el.GetDouble();
        if (!double.IsFinite(value) || value < 0 || value > 1)
            throw new InvalidDataException($"'{name}' must be between 0 and 1");
        return (float)value;
    }

    private static TEnum RequireEnum<TEnum>(JsonElement p, string name) where TEnum : struct, Enum
    {
        string raw = RequireString(p, name, 32);
        if (int.TryParse(raw, out _) || !Enum.TryParse<TEnum>(raw, ignoreCase: true, out var value) || !Enum.IsDefined(value))
            throw new InvalidDataException($"'{name}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}");
        return value;
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(Message.Json) { WriteIndented = false };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
