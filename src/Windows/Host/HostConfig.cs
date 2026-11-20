using System.Text.Json;
using BackThread.Media;
using BackThread.Transport;
using BackThread.Worker;

namespace BackThread.Host;

public sealed record AuthorizedAction(string Id, string Title, string Kind, string? Executable = null, string[]? Arguments = null);

public sealed record LanConfig
{
    public bool Enabled { get; init; }
    public int Port { get; init; } = 23453;
    public string BindAddress { get; init; } = "0.0.0.0";
    public string? CertificatePath { get; init; }
    public string? DeviceRegistryPath { get; init; }
    public bool AdvertiseMdns { get; init; } = true;
    public bool AutoProvisionOverUsb { get; init; } = true;
}

public sealed record HostConfig
{
    public string KeyFile { get; init; } = "config/session.key";
    /// <summary>auto | usb | wifi | localhost</summary>
    public string Transport { get; init; } = "usb";
    public int DevicePort { get; init; } = 23451;
    public string? DeviceSerial { get; init; }
    public int ApiPort { get; init; } = 23452;
    public bool EnableHttpApi { get; init; } = true;
    public bool EnableHiberDeskBridge { get; init; }
    public PowerProfile PowerProfile { get; init; } = PowerProfile.Balanced;
    public LanConfig Lan { get; init; } = new();
    public OffloadPolicyConfig Offload { get; init; } = new();
    public AutomationRuleConfig[] Rules { get; init; } = [];
    public ChargeGuardConfig ChargeGuard { get; init; } = new();
    public FeedProxyConfig FeedProxy { get; init; } = new();
    public DevMonitorConfig DevMonitor { get; init; } = new();
    public bool StreamAudio { get; init; } = true;
    public AudioOutputMode AudioOutputMode { get; init; } = AudioOutputMode.Both;
    public float AudioGain { get; init; } = 1;
    public bool FixtureAudio { get; init; }
    public bool DeviceHasInternet { get; init; }
    public bool HideSensitive { get; init; } = true;
    public bool CollectWindowsNotifications { get; init; }
    public bool AllowExperimentalYouTubeEmbed { get; init; }
    public string DefaultQualityCap { get; init; } = "auto";
    public bool KeepScreenAwakeDuringVideo { get; init; } = true;
    public bool PauseWindowsOnConfirmedHandoff { get; init; } = true;
    public bool PauseMediaOnCriticalNotification { get; init; }
    public string? SpotifyClientId { get; init; }
    public string SpotifyRedirectUri { get; init; } = "http://127.0.0.1:23452/v1/spotify/callback";
    public string? SpotifyTokenPath { get; init; }
    public string[] HighNotificationSources { get; init; } = [];
    public string[] CriticalNotificationSources { get; init; } = [];
    public string[] BlockedSources { get; init; } = [];
    public AuthorizedAction[] Actions { get; init; } = [new("media.toggle", "Play / Pause", "media.toggle")];

    public static HostConfig Load(string path)
    {
        var c = JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(path), Message.Json) ?? throw new InvalidDataException("Invalid configuration");
        if (c.Transport is not ("auto" or "usb" or "wifi" or "localhost") || c.DevicePort is < 1024 or > 65535 || c.ApiPort is < 1024 or > 65535 || c.ApiPort == c.DevicePort || c.Actions.Length > 32)
            throw new InvalidDataException("Invalid endpoint or actions");
        if (c.Lan.Port is < 1024 or > 65535 || c.Lan.Port == c.ApiPort || c.Lan.Port == c.DevicePort)
            throw new InvalidDataException("Invalid lan.port");
        if (!Enum.IsDefined(c.PowerProfile))
            throw new InvalidDataException("powerProfile must be Eco, Balanced, or Performance");
        if (!Enum.IsDefined(c.AudioOutputMode))
            throw new InvalidDataException("audioOutputMode must be PcOnly, PhoneOnly, or Both");
        if (!float.IsFinite(c.AudioGain) || c.AudioGain < 0 || c.AudioGain > 1)
            throw new InvalidDataException("audioGain must be between 0 and 1");
        if (c.DefaultQualityCap is not ("auto" or "720p" or "480p" or "audio-only"))
            throw new InvalidDataException("defaultQualityCap must be auto, 720p, 480p, or audio-only");
        if (c.Rules.Length > 64)
            throw new InvalidDataException("At most 64 automation rules are supported");
        return c;
    }
}
