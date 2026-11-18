using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using BackThread.Audio;
using BackThread.Events;
using BackThread.Media;
using BackThread.Transport;
using BackThread.Worker;
using NAudio.CoreAudioApi;

namespace BackThread.Host;

public sealed class HostRuntime : IDisposable
{
    private readonly HostConfig config;
    private readonly byte[] key;
    private readonly MediaSessions media = new();
    private readonly EventRouter events = new();
    private readonly NotificationCollector collector = new();
    private readonly SemaphoreSlim actions = new(1, 1);
    private readonly SemaphoreSlim bufferedRequests = new(4, 4);
    private readonly SemaphoreSlim audioPolicySignal = new(0, 1);
    private readonly object stateGate = new();
    private Connection? connection;
    private RemoteWorker? worker;
    private string state = "starting", lastError = "", audioError = "";
    private LoopbackBridge? audio;
    private bool lastLivePcmDesired = true;
    private readonly CancellationTokenSource? disposedCts = new();
    private CancellationTokenSource? liveAudioSliceCts;

    public MediaRouter Router { get; }
    public SpotifyProvider Spotify { get; }
    public HostConfig Config => config;

    public HostRuntime(HostConfig config, ISpotifyTokenStore? spotifyTokens = null, HttpMessageHandler? httpHandler = null, IMediaUrlProber? urlProber = null)
    {
        this.config = config;
        key = Authentication.LoadKey(config.KeyFile);
        events.HideSensitive = config.HideSensitive;

        string defaultTokenPath = config.SpotifyTokenPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BackThread",
            "spotify-token.dat");

