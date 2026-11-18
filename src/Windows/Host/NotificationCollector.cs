using System.Runtime.CompilerServices;
using BackThread.Events;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace BackThread.Host;

/// <summary>Reads only permission previously granted through a packaged, interactive consent flow.</summary>
public sealed class NotificationCollector
{
    public Task<int> CollectAsync(Func<CompanionEvent, CancellationToken, Task> publish, CancellationToken ct) =>
        OperatingSystem.IsWindows() ? CollectWindowsAsync(publish, ct) : Task.FromResult(0);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<int> CollectWindowsAsync(Func<CompanionEvent, CancellationToken, Task> publish, CancellationToken ct)
    {
        var listener = UserNotificationListener.Current;
        if (listener.GetAccessStatus() != UserNotificationListenerAccessStatus.Allowed) return 0;
        var notifications = await listener.GetNotificationsAsync(NotificationKinds.Toast);
        int count = 0;
        foreach (var n in notifications.OrderByDescending(n => n.CreationTime).Take(20))
        {
            ct.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow - n.CreationTime > TimeSpan.FromMinutes(2)) continue;
            var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding is null) continue;
            var text = binding.GetTextElements().Select(t => t.Text).ToArray(); if (text.Length == 0) continue;
            string source = n.AppInfo?.AppUserModelId ?? "windows.notification";
            var e = new CompanionEvent("win-" + n.Id + "-" + n.CreationTime.ToUnixTimeSeconds(), source, text[0][..Math.Min(text[0].Length, 256)], string.Join(" ", text.Skip(1))[..Math.Min(string.Join(" ", text.Skip(1)).Length, 2048)], 1, n.CreationTime.AddMinutes(2), true);
            await publish(e, ct); count++;
        }
        return count;
    }
}
