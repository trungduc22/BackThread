using BackThread.App.Services;
using BackThread.Desktop;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeShellKit.Abstractions.Navigation;
using NativeShellKit.Abstractions.Settings;
using NativeShellKit.DesignSystem.Controls;
using NativeShellKit.Windows.Startup;

namespace BackThread.App.Pages;

public sealed partial class DiagnosticsSettingsPage : Page
{
    private readonly DaemonClient daemon;
    private readonly WindowsStartupService startup;
    private readonly IThemeService theme;
    private readonly IDiagnosticsService diagnostics;
    private readonly Action closeApp;

    public DiagnosticsSettingsPage(
        DaemonClient daemon,
        WindowsStartupService startup,
        IThemeService theme,
        IDiagnosticsService diagnostics,
        Action closeApp)
    {
        this.daemon = daemon;
        this.startup = startup;
        this.theme = theme;
        this.diagnostics = diagnostics;
        this.closeApp = closeApp;
        InitializeComponent();
        BuildSettingsRows();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void BuildSettingsRows()
    {
        var startupBtn = new Button { Content = startup.IsEnabled() ? "Disable Auto-Start" : "Enable Auto-Start" };
        startupBtn.Click += (_, _) =>
        {
            if (startup.IsEnabled())
            {
                startup.Disable();
            }
            else
            {
                string? daemonExe = DaemonContract.ResolveSibling(DaemonContract.DaemonExecutableName, DaemonContract.DaemonPathEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(daemonExe))
                    startup.Enable(daemonExe, startMinimized: true);
                else
                    StatusText.Text = "BackThread.Daemon.exe not found next to BackThread.App.exe.";
            }
            startupBtn.Content = startup.IsEnabled() ? "Disable Auto-Start" : "Enable Auto-Start";
        };

        AppSettingsSection.AddRow(new SettingsRowControl
        {
            Header = "Start Daemon at Windows Sign-In",
            Description = "Launches headless BackThread.Daemon.exe --minimized (0 MB WinUI overhead)",
            Glyph = "\uE7E8",
            ActionContent = startupBtn
        });

        var themePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (mode, label) in new[] { (AppThemeMode.System, "System"), (AppThemeMode.Light, "Light"), (AppThemeMode.Dark, "Dark") })
        {
            var btn = new Button { Content = label };
            var captured = mode;
            btn.Click += (_, _) => theme.ApplyTheme(captured);
            themePanel.Children.Add(btn);
        }

        AppSettingsSection.AddRow(new SettingsRowControl
        {
            Header = "Theme",
            Description = "Follows Windows theme or forces Light/Dark Mica",
            Glyph = "\uE790",
            ActionContent = themePanel
        });
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnShutdownDaemonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await daemon.CallAsync(DaemonOperations.DaemonShutdown);
            closeApp();
        }
        catch (Exception ex) { StatusText.Text = "Shutdown failed: " + ex.Message; }
    }

    private async Task RefreshAsync()
    {
        var uiMetrics = diagnostics.CaptureMetrics();
        UiMemCard.Value = $"{uiMetrics.WorkingSetBytes / (1024.0 * 1024.0):0.0} MB";
        UiMemCard.Caption = $"Threads: {uiMetrics.ThreadCount} • Exits on close";

        try
        {
            var d = await daemon.CallAsync(DaemonOperations.DiagnosticsSnapshot);
            double wsMb = (d.Num("workingSetBytes") ?? 0) / (1024.0 * 1024.0);
            DaemonMemCard.Value = $"{wsMb:0.0} MB";
            DaemonMemCard.Caption = $"PID {d.Str("processId")} • Threads {d.Str("threadCount")} • Uptime {d.Str("uptimeSeconds")}s";

            var logs = await daemon.CallAsync(DaemonOperations.LogsTail, new { count = 60 });
            LogsBox.Text = string.Join(Environment.NewLine, logs.Items("lines").Select(l => l.GetString()));
            StatusText.Text = $"IPC pipe: {d.Str("pipeName")} ({d.Str("allowedOperations")} allowlisted ops)";
        }
        catch (Exception ex) { StatusText.Text = "Daemon diagnostics error: " + ex.Message; }
    }
}
