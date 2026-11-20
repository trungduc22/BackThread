using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BackThread.App.Pages;

public sealed partial class OffloadEcoPage : Page
{
    private readonly DaemonClient daemon;

    public OffloadEcoPage(DaemonClient daemon)
    {
        this.daemon = daemon;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void OnEcoClick(object sender, RoutedEventArgs e) => await SetProfileAsync("Eco");
    private async void OnBalancedClick(object sender, RoutedEventArgs e) => await SetProfileAsync("Balanced");
    private async void OnPerfClick(object sender, RoutedEventArgs e) => await SetProfileAsync("Performance");
    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task SetProfileAsync(string profile)
    {
        try
        {
            await daemon.CallAsync(DaemonOperations.PowerProfileSet, new { profile });
            await RefreshAsync();
        }
        catch (Exception ex) { EvalResultText.Text = "Error: " + ex.Message; }
    }

    private async void OnEvaluateClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string kind = (JobKindBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ocr.extract";
            double mb = double.TryParse(PayloadMbBox.Text, out double v) ? Math.Max(0.1, v) : 4.0;
            var res = await daemon.CallAsync(DaemonOperations.OffloadEvaluate, new { kind, payloadBytes = (long)(mb * 1024 * 1024) });
            EvalResultText.Text = $"Target: {res.Str("target")} • Offload={res.Str("shouldOffload")} • {res.Str("reason")} (PC {res.Str("estimatedPcMs")}ms vs Phone {res.Str("estimatedPhoneTotalMs")}ms)";
        }
        catch (Exception ex) { EvalResultText.Text = "Evaluate failed: " + ex.Message; }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snap = await daemon.CallAsync(DaemonOperations.OffloadStatus);
            PhoneHealthCard.Value = snap.Bool("phoneEligibleForCompute") ? "Eligible" : "Guarded";
            PhoneHealthCard.Caption = $"{snap.Str("deviceTier")} • {snap.Str("phoneHealthReason")}";
            AutoPipCard.Value = snap.Bool("autoPipRecommended") ? "Recommended" : "Idle";
            AutoPipCard.Caption = $"Profile: {snap.Str("powerProfile")} • Offloaded {snap.Str("offloadedJobsCount")}";
        }
        catch (Exception ex) { EvalResultText.Text = "Error: " + ex.Message; }
    }
}
