using System.Text.Json;
using BackThread.Transport;

namespace BackThread.Host;

public sealed record AuthorizedAction(string Id, string Title, string Kind, string? Executable = null, string[]? Arguments = null);
public sealed record HostConfig
{
    public string KeyFile { get; init; } = "config/session.key";
    public string Transport { get; init; } = "usb";
    public int DevicePort { get; init; } = 23451;
    public string? DeviceSerial { get; init; }
    public int ApiPort { get; init; } = 23452;
    public bool StreamAudio { get; init; } = true;
    public bool FixtureAudio { get; init; }
    public bool DeviceHasInternet { get; init; }
    public bool HideSensitive { get; init; } = true;
    public bool CollectWindowsNotifications { get; init; }
    public string[] BlockedSources { get; init; } = [];
    public AuthorizedAction[] Actions { get; init; } = [new("media.toggle", "Play / Pause", "media.toggle")];
    public static HostConfig Load(string path)
    {
        var c = JsonSerializer.Deserialize<HostConfig>(File.ReadAllText(path), Message.Json) ?? throw new InvalidDataException("Invalid configuration");
        if (c.Transport is not ("usb" or "localhost") || c.DevicePort is < 1024 or > 65535 || c.ApiPort is < 1024 or > 65535 || c.ApiPort == c.DevicePort || c.Actions.Length > 32) throw new InvalidDataException("Invalid endpoint or actions");
        return c;
    }
}
