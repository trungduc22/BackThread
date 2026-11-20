using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BackThread.App.Pages;

public sealed partial class ControlDeckRulesPage : Page
{
    private readonly DaemonClient daemon;

    public ControlDeckRulesPage(DaemonClient daemon)
    {
        this.daemon = daemon;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void OnToggleMediaClick(object sender, RoutedEventArgs e) => await SendMediaAsync("toggle");
    private async void OnNextMediaClick(object sender, RoutedEventArgs e) => await SendMediaAsync("next");

    private async void OnPauseRulesClick(object sender, RoutedEventArgs e)
    {
        try { await daemon.CallAsync(DaemonOperations.RulesPause, new { minutes = 60 }); await RefreshAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void OnResumeRulesClick(object sender, RoutedEventArgs e)
    {
        try { await daemon.CallAsync(DaemonOperations.RulesPause, new { minutes = 0 }); await RefreshAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async Task SendMediaAsync(string cmd)
    {
        try
        {
            await daemon.CallAsync(DaemonOperations.MediaCommand, new { command = cmd });
            StatusText.Text = $"Sent media command: {cmd}";
        }
        catch (Exception ex) { StatusText.Text = "Command failed: " + ex.Message; }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var rules = await daemon.CallAsync(DaemonOperations.RulesStatus);
            int activeCount = rules.Items("active").Count();
            RulesCard.Value = $"{activeCount} active";
            RulesCard.Caption = $"Configured: {rules.Str("configured", "0")}";

            var cg = await daemon.CallAsync(DaemonOperations.ChargeGuardStatus);
            ChargeCard.Value = cg.Bool("enabled") ? (cg.Str("lastPowerState", "Monitoring")) : "Disabled";
            ChargeCard.Caption = $"Range {cg.Str("minPercent", "50")}%–{cg.Str("maxPercent", "80")}%";
            StatusText.Text = "Synced with daemon.";
        }
        catch (Exception ex) { StatusText.Text = "Error: " + ex.Message; }
    }
}
