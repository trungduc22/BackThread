namespace BackThread.Media;

public sealed record HandoffResult(bool Supported, string Provider, string Reason, Uri? Url);
public interface IHandoffProvider { HandoffResult Evaluate(Uri url, bool deviceHasInternet); }
public sealed class DirectMediaProvider : IHandoffProvider
{
    public HandoffResult Evaluate(Uri url, bool deviceHasInternet)
    {
        if (url.Scheme != "https" || !string.IsNullOrEmpty(url.UserInfo)) return new(false, "direct", "Only credential-free HTTPS URLs are accepted", null);
        if (!deviceHasInternet) return new(false, "direct", "Device internet connectivity must be explicitly confirmed; USB does not provide internet tethering", null);
        string host = url.IdnHost.ToLowerInvariant();
        if (host == "youtu.be" || host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal)) return new(false, "youtube", "A web page URL is not a media stream; supported provider and background playback rights are unavailable", null);
        string ext = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        return ext is ".mp3" or ".m4a" or ".aac" or ".wav" or ".mp4" or ".m3u8"
            ? new(true, "direct", "AVPlayer will validate media availability and codec support", url)
            : new(false, "direct", "URL is not a recognized direct media resource", null);
    }
}
