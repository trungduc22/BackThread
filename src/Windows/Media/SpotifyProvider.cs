using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BackThread.Media;

public sealed record SpotifyTokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresUtc, string Scope);
public sealed record SpotifyPlaybackState(bool IsPlaying, string Title, string Artist, string Album, string? TrackUri, double PositionSeconds, double DurationSeconds, string? ArtworkUrl, string? DeviceId, string? DeviceName, bool CanControl);
public sealed record SpotifyDevice(string Id, string Name, string Type, bool IsActive, int VolumePercent);
public sealed record SpotifyPlaylist(string Id, string Name, string Uri, int TrackCount);
public sealed record SpotifyAuthorizationStart(string AuthorizeUrl, string State, string RedirectUri);

public interface ISpotifyTokenStore
{
    SpotifyTokenSet? Load();
    void Save(SpotifyTokenSet token);
    void Clear();
}

public sealed class InMemorySpotifyTokenStore : ISpotifyTokenStore
{
    private SpotifyTokenSet? token;
    public SpotifyTokenSet? Load() => token;
    public void Save(SpotifyTokenSet token) => this.token = token;
    public void Clear() => token = null;
}

public sealed class LocalSpotifyTokenStore(string path) : ISpotifyTokenStore
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int cbData; public nint pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob pDataIn, string? szDataDescr, nint pOptionalEntropy, nint pvReserved, nint pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob pDataIn, nint ppszDataDescr, nint pOptionalEntropy, nint pvReserved, nint pPromptStruct, int dwFlags, out DataBlob pDataOut);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);

    public SpotifyTokenSet? Load()
    {
        if (!File.Exists(path)) return null;
        try
        {
            byte[] raw = File.ReadAllBytes(path);
            byte[] decoded = OperatingSystem.IsWindows() ? UnprotectWindows(raw) : raw;
            return JsonSerializer.Deserialize<SpotifyTokenSet>(decoded, BackThread.Transport.Message.Json);
        }
        catch { return null; }
    }

    public void Save(SpotifyTokenSet token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(token, BackThread.Transport.Message.Json);
        byte[] encoded = OperatingSystem.IsWindows() ? ProtectWindows(json) : json;
        File.WriteAllBytes(path, encoded);
    }

    public void Clear() { if (File.Exists(path)) File.Delete(path); }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] ProtectWindows(byte[] data)
    {
        nint unmanaged = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, unmanaged, data.Length);
            var input = new DataBlob { cbData = data.Length, pbData = unmanaged };
            if (!CryptProtectData(ref input, "BackThread.Spotify", 0, 0, 0, 1, out var output))
                throw new IOException("Windows DPAPI protection failed");
            try
            {
                byte[] copy = new byte[output.cbData];
                Marshal.Copy(output.pbData, copy, 0, output.cbData);
                return copy;
            }
            finally { LocalFree(output.pbData); }
        }
        finally { Marshal.FreeHGlobal(unmanaged); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] UnprotectWindows(byte[] data)
    {
        nint unmanaged = Marshal.AllocHGlobal(data.Length);
        try
        {
            Marshal.Copy(data, 0, unmanaged, data.Length);
            var input = new DataBlob { cbData = data.Length, pbData = unmanaged };
            if (!CryptUnprotectData(ref input, 0, 0, 0, 0, 1, out var output))
                throw new IOException("Windows DPAPI unprotection failed");
            try
            {
                byte[] copy = new byte[output.cbData];
                Marshal.Copy(output.pbData, copy, 0, output.cbData);
                return copy;
            }
            finally { LocalFree(output.pbData); }
        }
        finally { Marshal.FreeHGlobal(unmanaged); }
    }
}

