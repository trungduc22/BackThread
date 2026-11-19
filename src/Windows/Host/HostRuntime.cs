using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Channels;
using BackThread.Audio;
using BackThread.Events;
using BackThread.Media;
using BackThread.Transport;
using BackThread.Worker;
using NAudio.CoreAudioApi;

namespace BackThread.Host;

public sealed record ClientViewportState(string Tab = "now", bool Awake = true, bool ScreenDimmed = false);

public sealed class HostRuntime : IDisposable
{
    private readonly HostConfig config;
    private readonly byte[] key;
    private readonly MediaSessions media = new();
    private readonly EventRouter events = new();
    private readonly NotificationCollector collector = new();
    private readonly IMultimediaAudioController endpointAudio;
    private readonly ISystemTelemetryProvider telemetryProvider;
    private readonly IPerAppAudioMixer audioMixer;
    private readonly IGpuMonitor gpuMonitor;
    private readonly ProcessMonitorService processMonitor;
    private readonly SemaphoreSlim actions = new(1, 1);
    private readonly SemaphoreSlim bufferedRequests = new(4, 4);
    private readonly SemaphoreSlim audioPolicySignal = new(0, 1);
    private readonly SemaphoreSlim audioControlGate = new(1, 1);
    private readonly SemaphoreSlim mediaChangedSignal = new(0, 1);
    private readonly object stateGate = new();

    private Connection? connection;
    private RemoteWorker? worker;
    private string state = "starting", lastError = "", audioError = "";
    private LoopbackBridge? audio;
    private bool lastLivePcmDesired = true;
    private AudioOutputMode audioOutputMode;
    private float phoneAppGain;
    private float phonePlayerVolume = 1f;
    private string preferredVolumeLayer = "auto";
    private PowerProfile powerProfile;
    private ClientViewportState viewport = new();
    private SystemTelemetrySnapshot? latestTelemetry;
    private readonly CancellationTokenSource? disposedCts = new();
    private CancellationTokenSource? liveAudioSliceCts;

    private X509Certificate2? lanCert;
    private LanTlsServer? lanServer;
    private MdnsAdvertiser? mdns;
    private Channel<Func<Task>>? commandQueue;

    public MediaRouter Router { get; }
    public SpotifyProvider Spotify { get; }
    public HostConfig Config => config;
    public DeviceRegistry Devices { get; }
    public ActiveRouteManager ActiveRoute { get; } = new();
    public QrPairingService Pairing { get; } = new();
    public OffloadCoordinator Offload { get; } = new();
    public AutomationRuleEngine Rules { get; } = new();
    public ChargeGuardService ChargeGuard { get; }
    public DevSessionMonitor DevMonitor { get; }

    public PowerProfile PowerProfile
    {
        get { lock (stateGate) return powerProfile; }
    }

    public HostRuntime(
        HostConfig config,
        ISpotifyTokenStore? spotifyTokens = null,
        HttpMessageHandler? httpHandler = null,
        IMediaUrlProber? urlProber = null,
        IMultimediaAudioController? audioController = null,
        ISystemTelemetryProvider? telemetryProvider = null,
        IPerAppAudioMixer? audioMixer = null,
        IGpuMonitor? gpuMonitor = null,
        ProcessMonitorService? processMonitor = null,
        DeviceRegistry? deviceRegistry = null)
    {
        this.config = config;
        this.endpointAudio = audioController ?? new WindowsEndpointAudio();
        this.telemetryProvider = telemetryProvider ?? new SystemTelemetryProvider();
        this.audioMixer = audioMixer ?? new WindowsPerAppAudioMixer();
        this.gpuMonitor = gpuMonitor ?? new WindowsGpuMonitor();
        this.processMonitor = processMonitor ?? new ProcessMonitorService();
        this.ChargeGuard = new ChargeGuardService(httpHandler);
        this.DevMonitor = new DevSessionMonitor(httpHandler);
        this.audioOutputMode = config.AudioOutputMode;
        this.phoneAppGain = config.AudioGain;
        this.powerProfile = config.PowerProfile;
        key = Authentication.LoadKey(config.KeyFile);
        events.HideSensitive = config.HideSensitive;

        string appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BackThread");

        string registryPath = config.Lan.DeviceRegistryPath ?? Path.Combine(appDataRoot, "devices.json");
        Devices = deviceRegistry ?? new DeviceRegistry(registryPath);

        string defaultTokenPath = config.SpotifyTokenPath ?? Path.Combine(appDataRoot, "spotify-token.dat");

        Spotify = new SpotifyProvider(spotifyTokens ?? new LocalSpotifyTokenStore(defaultTokenPath), httpHandler, config.SpotifyClientId);
        var youtube = new YouTubeProvider(httpHandler);
        var prober = urlProber ?? (httpHandler != null ? new HttpMediaUrlProber(httpHandler) : null);
        Router = new MediaRouter(new DirectMediaProvider(prober), youtube, prober, config.DeviceHasInternet, config.AllowExperimentalYouTubeEmbed)
        {
            PauseWindowsMediaAsync = PauseWindowsSourceAsync,
            ResumeWindowsMediaAsync = ResumeWindowsSourceAsync,
            WindowsCommandAsync = ExecuteWindowsMediaCommandAsync
        };
        Router.SetAudioOutputMode(this.audioOutputMode);
        lastLivePcmDesired = Router.ShouldStreamLivePcm;
        Router.StateChanged += s =>
        {
            MediaStateChanged?.Invoke(s);
            if (s.ShouldStreamLivePcm != Volatile.Read(ref lastLivePcmDesired))
            {
                Volatile.Write(ref lastLivePcmDesired, s.ShouldStreamLivePcm);
                SignalAudioPolicyChange();
            }
        };
        media.Changed += SignalMediaChanged;
    }

    public event Action<string>? Log;
    public event Action<JobState>? JobProgress;
    public event Action<MediaSessionState>? MediaStateChanged;
    public event Action<AudioControlState>? AudioControlChanged;

