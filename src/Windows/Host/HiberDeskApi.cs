using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.AspNetCore.Http;

namespace BackThread.Host;

internal static class HiberDeskApi
{
    public const string VersionHeader = "X-BackThread-Bridge";
    public const string Version = "1";
    public const string ProtocolVersion = "1.0";

    public static readonly string[] SupportedOperations =
    [
        "ecosystem.capabilities",
        "backthread.status",
        "backthread.audio",
        "backthread.media",
        "backthread.notifications",
        "backthread.events",
        "backthread.spotify",
        "backthread.jobs"
    ];

    public static string PipeName(int port)
    {
        using var identity = WindowsIdentity.GetCurrent();
        return $"BackThread.HiberDesk.v1.{identity.User!.Value}.{port}";
    }

    public static string Bearer(byte[] key) => Convert.ToHexString(
        HMACSHA256.HashData(key, "BackThread:local-api:hiberdesk:v1"u8)).ToLowerInvariant();

    public static bool OwnsWindowsNotification(string source, string displayName) =>
        string.Equals(displayName, "HiberDesk", StringComparison.OrdinalIgnoreCase)
        || string.Equals(displayName, "GhostCode", StringComparison.OrdinalIgnoreCase)
        || string.Equals(source, "trungduc22.hiberdesk", StringComparison.OrdinalIgnoreCase)
        || string.Equals(source, "GhostCode.App", StringComparison.OrdinalIgnoreCase);

    public static bool Allows(HttpRequest request) => (request.Method, request.Path.Value) switch
    {
        ("GET", "/v1/ecosystem/capabilities" or "/v1/status" or "/v1/audio/state" or "/v1/media/status" or "/v1/media/events"
            or "/v1/notifications/status" or "/v1/events" or "/v1/spotify/status"
            or "/v1/spotify/devices" or "/v1/spotify/playlists") => true,
        ("GET", string path) when path.StartsWith("/v1/jobs/", StringComparison.Ordinal) => true,
        ("POST", "/v1/audio/mode" or "/v1/audio/volume" or "/v1/media/capabilities" or "/v1/media/submit"
            or "/v1/media/return" or "/v1/media/cancel" or "/v1/media/command" or "/v1/events"
            or "/v1/spotify/auth/start" or "/v1/spotify/disconnect" or "/v1/spotify/play"
            or "/v1/spotify/pause" or "/v1/spotify/previous" or "/v1/spotify/next"
            or "/v1/spotify/seek" or "/v1/spotify/transfer") => true,
        _ => false
    };
}
