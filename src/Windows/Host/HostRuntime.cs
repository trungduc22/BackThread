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

public sealed class HostRuntime(HostConfig config)
{
    private readonly byte[] key = Authentication.LoadKey(config.KeyFile);
    private readonly MediaSessions media = new();
    private readonly EventRouter events = new() { HideSensitive = config.HideSensitive };
    private readonly SemaphoreSlim actions = new(1, 1);
    private readonly object stateGate = new();
    private Connection? connection;
    private RemoteWorker? worker;
    private string state = "starting", lastError = "", audioError = "";
    private LoopbackBridge? audio;
    public event Action<string>? Log;
    public event Action<JobState>? JobProgress;
    public object Status { get { lock (stateGate) return new { state, lastError, audioError, transport = config.Transport, simulated = config.Transport == "localhost", device = audio?.DeviceName, captureError = audio?.LastError, audioFrames = audio?.FramesSent ?? 0, audioDroppedFrames = connection?.DroppedAudio ?? 0,audioWriteMaximumMilliseconds=connection?.AudioWriteMaximumMilliseconds??0,audioSlowWrites=connection?.AudioSlowWrites??0 }; } }
    private void SetState(string value, string error = "") { lock (stateGate) { state = value; lastError = error; } Log?.Invoke(value + (error.Length == 0 ? "" : ": " + error)); }
    public RemoteWorker Worker => worker ?? throw new IOException("Agent is not connected");
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
                            var hello = await c.RequestAsync(LogicalChannel.Control, "hello", new { host = "BackThread.Host", protocol = 1 }, session.Token);
                            lock(stateGate)audioError="";
                            SetState("connected"); Log?.Invoke("Agent capabilities: " + hello.Body.GetRawText()); attempt = 0;
                            await c.RequestAsync(LogicalChannel.Control, "action.registry", new { actions = config.Actions.Select(a => new { a.Id, a.Title }) }, session.Token);
                            heartbeat = HeartbeatAsync(c, session.Token); mediaLoop = MediaLoopAsync(c, session.Token);
                            if (config.CollectWindowsNotifications) notificationLoop = NotificationLoopAsync(session.Token);
                            audioLoop=AudioLoopAsync(c,session.Token);
                            var completed = await Task.WhenAny(reader, heartbeat, audioLoop, apiTask); await completed;
                        }
                        finally
                        {
                            session.Cancel(); worker = null; connection = null;
                            foreach (var t in new[] { reader, heartbeat, mediaLoop, audioLoop, notificationLoop }) if (t != null) try { await t; } catch (Exception) when (session.IsCancellationRequested) { }
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
    private async Task AudioLoopAsync(Connection c,CancellationToken ct)
    {
        audio=new LoopbackBridge();
        try {
            if(!config.StreamAudio){await Task.Delay(Timeout.Infinite,ct);return;}
            // Restore app gain after quiet tests; never change the phone system volume.
            if(config.Transport=="usb")await c.RequestAsync(LogicalChannel.Control,"audio.volume",new{value=config.AudioGain},ct);
            await audio.RunAsync(c,config.FixtureAudio,ct);
        }catch(Exception ex)when(!ct.IsCancellationRequested&&(ex is IOException or OperationCanceledException)){
            lock(stateGate)audioError=ex.Message;
            Log?.Invoke("Audio unavailable; control remains connected: "+ex.Message);
            // Reader/heartbeat still detect real connection loss. An audio-only startup
            // error must not repeatedly tear down working jobs and media control.
            await Task.Delay(Timeout.Infinite,ct);
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
            try { await c.SendAsync(LogicalChannel.Media, Message.Create("media.state", new { session = await media.SnapshotAsync() }), ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested) { Log?.Invoke("Media update: " + ex.Message); }
            await Task.Delay(1000, ct);
        }
    }
    private async Task NotificationLoopAsync(CancellationToken ct)
    {
        var collector = new NotificationCollector();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await collector.CollectAsync(async (e, token) => { await PublishAsync(e, token); }, ct);
                await Task.Delay(5000, ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { Log?.Invoke("Windows notification collection unavailable: " + ex.Message + "; use the opt-in /v1/events API"); }
    }
    private async ValueTask ReceiveAsync(Frame f, Message? m, CancellationToken ct)
    {
        worker?.Receive(f, m); if (m is null) return;
        if(m.Type=="audio.error") { string error=m.Body.TryGetProperty("error",out var value)?value.GetString()??"Receiver audio failed":"Receiver audio failed";lock(stateGate)audioError=error;Log?.Invoke("Audio receiver: "+error);return; }
        if (m.Type == "job.progress") { var s = m.Body.Deserialize<JobState>(Message.Json); if (s != null) JobProgress?.Invoke(s); return; }
        if (m.Type is not ("media.command" or "action.invoke")) return;
        // Dispatch without blocking the protocol reader, since actions can issue agent requests.
        if (!await actions.WaitAsync(0, ct)) { await connection!.SendAsync(f.Channel, Message.Create("error", new { error = "An action is already running" }, replyTo: m.Id), ct); return; }
        _ = ExecuteAndReplyAsync();
        async Task ExecuteAndReplyAsync()
        {
            var c = connection;
            try
            {
                object result = m.Type == "media.command" ? new { success = await media.CommandAsync(m.Body.GetProperty("command").GetString()!, m.Body.TryGetProperty("seconds", out var s) ? s.GetDouble() : 0, m.Body.TryGetProperty("source", out var src) ? src.GetString() : null) } : await ActionAsync(m.Body.GetProperty("actionId").GetString()!, ct);
                if (c != null) await c.SendAsync(f.Channel, Message.Create("ok", result, replyTo: m.Id), ct);
            }
            catch (Exception ex) { if (c != null) try { await c.SendAsync(f.Channel, Message.Create("error", new { error = ex.Message }, replyTo: m.Id), ct); } catch (Exception) when (ct.IsCancellationRequested) { } }
            finally { actions.Release(); }
        }
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
    public async Task<object> HandoffAsync(string url, CancellationToken ct)
    {
        var result = new DirectMediaProvider().Evaluate(new Uri(url, UriKind.Absolute), config.DeviceHasInternet);
        if (!result.Supported) return result;
        var c = connection ?? throw new IOException("Agent is not connected");
        return (await c.RequestAsync(LogicalChannel.Media, "media.handoff", new { url = result.Url!.AbsoluteUri }, ct)).Body;
    }
}
