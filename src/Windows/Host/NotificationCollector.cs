using System.Runtime.CompilerServices;
using BackThread.Events;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace BackThread.Host;

/// <summary>Reads only permission previously granted through a packaged, interactive consent flow.</summary>
public sealed class NotificationCollector
{
    private readonly Dictionary<uint, long> publishedCreationTicks = [];

    public string GetAccessStatus() =>
        OperatingSystem.IsWindows() ? GetWindowsAccessStatus() : "Unavailable";

    public Task<string> RequestAccessAsync() =>
        OperatingSystem.IsWindows() ? RequestWindowsAccessAsync() : Task.FromResult("Unavailable");

    public Task<int> CollectAsync(Func<CompanionEvent, CancellationToken, Task> publish, CancellationToken ct, HostConfig? config = null) =>
        OperatingSystem.IsWindows() ? CollectWindowsAsync(publish, ct, config) : Task.FromResult(0);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GetWindowsAccessStatus()
    {
        try { return UserNotificationListener.Current.GetAccessStatus().ToString(); }
        catch { return "Unavailable"; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string> RequestWindowsAccessAsync()
    {
        try
        {
            var status = await UserNotificationListener.Current.RequestAccessAsync();
            return status.ToString();
        }
        catch (Exception ex)
        {
            return "Unavailable: " + ex.Message;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<int> CollectWindowsAsync(Func<CompanionEvent, CancellationToken, Task> publish, CancellationToken ct, HostConfig? config)
    {
        var listener = UserNotificationListener.Current;
        if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) return 0;
        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        int count = 0;
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(2);
        foreach (uint stale in publishedCreationTicks.Where(kv => kv.Value < cutoff.ToUnixTimeSeconds()).Select(kv => kv.Key).ToArray())
            publishedCreationTicks.Remove(stale);

        foreach (var n in notifications.OrderByDescending(n => n.CreationTime).Take(20))
        {
            ct.ThrowIfCancellationRequested();
            if (n.CreationTime < cutoff) continue;
            long createdSec = n.CreationTime.ToUnixTimeSeconds();
            if (publishedCreationTicks.TryGetValue(n.Id, out long prevSec) && prevSec == createdSec) continue;

            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) continue;
            var text = binding.GetTextElements().Select(t => t.Text).ToArray();
            if (text.Length == 0) continue;

            string source = n.AppInfo?.AppUserModelId ?? "windows.notification";
            string appName = n.AppInfo?.DisplayInfo.DisplayName ?? source;
            int priority = DeterminePriority(source, appName, config);
            string body = string.Join(" ", text.Skip(1));
            var e = new CompanionEvent(
                Id: "win-" + n.Id + "-" + createdSec,
                Source: source[..Math.Min(source.Length, 128)],
                Title: text[0][..Math.Min(text[0].Length, 256)],
                Body: body[..Math.Min(body.Length, 2048)],
                Priority: priority,
                ExpiresUtc: n.CreationTime.AddMinutes(2),
                Sensitive: config?.HideSensitive ?? true,
                AppName: appName[..Math.Min(appName.Length, 128)],
                Category: "windows.toast",
                CreatedUtc: n.CreationTime,
                FullscreenEligible: priority >= 3);
            await publish(e, ct);
            publishedCreationTicks[n.Id] = createdSec;
            count++;
        }
        return count;
    }

    internal static int DeterminePriority(string source, string appName, HostConfig? config)
    {
        if (config != null)
        {
            if (config.CriticalNotificationSources.Any(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase) || string.Equals(s, appName, StringComparison.OrdinalIgnoreCase)))
                return 3;
            if (config.HighNotificationSources.Any(s => string.Equals(s, source, StringComparison.OrdinalIgnoreCase) || string.Equals(s, appName, StringComparison.OrdinalIgnoreCase)))
                return 2;
        }
        return 1;
    }
}
