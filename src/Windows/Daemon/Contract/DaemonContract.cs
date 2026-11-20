using NativeShellKit.Abstractions.Identity;

namespace BackThread.Desktop;

/// <summary>
/// Shared Daemon ⇄ Dashboard contract. This file is compiled into BOTH BackThread.Daemon and
/// BackThread.App (linked source) so the AppIdentity, IPC pipe, DPAPI HMAC key, and allowlisted
/// operation names can never drift between the two processes.
/// </summary>
public static class DaemonContract
{
    public const string AppId = "backthread";
    public const string DisplayName = "BackThread";
    public const string Publisher = "trungduc22";
    public const string Version = "0.3.0";

    public const string DaemonMutexName = @"Local\BackThread_Daemon_v1";
    public const string DaemonActivationPipe = "BackThread_Daemon_Act_v1";
    public const string AppMutexName = @"Local\BackThread_App_v1";
    public const string AppActivationPipe = "BackThread_App_Act_v1";

    public const string DaemonExecutableName = "BackThread.Daemon.exe";
    public const string AppExecutableName = "BackThread.App.exe";

    /// <summary>Optional override for locating the sibling executable when the two apps are not co-located.</summary>
    public const string DaemonPathEnvironmentVariable = "BACKTHREAD_DAEMON_PATH";
    public const string AppPathEnvironmentVariable = "BACKTHREAD_APP_PATH";

    public static AppIdentity CreateIdentity(string? baseDataRoot = null) =>
        new(AppId, DisplayName, Publisher, Version, baseDataRoot);

    /// <summary>Resolves a sibling executable (same folder first, then the environment override).</summary>
    public static string? ResolveSibling(string executableName, string environmentVariable)
    {
        string local = Path.Combine(AppContext.BaseDirectory, executableName);
        if (File.Exists(local))
            return local;
        string? overridePath = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath)
            && overridePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return Path.GetFullPath(overridePath);
        return null;
    }
}

/// <summary>Explicit Named Pipe operation allowlist (AGENTS.md: never expose arbitrary command execution).</summary>
public static class DaemonOperations
{
    public const string StatusGet = "status.get";
    public const string TelemetryGet = "telemetry.get";
    public const string LanStatus = "lan.status";
    public const string LanPairStart = "lan.pair.start";
    public const string LanDevicesList = "lan.devices.list";
    public const string LanDevicesRevoke = "lan.devices.revoke";
    public const string LanDevicesRotate = "lan.devices.rotate";
    public const string LanDevicesDisconnect = "lan.devices.disconnect";
    public const string LanDevicesPermissions = "lan.devices.permissions";
    public const string InputStatus = "input.status";
    public const string InputEmergencyStop = "input.emergency.stop";
    public const string AudioState = "audio.state";
    public const string AudioModeSet = "audio.mode.set";
    public const string AudioVolumeSet = "audio.volume.set";
    public const string AudioMixerList = "audio.mixer.list";
    public const string AudioMixerSet = "audio.mixer.set";
    public const string MediaStatus = "media.status";
    public const string MediaSubmit = "media.submit";
    public const string MediaCommand = "media.command";
    public const string MediaReturn = "media.return";
    public const string OffloadStatus = "offload.status";
    public const string OffloadEvaluate = "offload.evaluate";
    public const string PowerProfileSet = "power.profile.set";
    public const string GpuSample = "gpu.sample";
    public const string ProcessesSample = "processes.sample";
    public const string DevSample = "dev.sample";
    public const string RulesStatus = "rules.status";
    public const string RulesPause = "rules.pause";
    public const string ChargeGuardStatus = "chargeguard.status";
    public const string NotificationsStatus = "notifications.status";
    public const string DiagnosticsSnapshot = "diagnostics.snapshot";
    public const string LogsTail = "logs.tail";
    public const string DaemonShutdown = "daemon.shutdown";

    public static readonly IReadOnlyList<string> All =
    [
        StatusGet, TelemetryGet, LanStatus, LanPairStart, LanDevicesList, LanDevicesRevoke,
        LanDevicesRotate, LanDevicesDisconnect, LanDevicesPermissions,
        InputStatus, InputEmergencyStop,
        AudioState, AudioModeSet, AudioVolumeSet, AudioMixerList, AudioMixerSet,
        MediaStatus, MediaSubmit, MediaCommand, MediaReturn,
        OffloadStatus, OffloadEvaluate, PowerProfileSet,
        GpuSample, ProcessesSample, DevSample,
        RulesStatus, RulesPause, ChargeGuardStatus, NotificationsStatus,
        DiagnosticsSnapshot, LogsTail, DaemonShutdown
    ];

    /// <summary>Media transport commands the dashboard may forward (no free-form strings reach the router).</summary>
    public static readonly IReadOnlyList<string> AllowedMediaCommands = ["toggle", "play", "pause", "next", "previous", "seek"];
}
