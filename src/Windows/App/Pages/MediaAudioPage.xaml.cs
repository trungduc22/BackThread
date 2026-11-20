using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BackThread.App.Pages;

public sealed partial class MediaAudioPage : Page
{
    private readonly DaemonClient daemon;

    public MediaAudioPage(DaemonClient daemon)
    {
        this.daemon = daemon;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void OnModePcClick(object sender, RoutedEventArgs e) => await SetModeAsync("PcOnly");
    private async void OnModePhoneClick(object sender, RoutedEventArgs e) => await SetModeAsync("PhoneOnly");
    private async void OnModeBothClick(object sender, RoutedEventArgs e) => await SetModeAsync("Both");

    private async void OnSendMediaClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(MediaUrlBox.Text)) return;
        try
        {
            var res = await daemon.CallAsync(DaemonOperations.MediaSubmit, new { url = MediaUrlBox.Text.Trim() });
            MediaStatusText.Text = $"Handoff stage: {res.Str("stage")} ({res.Str("mode")})";
        }
        catch (Exception ex) { MediaStatusText.Text = "Submit failed: " + ex.Message; }
    }

    private async void OnReturnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var res = await daemon.CallAsync(DaemonOperations.MediaReturn, new { resumeWindows = true });
            MediaStatusText.Text = $"Returned to PC: owner={res.Str("owner")}";
        }
        catch (Exception ex) { MediaStatusText.Text = "Return failed: " + ex.Message; }
    }

    private async Task SetModeAsync(string mode)
    {
        try
        {
            var res = await daemon.CallAsync(DaemonOperations.AudioModeSet, new { mode });
            AudioStatusText.Text = $"Mode: {res.Str("mode")} • Capture: {res.Str("captureSignalStatus")}";
        }
        catch (Exception ex) { AudioStatusText.Text = "Mode change failed: " + ex.Message; }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var media = await daemon.CallAsync(DaemonOperations.MediaStatus);
            MediaStatusText.Text = $"Owner: {media.Str("owner")} • Stage: {media.Str("stage")} • Mode: {media.Str("mode")}";

            var audio = await daemon.CallAsync(DaemonOperations.AudioState);
            AudioStatusText.Text = $"Mode: {audio.Str("mode")} • Capture: {audio.Str("captureSignalStatus")} • Device: {audio.Str("deviceName")}";

            var mixer = await daemon.CallAsync(DaemonOperations.AudioMixerList);
            MixerListPanel.Children.Clear();
            var sessions = mixer.Items("sessions").ToArray();
            if (sessions.Length == 0)
            {
                MixerListPanel.Children.Add(new TextBlock { Text = "No active Windows audio sessions.", Opacity = 0.7 });
                return;
            }

            foreach (var s in sessions)
            {
                int pid = (int)(s.Num("processId") ?? 0);
                double vol = (s.Num("volumeScalar") ?? 1.0) * 100.0;
                bool muted = s.Bool("muted");

                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock { Text = s.Str("displayName", s.Str("processName", $"PID {pid}")), VerticalAlignment = VerticalAlignment.Center };
                var slider = new Slider { Minimum = 0, Maximum = 100, Value = vol, VerticalAlignment = VerticalAlignment.Center };
                var muteBtn = new Button { Content = muted ? "Unmute" : "Mute" };

                slider.ValueChanged += async (_, args) =>
                {
                    try { await daemon.CallAsync(DaemonOperations.AudioMixerSet, new { processId = pid, volume = (float)(args.NewValue / 100.0) }); }
                    catch { }
                };
                muteBtn.Click += async (_, _) =>
                {
                    muted = !muted;
                    muteBtn.Content = muted ? "Unmute" : "Mute";
                    try { await daemon.CallAsync(DaemonOperations.AudioMixerSet, new { processId = pid, muted }); }
                    catch { }
                };

                Grid.SetColumn(label, 0);
                Grid.SetColumn(slider, 1);
                Grid.SetColumn(muteBtn, 2);
                row.Children.Add(label);
                row.Children.Add(slider);
                row.Children.Add(muteBtn);
                MixerListPanel.Children.Add(row);
            }
        }
        catch (Exception ex) { AudioStatusText.Text = "Error: " + ex.Message; }
    }
}
