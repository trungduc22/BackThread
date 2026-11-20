using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.Notifications;

namespace BackThread.App.Pages;

public sealed partial class NotificationsIntegrationsPage : Page
{
    private readonly DaemonClient daemon;
    private readonly INotificationService notifications;

    public NotificationsIntegrationsPage(DaemonClient daemon, INotificationService notifications)
    {
        this.daemon = daemon;
        this.notifications = notifications;
        InitializeComponent();
        UpdateQuietButton();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void UpdateQuietButton() =>
        QuietHoursBtn.Content = notifications.Preferences.QuietHoursEnabled ? "Quiet Hours: ON" : "Quiet Hours: OFF";

    private void OnToggleQuietClick(object sender, RoutedEventArgs e)
    {
        notifications.Preferences.QuietHoursEnabled = !notifications.Preferences.QuietHoursEnabled;
        UpdateQuietButton();
    }

    private async void OnTestNotificationClick(object sender, RoutedEventArgs e)
    {
        var result = await notifications.PublishAsync(new AppNotificationRequest(
            "BackThread Companion",
            "Unified NativeShellKit notification channel is active.",
            NotificationSeverity.Success,
            DeduplicationKey: $"test-{DateTimeOffset.UtcNow.Ticks}"));
        StatusText.Text = $"Notification dispatched: inApp={result.PresentedInApp}, native={result.NativeSubmissionStatus}";
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var dev = await daemon.CallAsync(DaemonOperations.DevSample);
            int models = dev.Items("loadedOllamaModels").Count();
            int procs = dev.Items("activeDevProcesses").Count();
            DevCard.Value = dev.Bool("enabled") ? (dev.Bool("ollamaReachable") ? $"Ollama ({models})" : "Enabled") : "Opt-in Off";
            DevCard.Caption = $"Active dev processes: {procs}";

            var gpu = await daemon.CallAsync(DaemonOperations.GpuSample);
            var first = gpu.Items("gpus").FirstOrDefault();
            if (first.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                GpuCard.Value = first.Num("utilizationPercent") is double u ? $"{u:0}%" : "Detected";
                GpuCard.Caption = first.Str("name", "GPU");
            }
            else
            {
                GpuCard.Value = "Unavailable";
                GpuCard.Caption = "No GPU telemetry reported";
            }
        }
        catch (Exception ex) { StatusText.Text = "Error: " + ex.Message; }
    }
}
