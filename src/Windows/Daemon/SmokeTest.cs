using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using BackThread.Desktop;
using BackThread.Host;
using BackThread.Transport;
using NativeShellKit.Communication.Ipc;
using NativeShellKit.Security;

namespace BackThread.Daemon;

/// <summary>
/// Isolated CI verification of the daemon IPC surface: HMAC handshake, allowlist coverage, real HostRuntime
/// queries, FORBIDDEN for non-allowlisted operations, UNAUTHORIZED for a wrong key, and signed shutdown.
/// Uses a temp data root, an explicit random HMAC key (no DPAPI), and a unique pipe name.
/// </summary>
public static class SmokeTest
{
    public static async Task<int> RunAsync(TextWriter output)
    {
        string root = Path.Combine(Path.GetTempPath(), "backthread-daemon-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var failures = new List<string>();
        try
        {
            var identity = DaemonContract.CreateIdentity(root);
            string keyPath = Path.Combine(root, "session.key");
            Authentication.CreateKey(keyPath);
            var config = new HostConfig
            {
                KeyFile = keyPath,
                Transport = "localhost",
                DevicePort = FreePort(),
                ApiPort = FreePort(),
                EnableHttpApi = false,
                StreamAudio = false,
                Lan = new LanConfig
                {
                    Port = FreePort(),
                    BindAddress = "127.0.0.1",
                    AdvertiseMdns = false,
                    CertificatePath = Path.Combine(root, "lan-tls.pfx"),
                    DeviceRegistryPath = Path.Combine(root, "devices.json")
                }
            };

            using var runtime = new HostRuntime(config);
            byte[] ipcKey = RandomNumberGenerator.GetBytes(32);
            string pipe = "BackThread_Smoke_" + Guid.NewGuid().ToString("N");
            bool shutdownRequested = false;
            var log = new DaemonLog();
            await using var ipc = new DaemonIpcHost(identity, new IpcAuthManager(identity, ipcKey), runtime, log, () => shutdownRequested = true, pipe);
            await ipc.StartAsync().ConfigureAwait(false);

            var clientAuth = new IpcAuthManager(identity, ipcKey);
            var client = new NamedPipeIpcClient(identity, clientAuth, pipe, 3000);

            var caps = await client.HandshakeAsync().ConfigureAwait(false);
            var missing = DaemonOperations.All.Except(caps.SupportedOperations, StringComparer.OrdinalIgnoreCase).ToArray();
            if (missing.Length > 0)
                failures.Add("handshake missing operations: " + string.Join(",", missing));

            foreach (string op in new[]
            {
                DaemonOperations.StatusGet, DaemonOperations.LanStatus, DaemonOperations.OffloadStatus,
                DaemonOperations.RulesStatus, DaemonOperations.ChargeGuardStatus, DaemonOperations.DiagnosticsSnapshot,
                DaemonOperations.LanDevicesList, DaemonOperations.MediaStatus, DaemonOperations.LogsTail
            })
            {
                var resp = await client.SendAsync(op).ConfigureAwait(false);
                if (resp.Operation != "OK" || !clientAuth.VerifyEnvelope(resp))
                    failures.Add($"{op} -> {resp.Operation}: {Truncate(resp.PayloadJson)}");
            }

            var power = await client.SendAsync(DaemonOperations.PowerProfileSet, "{\"profile\":\"Eco\"}").ConfigureAwait(false);
            if (power.Operation != "OK" || runtime.PowerProfile != BackThread.Worker.PowerProfile.Eco)
                failures.Add("power.profile.set Eco failed: " + Truncate(power.PayloadJson));

            var invalid = await client.SendAsync(DaemonOperations.PowerProfileSet, "{\"profile\":\"7\"}").ConfigureAwait(false);
            if (invalid.Operation != "ERROR" || !invalid.PayloadJson.Contains("INVALID_REQUEST", StringComparison.Ordinal))
                failures.Add("numeric enum payload was not rejected");

            var badUrl = await client.SendAsync(DaemonOperations.MediaSubmit, "{\"url\":\"file:///C:/Windows/win.ini\"}").ConfigureAwait(false);
            if (badUrl.Operation != "ERROR")
                failures.Add("file:// media URL was not rejected");

            var forbidden = await client.SendAsync("shell.exec", "{\"cmd\":\"calc\"}").ConfigureAwait(false);
            if (forbidden.Operation != "ERROR" || !forbidden.PayloadJson.Contains("FORBIDDEN", StringComparison.Ordinal))
                failures.Add("non-allowlisted operation was not FORBIDDEN");

            var intruder = new NamedPipeIpcClient(identity, new IpcAuthManager(identity, RandomNumberGenerator.GetBytes(32)), pipe, 3000);
            var unauthorized = await intruder.SendAsync(DaemonOperations.StatusGet).ConfigureAwait(false);
            if (unauthorized.Operation != "ERROR" || !unauthorized.PayloadJson.Contains("UNAUTHORIZED", StringComparison.Ordinal))
                failures.Add("wrong-key client was not UNAUTHORIZED");

            var shutdown = await client.SendAsync(DaemonOperations.DaemonShutdown).ConfigureAwait(false);
            for (int i = 0; i < 40 && !shutdownRequested; i++)
                await Task.Delay(25).ConfigureAwait(false);
            if (shutdown.Operation != "OK" || !shutdownRequested)
                failures.Add("daemon.shutdown did not complete");

            await ipc.StopAsync().ConfigureAwait(false);

            if (failures.Count == 0)
            {
                output.WriteLine($"DAEMON_SMOKE_TEST_PASSED ops={caps.SupportedOperations.Length} protocol={caps.ProtocolVersion} pipe={pipe}");
                return 0;
            }
            foreach (string failure in failures)
                output.WriteLine("FAIL " + failure);
            return 1;
        }
        catch (Exception ex)
        {
            output.WriteLine("FAIL smoke test exception: " + ex);
            return 1;
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* temp cleanup best effort */ }
        }
    }

    private static string Truncate(string value) => value.Length <= 300 ? value : value[..300] + "…";

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    internal static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
