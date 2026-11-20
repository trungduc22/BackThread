using System.Runtime.InteropServices.WindowsRuntime;
using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace BackThread.App.Pages;

public sealed partial class DevicesPairingPage : Page
{
    private readonly DaemonClient daemon;

    public DevicesPairingPage(DaemonClient daemon)
    {
        this.daemon = daemon;
        InitializeComponent();
        Loaded += async (_, _) => await RefreshDevicesAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async void OnStartPairingClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var session = await daemon.CallAsync(DaemonOperations.LanPairStart);
            string base64 = session.Str("qrPngBase64", "");
            if (!string.IsNullOrEmpty(base64))
            {
                byte[] bytes = Convert.FromBase64String(base64);
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                var bmp = new BitmapImage();
                await bmp.SetSourceAsync(stream);
                QrImage.Source = bmp;
            }
            CheckCodeText.Text = session.Str("humanCheckCode", "—");
            var hosts = session.Items("hosts").Select(h => h.GetString()).Where(h => !string.IsNullOrWhiteSpace(h));
            HostsText.Text = $"Port {session.Str("port")} • Hosts: {string.Join(", ", hosts)}";
            FingerprintText.Text = "SHA-256: " + session.Str("fingerprint");
            QrPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Scan QR with BackThread on iOS (valid for 5 minutes, single-use).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Pairing failed: " + ex.Message;
        }
    }

    private async Task RefreshDevicesAsync()
    {
        try
        {
            var res = await daemon.CallAsync(DaemonOperations.LanDevicesList);
            DevicesListPanel.Children.Clear();
            var devices = res.Items("devices").ToArray();
            if (devices.Length == 0)
            {
                DevicesListPanel.Children.Add(new TextBlock { Text = "No paired devices yet. Connect via USB or generate a Wi-Fi QR.", Opacity = 0.7 });
                return;
            }

            foreach (var dev in devices)
            {
                string id = dev.Str("deviceId", "");
                bool revoked = dev.Bool("revoked");
                var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(12) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { Spacing = 2 };
                info.Children.Add(new TextBlock { Text = $"{dev.Str("name", "iPhone")} ({dev.Str("capabilityTier", "Compact")})", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                info.Children.Add(new TextBlock
                {
                    Text = $"Route: {dev.Str("lastRoute")} • iOS {dev.Str("iosVersion", "?")} • Battery {dev.Str("batteryLevel", "?")}% • Thermal {dev.Str("thermalState", "nominal")}" + (revoked ? " • REVOKED" : ""),
                    Opacity = 0.75,
                    FontSize = 12
                });
                Grid.SetColumn(info, 0);
                row.Children.Add(info);

                if (!revoked && !string.IsNullOrEmpty(id))
                {
                    var revokeBtn = new Button { Content = "Revoke" };
                    revokeBtn.Click += async (_, _) =>
                    {
                        await daemon.CallAsync(DaemonOperations.LanDevicesRevoke, new { deviceId = id });
                        await RefreshDevicesAsync();
                    };
                    Grid.SetColumn(revokeBtn, 1);
                    row.Children.Add(revokeBtn);
                }

                DevicesListPanel.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Child = row
                });
            }
            StatusText.Text = $"Loaded {devices.Length} device(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Error: " + ex.Message;
        }
    }
}
