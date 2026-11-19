using System.Security.Cryptography;
using System.Text;
using QRCoder;

namespace BackThread.Transport;

public sealed record PairingSessionSnapshot(
    long Attempt,
    string OneTimeCode,
    string PairingUri,
    string HumanCheckCode,
    string Fingerprint,
    string PcId,
    string PcName,
    int Port,
    IReadOnlyList<string> Hosts,
    long ExpiresAtUnixMs,
    string SvgMarkup);

public sealed record WifiProvisionPayload(
    string PcId,
    string PcName,
    string Fingerprint,
    string CheckCode,
    int TlsPort,
    IReadOnlyList<string> Hosts,
    string SessionKeyHex);

/// <summary>
/// Single-use, short-lived QR Wi-Fi pairing manager.
/// The actual 32-byte session key is NEVER placed in the QR URI; instead, the QR carries a 128-bit single-use
/// claim code in the URL fragment (#) along with the pinned TLS certificate SHA-256 fingerprint.
/// The iPhone connects over TLS (verifying the exact fingerprint) and trades the one-time code for the key.
/// </summary>
public sealed class QrPairingService
{
    public const long LifetimeMs = 5 * 60_000;
    public const int MaxFailures = 10;

    private readonly Lock gate = new();
    private byte[]? activeCodeUtf8;
    private long expiresAtUnixMs;
    private int failures;
    private long claims;
    private long attempt;

    public long TotalClaims => Interlocked.Read(ref claims);

    public (string Code, long ExpiresAtUnixMs, long Attempt) Start(long? nowUnixMs = null)
    {
        long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        byte[] random = RandomNumberGenerator.GetBytes(16);
        string code = Convert.ToBase64String(random).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (gate)
        {
            activeCodeUtf8 = Encoding.UTF8.GetBytes(code);
            expiresAtUnixMs = now + LifetimeMs;
            failures = 0;
            return (code, expiresAtUnixMs, ++attempt);
        }
    }

    public PairingSessionSnapshot CreateSession(string pcName, string fingerprint, int tlsPort, IReadOnlyList<string> hosts, long? nowUnixMs = null)
    {
        var (code, exp, currentAttempt) = Start(nowUnixMs);
        string pcId = LanCertificateStore.GetPcId(fingerprint);
        string checkCode = LanCertificateStore.GetHumanCheckCode(fingerprint);
        string uri = BuildPairingUri(pcName, fingerprint, tlsPort, hosts, code);
        string svg = RenderSvg(uri);
        return new PairingSessionSnapshot(
            Attempt: currentAttempt,
            OneTimeCode: code,
            PairingUri: uri,
            HumanCheckCode: checkCode,
            Fingerprint: fingerprint,
            PcId: pcId,
            PcName: pcName,
            Port: tlsPort,
            Hosts: hosts,
            ExpiresAtUnixMs: exp,
            SvgMarkup: svg);
    }

    public void Cancel(long expectedAttempt)
    {
        lock (gate)
        {
            if (expectedAttempt == attempt)
                activeCodeUtf8 = null;
        }
    }

    /// <summary>Returns true at most once for an active, unexpired code using constant-time comparison.</summary>
    public bool Claim(string? code, long? nowUnixMs = null)
    {
        long now = nowUnixMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (gate)
        {
            if (activeCodeUtf8 is null || string.IsNullOrEmpty(code) || code.Length > 64)
                return false;

            if (now > expiresAtUnixMs)
            {
                activeCodeUtf8 = null;
                return false;
            }

            byte[] given = Encoding.UTF8.GetBytes(code);
            if (!CryptographicOperations.FixedTimeEquals(given, activeCodeUtf8))
            {
                if (++failures >= MaxFailures)
                    activeCodeUtf8 = null;
                return false;
            }

            activeCodeUtf8 = null;
            Interlocked.Increment(ref claims);
            return true;
        }
    }

    public static string BuildPairingUri(string pcName, string fingerprint, int tlsPort, IEnumerable<string> hosts, string oneTimeCode)
        => "backthread://pair#v=1"
            + "&pc=" + Uri.EscapeDataString(pcName)
            + "&fp=" + fingerprint.ToLowerInvariant()
            + "&port=" + tlsPort
            + "&h=" + string.Join(",", hosts.Select(Uri.EscapeDataString))
            + "&c=" + Uri.EscapeDataString(oneTimeCode);

    public static WifiProvisionPayload BuildUsbProvisionPayload(string pcName, string fingerprint, int tlsPort, IReadOnlyList<string> hosts, byte[] sessionKey)
        => new(
            PcId: LanCertificateStore.GetPcId(fingerprint),
            PcName: pcName,
            Fingerprint: fingerprint.ToLowerInvariant(),
            CheckCode: LanCertificateStore.GetHumanCheckCode(fingerprint),
            TlsPort: tlsPort,
            Hosts: hosts,
            SessionKeyHex: Convert.ToHexString(sessionKey).ToLowerInvariant());

    public static string RenderSvg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var svg = new SvgQRCode(data);
        return svg.GetGraphic(8, "#000000", "#ffffff", drawQuietZones: true);
    }

    public static byte[] RenderPng(string text, int pixelsPerModule = 8)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
