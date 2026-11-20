using System.Diagnostics;
using System.Text.Json;
using BackThread.Desktop;
using NativeShellKit.Abstractions.Communication;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Communication.Ipc;
using NativeShellKit.Security;

namespace BackThread.App.Services;

public sealed class DaemonException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Typed dashboard client for BackThread.Daemon. Requests are HMAC-signed by NativeShellKit and every
/// response envelope is verified too (mutual authentication). If the daemon is not running, the sibling
/// BackThread.Daemon.exe is started once and the handshake is retried.
/// </summary>
public sealed class DaemonClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppIdentity identity;
    private readonly IpcAuthManager auth;
    private readonly NamedPipeIpcClient client;
    private readonly SemaphoreSlim connectGate = new(1, 1);
    private bool handshakeDone;

    public DaemonClient(AppIdentity identity, IpcAuthManager auth)
    {
        this.identity = identity ?? throw new ArgumentNullException(nameof(identity));
        this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
        client = new NamedPipeIpcClient(identity, auth, pipeNameOverride: null, connectTimeoutMs: 700);
    }

    public bool IsConnected => handshakeDone;
    public string PipeName => client.PipeName;
    public string? LastError { get; private set; }

    public async Task<JsonElement> CallAsync(string operation, object? payload = null, CancellationToken ct = default)
    {
        if (!DaemonOperations.All.Contains(operation))
            throw new ArgumentException("Operation is not in the shared daemon allowlist", nameof(operation));

        await EnsureConnectedAsync(ct).ConfigureAwait(false);
        string json = payload is null ? "{}" : JsonSerializer.Serialize(payload, Json);
        IpcEnvelope response;
        try
        {
            response = await client.SendAsync(operation, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            handshakeDone = false;
            LastError = ex.Message;
            throw new DaemonException("UNREACHABLE", "BackThread daemon is not reachable: " + ex.Message);
        }

        if (!auth.VerifyEnvelope(response))
            throw new DaemonException("UNTRUSTED_RESPONSE", "Daemon response failed HMAC verification");

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(response.PayloadJson) ? "{}" : response.PayloadJson);
        var root = doc.RootElement.Clone();
        if (!string.Equals(response.Operation, "OK", StringComparison.Ordinal))
        {
            string code = root.TryGetProperty("error", out var e) ? e.GetString() ?? "ERROR" : "ERROR";
            string message = root.TryGetProperty("message", out var m) ? m.GetString() ?? code : code;
            LastError = message;
            throw new DaemonException(code, message);
        }
        LastError = null;
        return root;
    }

    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (handshakeDone)
            return;
        await connectGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (handshakeDone)
                return;
            if (await TryHandshakeAsync(ct).ConfigureAwait(false))
                return;

            if (!TryLaunchDaemon())
                throw new DaemonException("DAEMON_NOT_FOUND",
                    $"BackThread.Daemon.exe was not found next to the dashboard (set {DaemonContract.DaemonPathEnvironmentVariable}).");

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
                if (await TryHandshakeAsync(ct).ConfigureAwait(false))
                    return;
            }
            throw new DaemonException("UNREACHABLE", "BackThread daemon did not answer the IPC handshake: " + LastError);
        }
        finally
        {
            connectGate.Release();
        }
    }

    private async Task<bool> TryHandshakeAsync(CancellationToken ct)
    {
        try
        {
            var caps = await client.HandshakeAsync(ct).ConfigureAwait(false);
            if (!string.Equals(caps.AppId, identity.AppId, StringComparison.OrdinalIgnoreCase))
            {
                LastError = caps.SupportedOperations.Length == 0 ? "Handshake rejected (UNAUTHORIZED?)" : "Unexpected AppId " + caps.AppId;
                return false;
            }
            handshakeDone = true;
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static bool TryLaunchDaemon()
    {
        string? exe = DaemonContract.ResolveSibling(DaemonContract.DaemonExecutableName, DaemonContract.DaemonPathEnvironmentVariable);
        if (exe is null)
            return false;
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Null-safe readers for the daemon's camelCase JSON payloads.</summary>
public static class Json
{
    public static JsonElement? Get(this JsonElement element, string path)
    {
        var current = element;
        foreach (string part in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out var next))
                return null;
            current = next;
        }
        return current.ValueKind == JsonValueKind.Null ? null : current;
    }

    public static string Str(this JsonElement element, string path, string fallback = "—")
    {
        var value = element.Get(path);
        if (value is null)
            return fallback;
        return value.Value.ValueKind switch
        {
            JsonValueKind.String => value.Value.GetString() ?? fallback,
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Number => value.Value.TryGetInt64(out long l) ? l.ToString() : value.Value.GetDouble().ToString("0.#"),
            _ => value.Value.GetRawText()
        };
    }

    public static double? Num(this JsonElement element, string path)
    {
        var value = element.Get(path);
        return value is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;
    }

    public static bool Bool(this JsonElement element, string path) =>
        element.Get(path) is { ValueKind: JsonValueKind.True };

    public static IEnumerable<JsonElement> Items(this JsonElement element, string path) =>
        element.Get(path) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray().ToArray() : [];
}
