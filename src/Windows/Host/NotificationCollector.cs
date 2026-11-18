using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackThread.Events;
using Windows.ApplicationModel;
using Windows.Storage.Streams;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace BackThread.Host;

public sealed record NotificationCollectorDiagnostics(
    string AccessStatus,
    bool CollectEnabled,
    bool HasPackageIdentity,
    long PollCount,
    long PublishedCount,
    string? LastError,
    string Guidance);

/// <summary>Reads only permission previously granted through a packaged, interactive consent flow.</summary>
public sealed class NotificationCollector
{
    private readonly Dictionary<uint, long> publishedCreationTicks = [];
    private long pollCount;
    private long publishedCount;
    private string? lastError;

    public string GetAccessStatus() =>
        OperatingSystem.IsWindows() ? GetWindowsAccessStatus() : "Unavailable";

    public Task<string> RequestAccessAsync() =>
        OperatingSystem.IsWindows() ? RequestWindowsAccessAsync() : Task.FromResult("Unavailable");

    public Task<int> CollectAsync(Func<CompanionEvent, CancellationToken, Task> publish, CancellationToken ct, HostConfig? config = null)
    {
        Interlocked.Increment(ref pollCount);
        return OperatingSystem.IsWindows() ? CollectWindowsAsync(publish, ct, config) : Task.FromResult(0);
    }

    public NotificationCollectorDiagnostics GetDiagnostics(bool collectEnabled)
    {
        bool packaged = OperatingSystem.IsWindows() && HasWindowsPackageIdentity();
        string status = GetAccessStatus();
        string guidance = !collectEnabled
            ? "Windows toast mirror is disabled in config (collectWindowsNotifications=false)."
            : !packaged
                ? "Unpackaged process has no MSIX/Sparse package identity; Windows blocks UserNotificationListener. Package via packaging/msix/Build-Msix.ps1 or push via POST /v1/events."
                : status == "Allowed"
                    ? "Windows toast listener allowed and active."
                    : $"Listener status is '{status}'. Request consent interactively via Tray -> Notifications -> Request Permission.";

        return new(
            AccessStatus: status,
            CollectEnabled: collectEnabled,
            HasPackageIdentity: packaged,
            PollCount: Interlocked.Read(ref pollCount),
            PublishedCount: Interlocked.Read(ref publishedCount),
            LastError: Volatile.Read(ref lastError),
            Guidance: guidance);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HasWindowsPackageIdentity()
    {
        try
        {
            _ = Package.Current.Id;
            return true;
        }
        catch
        {
            return false;
        }
    }

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
        try
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
                var text = binding.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
                if (text.Length == 0) continue;

                string source = n.AppInfo?.AppUserModelId ?? "windows.notification";
                string appName = n.AppInfo?.DisplayInfo.DisplayName ?? source;
                string? iconB64 = await TryReadBoundedLogoAsync(n.AppInfo, ct);
                var e = BuildCompanionEvent(n.Id, source, appName, text, n.CreationTime, config, iconB64);
                await publish(e, ct);
                publishedCreationTicks[n.Id] = createdSec;
                Interlocked.Increment(ref publishedCount);
                count++;
            }
            Volatile.Write(ref lastError, null);
            return count;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref lastError, ex.Message);
            return 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<string?> TryReadBoundedLogoAsync(AppInfo? appInfo, CancellationToken ct)
    {
        if (appInfo is null) return null;
        try
        {
            var streamRef = appInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            if (streamRef is null) return null;
            using IRandomAccessStreamWithContentType stream = await streamRef.OpenReadAsync();
            if (stream.Size is 0 or > 16384) return null;
            byte[] bytes = new byte[(int)stream.Size];
            using var netStream = stream.AsStreamForRead();
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = await netStream.ReadAsync(bytes.AsMemory(offset, bytes.Length - offset), ct);
                if (read == 0) break;
                offset += read;
            }
            if (offset == 0) return null;
            string b64 = Convert.ToBase64String(bytes, 0, offset);
            return b64.Length <= 24000 ? b64 : null;
        }
        catch
        {
            return null;
        }
    }

    internal static CompanionEvent BuildCompanionEvent(
        uint notificationId,
        string source,
        string appName,
        IReadOnlyList<string> textLines,
        DateTimeOffset createdUtc,
        HostConfig? config,
        string? iconBase64 = null)
    {
        long createdSec = createdUtc.ToUnixTimeSeconds();
        string safeSource = string.IsNullOrWhiteSpace(source) ? "windows.notification" : source;
        string safeApp = string.IsNullOrWhiteSpace(appName) ? safeSource : appName;
        string title = textLines.Count > 0 ? textLines[0] : "Windows Notification";
        string body = textLines.Count > 1 ? string.Join(" ", textLines.Skip(1)) : "";
        int priority = DeterminePriority(safeSource, safeApp, config);
        return new CompanionEvent(
            Id: "win-" + notificationId + "-" + createdSec,
            Source: safeSource[..Math.Min(safeSource.Length, 128)],
            Title: title[..Math.Min(title.Length, 256)],
            Body: body[..Math.Min(body.Length, 2048)],
            Priority: priority,
            ExpiresUtc: createdUtc.AddMinutes(2),
            Sensitive: config?.HideSensitive ?? true,
            AppName: safeApp[..Math.Min(safeApp.Length, 128)],
            Category: "windows.toast",
            IconBase64: iconBase64 is { Length: <= 24000 } ? iconBase64 : null,
            CreatedUtc: createdUtc,
            FullscreenEligible: false,
            Style: priority >= 3 ? "beacon" : "standard");
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
