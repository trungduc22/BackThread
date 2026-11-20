using System.Diagnostics;
using System.Text.Json;
using BackThread.Desktop;
using BackThread.Host;
using BackThread.Media;
using BackThread.Worker;
using NativeShellKit.Abstractions.Identity;
using NativeShellKit.Security;
using NativeShellKit.Tray;
using NativeShellKit.Windows.SingleInstance;

namespace BackThread.Daemon;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--smoke-test", StringComparison.OrdinalIgnoreCase)))
            return await SmokeTest.RunAsync(Console.Out).ConfigureAwait(false);

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("BackThread.Daemon runs on Windows. Use --smoke-test to verify the IPC layer on other platforms.");
            return 2;
        }

        var identity = DaemonContract.CreateIdentity();
        await using var coordinator = new SingleInstanceCoordinator(identity, DaemonContract.DaemonMutexName, DaemonContract.DaemonActivationPipe);
        bool openDashboard = args.Any(a => string.Equals(a, "--open-dashboard", StringComparison.OrdinalIgnoreCase));
        if (!coordinator.TryAcquirePrimaryInstance())
        {
            await coordinator.SignalExistingPrimaryInstanceAsync(openDashboard ? "open-dashboard" : "ping").ConfigureAwait(false);
            return 0;
        }

        var log = new DaemonLog();
        try
        {
            identity.EnsureDirectoriesExist();
            string configPath = ResolveConfigPath(Value(args, "--config"), identity);
            // Relative key paths are resolved against the configuration's parent project directory (same as BackThread.Tray).
            string? projectRoot = Path.GetDirectoryName(Path.GetDirectoryName(configPath));
            if (!string.IsNullOrEmpty(projectRoot))
                Directory.SetCurrentDirectory(projectRoot);

            var config = HostConfig.Load(configPath);
            string keySource = await SessionKeyGuard.EnsureAsync(config.KeyFile, new DpapiSecretStore(identity)).ConfigureAwait(false);
            log.Write($"Config: {configPath} (session key: {keySource})");

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

            using var runtime = new HostRuntime(config);
            runtime.Log += log.Write;
            var auth = new IpcAuthManager(identity);
            await using var ipc = new DaemonIpcHost(identity, auth, runtime, log, () => stop.Cancel());
            await ipc.StartAsync(stop.Token).ConfigureAwait(false);
            log.Write($"IPC listening on pipe {ipc.PipeName} ({ipc.AllowedOperations.Count} allowlisted operations)");

            coordinator.ActivationCommandReceived += command =>
            {
                if (command.Contains("dashboard", StringComparison.OrdinalIgnoreCase))
                    DashboardLauncher.Open(log);
            };

            using var trayWindow = args.Any(a => string.Equals(a, "--no-tray", StringComparison.OrdinalIgnoreCase))
                ? null
                : new TrayMessageWindow();
            if (trayWindow is not null)
            {
                try
                {
                    await trayWindow.StartAsync(hwnd => CreateTray(identity, hwnd, trayWindow, runtime, log, stop)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log.Write("Tray unavailable (daemon keeps running headless): " + ex.Message);
                }
            }

            if (openDashboard)
                DashboardLauncher.Open(log);

            try
            {
                await runtime.RunAsync(stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
            }
            log.Write("Daemon stopped");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().Name + ": " + ex.Message);
            log.Write("Fatal: " + ex.Message);
            return 1;
        }
    }

    private static Action? CreateTray(AppIdentity identity, nint hwnd, TrayMessageWindow window, HostRuntime runtime, DaemonLog log, CancellationTokenSource stop)
    {
        var tray = new NativeTrayService(identity) { MinimizeToTrayOnClose = false };
        void Background(Func<Task> work) => _ = Task.Run(async () =>
        {
            try { await work().ConfigureAwait(false); }
            catch (Exception ex) { log.Write("Tray action failed: " + ex.Message); }
        });

        tray.SetMenuItems(
        [
            new TrayMenuItem(1, "Open Dashboard", () => DashboardLauncher.Open(log)),
            new TrayMenuItem(2, "Pair iPhone over Wi-Fi (QR)…", () => DashboardLauncher.Open(log, "devices")),
            new TrayMenuItem(3, "-", IsSeparator: true),
            new TrayMenuItem(10, "Power: Eco (weak PC)", () => runtime.SetPowerProfile(PowerProfile.Eco)),
            new TrayMenuItem(11, "Power: Balanced", () => runtime.SetPowerProfile(PowerProfile.Balanced)),
            new TrayMenuItem(12, "Power: Performance", () => runtime.SetPowerProfile(PowerProfile.Performance)),
            new TrayMenuItem(4, "-", IsSeparator: true),
            new TrayMenuItem(20, "Audio: PC only", () => Background(() => runtime.SetAudioOutputModeAsync(AudioOutputMode.PcOnly, stop.Token))),
            new TrayMenuItem(21, "Audio: iPhone only", () => Background(() => runtime.SetAudioOutputModeAsync(AudioOutputMode.PhoneOnly, stop.Token))),
            new TrayMenuItem(22, "Audio: PC + iPhone", () => Background(() => runtime.SetAudioOutputModeAsync(AudioOutputMode.Both, stop.Token))),
            new TrayMenuItem(23, "Return playback to Windows", () => Background(() => runtime.ReturnToWindowsAsync(true, stop.Token))),
            new TrayMenuItem(5, "-", IsSeparator: true),
            new TrayMenuItem(99, "Exit BackThread", () =>
            {
                tray.RequestFullExit();
                stop.Cancel();
            })
        ]);

        // Left-click restores the owner window inside NativeTrayService; keep the owner hidden and open the dashboard instead.
        tray.RestoreRequested += (_, _) =>
        {
            window.Hide();
            DashboardLauncher.Open(log);
        };

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "backthread.ico");
        if (!tray.Initialize(hwnd, iconPath))
            log.Write("Tray icon not added: " + (tray.LastTrayError ?? "unknown error"));
        return tray.Dispose;
    }

    internal static string ResolveConfigPath(string? explicitPath, AppIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);

        string beside = Path.Combine(AppContext.BaseDirectory, "config", "local.json");
        if (File.Exists(beside))
            return beside;

        string userConfigDir = Path.Combine(identity.DataDirectory, "config");
        string userConfig = Path.Combine(userConfigDir, "local.json");
        if (!File.Exists(userConfig))
        {
            Directory.CreateDirectory(userConfigDir);
            // Safe first-run defaults: USB first with on-demand Wi-Fi pairing; the LAN listener only opens when pairing starts.
            var defaults = new
            {
                keyFile = Path.Combine(userConfigDir, "session.key"),
                transport = "auto",
                powerProfile = "Balanced",
                lan = new { enabled = false, port = 23453, bindAddress = "0.0.0.0", advertiseMdns = true, autoProvisionOverUsb = true },
                audioOutputMode = "Both",
                hideSensitive = true
            };
            File.WriteAllText(userConfig, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
        }
        return userConfig;
    }

    private static string? Value(string[] args, string name)
    {
        int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

/// <summary>Spawns the on-demand WinUI dashboard (it exits completely when its window closes).</summary>
internal static class DashboardLauncher
{
    private static long lastLaunchTicks;

    public static void Open(DaemonLog log, string? page = null)
    {
        // Debounce double-clicks; the dashboard is single-instance and re-activates itself anyway.
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastLaunchTicks) < 1500)
            return;
        Interlocked.Exchange(ref lastLaunchTicks, now);

        string? exe = DaemonContract.ResolveSibling(DaemonContract.AppExecutableName, DaemonContract.AppPathEnvironmentVariable);
        if (exe is null)
        {
            log.Write($"Dashboard not found next to the daemon (set {DaemonContract.AppPathEnvironmentVariable} to BackThread.App.exe)");
            return;
        }

        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
            if (!string.IsNullOrWhiteSpace(page))
            {
                start.ArgumentList.Add("--page");
                start.ArgumentList.Add(page);
            }
            using var _ = Process.Start(start);
        }
        catch (Exception ex)
        {
            log.Write("Failed to open dashboard: " + ex.Message);
        }
    }
}