public sealed class SpotifyApiException(int statusCode, string code, string message, TimeSpan? retryAfter = null) : IOException(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Official Spotify Web API OAuth PKCE client. Never embeds client secrets, scrapes DRM streams,
/// or claims native audio extraction on iOS. All playback control runs via authorized Spotify Connect APIs (Mode D).
/// </summary>
public sealed class SpotifyProvider : IDisposable
{
    private readonly ISpotifyTokenStore store;
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly object authGate = new();
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private string? pendingState, pendingVerifier, pendingClientId, pendingRedirectUri;
    private DateTimeOffset rateLimitedUntil = DateTimeOffset.MinValue;
    public string? ClientId { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.spotify.com";
    public string AccountsBaseUrl { get; set; } = "https://accounts.spotify.com";

    public SpotifyProvider(ISpotifyTokenStore? store = null, HttpMessageHandler? handler = null, string? clientId = null)
    {
        this.store = store ?? new InMemorySpotifyTokenStore();
        ownsHttp = handler == null;
        http = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: ownsHttp) { Timeout = TimeSpan.FromSeconds(10) };
        ClientId = clientId;
    }

    public bool IsAuthenticated => store.Load() != null;

    public static bool IsSpotifyUrl(Uri url)
    {
        string host = url.IdnHost.ToLowerInvariant();
        return url.Scheme == "spotify" || host == "spotify.com" || host.EndsWith(".spotify.com", StringComparison.Ordinal);
    }

    public static MediaCapabilities BuildCapabilities() => new(
        SupportsDirectInternet: false,
        SupportsBufferedUsb: false,
        SupportsLivePcm: true,
        SupportsRemoteControl: true,
        SupportsVideo: false,
        SupportsSeek: true,
        IsLiveStream: false,
        RequiresInteractiveAuth: true,
        DrmProtected: true,
        TransfersPlaybackToPhone: false,
        SurvivesUsbDisconnect: false,
        RequiresDeviceInternet: false,
        BackgroundSupported: false,
        RecommendedMode: PlaybackMode.RemoteControl,
        Provider: "spotify",
        Reason: "Spotify streams are DRM-protected; BackThread controls Spotify on Windows/Connect via official OAuth PKCE Web API (Mode D) + Live PCM (Mode C)",
        Limitations: [
            "No direct independent AVPlayer audio playback for Spotify DRM tracks",
            "Playback control (play/pause/next/previous/seek/transfer) requires Spotify Premium account and authorized OAuth PKCE session",
            "Track metadata, album art, and active device reading work after user authorization"
        ]);

    public SpotifyAuthorizationStart BeginAuthorization(string clientId, string redirectUri, string scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing playlist-read-private")
    {
        if (string.IsNullOrWhiteSpace(clientId)) throw new InvalidDataException("Configure a Spotify Client ID before connecting Spotify");
        byte[] verifierBytes = RandomNumberGenerator.GetBytes(64);
        string verifier = Base64Url(verifierBytes);
        string challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        lock (authGate)
        {
            ClientId = clientId.Trim();
            pendingClientId = ClientId;
            pendingRedirectUri = redirectUri;
            pendingVerifier = verifier;
            pendingState = state;
        }
        string url = $"{AccountsBaseUrl.TrimEnd('/')}/authorize" +
            $"?client_id={Uri.EscapeDataString(ClientId)}" +
            $"&response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&code_challenge_method=S256" +
            $"&code_challenge={Uri.EscapeDataString(challenge)}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&scope={Uri.EscapeDataString(scopes)}";
        return new(url, state, redirectUri);
    }

    public async Task<SpotifyTokenSet> CompleteAuthorizationAsync(string code, string state, CancellationToken ct)
    {
        string? expectedState, verifier, clientId, redirectUri;
        lock (authGate)
        {
            expectedState = pendingState; verifier = pendingVerifier; clientId = pendingClientId ?? ClientId; redirectUri = pendingRedirectUri;
        }
        if (string.IsNullOrEmpty(expectedState) || string.IsNullOrEmpty(verifier) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(redirectUri))
            throw new InvalidOperationException("No pending Spotify PKCE authorization session");
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(state ?? ""), Encoding.ASCII.GetBytes(expectedState)))
            throw new UnauthorizedAccessException("Spotify OAuth state verification failed");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = verifier
        });
        var token = await ExchangeTokenAsync(form, ct);
        lock (authGate) { pendingState = pendingVerifier = null; }
        store.Save(token);
        return token;
    }

    public void Disconnect() => store.Clear();

    public async Task<SpotifyPlaybackState?> GetPlaybackStateAsync(CancellationToken ct)
    {
        using var response = await SendApiAsync(HttpMethod.Get, "/v1/me/player", null, ct);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;
        bool playing = root.TryGetProperty("is_playing", out var ip) && ip.GetBoolean();
        double pos = root.TryGetProperty("progress_ms", out var pm) && pm.ValueKind == JsonValueKind.Number ? pm.GetDouble() / 1000.0 : 0;
        string title = "", artist = "", album = "";
        string? trackUri = null, artworkUrl = null;
        double duration = 0;
        if (root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object)
        {
            title = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            trackUri = item.TryGetProperty("uri", out var u) ? u.GetString() : null;
            duration = item.TryGetProperty("duration_ms", out var dm) && dm.ValueKind == JsonValueKind.Number ? dm.GetDouble() / 1000.0 : 0;
            if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
                artist = string.Join(", ", artists.EnumerateArray().Select(a => a.TryGetProperty("name", out var an) ? an.GetString() : null).Where(s => !string.IsNullOrEmpty(s)));
            if (item.TryGetProperty("album", out var alb) && alb.ValueKind == JsonValueKind.Object)
            {
                album = alb.TryGetProperty("name", out var aln) ? aln.GetString() ?? "" : "";
                if (alb.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array && imgs.GetArrayLength() > 0)
                    artworkUrl = imgs[0].TryGetProperty("url", out var iu) ? iu.GetString() : null;
            }
        }
        string? deviceId = null, deviceName = null;
        if (root.TryGetProperty("device", out var dev) && dev.ValueKind == JsonValueKind.Object)
        {
            deviceId = dev.TryGetProperty("id", out var did) ? did.GetString() : null;
            deviceName = dev.TryGetProperty("name", out var dn) ? dn.GetString() : null;
        }
        return new(playing, title, artist, album, trackUri, pos, duration, artworkUrl, deviceId, deviceName, !string.IsNullOrEmpty(deviceId));
    }

    public Task PlayAsync(string? contextOrTrackUri = null, string? deviceId = null, CancellationToken ct = default)
    {
        object? payload = string.IsNullOrWhiteSpace(contextOrTrackUri) ? null :
            contextOrTrackUri.Contains(":track:", StringComparison.OrdinalIgnoreCase)
                ? new { uris = new[] { contextOrTrackUri } }
                : new { context_uri = contextOrTrackUri };
        string path = string.IsNullOrWhiteSpace(deviceId) ? "/v1/me/player/play" : $"/v1/me/player/play?device_id={Uri.EscapeDataString(deviceId)}";
        return SendCommandAsync(HttpMethod.Put, path, payload, ct);
    }

    public Task PauseAsync(CancellationToken ct = default) => SendCommandAsync(HttpMethod.Put, "/v1/me/player/pause", null, ct);
    public Task NextAsync(CancellationToken ct = default) => SendCommandAsync(HttpMethod.Post, "/v1/me/player/next", null, ct);
    public Task PreviousAsync(CancellationToken ct = default) => SendCommandAsync(HttpMethod.Post, "/v1/me/player/previous", null, ct);
    public Task SeekAsync(double seconds, CancellationToken ct = default) => SendCommandAsync(HttpMethod.Put, $"/v1/me/player/seek?position_ms={(long)Math.Max(0, seconds * 1000)}", null, ct);
    public Task AddToQueueAsync(string trackUri, CancellationToken ct = default) => SendCommandAsync(HttpMethod.Post, $"/v1/me/player/queue?uri={Uri.EscapeDataString(trackUri)}", null, ct);
    public Task TransferPlaybackAsync(string deviceId, bool play = true, CancellationToken ct = default) => SendCommandAsync(HttpMethod.Put, "/v1/me/player", new { device_ids = new[] { deviceId }, play }, ct);

    public async Task<SpotifyDevice[]> GetDevicesAsync(CancellationToken ct = default)
    {
        using var response = await SendApiAsync(HttpMethod.Get, "/v1/me/player/devices", null, ct);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("devices", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray().Select(d => new SpotifyDevice(
            d.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            d.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            d.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "",
            d.TryGetProperty("is_active", out var act) && act.GetBoolean(),
            d.TryGetProperty("volume_percent", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0)).ToArray();
    }

    public async Task<SpotifyPlaylist[]> GetPlaylistsAsync(int limit = 20, CancellationToken ct = default)
    {
        using var response = await SendApiAsync(HttpMethod.Get, $"/v1/me/playlists?limit={Math.Clamp(limit, 1, 50)}", null, ct);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("items", out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        return arr.EnumerateArray().Select(p => new SpotifyPlaylist(
            p.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
            p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
            p.TryGetProperty("uri", out var u) ? u.GetString() ?? "" : "",
            p.TryGetProperty("tracks", out var tr) && tr.TryGetProperty("total", out var tot) ? tot.GetInt32() : 0)).ToArray();
    }

    private async Task SendCommandAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendApiAsync(method, path, body, ct);
    }

    private async Task<HttpResponseMessage> SendApiAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (DateTimeOffset.UtcNow < rateLimitedUntil)
            throw new SpotifyApiException(429, "rate_limited", "Spotify rate limit active; wait before retrying", rateLimitedUntil - DateTimeOffset.UtcNow);

        var token = await EnsureFreshTokenAsync(ct);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(method, ApiBaseUrl.TrimEnd('/') + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body, BackThread.Transport.Message.Json), Encoding.UTF8, "application/json");
            var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return res;

            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0 && !string.IsNullOrEmpty(token.RefreshToken))
            {
                res.Dispose();
                token = await RefreshAsync(token.RefreshToken, ct, token.AccessToken);
                continue;
            }

            TimeSpan? retry = res.Headers.RetryAfter?.Delta;
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
                rateLimitedUntil = DateTimeOffset.UtcNow + (retry ?? TimeSpan.FromSeconds(5));
            string detail = await res.Content.ReadAsStringAsync(ct);
            int code = (int)res.StatusCode;
            res.Dispose();
            throw code switch
            {
                401 => new SpotifyApiException(401, "unauthorized", "Spotify session expired; reconnect Spotify via PKCE"),
                403 => new SpotifyApiException(403, "premium_or_scope_required", "Spotify Premium or missing playback scope required for remote control"),
                404 => new SpotifyApiException(404, "no_active_device", "No active Spotify playback device found"),
                429 => new SpotifyApiException(429, "rate_limited", "Spotify API rate limit reached", retry),
                _ => new SpotifyApiException(code, "api_error", $"Spotify API error {code}: {(detail.Length > 180 ? detail[..180] : detail)}")
            };
        }
        throw new SpotifyApiException(401, "unauthorized", "Spotify authentication failed");
    }

    private async Task<SpotifyTokenSet> EnsureFreshTokenAsync(CancellationToken ct)
    {
        var token = store.Load() ?? throw new SpotifyApiException(401, "not_authenticated", "Spotify is not connected");
        if (DateTimeOffset.UtcNow < token.ExpiresUtc.AddSeconds(-60)) return token;
        return string.IsNullOrEmpty(token.RefreshToken) ? token : await RefreshAsync(token.RefreshToken, ct, token.AccessToken);
    }

    private async Task<SpotifyTokenSet> RefreshAsync(string refreshToken, CancellationToken ct, string? failedAccessToken = null)
    {
        await refreshGate.WaitAsync(ct);
        try
        {
            var latest = store.Load();
            if (latest != null && DateTimeOffset.UtcNow < latest.ExpiresUtc.AddSeconds(-60) && latest.AccessToken != failedAccessToken)
                return latest;

            string effectiveRefresh = latest?.RefreshToken is { Length: > 0 } lr ? lr : refreshToken;
            string clientId = ClientId ?? throw new SpotifyApiException(401, "missing_client_id", "Spotify Client ID is required for token refresh");
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = effectiveRefresh,
                ["client_id"] = clientId
            });
            var refreshed = await ExchangeTokenAsync(form, ct, effectiveRefresh);
            store.Save(refreshed);
            return refreshed;
        }
        finally
        {
            refreshGate.Release();
        }
    }

    private async Task<SpotifyTokenSet> ExchangeTokenAsync(FormUrlEncodedContent form, CancellationToken ct, string? fallbackRefresh = null)
    {
        using var res = await http.PostAsync(AccountsBaseUrl.TrimEnd('/') + "/api/token", form, ct);
        string text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new SpotifyApiException((int)res.StatusCode, "token_exchange_failed", $"Spotify token exchange failed ({(int)res.StatusCode})");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        string access = root.GetProperty("access_token").GetString() ?? throw new InvalidDataException("Missing access_token");
        string refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() ?? fallbackRefresh ?? "" : fallbackRefresh ?? "";
        int expires = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
        string scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() ?? "" : "";
        return new(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expires), scope);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() { if (ownsHttp) http.Dispose(); refreshGate.Dispose(); }
}
