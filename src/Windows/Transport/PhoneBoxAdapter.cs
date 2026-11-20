using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace BackThread.Transport;

/// <summary>
/// Parsed PhoneBox QR pairing proposal from:
/// https://ersinkoc.github.io/phonebox/pair/#v=1&amp;pc=...&amp;fp=...&amp;port=...&amp;h=...&amp;c=...
/// Separate transport/provider adapter; never merged into BackThread's BTH1 protocol.
/// </summary>
public sealed record PhoneBoxPairingProposal(
    string PcName,
    string FingerprintHex,
    string CheckCode,
    int Port,
    IReadOnlyList<string> Hosts,
    string OneTimeCode);

public sealed record PhoneBoxPairedSession(
    string PcName,
    string FingerprintHex,
    string CheckCode,
    int Port,
    IReadOnlyList<string> Hosts,
    string BearerToken)
{
    /// <summary>PhoneBox does not implement BackThread-specific PCM audio or native worker jobs.</summary>
    public bool SupportsBackThreadPcm => false;
    public bool SupportsBackThreadWorkers => false;
}

public sealed record PhoneBoxCapabilitySummary(
    string Provider,
    bool Connected,
    bool SupportsBackThreadPcm,
    bool SupportsBackThreadWorkers,
    IReadOnlyList<string> AllowedLanEndpoints);

/// <summary>
/// Optional interoperability client for upstream PhoneBox.Agent (https://github.com/ersinkoc/phonebox, MIT).
/// Enforces exact SHA-256 certificate fingerprint pinning, PhoneBox's documented LAN endpoint allowlist,
/// no Origin header, and explicit authenticated pairing via POST /api/pair/claim.
/// </summary>
public sealed class PhoneBoxAdapter : IDisposable
{
    public static readonly IReadOnlyList<string> SupportedLanPrefixes =
    [
        "/api/pair/claim",
        "/api/status",
        "/api/media",
        "/api/trackpad",
        "/api/work",
        "/api/procs",
        "/api/batteries",
        "/api/rules/pause",
        "/api/phone/battery"
    ];

    private readonly HttpClient http;
    private readonly string host;
    private readonly int port;
    private readonly string expectedFingerprint;
    private string? bearerToken;

    public PhoneBoxAdapter(string host, int port, string expectedFingerprintHex, string? bearerToken = null, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host is required.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (string.IsNullOrWhiteSpace(expectedFingerprintHex) || expectedFingerprintHex.Trim().Length != 64)
            throw new ArgumentException("Expected 64-char SHA-256 hex certificate fingerprint.", nameof(expectedFingerprintHex));

        this.host = host.Trim();
        this.port = port;
        expectedFingerprint = expectedFingerprintHex.Trim().ToLowerInvariant();
        this.bearerToken = bearerToken;

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions =
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                {
                    if (cert is null) return false;
                    using var x509 = new X509Certificate2(cert);
                    string actual = x509.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant();
                    return CryptographicOperations.FixedTimeEquals(
                        Encoding.ASCII.GetBytes(actual),
                        Encoding.ASCII.GetBytes(expectedFingerprint));
                }
            }
        };

        http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{this.host}:{this.port}"),
            Timeout = timeout ?? TimeSpan.FromSeconds(6)
        };
    }

    public static PhoneBoxPairingProposal? ParsePairingUri(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return null;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return null;
        string frag = uri.Fragment.TrimStart('#');
        if (string.IsNullOrWhiteSpace(frag)) return null;

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string part in frag.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            map[part[..eq]] = Uri.UnescapeDataString(part[(eq + 1)..]);
        }

        if (!map.TryGetValue("v", out var ver) || ver != "1") return null;
        if (!map.TryGetValue("fp", out var fp) || fp.Length != 64 || !fp.All(Uri.IsHexDigit)) return null;
        if (!map.TryGetValue("port", out var portText) || !int.TryParse(portText, out int port) || port is < 1 or > 65535) return null;
        if (!map.TryGetValue("c", out var code) || code.Length is < 1 or > 64) return null;
        if (!map.TryGetValue("h", out var hostsRaw)) return null;

        var hosts = hostsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(h => h.Length is > 0 and <= 128)
            .ToArray();
        if (hosts.Length == 0) return null;

        string pcName = map.TryGetValue("pc", out var pc) && !string.IsNullOrWhiteSpace(pc) ? pc : hosts[0];
        string lowerFp = fp.ToLowerInvariant();
        string check = $"{lowerFp[..4]} {lowerFp[4..8]}".ToUpperInvariant();
        return new PhoneBoxPairingProposal(pcName, lowerFp, check, port, hosts, code);
    }

    public static bool IsAllowedLanPath(string path) =>
        SupportedLanPrefixes.Any(prefix => path.Equals(prefix, StringComparison.Ordinal) || path.StartsWith(prefix + "/", StringComparison.Ordinal));

    /// <summary>Trades a one-time pairing code over pinned TLS via PhoneBox's documented POST /api/pair/claim.</summary>
    public async Task<PhoneBoxPairedSession> PairAsync(PhoneBoxPairingProposal proposal, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/pair/claim")
        {
            Content = JsonContent.Create(new { code = proposal.OneTimeCode })
        };
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new UnauthorizedAccessException($"PhoneBox pairing rejected ({(int)resp.StatusCode}).");

        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        string? token = doc.RootElement.TryGetProperty("token", out var t) ? t.GetString()
            : (doc.RootElement.TryGetProperty("key", out var k) ? k.GetString() : null);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidDataException("PhoneBox /api/pair/claim did not return a credential token.");

        bearerToken = token;
        return new PhoneBoxPairedSession(proposal.PcName, proposal.FingerprintHex, proposal.CheckCode, proposal.Port, proposal.Hosts, token);
    }

    public PhoneBoxCapabilitySummary DescribeCapabilities() =>
        new("phonebox", !string.IsNullOrWhiteSpace(bearerToken), SupportsBackThreadPcm: false, SupportsBackThreadWorkers: false, SupportedLanPrefixes);

    public async Task<JsonElement> GetReadOnlyAsync(string path, CancellationToken ct = default)
    {
        EnsureAllowedAndAuthenticated(path);
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"PhoneBox denied {path} ({(int)resp.StatusCode}).");
        resp.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    public async Task<bool> PostControlAsync(string path, object payload, CancellationToken ct = default)
    {
        EnsureAllowedAndAuthenticated(path);
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        using var resp = await http.SendAsync(req, ct);
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new UnauthorizedAccessException($"PhoneBox denied control {path} ({(int)resp.StatusCode}).");
        return resp.IsSuccessStatusCode;
    }

    private void EnsureAllowedAndAuthenticated(string path)
    {
        if (!IsAllowedLanPath(path))
            throw new InvalidOperationException($"Path '{path}' is outside the documented PhoneBox LAN allowlist.");
        if (string.IsNullOrWhiteSpace(bearerToken))
            throw new UnauthorizedAccessException("PhoneBox adapter is not paired/authenticated.");
    }

    public void Dispose() => http.Dispose();
}
