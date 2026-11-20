using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

namespace BackThread.Transport;

public sealed record IncomingTlsSession(
    Stream Stream,
    TcpClient TcpClient,
    TransportRouteKind RouteKind,
    string RemoteAddress,
    string? DeviceId,
    string? DeviceName) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        try { Stream.Dispose(); } catch { }
        try { TcpClient.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Opt-in TLS 1.2/1.3 listener for Wi-Fi / Tailscale iPhone connections.
/// After TLS handshake completes, inspects the initial BTH1 control frame:
/// - "pair.claim": exchanges a single-use QR pairing code for the 32-byte session key over pinned TLS and closes.
/// - "auth.challenge": completes the standard mutual BTH1 HMAC-SHA256 handshake and hands the session to HostRuntime.
/// </summary>
public sealed class LanTlsServer : IAsyncDisposable
{
    private readonly X509Certificate2 certificate;
    private readonly string fingerprint;
    private readonly string pcId;
    private readonly string pcName;
    private readonly byte[] sessionKey;
    private readonly QrPairingService pairing;
    private readonly DeviceRegistry? devices;
    private readonly IPAddress bindAddress;
    private readonly TcpListener listener;
    private readonly SemaphoreSlim handshakeSlots = new(8, 8);
    private readonly Channel<IncomingTlsSession> accepted = Channel.CreateBounded<IncomingTlsSession>(new BoundedChannelOptions(2)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly CancellationTokenSource stop = new();
    private Task? acceptLoop;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string Fingerprint => fingerprint;
    public string PcId => pcId;
    public string HumanCheckCode => LanCertificateStore.GetHumanCheckCode(fingerprint);
    public ChannelReader<IncomingTlsSession> Sessions => accepted.Reader;
    public event Action<string>? Log;

    public LanTlsServer(
        X509Certificate2 certificate,
        byte[] sessionKey,
        QrPairingService pairing,
        int port = 23453,
        IPAddress? bindAddress = null,
        string? pcName = null,
        DeviceRegistry? devices = null)
    {
        this.certificate = certificate;
        this.fingerprint = LanCertificateStore.GetFingerprint(certificate);
        this.pcId = LanCertificateStore.GetPcId(this.fingerprint);
        this.pcName = string.IsNullOrWhiteSpace(pcName) ? Environment.MachineName : pcName.Trim();
        this.sessionKey = sessionKey;
        this.pairing = pairing;
        this.devices = devices;
        this.bindAddress = bindAddress ?? IPAddress.Any;
        listener = new TcpListener(this.bindAddress, port);
    }

    public void Start()
    {
        if (acceptLoop is not null) return;
        listener.Start(8);
        acceptLoop = Task.Run(() => AcceptLoopAsync(stop.Token));
        Log?.Invoke($"Wi-Fi TLS listener active on {bindAddress}:{Port} (check code {HumanCheckCode})");
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(ct);
                tcp.NoDelay = true;
                if (!await handshakeSlots.WaitAsync(0, ct))
                {
                    tcp.Dispose();
                    continue;
                }

                var captured = tcp;
                tcp = null;
                _ = Task.Run(async () =>
                {
                    try { await HandleClientAsync(captured, ct); }
                    finally { handshakeSlots.Release(); }
                }, CancellationToken.None);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                tcp?.Dispose();
                break;
            }
            catch (ObjectDisposedException)
            {
                tcp?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                tcp?.Dispose();
                Log?.Invoke("Wi-Fi TLS accept error: " + ex.Message);
                try { await Task.Delay(250, ct); } catch { break; }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
    {
        bool handedOff = false;
        SslStream? ssl = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));

            ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, timeout.Token);

            var firstFrame = await Wire.ReadAsync(ssl, timeout.Token);
            if (firstFrame.Channel != LogicalChannel.Control)
                throw new InvalidDataException("Expected control frame on TLS connect");

            var msg = Message.Decode(firstFrame.Payload);
            var remoteIp = (tcp.Client.RemoteEndPoint as IPEndPoint)?.Address;
            string remoteText = remoteIp?.ToString() ?? "unknown";
            var route = LanNetworkDiscovery.ClassifyRemoteAddress(remoteIp);

            if (msg.Type == "pair.claim")
            {
                await HandlePairingClaimAsync(ssl, msg, remoteText, route, timeout.Token);
                return;
            }

            if (msg.Type == "auth.challenge")
            {
                string? deviceId = msg.Body.TryGetProperty("deviceId", out var did) ? did.GetString() : null;
                string? deviceName = msg.Body.TryGetProperty("deviceName", out var dname) ? dname.GetString() : null;
                if (deviceId is not null && devices?.IsRevoked(deviceId) == true)
                {
                    await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                        Message.Create("error", new { error = "Device pairing was revoked on PC" }, replyTo: msg.Id).Encode()), timeout.Token);
                    Log?.Invoke($"Rejected revoked device {deviceId} from {remoteText}");
                    return;
                }

                // Prefer the device-specific key issued during QR/USB pairing; fall back to legacy global key
                // only for previously provisioned devices that have not yet received a device-specific credential.
                byte[] expectedKey = (deviceId is not null ? devices?.TryGetDeviceKey(deviceId) : null) ?? sessionKey;
                await Authentication.ClientFromChallengeAsync(ssl, firstFrame, expectedKey, timeout.Token);
                var session = new IncomingTlsSession(ssl, tcp, route, remoteText, deviceId, deviceName);
                if (!accepted.Writer.TryWrite(session))
                {
                    await session.DisposeAsync();
                    return;
                }
                handedOff = true;
                Log?.Invoke($"Authenticated Wi-Fi TLS client ({route}) from {remoteText}");
                return;
            }

            await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                Message.Create("error", new { error = "Unsupported initial TLS control message" }, replyTo: msg.Id).Encode()), timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log?.Invoke("Wi-Fi TLS client rejected: " + ex.Message);
        }
        finally
        {
            if (!handedOff)
            {
                try { ssl?.Dispose(); } catch { }
                try { tcp.Dispose(); } catch { }
            }
        }
    }

    private async Task HandlePairingClaimAsync(SslStream ssl, Message request, string remoteAddress, TransportRouteKind route, CancellationToken ct)
    {
        string? code = request.Body.TryGetProperty("code", out var cEl) ? cEl.GetString() : null;
        string deviceId = request.Body.TryGetProperty("deviceId", out var dEl) && !string.IsNullOrWhiteSpace(dEl.GetString())
            ? dEl.GetString()!.Trim()
            : Guid.NewGuid().ToString("N")[..16];
        string deviceName = request.Body.TryGetProperty("deviceName", out var nEl) && !string.IsNullOrWhiteSpace(nEl.GetString())
            ? nEl.GetString()!.Trim()
            : "iPhone";

        if (!pairing.Claim(code))
        {
            await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                Message.Create("error", new { error = "Invalid, expired, or already-used pairing code" }, replyTo: request.Id).Encode()), ct);
            Log?.Invoke($"Rejected invalid pairing claim from {remoteAddress}");
            return;
        }

        // Issue a device-specific 32-byte secret so revoking or rotating one device never affects others.
        byte[] issuedKey;
        int keyVersion = 1;
        if (devices is not null)
        {
            var issued = devices.IssueDeviceKey(deviceId, deviceName, route.ToString());
            issuedKey = issued.Key;
            keyVersion = issued.Record.KeyVersion;
        }
        else
        {
            issuedKey = sessionKey;
        }

        try
        {
            await Wire.WriteAsync(ssl, new(LogicalChannel.Control, 0, 0,
                Message.Create("pair.ok", new
                {
                    pcId,
                    pcName,
                    fingerprint,
                    checkCode = HumanCheckCode,
                    port = Port,
                    deviceId,
                    keyVersion,
                    sessionKey = Convert.ToHexString(issuedKey).ToLowerInvariant(),
                    hosts = LanNetworkDiscovery.GetReachableAddresses()
                }, replyTo: request.Id).Encode()), ct);
        }
        finally
        {
            if (!ReferenceEquals(issuedKey, sessionKey))
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(issuedKey);
        }
        Log?.Invoke($"Paired device '{deviceName}' ({deviceId}, key v{keyVersion}) via QR over pinned TLS from {remoteAddress}");
    }

    /// <summary>
    /// Helper for TLS clients (tests, simulator) that connects with strict SHA-256 certificate pinning.
    /// </summary>
    public static async Task<(TcpClient Tcp, SslStream Ssl)> ConnectPinnedClientAsync(
        string host,
        int port,
        string expectedFingerprintHex,
        CancellationToken ct)
    {
        string expected = expectedFingerprintHex.Trim().ToLowerInvariant();
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, ct);
            var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, cert, _, _) =>
            {
                if (cert is null) return false;
                using var cert2 = new X509Certificate2(cert);
                string actual = LanCertificateStore.GetFingerprint(cert2);
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, ct);
            return (tcp, ssl);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { listener.Stop(); } catch { }
        accepted.Writer.TryComplete();
        if (acceptLoop is not null)
        {
            try { await acceptLoop; } catch { }
        }
        while (accepted.Reader.TryRead(out var leftover))
            await leftover.DisposeAsync();
        handshakeSlots.Dispose();
        stop.Dispose();
    }
}
