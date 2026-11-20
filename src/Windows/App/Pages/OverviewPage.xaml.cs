using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.DesignSystem.Controls;

namespace BackThread.App.Pages;

public sealed partial class OverviewPage : Page
{
    private readonly DaemonClient daemon;
    private readonly DispatcherTimer timer;

    public OverviewPage(DaemonClient daemon)
    {
        this.daemon = daemon;
        InitializeComponent();
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { timer.Start(); await RefreshAsync(); };
        Unloaded += (_, _) => timer.Stop();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        try
        {
            var status = await daemon.CallAsync(DaemonOperations.StatusGet);
            ErrorText.Visibility = Visibility.Collapsed;

            string state = status.Str("state", "unknown");
            ConnectionIndicator.State = state == "connected" ? ShellIndicatorState.Healthy
                : state == "connecting" ? ShellIndicatorState.Active
                : ShellIndicatorState.Warning;
            ConnectionIndicator.Label = $"Host: {state}";

            string route = status.Str("activeRoute", "None");
            string rtt = status.Num("rttMilliseconds") is double r ? $"{r:0.0} ms" : "n/a";
            RouteCard.Value = route;
            RouteCard.Caption = $"RTT {rtt} • Failovers {status.Str("failoverCount", "0")}";

            double? cpu = status.Num("telemetry.cpuUsagePercent");
            double? ram = status.Num("telemetry.memoryUsedPercent");
            TelemetryCard.Value = cpu is double c ? $"{c:0}% CPU" : "n/a";
            TelemetryCard.Caption = ram is double m ? $"RAM {m:0}% • {status.Str("telemetry.batteryStatus", "")}" : "Telemetry pending";

            AudioCard.Value = status.Str("audioOutputMode", "Both");
            AudioCard.Caption = $"{status.Str("audioPacketMs", "10")} ms • SilenceSleep: {status.Str("audioSilenceSleep", "No")}";

            OffloadCard.Value = status.Str("powerProfile", "Balanced");
            OffloadCard.Caption = $"Offloaded {status.Str("offload.offloadedJobsCount", "0")} • Local {status.Str("offload.keptLocalJobsCount", "0")}";
        }
        catch (Exception ex)
        {
            ConnectionIndicator.State = ShellIndicatorState.Error;
            ConnectionIndicator.Label = "Daemon unreachable";
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
