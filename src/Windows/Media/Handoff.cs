using System.Net.Http.Headers;

namespace BackThread.Media;

public sealed record HandoffResult(bool Supported, string Provider, string Reason, Uri? Url, MediaCapabilities? Capabilities = null, string? MimeType = null);
public interface IHandoffProvider { HandoffResult Evaluate(Uri url, bool deviceHasInternet); }

public interface IMediaUrlProber
{
    Task<string?> ProbeContentTypeAsync(Uri url, CancellationToken ct);
}

public sealed class HttpMediaUrlProber(HttpMessageHandler? handler = null) : IMediaUrlProber, IDisposable
{
    private readonly HttpClient http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = true }, disposeHandler: handler == null)
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    public async Task<string?> ProbeContentTypeAsync(Uri url, CancellationToken ct)
    {
        if (url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo)) return null;
        try
        {
            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
                return response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if ((int)response.StatusCode is 405 or 501)
            {
                using var get = new HttpRequestMessage(HttpMethod.Get, url);
                get.Headers.Range = new RangeHeaderValue(0, 0);
                using var fallback = await http.SendAsync(get, HttpCompletionOption.ResponseHeadersRead, ct);
                if (fallback.IsSuccessStatusCode)
                    return fallback.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Keep probe failures non-fatal so caller can report capability clearly or fall back.
        }
        return null;
    }

    public void Dispose() => http.Dispose();
}

public sealed class DirectMediaProvider(IMediaUrlProber? prober = null) : IHandoffProvider
{
    private static readonly Dictionary<string, (string Mime, bool Video, bool Live)> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = ("video/mp4", true, false),
        [".m4v"] = ("video/x-m4v", true, false),
        [".mov"] = ("video/quicktime", true, false),
        [".m3u8"] = ("application/vnd.apple.mpegurl", true, false),
        [".mp3"] = ("audio/mpeg", false, false),
        [".m4a"] = ("audio/mp4", false, false),
        [".aac"] = ("audio/aac", false, false),
        [".wav"] = ("audio/wav", false, false),
        [".flac"] = ("audio/flac", false, false),
    };

    private static readonly Dictionary<string, (string Mime, bool Video, bool Live)> MimeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["video/mp4"] = ("video/mp4", true, false),
        ["video/quicktime"] = ("video/quicktime", true, false),
        ["video/x-m4v"] = ("video/x-m4v", true, false),
        ["application/vnd.apple.mpegurl"] = ("application/vnd.apple.mpegurl", true, false),
        ["application/x-mpegurl"] = ("application/vnd.apple.mpegurl", true, false),
        ["audio/mpeg"] = ("audio/mpeg", false, false),
        ["audio/mp3"] = ("audio/mpeg", false, false),
        ["audio/mp4"] = ("audio/mp4", false, false),
        ["audio/aac"] = ("audio/aac", false, false),
        ["audio/aacp"] = ("audio/aac", false, true),
        ["audio/wav"] = ("audio/wav", false, false),
        ["audio/x-wav"] = ("audio/wav", false, false),
        ["audio/flac"] = ("audio/flac", false, false),
    };

    public HandoffResult Evaluate(Uri url, bool deviceHasInternet) => Inspect(url, deviceHasInternet, null);

    public HandoffResult Inspect(Uri url, bool deviceHasInternet, string? contentTypeHint = null)
    {
        if (url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo))
        {
            var caps = new MediaCapabilities(false, false, true, false, false, false, false, false, false, false, false, true, false, PlaybackMode.LivePcm, "direct", "Only credential-free HTTPS URLs are accepted for independent device playback", ["Non-HTTPS URL or embedded credentials rejected"]);
            return new(false, "direct", caps.Reason, null, caps);
        }

        string host = url.IdnHost.ToLowerInvariant();
        if (host == "youtu.be" || host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal))
        {
            var caps = YouTubeProvider.BuildCapabilities(url, allowExperimentalEmbed: false);
            return new(false, "youtube", caps.Reason, null, caps);
        }
        if (host == "spotify.com" || host.EndsWith(".spotify.com", StringComparison.Ordinal))
        {
            var caps = SpotifyProvider.BuildCapabilities();
            return new(false, "spotify", caps.Reason, null, caps);
        }

        if (!deviceHasInternet)
        {
            var caps = new MediaCapabilities(false, false, true, false, false, false, false, false, false, false, false, true, false, PlaybackMode.LivePcm, "direct", "Device internet connectivity must be explicitly confirmed; USB does not provide internet tethering", ["Enable deviceHasInternet when iPhone has independent Wi-Fi"]);
            return new(false, "direct", caps.Reason, null, caps);
        }

        string ext = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        if (ExtensionMap.TryGetValue(ext, out var extInfo))
        {
            bool live = extInfo.Live || url.AbsolutePath.Contains("live", StringComparison.OrdinalIgnoreCase);
            var caps = MediaCapabilities.DirectUrl(extInfo.Mime, extInfo.Video, live, deviceHasInternet, "AVPlayer will validate media availability, codecs, and time advancement");
            return new(true, "direct", caps.Reason, url, caps, extInfo.Mime);
        }

        if (!string.IsNullOrWhiteSpace(contentTypeHint) && MimeMap.TryGetValue(contentTypeHint.Trim(), out var mimeInfo))
        {
            bool live = mimeInfo.Live || url.AbsolutePath.Contains("live", StringComparison.OrdinalIgnoreCase);
            var caps = MediaCapabilities.DirectUrl(mimeInfo.Mime, mimeInfo.Video, live, deviceHasInternet, $"Resolved direct media Content-Type ({mimeInfo.Mime}) for AVPlayer");
            return new(true, "direct", caps.Reason, url, caps, mimeInfo.Mime);
        }

        var unsupported = new MediaCapabilities(false, false, true, false, false, false, false, false, false, false, false, true, false, PlaybackMode.LivePcm, "direct", "URL is not a recognized direct media resource", ["Unsupported extension and no supported Content-Type resolved; use Live PCM or Mode B"]);
        return new(false, "direct", unsupported.Reason, null, unsupported);
    }

    public async Task<HandoffResult> EvaluateAsync(Uri url, bool deviceHasInternet, CancellationToken ct)
    {
        var initial = Inspect(url, deviceHasInternet, null);
        if (initial.Supported || prober is null || !deviceHasInternet || initial.Provider != "direct") return initial;
        if (url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo)) return initial;
        string? contentType = await prober.ProbeContentTypeAsync(url, ct);
        return contentType is null ? initial : Inspect(url, deviceHasInternet, contentType);
    }
}