    public SystemTelemetrySnapshot LatestTelemetry
    {
        get { lock (stateGate) return latestTelemetry ?? new(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), null, null, null, null, null, null, "Unavailable", null, null, "Unavailable", 0, null, false, Environment.OSVersion.Platform.ToString()); }
    }

    public PairedDeviceRecord? ActiveDevice
    {
        get
        {
            string? id = ActiveRoute.Snapshot.ActiveDeviceId;
            return id is not null ? Devices.Get(id) : null;
        }
    }

    public object LanStatus
    {
        get
        {
            var route = ActiveRoute.Snapshot;
            return new
            {
                enabled = lanServer is not null,
                configuredEnabled = config.Lan.Enabled || config.Transport == "wifi",
                port = lanServer?.Port ?? config.Lan.Port,
                pcId = lanServer?.PcId,
                fingerprint = lanServer?.Fingerprint,
                checkCode = lanServer?.HumanCheckCode,
                mdnsActive = mdns is not null,
                hosts = LanNetworkDiscovery.GetReachableAddresses(),
                activeRoute = route.Route.ToString(),
                remoteAddress = route.RemoteAddress,
                rttMilliseconds = route.RttMilliseconds,
                failoverCount = route.FailoverCount,
                audioPacketMs = route.AudioProfile.PacketMs,
                pairedDevicesCount = Devices.List().Count(d => !d.Revoked)
            };
        }
    }

    public object Status
    {
        get
        {
            var m = Router.CurrentState;
            var ac = CurrentAudioControlState();
            var tel = LatestTelemetry;
            var route = ActiveRoute.Snapshot;
            var activeDev = ActiveDevice;
            var offloadSnap = Offload.Snapshot(config.Offload, PowerProfile, activeDev);
            lock (stateGate)
                return new
                {
                    state,
                    lastError,
                    audioError,
                    transport = config.Transport,
                    activeRoute = route.Route.ToString(),
                    remoteAddress = route.RemoteAddress,
                    rttMilliseconds = route.RttMilliseconds,
                    failoverCount = route.FailoverCount,
                    audioPacketMs = audio?.ActivePacketMs ?? route.AudioProfile.PacketMs,
                    audioSilenceSleep = audio?.IsInSilenceSleep ?? false,
                    powerProfile = powerProfile.ToString(),
                    viewportTab = viewport.Tab,
                    lanEnabled = lanServer is not null,
                    lanPort = lanServer?.Port ?? config.Lan.Port,
                    lanCheckCode = lanServer?.HumanCheckCode,
                    simulated = config.Transport == "localhost",
                    device = audio?.DeviceName ?? ac.DeviceName,
                    activeDevice = activeDev,
                    captureError = audio?.LastError,
                    captureSignalStatus = ac.CaptureSignalStatus,
                    audioOutputMode = ac.Mode.ToString(),
                    pcVolumeScalar = ac.PcVolumeScalar,
                    pcMuted = ac.PcMuted,
                    phoneAppGain = ac.PhoneAppGain,
                    phonePlayerVolume = ac.PhonePlayerVolume,
                    audioFrames = audio?.FramesSent ?? 0,
                    captureBufferedBytes = audio?.Ring.Count ?? 0,
                    captureDroppedBytes = audio?.Ring.DroppedBytes ?? 0,
                    captureSilenceBytes = audio?.Ring.SilenceBytes ?? 0,
                    audioDroppedFrames = connection?.DroppedAudio ?? 0,
                    audioWriteMaximumMilliseconds = connection?.AudioWriteMaximumMilliseconds ?? 0,
                    audioSlowWrites = connection?.AudioSlowWrites ?? 0,
                    audioQueueMaximumMilliseconds = connection?.AudioQueueMaximumMilliseconds ?? 0,
                    audioQueueResidenceMaximumMilliseconds = connection?.AudioQueueResidenceMaximumMilliseconds ?? 0,
                    audioPacingMaximumLatenessMilliseconds = audio?.PacingMaximumLatenessMilliseconds ?? 0,
                    audioPacingLateFrames = audio?.PacingLateFrames ?? 0,
                    audioPacingResets = audio?.PacingResets ?? 0,
                    deviceHasInternet = config.DeviceHasInternet,
                    mediaOwner = m.Owner.ToString(),
                    playbackMode = m.Mode.ToString(),
                    handoffStage = m.Stage.ToString(),
                    shouldStreamLivePcm = m.ShouldStreamLivePcm,
                    spotifyConnected = Spotify.IsAuthenticated,
                    notificationAccess = collector.GetAccessStatus(),
                    offload = offloadSnap,
                    rulesActive = Rules.ActiveRuleIds,
                    rulesPausedUntilUnixMs = Rules.PausedUntilUnixMs,
                    chargeGuard = ChargeGuard.GetStatus(config.ChargeGuard),
                    telemetry = tel
                };
        }
    }

    private void SetState(string value, string error = "")
    {
        lock (stateGate) { state = value; lastError = error; }
        Log?.Invoke(value + (error.Length == 0 ? "" : ": " + error));
    }
    public RemoteWorker Worker => worker ?? throw new IOException("Agent is not connected");

    public PowerProfile SetPowerProfile(PowerProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new InvalidDataException("Invalid power profile");
        lock (stateGate) powerProfile = profile;
        Log?.Invoke("Power profile changed to " + profile);
        return profile;
    }

    public PairingSessionSnapshot StartQrPairing()
    {
        EnsureLanServerStarted();
        var server = lanServer ?? throw new InvalidOperationException("Wi-Fi TLS listener is not enabled");
        var hosts = LanNetworkDiscovery.GetReachableAddresses();
        return Pairing.CreateSession(Environment.MachineName, server.Fingerprint, server.Port, hosts);
    }

    public IReadOnlyList<PairedDeviceRecord> ListDevices() => Devices.List();
    public bool RevokeDevice(string deviceId) => Devices.Revoke(deviceId);
    public IReadOnlyList<PerAppAudioSession> ListPerAppAudio() => audioMixer.ListSessions();
    public PerAppAudioSession? SetPerAppAudio(int processId, float? volumeScalar, bool? muted) => audioMixer.SetSessionVolume(processId, volumeScalar, muted);
    public IReadOnlyList<GpuAdapterSnapshot> SampleGpu() => gpuMonitor.Sample();
    public IReadOnlyList<ProcessSummary> SampleProcesses(int limit = 10) => processMonitor.SampleTop(limit);
    public Task<DevSessionSnapshot> SampleDevSessionsAsync(CancellationToken ct) => DevMonitor.SampleAsync(config.DevMonitor, ct);
    public ChargeGuardStatus GetChargeGuardStatus() => ChargeGuard.GetStatus(config.ChargeGuard);
    public object GetRulesStatus() => new { configured = config.Rules.Length, active = Rules.ActiveRuleIds, pausedUntilUnixMs = Rules.PausedUntilUnixMs };
    public void PauseRules(int minutes) => Rules.Pause(minutes);
    public Task<FeedProxyResult> FetchSafeFeedAsync(string? url, CancellationToken ct) => SafeFeedProxy.FetchAsync(url, config.FeedProxy, ct);

    public OffloadCoordinatorSnapshot GetOffloadSnapshot() => Offload.Snapshot(config.Offload, PowerProfile, ActiveDevice);

    public OffloadDecision EvaluateOffload(OffloadCandidate candidate)
    {
        var tel = LatestTelemetry;
        var hostLoad = new HostLoadMetrics(
            CpuUsagePercent: tel.CpuUsagePercent ?? 0,
            MemoryUsedPercent: tel.MemoryUsedPercent ?? 0,
            PowerProfile: PowerProfile,
            VideoPlayingOnPc: Router.CurrentState.Owner == PlaybackOwner.WindowsOwned);
        return Offload.Evaluate(candidate, hostLoad, ActiveDevice, ActiveRoute.Snapshot, config.Offload);
    }

    private void SignalAudioPolicyChange()
    {
        try { Volatile.Read(ref liveAudioSliceCts)?.Cancel(); } catch (ObjectDisposedException) { }
        if (audioPolicySignal.CurrentCount == 0)
            try { audioPolicySignal.Release(); } catch (SemaphoreFullException) { }
    }

    private void SignalMediaChanged()
    {
        if (mediaChangedSignal.CurrentCount == 0)
            try { mediaChangedSignal.Release(); } catch (SemaphoreFullException) { }
    }

    public AudioControlState CurrentAudioControlState()
    {
        var ep = endpointAudio.GetState();
        var m = Router.CurrentState;
        AudioOutputMode mode;
        float gain, playerVol;
        string layerPref;
        bool streamActive;
        lock (stateGate)
        {
            mode = audioOutputMode;
            gain = phoneAppGain;
            playerVol = phonePlayerVolume;
            layerPref = preferredVolumeLayer;
            streamActive = connection != null && config.StreamAudio && Router.ShouldStreamLivePcm;
        }
        string activeLayer = ResolveActiveVolumeLayer(layerPref, mode, m.Owner);
        string capStatus = audio != null
            ? audio.GetCaptureSignalStatus(config.FixtureAudio, streamActive, ep.Muted)
            : (streamActive ? (config.FixtureAudio ? "fixture" : "unknown") : "stopped");
        string? warning = mode == AudioOutputMode.PhoneOnly && capStatus == "silent-driver-coupled"
            ? "Windows driver silences loopback capture while PC speaker is muted; switch to Both if phone audio is silent."
            : ep.Error;

        return new AudioControlState(
            Mode: mode,
            PcVolumeScalar: ep.VolumeScalar,
            PcMuted: ep.Muted,
            PcMuteSavedByBackThread: ep.MuteSavedByBackThread,
            PcEndpointAvailable: ep.Available,
            ActiveVolumeLayer: activeLayer,
            PhoneAppGain: gain,
            PhonePlayerVolume: playerVol,
            CaptureSignalStatus: capStatus,
            DeviceName: ep.DeviceName,
            Warning: warning);
    }

    private static string ResolveActiveVolumeLayer(string preferred, AudioOutputMode mode, PlaybackOwner owner)
    {
        if (preferred is "pc" or "app" or "player") return preferred;
        if (owner == PlaybackOwner.iPhoneOwned && mode != AudioOutputMode.PcOnly) return "player";
        if (mode == AudioOutputMode.PcOnly) return "pc";
        if (mode == AudioOutputMode.PhoneOnly) return "app";
        return "pc";
    }

    private void EnsureLanServerStarted()
    {
        if (lanServer is not null) return;
        string appDataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BackThread");
        string certPath = config.Lan.CertificatePath ?? Path.Combine(appDataRoot, "lan-tls.pfx");
        lanCert ??= LanCertificateStore.LoadOrCreate(certPath, Environment.MachineName);

        IPAddress bindIp = IPAddress.TryParse(config.Lan.BindAddress, out var parsed) ? parsed : IPAddress.Any;
        lanServer = new LanTlsServer(lanCert, key, Pairing, config.Lan.Port, bindIp, Environment.MachineName, Devices);
        lanServer.Log += msg => Log?.Invoke(msg);
        lanServer.Start();

        if (config.Lan.AdvertiseMdns)
        {
            mdns = new MdnsAdvertiser(Environment.MachineName, lanServer.Port, lanServer.PcId, lanServer.Fingerprint);
            mdns.Start();
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (string source in config.BlockedSources) events.BlockedSources.Add(source);
        try { await media.InitializeAsync(); } catch (Exception ex) { Log?.Invoke("Media unavailable: " + ex.Message); }

        if (config.Lan.Enabled || config.Transport == "wifi")
        {
            try { EnsureLanServerStarted(); }
            catch (Exception ex) { Log?.Invoke("Wi-Fi TLS listener unavailable: " + ex.Message); }
        }

        // Bounded 2-worker command queue replaces unbounded Task.Run dispatch on weak PCs.
        var queue = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait });
        commandQueue = queue;
        var commandWorkers = new[]
        {
            RunCommandWorkerAsync(queue.Reader, ct),
            RunCommandWorkerAsync(queue.Reader, ct)
        };

        LocalApi? api = config.EnableHttpApi ? new LocalApi(this, config.ApiPort, key) : null;
        Task apiTask = api is not null ? api.RunAsync(ct) : Task.Delay(Timeout.Infinite, ct);
        int attempt = 0;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
                IncomingTlsSession? tlsSession = null;
                try
                {
                    SetState("connecting");
                    using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    connectTimeout.CancelAfter(TimeSpan.FromSeconds(8));

                    Stream stream;
                    bool alreadyAuthenticated = false;
                    TransportRouteKind routeKind;
                    string remoteAddr;
                    string deviceId;
                    string deviceName = "iPhone";

                    if (config.Transport == "localhost")
                    {
                        var client = new TcpClient { NoDelay = true };
                        try
                        {
                            await client.ConnectAsync(IPAddress.Loopback, config.DevicePort, connectTimeout.Token);
                            stream = client.GetStream();
                        }
                        catch { client.Dispose(); throw; }
                        routeKind = TransportRouteKind.Localhost;
                        remoteAddr = "127.0.0.1";
                        deviceId = "simulator";
                        deviceName = "BackThread Simulator";
                    }
                    else if (config.Transport == "wifi")
                    {
                        EnsureLanServerStarted();
                        tlsSession = await lanServer!.Sessions.ReadAsync(ct);
                        stream = tlsSession.Stream;
                        alreadyAuthenticated = true;
                        routeKind = tlsSession.RouteKind;
                        remoteAddr = tlsSession.RemoteAddress;
                        deviceId = tlsSession.DeviceId ?? ("wifi-" + remoteAddr);
                        deviceName = tlsSession.DeviceName ?? "iPhone";
                    }
                    else if (config.Transport == "auto")
                    {
                        var usbAttempt = await TryConnectUsbAsync(TimeSpan.FromSeconds(2.5), ct);
                        if (usbAttempt is not null)
                        {
                            stream = usbAttempt.Value.Stream;
                            routeKind = TransportRouteKind.Usb;
                            remoteAddr = usbAttempt.Value.Serial;
                            deviceId = usbAttempt.Value.Serial;
                        }
                        else if (lanServer is not null)
                        {
                            using var wifiWait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            wifiWait.CancelAfter(TimeSpan.FromSeconds(2.5));
                            tlsSession = await lanServer.Sessions.ReadAsync(wifiWait.Token);
                            stream = tlsSession.Stream;
                            alreadyAuthenticated = true;
                            routeKind = tlsSession.RouteKind;
                            remoteAddr = tlsSession.RemoteAddress;
                            deviceId = tlsSession.DeviceId ?? ("wifi-" + remoteAddr);
                            deviceName = tlsSession.DeviceName ?? "iPhone";
                        }
                        else
                        {
                            throw new IOException("No USB iPhone detected (enable lan.enabled for Wi-Fi failover)");
                        }
                    }
                    else
                    {
                        var mux = new UsbMux();
                        var devices = await mux.ListAsync(connectTimeout.Token);
                        if (config.DeviceSerial is null && devices.Count > 1)
                            throw new IOException("Multiple USB devices; set deviceSerial explicitly");
                        var device = devices.FirstOrDefault(d => config.DeviceSerial is null || d.Serial == config.DeviceSerial)
                            ?? throw new IOException("Configured USB iPhone not detected");
                        stream = await mux.ConnectAsync(device.Id, config.DevicePort, connectTimeout.Token);
                        routeKind = TransportRouteKind.Usb;
                        remoteAddr = device.Serial;
                        deviceId = device.Serial;
                    }

                    using (stream)
                    {
                        if (!alreadyAuthenticated)
                            await Authentication.ClientAsync(stream, key, connectTimeout.Token);

                        await using var c = new Connection(stream);
                        connection = c;
                        worker = new(c);
                        ActiveRoute.SetActive(routeKind, remoteAddr, deviceId);

                        var reader = c.RunAsync(ReceiveAsync, session.Token);
                        Task? heartbeat = null, mediaLoop = null, audioLoop = null, notificationLoop = null, telemetryLoop = null, usbPreempt = null;
                        try
                        {
                            var hello = await c.RequestAsync(LogicalChannel.Control, "hello", new
                            {
                                host = "BackThread.Host",
                                protocol = 2,
                                route = routeKind.ToString(),
                                capabilities = new[]
                                {
                                    "hybrid-media", "buffered-media", "top-bar-notifications",
                                    "telemetry", "audio-modes", "sync",
                                    "wifi-tls", "adaptive-audio", "per-app-mixer", "gpu-monitor", "offload-v2", "rules"
                                }
                            }, session.Token);

                            Devices.UpdateFromHello(deviceId, deviceName, routeKind, hello.Body);

                            lock (stateGate) audioError = "";
                            SetState("connected");
                            Log?.Invoke($"Agent capabilities ({routeKind}): " + hello.Body.GetRawText());
                            attempt = 0;

                            await c.RequestAsync(LogicalChannel.Control, "action.registry", new { actions = config.Actions.Select(a => new { a.Id, a.Title }) }, session.Token);

                            // 1-tap automatic Wi-Fi credential provisioning over trusted USB so unplugging fails over seamlessly!
                            if (routeKind == TransportRouteKind.Usb && lanServer is not null && config.Lan.AutoProvisionOverUsb)
                            {
                                try
                                {
                                    var provision = QrPairingService.BuildUsbProvisionPayload(
                                        Environment.MachineName,
                                        lanServer.Fingerprint,
                                        lanServer.Port,
                                        LanNetworkDiscovery.GetReachableAddresses(),
                                        key);
                                    await c.SendAsync(LogicalChannel.Control, Message.Create("transport.wifi.provision", provision), session.Token);
                                }
                                catch { /* Older iOS agents without Wi-Fi support ignore cleanly. */ }
                            }

                            await ReconcileOnConnectAsync(c, hello.Body, session.Token);
                            await ApplyConfiguredOutputModeOnConnectAsync(c, session.Token);

                            heartbeat = HeartbeatAsync(c, session.Token);
                            mediaLoop = MediaLoopAsync(c, session.Token);
                            telemetryLoop = TelemetryLoopAsync(c, session.Token);
                            if (config.CollectWindowsNotifications) notificationLoop = NotificationLoopAsync(session.Token);
                            audioLoop = AudioLoopAsync(c, routeKind, session.Token);

                            // When running over Wi-Fi in "auto" mode, automatically preempt back to USB if plugged in.
                            if (config.Transport == "auto" && routeKind is TransportRouteKind.WifiLan or TransportRouteKind.Tailscale)
                                usbPreempt = UsbPreemptMonitorAsync(session);

                            var raced = usbPreempt is not null
                                ? Task.WhenAny(reader, heartbeat, audioLoop, apiTask, usbPreempt)
                                : Task.WhenAny(reader, heartbeat, audioLoop, apiTask);
                            var completed = await raced;
                            await completed;
                        }
                        finally
                        {
                            Router.OnTransportDisconnected();
                            ActiveRoute.Clear();
                            SafeRestorePcMute();
                            session.Cancel();
                            SignalAudioPolicyChange();
                            worker = null;
                            connection = null;
                            foreach (var t in new[] { reader, heartbeat, mediaLoop, telemetryLoop, audioLoop, notificationLoop, usbPreempt })
                                if (t != null) try { await t; } catch (Exception) when (session.IsCancellationRequested) { }
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    SafeRestorePcMute();
                    SetState("disconnected", ex.Message);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    SafeRestorePcMute();
                    break;
                }
                finally
                {
                    if (tlsSession is not null) await tlsSession.DisposeAsync();
                    worker = null;
                    connection = null;
                    ActiveRoute.Clear();
                    SafeRestorePcMute();
                }

                int delay = Math.Min(5000, 250 * (1 << Math.Min(attempt++, 4))) + Random.Shared.Next(100);
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            queue.Writer.TryComplete();
            try { await Task.WhenAll(commandWorkers); } catch { }
            commandQueue = null;
            SafeRestorePcMute();
            api?.Stop();
            try { await apiTask; } catch (Exception) when (ct.IsCancellationRequested) { }
            if (mdns is not null) { await mdns.DisposeAsync(); mdns = null; }
            if (lanServer is not null) { await lanServer.DisposeAsync(); lanServer = null; }
            SetState("stopped");
        }
    }

    private async Task<(Stream Stream, string Serial)?> TryConnectUsbAsync(TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var mux = new UsbMux();
            var devices = await mux.ListAsync(cts.Token);
            if (devices.Count == 0) return null;
            if (config.DeviceSerial is null && devices.Count > 1)
                throw new IOException("Multiple USB devices; set deviceSerial explicitly");
            var device = devices.FirstOrDefault(d => config.DeviceSerial is null || d.Serial == config.DeviceSerial);
            if (device is null) return null;
            var stream = await mux.ConnectAsync(device.Id, config.DevicePort, cts.Token);
            return (stream, device.Serial);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private async Task UsbPreemptMonitorAsync(CancellationTokenSource sessionCts)
    {
        var mux = new UsbMux();
        while (!sessionCts.IsCancellationRequested)
        {
            await Task.Delay(2500, sessionCts.Token);
            try
            {
                using var probe = CancellationTokenSource.CreateLinkedTokenSource(sessionCts.Token);
                probe.CancelAfter(1000);
                var devices = await mux.ListAsync(probe.Token);
                bool matched = devices.Any(d => config.DeviceSerial is null || d.Serial == config.DeviceSerial);
                if (matched)
                {
                    Log?.Invoke("USB iPhone plugged in — switching seamlessly from Wi-Fi back to USB");
                    sessionCts.Cancel();
                    return;
                }
            }
            catch (OperationCanceledException) when (sessionCts.IsCancellationRequested) { return; }
            catch { }
        }
    }

    private async Task RunCommandWorkerAsync(ChannelReader<Func<Task>> reader, CancellationToken ct)
    {
        try
        {
            await foreach (var work in reader.ReadAllAsync(ct))
            {
                try { await work(); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { Log?.Invoke("Command dispatch error: " + ex.Message); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void DispatchBounded(Func<Task> work)
    {
        var q = commandQueue;
        if (q is not null && q.Writer.TryWrite(work)) return;
        _ = Task.Run(work, CancellationToken.None);
    }

    private void SafeRestorePcMute()
    {
        try { endpointAudio.RestoreMuteIfSaved(); } catch { }
    }

    private async Task ApplyConfiguredOutputModeOnConnectAsync(Connection c, CancellationToken ct)
    {
        AudioOutputMode currentMode;
        lock (stateGate) currentMode = audioOutputMode;
        if (currentMode == AudioOutputMode.PhoneOnly)
            endpointAudio.EnsureMutedForPhoneOnly();
        else
            endpointAudio.RestoreMuteIfSaved();
        Router.SetAudioOutputMode(currentMode);
        await PushAudioControlStateAsync(c, ct);
    }

    private async Task PushAudioControlStateAsync(Connection? c, CancellationToken ct)
    {
        var ac = CurrentAudioControlState();
        AudioControlChanged?.Invoke(ac);
        if (c != null)
        {
            try { await c.SendAsync(LogicalChannel.Control, Message.Create("audio.control.state", ac), ct); }
            catch { }
        }
    }

    private async Task ReconcileOnConnectAsync(Connection c, JsonElement helloBody, CancellationToken ct)
    {
        bool supportsSync = false;
        if (helloBody.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
            supportsSync = caps.EnumerateArray().Any(x => x.GetString() is "hybrid-media" or "sync");
        if (!supportsSync) return;
        try
        {
            var syncReply = await c.RequestAsync(LogicalChannel.Media, "media.sync", Router.BuildHostSyncPayload(), ct, TimeSpan.FromSeconds(5));
            var agentSync = syncReply.Body.Deserialize<AgentMediaSync>(Message.Json);
            if (agentSync != null) Router.ApplyAgentSync(agentSync);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log?.Invoke("Media sync unavailable: " + ex.Message);
        }
    }

    private async Task AudioLoopAsync(Connection c, TransportRouteKind route, CancellationToken ct)
    {
        audio = new LoopbackBridge();
        var profile = AdaptiveAudioProfile.ForRoute(route);
        while (!ct.IsCancellationRequested)
        {
            if (!config.StreamAudio || !Router.ShouldStreamLivePcm)
            {
                await audioPolicySignal.WaitAsync(ct);
                continue;
            }
            using var slice = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Volatile.Write(ref liveAudioSliceCts, slice);
            try
            {
                float currentGain;
                lock (stateGate) currentGain = phoneAppGain;
                if (route is TransportRouteKind.Usb or TransportRouteKind.WifiLan or TransportRouteKind.Tailscale)
                    await c.RequestAsync(LogicalChannel.Control, "audio.volume", new { value = currentGain }, slice.Token);
                await audio.RunAsync(c, config.FixtureAudio, slice.Token, audioProfile: profile, enableSilenceSleep: !config.FixtureAudio);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && !Router.ShouldStreamLivePcm)
            {
                try { await c.RequestAsync(LogicalChannel.Control, "audio.stop", new { }, ct, TimeSpan.FromSeconds(3)); } catch { }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && (ex is IOException or OperationCanceledException))
            {
                lock (stateGate) audioError = ex.Message;
                Log?.Invoke("Audio unavailable; control remains connected: " + ex.Message);
                await Task.Delay(Timeout.Infinite, ct);
            }
            finally
            {
                Interlocked.CompareExchange(ref liveAudioSliceCts, null, slice);
            }
        }
    }

    private async Task HeartbeatAsync(Connection c, CancellationToken ct)
    {
        while (true)
        {
            await Task.Delay(3000, ct);
            long started = Stopwatch.GetTimestamp();
            await c.RequestAsync(LogicalChannel.Control, "ping", new { }, ct, TimeSpan.FromSeconds(5));
            ActiveRoute.RecordRtt(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private int GetTelemetryIntervalMs()
    {
        if (config.Transport == "localhost") return 1000;
        ClientViewportState vp;
        PowerProfile prof;
        lock (stateGate) { vp = viewport; prof = powerProfile; }
        if (vp.ScreenDimmed || string.Equals(vp.Tab, "ambient", StringComparison.OrdinalIgnoreCase))
            return prof == PowerProfile.Performance ? 5000 : 12_000;
        return prof switch
        {
            PowerProfile.Eco => 5000,
            PowerProfile.Performance => 1000,
            _ => 2500
        };
    }

    private async Task TelemetryLoopAsync(Connection c, CancellationToken ct)
    {
        string? lastCapStatus = null;
        bool lastAutoPip = false;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snap = await telemetryProvider.SampleAsync(ct);
                lock (stateGate) latestTelemetry = snap;
                await c.SendAsync(LogicalChannel.Diagnostics, Message.Create("system.telemetry", snap), ct);

                var ac = CurrentAudioControlState();
                if (ac.CaptureSignalStatus != lastCapStatus)
                {
                    lastCapStatus = ac.CaptureSignalStatus;
                    await PushAudioControlStateAsync(c, ct);
                }

                // Piggyback Eco Auto-PiP advisor, Automation Rules, and Charge Guard onto this single timer tick (0 extra threads!).
                var mediaState = Router.CurrentState;
                bool windowsPlaying = mediaState.Owner == PlaybackOwner.WindowsOwned;
                var hostLoad = new HostLoadMetrics(snap.CpuUsagePercent ?? 0, snap.MemoryUsedPercent ?? 0, PowerProfile, windowsPlaying);
                bool recommendPip = Offload.UpdateAutoPipAdvisor(hostLoad, config.Offload);
                if (recommendPip && !lastAutoPip)
                {
                    try
                    {
                        await c.SendAsync(LogicalChannel.Control, Message.Create("offload.pip.suggest", new
                        {
                            cpuPercent = snap.CpuUsagePercent,
                            reason = "Sustained high PC CPU while playing video — tap to hand off video to iPhone"
                        }), ct);
                    }
                    catch { }
                }
                lastAutoPip = recommendPip;

                if (config.Rules.Length > 0)
                {
                    var route = ActiveRoute.Snapshot.Route;
                    var ruleInput = new RuleEvaluationInput(
                        CpuUsagePercent: snap.CpuUsagePercent,
                        MemoryUsedPercent: snap.MemoryUsedPercent,
                        MediaPlaying: windowsPlaying || mediaState.IsPlaying,
                        PhoneBatteryPercent: ActiveDevice?.BatteryLevel,
                        OnWifiRoute: route is TransportRouteKind.WifiLan or TransportRouteKind.Tailscale);

                    var cmds = Rules.Evaluate(config.Rules, ruleInput);
                    foreach (var cmd in cmds)
                    {
                        try
                        {
                            await c.SendAsync(LogicalChannel.Control, Message.Create("rule.action", cmd), ct);
                        }
                        catch { }
                    }
                }

                if (config.ChargeGuard.Enabled)
                {
                    _ = await ChargeGuard.EvaluateAndApplyAsync(
                        config.ChargeGuard,
                        async (actId, token) => { _ = await ActionAsync(actId, token); },
                        ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) when (!ct.IsCancellationRequested) { Log?.Invoke("Telemetry sample skipped: " + ex.Message); }

            await Task.Delay(GetTelemetryIntervalMs(), ct);
        }
    }

    private async Task MediaLoopAsync(Connection c, CancellationToken ct)
    {
        string? lastFingerprint = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snap = await media.SnapshotAsync();
                if (snap is null && Spotify.IsAuthenticated)
                {
                    try
                    {
                        var sp = await Spotify.GetPlaybackStateAsync(ct);
                        if (sp != null)
                        {
                            snap = new MediaSnapshot("spotify.webapi", sp.Title, sp.Artist, sp.Album, sp.IsPlaying ? "Playing" : "Paused", sp.PositionSeconds, sp.DurationSeconds, sp.CanControl, null, sp.CanControl, sp.CanControl, sp.CanControl, sp.CanControl);
                        }
                    }
                    catch { /* Do not interrupt session loop on transient Spotify polling failure. */ }
                }
                Router.UpdateWindowsObservation(snap);
                if (!Router.ShouldSuppressWindowsMediaPush)
                {
                    var current = Router.CurrentState;
                    string fp = $"{current.Owner}|{current.Mode}|{current.Generation}|{current.Provider}|{snap?.Source}|{snap?.Title}|{snap?.Artist}|{snap?.State}|{(int)(snap?.PositionSeconds ?? 0)}";
                    if (config.Transport == "localhost" || !string.Equals(fp, lastFingerprint, StringComparison.Ordinal))
                    {
                        lastFingerprint = fp;
                        await c.SendAsync(LogicalChannel.Media, Message.Create("media.state", new
                        {
                            owner = current.Owner.ToString(),
                            mode = current.Mode.ToString(),
                            generation = current.Generation,
                            provider = current.Provider,
                            session = snap
                        }), ct);
                    }
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { Log?.Invoke("Media update: " + ex.Message); }

            int waitMs = config.Transport == "localhost"
                ? 1000
                : PowerProfile == PowerProfile.Eco ? 4000 : 2000;
            try { await mediaChangedSignal.WaitAsync(waitMs, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }

    private async Task NotificationLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await collector.CollectAsync(async (e, token) => { await PublishAsync(e, token); }, ct, config);
                await Task.Delay(5000, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Log?.Invoke("Windows notification collection unavailable: " + ex.Message + "; use the opt-in /v1/events API"); }
    }

    private async ValueTask ReceiveAsync(Frame f, Message? m, CancellationToken ct)
    {
        worker?.Receive(f, m); if (m is null) return;
        if (m.Type == "audio.error")
        {
            string error = m.Body.TryGetProperty("error", out var value) ? value.GetString() ?? "Receiver audio failed" : "Receiver audio failed";
            lock (stateGate) audioError = error; Log?.Invoke("Audio receiver: " + error); return;
        }
        if (m.Type == "job.progress") { var s = m.Body.Deserialize<JobState>(Message.Json); if (s != null) JobProgress?.Invoke(s); return; }

        if (m.Type == "client.viewport")
        {
            string tab = m.Body.TryGetProperty("tab", out var tEl) ? tEl.GetString() ?? "now" : "now";
            bool awake = !m.Body.TryGetProperty("awake", out var aEl) || aEl.GetBoolean();
            bool dimmed = m.Body.TryGetProperty("screenDimmed", out var dEl) && dEl.GetBoolean();
            lock (stateGate) viewport = new ClientViewportState(tab, awake, dimmed);
            return;
        }

        if (m.Type == "phone.health")
        {
            int? bat = m.Body.TryGetProperty("batteryLevel", out var bEl) && bEl.TryGetInt32(out int b) ? b : null;
            bool? chg = m.Body.TryGetProperty("batteryCharging", out var cEl) && cEl.ValueKind is JsonValueKind.True or JsonValueKind.False ? cEl.GetBoolean() : null;
            string? thermal = m.Body.TryGetProperty("thermalState", out var thEl) ? thEl.GetString() : null;
            if (bat is int lv && chg is bool isChg)
                ChargeGuard.RecordPhoneBattery(lv, isChg, thermal);
            if (ActiveRoute.Snapshot.ActiveDeviceId is string devId)
                Devices.UpdateHealth(devId, thermal, bat, chg);
            return;
        }

        if (m.Type == "audio.mixer.list")
        {
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    var sessions = ListPerAppAudio();
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", new { sessions }, replyTo: m.Id), ct);
                }
                catch { }
            });
            return;
        }

        if (m.Type == "audio.mixer.set")
        {
            int pid = m.Body.TryGetProperty("processId", out var pEl) && pEl.TryGetInt32(out int p) ? p : 0;
            float? vol = m.Body.TryGetProperty("volume", out var vEl) && vEl.ValueKind == JsonValueKind.Number ? (float)vEl.GetDouble() : null;
            bool? muted = m.Body.TryGetProperty("muted", out var muEl) && muEl.ValueKind is JsonValueKind.True or JsonValueKind.False ? muEl.GetBoolean() : null;
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    var updated = SetPerAppAudio(pid, vol, muted);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                    {
                        if (updated is not null) await conn.SendAsync(f.Channel, Message.Create("ok", updated, replyTo: m.Id), ct);
                        else await conn.SendAsync(f.Channel, Message.Create("error", new { error = "Audio session not found" }, replyTo: m.Id), ct);
                    }
                }
                catch { }
            });
            return;
        }

        if (m.Type == "system.gpu")
        {
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    var gpus = SampleGpu();
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", new { gpus }, replyTo: m.Id), ct);
                }
                catch { }
            });
            return;
        }

        if (m.Type == "system.processes")
        {
            int limit = m.Body.TryGetProperty("limit", out var lEl) && lEl.TryGetInt32(out int lv) ? lv : 10;
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    var procs = SampleProcesses(limit);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", new { processes = procs }, replyTo: m.Id), ct);
                }
                catch { }
            });
            return;
        }

        if (m.Type == "system.dev")
        {
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    var dev = await SampleDevSessionsAsync(ct);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", dev, replyTo: m.Id), ct);
                }
                catch { }
            });
            return;
        }

        if (m.Type == "feed.proxy")
        {
            string? feedUrl = m.Body.TryGetProperty("url", out var uEl) ? uEl.GetString() : null;
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    var res = await FetchSafeFeedAsync(feedUrl, ct);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                    {
                        if (res.Error is null)
                            await conn.SendAsync(f.Channel, Message.Create("ok", new { status = res.StatusCode, contentType = res.ContentType, bodyBase64 = Convert.ToBase64String(res.Body) }, replyTo: m.Id), ct);
                        else
                            await conn.SendAsync(f.Channel, Message.Create("error", new { status = res.StatusCode, error = res.Error }, replyTo: m.Id), ct);
                    }
                }
                catch { }
            });
            return;
        }

        if (m.Type == "rules.pause")
        {
            int minutes = m.Body.TryGetProperty("minutes", out var minEl) && minEl.TryGetInt32(out int mv) ? mv : 60;
            PauseRules(minutes);
            var conn = connection;
            if (conn != null && !string.IsNullOrEmpty(m.Id))
                await conn.SendAsync(f.Channel, Message.Create("ok", GetRulesStatus(), replyTo: m.Id), ct);
            return;
        }

        if (m.Type == "media.handoff.status")
        {
            var update = m.Body.Deserialize<HandoffStatusUpdate>(Message.Json);
            if (update != null) DispatchBounded(async () => { try { await Router.ProcessHandoffStatusAsync(update, ct); } catch { } });
            return;
        }
        if (m.Type == "media.player.state")
        {
            string sid = m.Body.TryGetProperty("sessionId", out var pSid) ? pSid.GetString() ?? "" : "";
            long gen = m.Body.TryGetProperty("generation", out var pGen) ? pGen.GetInt64() : 0;
            bool playing = m.Body.TryGetProperty("isPlaying", out var pPlay) && pPlay.GetBoolean();
            double pos = m.Body.TryGetProperty("positionSeconds", out var pPos) ? pPos.GetDouble() : 0;
            double dur = m.Body.TryGetProperty("durationSeconds", out var pDur) ? pDur.GetDouble() : 0;
            double buf = m.Body.TryGetProperty("bufferedSeconds", out var pBuf) ? pBuf.GetDouble() : 0;
            bool ready = m.Body.TryGetProperty("readyForDisplay", out var pReady) && pReady.GetBoolean();
            int vw = m.Body.TryGetProperty("videoWidth", out var pVw) ? pVw.GetInt32() : 0;
            int vh = m.Body.TryGetProperty("videoHeight", out var pVh) ? pVh.GetInt32() : 0;
            Router.ProcessPlayerTelemetry(sid, gen, playing, pos, dur, buf, ready, vw, vh);
            return;
        }
        if (m.Type == "media.buffered.request")
        {
            var conn = connection; if (conn is null) return;
            if (!await bufferedRequests.WaitAsync(0, ct)) return;
            _ = Task.Run(async () =>
            {
                try
                {
                    string streamId = m.Body.GetProperty("streamId").GetString()!;
                    long gen = m.Body.GetProperty("generation").GetInt64();
                    long offset = m.Body.GetProperty("offset").GetInt64();
                    int len = m.Body.TryGetProperty("length", out var l) ? l.GetInt32() : BufferedMediaSource.MaxChunkBytes;
                    var res = await Router.ServeBufferedChunkAsync(streamId, gen, offset, len, conn, ct);
                    if (!string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create(res.Found ? "ok" : "error", res.Found ? res : new { error = res.Error }, replyTo: m.Id), ct);
                }
                catch { }
                finally { bufferedRequests.Release(); }
            }, CancellationToken.None);
            return;
        }
        if (m.Type == "media.return_to_windows")
        {
            bool resume = m.Body.TryGetProperty("resumeWindows", out var r) && r.GetBoolean();
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    var settled = await Router.ReturnToWindowsAsync(null, resume, ct);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", settled, replyTo: m.Id), ct);
                }
                catch { }
            });
            return;
        }
        if (m.Type == "media.focus.set")
        {
            string? pol = m.Body.TryGetProperty("policy", out var pPol) ? pPol.GetString() : null;
            if (!string.IsNullOrWhiteSpace(pol) && Enum.TryParse<AudioFocusPolicy>(pol, true, out var parsed))
            {
                var conn = connection;
                DispatchBounded(async () =>
                {
                    try
                    {
                        var updated = await Router.SetFocusPolicyAsync(parsed, null, ct);
                        if (conn != null && !string.IsNullOrEmpty(m.Id))
                            await conn.SendAsync(f.Channel, Message.Create("ok", updated, replyTo: m.Id), ct);
                    }
                    catch { }
                });
            }
            return;
        }

        if (m.Type == "audio.output.set")
        {
            string? modeRaw = m.Body.TryGetProperty("mode", out var mEl) ? mEl.GetString() : null;
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    if (!Enum.TryParse<AudioOutputMode>(modeRaw, true, out var parsed) || !Enum.IsDefined(parsed))
                    {
                        if (conn != null && !string.IsNullOrEmpty(m.Id))
                            await conn.SendAsync(f.Channel, Message.Create("error", new { error = "Invalid audio output mode" }, replyTo: m.Id), ct);
                        return;
                    }
                    var ac = await SetAudioOutputModeAsync(parsed, ct);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", ac, replyTo: m.Id), ct);
                }
                catch { }
            });
            return;
        }
        if (m.Type == "audio.volume.set")
        {
            float val = m.Body.TryGetProperty("value", out var vEl) && vEl.ValueKind == JsonValueKind.Number ? (float)vEl.GetDouble() : float.NaN;
            string? layer = m.Body.TryGetProperty("layer", out var lEl) ? lEl.GetString() : null;
            var conn = connection;
            DispatchBounded(async () =>
            {
                try
                {
                    if (!float.IsFinite(val) || val is < 0f or > 1f)
                    {
                        if (conn != null && !string.IsNullOrEmpty(m.Id))
                            await conn.SendAsync(f.Channel, Message.Create("error", new { error = "Volume must be between 0 and 1" }, replyTo: m.Id), ct);
                        return;
                    }
                    var ac = await SetVolumeAsync(val, layer, ct);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", ac, replyTo: m.Id), ct);
                }
                catch (Exception ex)
                {
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        try { await conn.SendAsync(f.Channel, Message.Create("error", new { error = ex.Message }, replyTo: m.Id), ct); } catch { }
                }
            });
            return;
        }
        if (m.Type == "audio.control.get")
        {
            var conn = connection;
            if (conn != null && !string.IsNullOrEmpty(m.Id))
                await conn.SendAsync(f.Channel, Message.Create("ok", CurrentAudioControlState(), replyTo: m.Id), ct);
            return;
        }

        if (m.Type is not ("media.command" or "action.invoke")) return;
        // Dispatch without blocking the protocol reader, since actions can issue agent requests.
        if (!await actions.WaitAsync(0, ct)) { await connection!.SendAsync(f.Channel, Message.Create("error", new { error = "An action is already running" }, replyTo: m.Id), ct); return; }
        DispatchBounded(ExecuteAndReplyAsync);
        async Task ExecuteAndReplyAsync()
        {
            var c = connection;
            try
            {
                object result;
                if (m.Type == "media.command")
                {
                    string cmd = m.Body.GetProperty("command").GetString()!;
                    double sec = m.Body.TryGetProperty("seconds", out var s) ? s.GetDouble() : 0;
                    string? src = m.Body.TryGetProperty("source", out var sr) ? sr.GetString() : null;
                    bool success = await ExecuteCommandDirectAsync(cmd, sec, src, ct);
                    result = new { success };
                }
                else
                {
                    result = await ActionAsync(m.Body.GetProperty("actionId").GetString()!, ct);
                }
                if (c != null) await c.SendAsync(f.Channel, Message.Create("ok", result, replyTo: m.Id), ct);
            }
            catch (Exception ex) { if (c != null) try { await c.SendAsync(f.Channel, Message.Create("error", new { error = ex.Message }, replyTo: m.Id), ct); } catch (Exception) when (ct.IsCancellationRequested) { } }
            finally { actions.Release(); }
        }
    }

    private async Task<bool> ExecuteCommandDirectAsync(string command, double seconds, string? source, CancellationToken ct)
    {
        if (string.Equals(source, "spotify.webapi", StringComparison.OrdinalIgnoreCase) && Spotify.IsAuthenticated)
        {
            switch (command)
            {
                case "play": await Spotify.PlayAsync(ct: ct); return true;
                case "pause": await Spotify.PauseAsync(ct); return true;
                case "toggle":
                    var st = await Spotify.GetPlaybackStateAsync(ct);
                    if (st?.IsPlaying == true) await Spotify.PauseAsync(ct);
                    else await Spotify.PlayAsync(ct: ct);
                    return true;
                case "next": await Spotify.NextAsync(ct); return true;
                case "previous": await Spotify.PreviousAsync(ct); return true;
                case "seek": await Spotify.SeekAsync(seconds, ct); return true;
            }
        }
        return await media.CommandAsync(command, seconds, source);
    }

    private async Task PauseWindowsSourceAsync(string? source)
    {
        await ExecuteCommandDirectAsync("pause", 0, source, CancellationToken.None);
    }

    private async Task ResumeWindowsSourceAsync(string? source, double? positionSeconds)
    {
        if (positionSeconds is > 0)
            await ExecuteCommandDirectAsync("seek", positionSeconds.Value, source, CancellationToken.None);
        await ExecuteCommandDirectAsync("play", 0, source, CancellationToken.None);
    }

    private async Task ExecuteWindowsMediaCommandAsync(string command, string? secondsStr, string? source)
    {
        double sec = double.TryParse(secondsStr, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;
        await ExecuteCommandDirectAsync(command, sec, source, CancellationToken.None);
    }

    public async Task<object> ActionAsync(string id, CancellationToken ct)
    {
        var action = config.Actions.SingleOrDefault(a => a.Id == id) ?? throw new UnauthorizedAccessException("Action is not in the local allowlist");
        switch (action.Kind)
        {
            case "media.toggle": return new { success = await media.CommandAsync("toggle") };
            case "microphone.toggleMute":
                if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Microphone mute requires Windows");
                return ToggleWindowsMicrophoneMute();
            case "launch":
                if (action.Executable is null || !Path.IsPathFullyQualified(action.Executable) || !File.Exists(action.Executable) || !action.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Configure a full path to an executable");
                var start = new ProcessStartInfo(action.Executable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in action.Arguments ?? []) start.ArgumentList.Add(arg);
                using (var process = Process.Start(start))
                {
                    WindowsJobObject.Track(process);
                    return new { launched = process != null };
                }
            default: throw new InvalidDataException("Unsupported action kind");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static object ToggleWindowsMicrophoneMute()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        mic.AudioEndpointVolume.Mute = !mic.AudioEndpointVolume.Mute;
        return new { muted = mic.AudioEndpointVolume.Mute };
    }

    public async Task<object> PublishAsync(CompanionEvent e, CancellationToken ct)
    {
        var accepted = events.Accept(e); if (accepted is null) return new { accepted = false };
        var c = connection ?? throw new IOException("Agent is not connected");
        return (await c.RequestAsync(LogicalChannel.Notifications, "event.publish", accepted, ct)).Body;
    }

    public CompanionEvent[] History() => events.History();

    public Task<object> HandoffAsync(string url, CancellationToken ct) =>
        SubmitMediaTaskAsync(new MediaSubmissionRequest(Url: url, Mode: PlaybackMode.Auto, QualityCap: config.DefaultQualityCap, KeepScreenAwake: config.KeepScreenAwakeDuringVideo, AutoPauseWindowsOnConfirm: config.PauseWindowsOnConfirmedHandoff), ct);

    public Task<MediaSessionState> SubmitMediaAsync(MediaSubmissionRequest request, CancellationToken ct)
    {
        var resolved = request with
        {
            QualityCap = request.QualityCap ?? config.DefaultQualityCap,
            KeepScreenAwake = request.KeepScreenAwake ?? config.KeepScreenAwakeDuringVideo
        };
        return Router.SubmitAsync(resolved, connection, ct);
    }

    private async Task<object> SubmitMediaTaskAsync(MediaSubmissionRequest req, CancellationToken ct) => await SubmitMediaAsync(req, ct);

    public Task<CapabilityEvaluation> InspectCapabilitiesAsync(MediaSubmissionRequest request, CancellationToken ct) =>
        Router.EvaluateCapabilitiesAsync(request, ct);

    public Task<MediaSessionState> CancelMediaHandoffAsync(string? reason, CancellationToken ct) =>
        Router.CancelHandoffAsync(connection, reason, ct);

    public Task<MediaSessionState> ReturnToWindowsAsync(bool resumeWindows, CancellationToken ct) =>
        Router.ReturnToWindowsAsync(connection, resumeWindows, ct);

    public Task<MediaSessionState> SetFocusPolicyAsync(AudioFocusPolicy policy, CancellationToken ct) =>
        Router.SetFocusPolicyAsync(policy, connection, ct);

    public Task<object> MediaCommandAsync(string command, double seconds, string? qualityCap, bool? keepScreenAwake, string? target, CancellationToken ct) =>
        Router.DispatchMediaCommandAsync(command, seconds, qualityCap, keepScreenAwake, target, connection, ct);

    public async Task<AudioControlState> SetAudioOutputModeAsync(AudioOutputMode mode, CancellationToken ct)
    {
        if (!Enum.IsDefined(mode)) throw new InvalidDataException("Invalid audio output mode");
        await audioControlGate.WaitAsync(ct);
        try
        {
            lock (stateGate) audioOutputMode = mode;
            if (mode == AudioOutputMode.PhoneOnly)
                endpointAudio.EnsureMutedForPhoneOnly();
            else
                endpointAudio.RestoreMuteIfSaved();

            Router.SetAudioOutputMode(mode);
            await PushAudioControlStateAsync(connection, ct);
            return CurrentAudioControlState();
        }
        finally
        {
            audioControlGate.Release();
        }
    }

    public async Task<AudioControlState> SetVolumeAsync(float value, string? layer, CancellationToken ct)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
            throw new InvalidDataException("Volume must be between 0 and 1");
        float clamped = Math.Clamp(value, 0f, 1f);

        await audioControlGate.WaitAsync(ct);
        try
        {
            AudioOutputMode mode;
            lock (stateGate) mode = audioOutputMode;
            string targetLayer = layer is "pc" or "app" or "player"
                ? layer
                : ResolveActiveVolumeLayer("auto", mode, Router.CurrentState.Owner);
            lock (stateGate) preferredVolumeLayer = targetLayer;

            if (targetLayer == "pc")
            {
                endpointAudio.SetVolumeScalar(clamped);
            }
            else if (targetLayer == "app")
            {
                var c = connection;
                if (c != null)
                    await c.RequestAsync(LogicalChannel.Control, "audio.volume", new { value = clamped }, ct);
                lock (stateGate) phoneAppGain = clamped;
            }
            else
            {
                var c = connection;
                if (c != null)
                    await c.RequestAsync(LogicalChannel.Media, "media.volume.set", new { value = clamped }, ct);
                lock (stateGate) phonePlayerVolume = clamped;
            }

            await PushAudioControlStateAsync(connection, ct);
            return CurrentAudioControlState();
        }
        finally
        {
            audioControlGate.Release();
        }
    }

    public Task<string> RequestNotificationAccessAsync() => collector.RequestAccessAsync();
    public string NotificationAccessStatus() => collector.GetAccessStatus();
    public NotificationCollectorDiagnostics NotificationDiagnostics() => collector.GetDiagnostics(config.CollectWindowsNotifications);

    public void Dispose()
    {
        media.Changed -= SignalMediaChanged;
        SafeRestorePcMute();
        endpointAudio.Dispose();
        (telemetryProvider as IDisposable)?.Dispose();
        media.Dispose();
        gpuMonitor.Dispose();
        ChargeGuard.Dispose();
        DevMonitor.Dispose();
        lanCert?.Dispose();
        disposedCts?.Dispose();
        Router.Dispose();
        Spotify.Dispose();
        actions.Dispose();
        bufferedRequests.Dispose();
        audioPolicySignal.Dispose();
        audioControlGate.Dispose();
        mediaChangedSignal.Dispose();
    }
}
