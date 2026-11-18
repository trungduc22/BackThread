using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BackThread.Media;

public sealed record YouTubeMetadata(string VideoId, string Title, string AuthorName, string? ThumbnailUrl, int StartSeconds, string EmbedUrl, bool IsMusic);

/// <summary>
/// Compliant YouTube capability adapter.
/// Inspects canonical URLs and optional official oEmbed metadata without scraping pages, extracting streams,
/// or bypassing playback/background/DRM policies. See docs/YOUTUBE_INTEGRATION.md and docs/DEPENDENCIES.md.
/// </summary>
public sealed class YouTubeProvider(HttpMessageHandler? handler = null) : IDisposable
{
    private static readonly Regex VideoIdRegex = new("^[a-zA-Z0-9_-]{11}$", RegexOptions.Compiled);
    private readonly HttpClient http = new(handler ?? new HttpClientHandler(), disposeHandler: handler == null)
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public static bool IsYouTubeUrl(Uri url)
    {
        string host = url.IdnHost.ToLowerInvariant();
        return host == "youtu.be" || host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal);
    }

    public static bool TryParseVideoId(Uri url, out string videoId, out int startSeconds, out bool isMusic)
    {
        videoId = "";
        startSeconds = 0;
        isMusic = url.IdnHost.Equals("music.youtube.com", StringComparison.OrdinalIgnoreCase);
        if (!IsYouTubeUrl(url)) return false;

        string host = url.IdnHost.ToLowerInvariant();
        string path = url.AbsolutePath.Trim('/');
        string? candidate = null;

        if (host == "youtu.be")
        {
            candidate = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        else
        {
            var query = ParseQuery(url.Query);
            if (query.TryGetValue("v", out string? v)) candidate = v;
            else if (path.StartsWith("shorts/", StringComparison.OrdinalIgnoreCase)) candidate = path["shorts/".Length..].Split('/')[0];
            else if (path.StartsWith("live/", StringComparison.OrdinalIgnoreCase)) candidate = path["live/".Length..].Split('/')[0];
            else if (path.StartsWith("embed/", StringComparison.OrdinalIgnoreCase)) candidate = path["embed/".Length..].Split('/')[0];

            if (query.TryGetValue("t", out string? t) || query.TryGetValue("start", out t))
                startSeconds = ParseTimestampSeconds(t);
        }

        if (host == "youtu.be")
        {
            var query = ParseQuery(url.Query);
            if (query.TryGetValue("t", out string? t)) startSeconds = ParseTimestampSeconds(t);
        }

        if (candidate != null && VideoIdRegex.IsMatch(candidate))
        {
            videoId = candidate;
            return true;
        }
        return false;
    }

    public static MediaCapabilities BuildCapabilities(Uri url, bool allowExperimentalEmbed)
    {
        bool parsed = TryParseVideoId(url, out _, out _, out _);
        string[] limits = allowExperimentalEmbed
            ? [
                "No direct AVPlayer stream extraction or DRM bypass",
                "Experimental official youtube-nocookie iframe embed only works for embed-enabled videos",
                "Background playback in WKWebView is unsupported by iOS/YouTube",
                "Falls back automatically to Windows Mode D remote control + Mode C Live PCM"
            ]
            : [
                "No direct AVPlayer stream extraction, cipher decoding, or DRM bypass",
                "Background audio playback via YouTube web URLs is unsupported",
                "Recommended workflow: play in Windows browser/app with GSMTC Mode D controls + USB Live PCM audio"
            ];

        return new MediaCapabilities(
            SupportsDirectInternet: false,
            SupportsBufferedUsb: false,
            SupportsLivePcm: true,
            SupportsRemoteControl: true,
            SupportsVideo: allowExperimentalEmbed && parsed,
            SupportsSeek: true,
            IsLiveStream: false,
            RequiresInteractiveAuth: false,
            DrmProtected: false,
            TransfersPlaybackToPhone: allowExperimentalEmbed && parsed,
            SurvivesUsbDisconnect: false,
            RequiresDeviceInternet: allowExperimentalEmbed && parsed,
            BackgroundSupported: false,
            RecommendedMode: PlaybackMode.RemoteControl,
            Provider: "youtube",
            Reason: allowExperimentalEmbed && parsed
                ? "Official YouTube metadata + opt-in nocookie embed candidate ready; Mode D + Live PCM fallback preserved"
                : "YouTube web URLs cannot bypass platform playback rules for direct AVPlayer handoff; use Windows browser + Mode D controls + Live PCM",
            Limitations: limits);
    }

    public async Task<YouTubeMetadata?> FetchMetadataAsync(Uri url, CancellationToken ct)
    {
        if (!TryParseVideoId(url, out string id, out int startSeconds, out bool isMusic)) return null;
        string canonicalWatch = $"https://www.youtube.com/watch?v={id}";
        string embedUrl = $"https://www.youtube-nocookie.com/embed/{id}?playsinline=1&rel=0{(startSeconds > 0 ? $"&start={startSeconds}" : "")}";
        string oembed = $"https://www.youtube.com/oembed?url={Uri.EscapeDataString(canonicalWatch)}&format=json";
        try
        {
            using var response = await http.GetAsync(oembed, ct);
            if (!response.IsSuccessStatusCode)
                return new(id, $"YouTube video ({id})", isMusic ? "YouTube Music" : "YouTube", null, startSeconds, embedUrl, isMusic);
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = doc.RootElement;
            string title = root.TryGetProperty("title", out var t) ? t.GetString() ?? $"YouTube ({id})" : $"YouTube ({id})";
            string author = root.TryGetProperty("author_name", out var a) ? a.GetString() ?? "YouTube" : "YouTube";
            string? thumb = root.TryGetProperty("thumbnail_url", out var th) ? th.GetString() : null;
            return new(id, title, author, thumb, startSeconds, embedUrl, isMusic);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(id, $"YouTube video ({id})", isMusic ? "YouTube Music" : "YouTube", null, startSeconds, embedUrl, isMusic);
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;
            result[WebUtility.UrlDecode(part[..eq])] = WebUtility.UrlDecode(part[(eq + 1)..]);
        }
        return result;
    }

    private static int ParseTimestampSeconds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        if (int.TryParse(raw.TrimEnd('s', 'S'), out int plain) && plain >= 0) return plain;
        int total = 0, current = 0;
        foreach (char c in raw.ToLowerInvariant())
        {
            if (char.IsDigit(c)) { current = current * 10 + (c - '0'); continue; }
            total += c switch { 'h' => current * 3600, 'm' => current * 60, 's' => current, _ => 0 };
            current = 0;
        }
        return total;
    }

    public void Dispose() => http.Dispose();
}