        Spotify = new SpotifyProvider(spotifyTokens ?? new LocalSpotifyTokenStore(defaultTokenPath), httpHandler, config.SpotifyClientId);
        var youtube = new YouTubeProvider(httpHandler);
        var prober = urlProber ?? (httpHandler != null ? new HttpMediaUrlProber(httpHandler) : null);
        Router = new MediaRouter(new DirectMediaProvider(prober), youtube, prober, config.DeviceHasInternet, config.AllowExperimentalYouTubeEmbed)
        {
            PauseWindowsMediaAsync = PauseWindowsSourceAsync,
            ResumeWindowsMediaAsync = ResumeWindowsSourceAsync,
            WindowsCommandAsync = ExecuteWindowsMediaCommandAsync
        };
        Router.StateChanged += s =>
        {
            MediaStateChanged?.Invoke(s);
            if (s.ShouldStreamLivePcm != Volatile.Read(ref lastLivePcmDesired))
            {
                Volatile.Write(ref lastLivePcmDesired, s.ShouldStreamLivePcm);
                SignalAudioPolicyChange();
            }
        };
    }

    public event Action<string>? Log;
    public event Action<JobState>? JobProgress;
    public event Action<MediaSessionState>? MediaStateChanged;

    public object Status
    {
        get
        {
            var m = Router.CurrentState;
            lock (stateGate)
                return new
                {
                    state,
                    lastError,
                    audioError,
                    transport = config.Transport,
                    simulated = config.Transport == "localhost",
                    device = audio?.DeviceName,
                    captureError = audio?.LastError,
                    audioFrames = audio?.FramesSent ?? 0,
                    audioDroppedFrames = connection?.DroppedAudio ?? 0,
                    audioWriteMaximumMilliseconds = connection?.AudioWriteMaximumMilliseconds ?? 0,
                    audioSlowWrites = connection?.AudioSlowWrites ?? 0,
                    audioQueueResidenceMaximumMilliseconds = connection?.AudioQueueResidenceMaximumMilliseconds ?? 0,
                    deviceHasInternet = config.DeviceHasInternet,
                    mediaOwner = m.Owner.ToString(),
                    playbackMode = m.Mode.ToString(),
                    handoffStage = m.Stage.ToString(),
                    shouldStreamLivePcm = m.ShouldStreamLivePcm,
                    spotifyConnected = Spotify.IsAuthenticated,
                    notificationAccess = collector.GetAccessStatus()
                };
        }
    }

    private void SetState(string value, string error = "")
    {
        lock (stateGate) { state = value; lastError = error; }
        Log?.Invoke(value + (error.Length == 0 ? "" : ": " + error));
    }

    public RemoteWorker Worker => worker ?? throw new IOException("Agent is not connected");

    private void SignalAudioPolicyChange()
    {
        try { Volatile.Read(ref liveAudioSliceCts)?.Cancel(); } catch (ObjectDisposedException) { }
        if (audioPolicySignal.CurrentCount == 0)
            try { audioPolicySignal.Release(); } catch (SemaphoreFullException) { }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        foreach (string source in config.BlockedSources) events.BlockedSources.Add(source);
        try { await media.InitializeAsync(); } catch (Exception ex) { Log?.Invoke("Media unavailable: " + ex.Message); }
        var api = new LocalApi(this, config.ApiPort, key);
        var apiTask = api.RunAsync(ct); int attempt = 0;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
                try
                {
                    SetState("connecting");
                    using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); connectTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                    Stream stream;
                    if (config.Transport == "localhost")
                    {
                        var client = new TcpClient { NoDelay = true };
                        try { await client.ConnectAsync(IPAddress.Loopback, config.DevicePort, connectTimeout.Token); stream = client.GetStream(); } catch { client.Dispose(); throw; }
                    }
                    else
                    {
                        var mux = new UsbMux(); var devices = await mux.ListAsync(connectTimeout.Token);
                        if (config.DeviceSerial is null && devices.Count > 1) throw new IOException("Multiple USB devices; set deviceSerial explicitly");
                        var device = devices.FirstOrDefault(d => config.DeviceSerial is null || d.Serial == config.DeviceSerial) ?? throw new IOException("Configured USB iPhone not detected");
                        stream = await mux.ConnectAsync(device.Id, config.DevicePort, connectTimeout.Token);
                    }
                    using (stream)
                    {
                        await Authentication.ClientAsync(stream, key, connectTimeout.Token);
                        await using var c = new Connection(stream); connection = c; worker = new(c);
                        var reader = c.RunAsync(ReceiveAsync, session.Token);
                        Task? heartbeat = null, mediaLoop = null, audioLoop = null, notificationLoop = null;
                        try
                        {
                            var hello = await c.RequestAsync(LogicalChannel.Control, "hello", new { host = "BackThread.Host", protocol = 1, capabilities = new[] { "hybrid-media", "buffered-media", "fullscreen-notifications", "sync" } }, session.Token);
                            lock (stateGate) audioError = "";
                            SetState("connected"); Log?.Invoke("Agent capabilities: " + hello.Body.GetRawText()); attempt = 0;
                            await c.RequestAsync(LogicalChannel.Control, "action.registry", new { actions = config.Actions.Select(a => new { a.Id, a.Title }) }, session.Token);
                            await ReconcileOnConnectAsync(c, hello.Body, session.Token);
                            heartbeat = HeartbeatAsync(c, session.Token);
                            mediaLoop = MediaLoopAsync(c, session.Token);
                            if (config.CollectWindowsNotifications) notificationLoop = NotificationLoopAsync(session.Token);
                            audioLoop = AudioLoopAsync(c, session.Token);
                            var completed = await Task.WhenAny(reader, heartbeat, audioLoop, apiTask); await completed;
                        }
                        finally
                        {
                            Router.OnTransportDisconnected();
                            session.Cancel(); SignalAudioPolicyChange(); worker = null; connection = null;
                            foreach (var t in new[] { reader, heartbeat, mediaLoop, audioLoop, notificationLoop })
                                if (t != null) try { await t; } catch (Exception) when (session.IsCancellationRequested) { }
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested) { SetState("disconnected", ex.Message); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                finally { worker = null; connection = null; }
                int delay = Math.Min(5000, 250 * (1 << Math.Min(attempt++, 4))) + Random.Shared.Next(100);
                try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            }
        }
        finally { api.Stop(); try { await apiTask; } catch (Exception) when (ct.IsCancellationRequested) { } SetState("stopped"); }
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

    private async Task AudioLoopAsync(Connection c, CancellationToken ct)
    {
        audio = new LoopbackBridge();
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
                // Restore app gain after quiet tests; never change the phone system volume.
                if (config.Transport == "usb") await c.RequestAsync(LogicalChannel.Control, "audio.volume", new { value = config.AudioGain }, slice.Token);
                await audio.RunAsync(c, config.FixtureAudio, slice.Token);
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

    private static async Task HeartbeatAsync(Connection c, CancellationToken ct)
    {
        while (true) { await Task.Delay(3000, ct); await c.RequestAsync(LogicalChannel.Control, "ping", new { }, ct, TimeSpan.FromSeconds(5)); }
    }

    private async Task MediaLoopAsync(Connection c, CancellationToken ct)
    {
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
            catch (Exception ex) when (!ct.IsCancellationRequested) { Log?.Invoke("Media update: " + ex.Message); }
            await Task.Delay(1000, ct);
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

        if (m.Type == "media.handoff.status")
        {
            var update = m.Body.Deserialize<HandoffStatusUpdate>(Message.Json);
            if (update != null) _ = Task.Run(async () => { try { await Router.ProcessHandoffStatusAsync(update, ct); } catch { } }, CancellationToken.None);
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
            _ = Task.Run(async () =>
            {
                try
                {
                    var settled = await Router.ReturnToWindowsAsync(null, resume, ct);
                    if (conn != null && !string.IsNullOrEmpty(m.Id))
                        await conn.SendAsync(f.Channel, Message.Create("ok", settled, replyTo: m.Id), ct);
                }
                catch { }
            }, CancellationToken.None);
            return;
        }
        if (m.Type == "media.focus.set")
        {
            string? pol = m.Body.TryGetProperty("policy", out var pPol) ? pPol.GetString() : null;
            if (!string.IsNullOrWhiteSpace(pol) && Enum.TryParse<AudioFocusPolicy>(pol, true, out var parsed))
            {
                var conn = connection;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var updated = await Router.SetFocusPolicyAsync(parsed, null, ct);
                        if (conn != null && !string.IsNullOrEmpty(m.Id))
                            await conn.SendAsync(f.Channel, Message.Create("ok", updated, replyTo: m.Id), ct);
                    }
                    catch { }
                }, CancellationToken.None);
            }
            return;
        }

        if (m.Type is not ("media.command" or "action.invoke")) return;
        // Dispatch without blocking the protocol reader, since actions can issue agent requests.
        if (!await actions.WaitAsync(0, ct)) { await connection!.SendAsync(f.Channel, Message.Create("error", new { error = "An action is already running" }, replyTo: m.Id), ct); return; }
        _ = ExecuteAndReplyAsync();
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
                using (var process = Process.Start(start)) return new { launched = process != null };
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

    public Task<string> RequestNotificationAccessAsync() => collector.RequestAccessAsync();
    public string NotificationAccessStatus() => collector.GetAccessStatus();

    public void Dispose()
    {
        disposedCts?.Dispose();
        Router.Dispose();
        Spotify.Dispose();
        actions.Dispose();
        bufferedRequests.Dispose();
        audioPolicySignal.Dispose();
    }
}
