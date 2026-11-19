using System.Net;
using System.Net.Sockets;

namespace BackThread.Host;

public sealed record FeedProxyConfig
{
    public bool Enabled { get; init; }
    public string[] ExtraAllowedHosts { get; init; } = [];
}

public sealed record FeedProxyResult(int StatusCode, string ContentType, byte[] Body, string? Error = null);

/// <summary>
/// SSRF-hardened HTTPS proxy for allowlisted public data feeds (weather, HN, FX rates, etc.)
/// so USB-only iPhones without internet access can still refresh companion cards safely.
/// Blocks redirects, non-HTTPS, custom ports, userinfo, private/loopback/CGNAT/link-local/ULA/NAT64 IPs, and >1 MiB bodies.
/// </summary>
public static class SafeFeedProxy
{
    public const int MaxResponseBytes = 1_048_576;

    public static readonly HashSet<string> DefaultAllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.open-meteo.com",
        "air-quality-api.open-meteo.com",
        "hacker-news.firebaseio.com",
        "api.frankfurter.dev",
        "api.coingecko.com",
        "api.github.com"
    };

    private static readonly Lazy<HttpClient> SharedClient = new(CreateSafeHttpClient);

    private static HttpClient CreateSafeHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            ConnectCallback = async (ctx, token) =>
            {
                var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, token);
                var publicIps = addresses
                    .Where(IsStrictlyPublicAddress)
                    .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .ToArray();
                if (publicIps.Length == 0)
                    throw new HttpRequestException("Refused non-public destination IP for feed host");

                Exception? last = null;
                foreach (var ip in publicIps)
                {
                    Socket? socket = null;
                    try
                    {
                        socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        socket?.Dispose();
                        throw;
                    }
                    catch (Exception ex)
                    {
                        socket?.Dispose();
                        last = ex;
                    }
                }
                throw last ?? new HttpRequestException("Could not connect to feed host");
            }
        };
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public static bool TryValidateUrl(string? rawUrl, IEnumerable<string>? extraAllowedHosts, out Uri? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.Length > 2048) return false;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (IPAddress.TryParse(uri.Host, out _)) return false; // Hostname allowlist only, no raw IP literals

        bool allowed = DefaultAllowedHosts.Contains(uri.Host)
            || (extraAllowedHosts?.Contains(uri.Host, StringComparer.OrdinalIgnoreCase) ?? false);
        if (!allowed) return false;

        target = uri;
        return true;
    }

    public static bool IsStrictlyPublicAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] is 0 or 10 or 127) return false;
            if (b[0] == 100 && (b[1] & 0xC0) == 64) return false; // 100.64.0.0/10 CGNAT / Tailscale
            if (b[0] == 169 && b[1] == 254) return false; // 169.254.0.0/16 link-local / cloud metadata
            if (b[0] == 172 && b[1] is >= 16 and <= 31) return false;
            if (b[0] == 192 && b[1] == 168) return false;
            if (b[0] == 192 && b[1] == 0 && b[2] is 0 or 2) return false;
            if (b[0] == 198 && (b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100))) return false;
            if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;
            if (b[0] >= 224) return false;
            return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.IPv6None)
                || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal || ip.IsIPv6Teredo)
                return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xE0) != 0x20) return false; // global unicast 2000::/3 only
            if (b[0] == 0x20 && b[1] == 0x01)
            {
                if (b[2] == 0x00 && b[3] == 0x00) return false; // Teredo
                if (b[2] == 0x00 && b[3] == 0x02 && b[4] == 0x00 && b[5] == 0x00) return false;
                if (b[2] == 0x0D && b[3] == 0xB8) return false; // documentation
            }
            if (b[0] == 0x3F && b[1] == 0xFF && (b[2] & 0xF0) == 0) return false; // RFC 9637 doc
            if (b[0] == 0x20 && b[1] == 0x02)
                return IsStrictlyPublicAddress(new IPAddress(b.AsSpan(2, 4))); // 6to4
            return true;
        }

        return false;
    }

    public static async Task<FeedProxyResult> FetchAsync(
        string? rawUrl,
        FeedProxyConfig config,
        CancellationToken ct,
        HttpClient? customClient = null)
    {
        if (!config.Enabled)
            return new FeedProxyResult(403, "application/json", [], "Feed proxy is disabled");
        if (!TryValidateUrl(rawUrl, config.ExtraAllowedHosts, out var target) || target is null)
            return new FeedProxyResult(400, "application/json", [], "Disallowed or invalid HTTPS feed URL");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, target);
            req.Headers.UserAgent.ParseAdd("BackThread-FeedProxy/0.3");
            using var res = await (customClient ?? SharedClient.Value).SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            int code = (int)res.StatusCode;
            if (code is >= 300 and < 400)
                return new FeedProxyResult(502, "application/json", [], "Redirects are disallowed by SafeFeedProxy");
            if (res.Content.Headers.ContentLength is { } cl && cl > MaxResponseBytes)
                return new FeedProxyResult(502, "application/json", [], "Feed payload exceeds 1 MiB cap");

            await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
            using var ms = new MemoryStream();
            byte[] buf = new byte[16 * 1024];
            while (true)
            {
                int read = await stream.ReadAsync(buf, cts.Token);
                if (read == 0) break;
                if (ms.Length + read > MaxResponseBytes)
                    return new FeedProxyResult(502, "application/json", [], "Feed payload exceeds 1 MiB cap");
                ms.Write(buf, 0, read);
            }

            string contentType = res.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
            return new FeedProxyResult(code, contentType, ms.ToArray());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new FeedProxyResult(504, "application/json", [], "Feed upstream timed out");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new FeedProxyResult(502, "application/json", [], ex.Message);
        }
    }
}
