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
    private volatile bool phonePlayback;
    private volatile bool collectNotifications=config.CollectWindowsNotifications;
    public void EnableNotificationCollection(){var permission=NotificationAccess.Inspect();if(!permission.Supported||permission.Status!="Allowed")throw new InvalidOperationException(permission.Reason);collectNotifications=true;}
    private CancellationTokenSource? activeAudio;
    private readonly SemaphoreSlim audioModeChanged=new(0,1);
    private readonly PcMetricsCounter metricsCounter=new();
    public PcMetrics? Metrics {get;private set;}
    private bool supportsApps;
    private int catalogDirty,catalogPublishing;
    private readonly SemaphoreSlim catalogSend=new(1,1);
    private string activeTransport=config.Transport;
    private readonly TemporaryWifiTransport wifi=new(Authentication.LoadKey(config.KeyFile));
    private readonly SemaphoreSlim wifiChanges=new(1,1);
    private TransportLease? prefetchedUsb;
    public object WifiStatus=>wifi.Status;
    public AppCatalog Apps {get;}=new(config.AppCatalogFile??Path.Combine(Path.GetDirectoryName(Path.GetFullPath(config.KeyFile))!,"apps.json"));
    public event Action<string>? Log;
    public event Action<JobState>? JobProgress;
    public object Status
    {
        get
        {
            lock(stateGate) return new
            {
                state,lastError,audioError,transport=activeTransport,simulated=config.Transport=="localhost",wifi=wifi.Status,notifications=NotificationAccess.Inspect(),notificationCollectionEnabled=collectNotifications,appsError=Apps.LoadError,
                device=audio?.DeviceName,captureError=audio?.LastError,audioFrames=audio?.FramesSent??0,
                captureBufferedBytes=audio?.Ring.Count??0,captureDroppedBytes=audio?.Ring.DroppedBytes??0,captureSilenceBytes=audio?.Ring.SilenceBytes??0,
                audioDroppedFrames=connection?.DroppedAudio??0,audioWriteMaximumMilliseconds=connection?.AudioWriteMaximumMilliseconds??0,
                audioSlowWrites=connection?.AudioSlowWrites??0,audioQueueMaximumMilliseconds=connection?.AudioQueueMaximumMilliseconds??0,
                audioPacingMaximumLatenessMilliseconds=audio?.PacingMaximumLatenessMilliseconds??0,
                audioPacingLateFrames=audio?.PacingLateFrames??0,audioPacingResets=audio?.PacingResets??0
            };
        }
    }
    private void SetState(string value, string error = "") { lock (stateGate) { state = value; lastError = error; } Log?.Invoke(value + (error.Length == 0 ? "" : ": " + error)); }
    public RemoteWorker Worker => worker ?? throw new IOException("Agent is not connected");
    public async Task RunAsync(CancellationToken ct)
    {
        Apps.Changed+=CatalogChanged;wifi.Changed+=WifiChanged;
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
                    var lease=await ConnectTransportAsync(connectTimeout.Token);Stream stream=lease.Stream;activeTransport=lease.Name;
                    using (stream)
                    {
                        if(!lease.Authenticated)await Authentication.ClientAsync(stream, key, connectTimeout.Token);
                        await using var c = new Connection(stream); connection = c; worker = new(c);
                        var reader = c.RunAsync(ReceiveAsync, session.Token);
                        Task? heartbeat = null, mediaLoop = null, audioLoop = null, notificationLoop = null;
                        Task<TransportLease>? usbReturn=null;
                        try
                        {
                            var hello = await c.RequestAsync(LogicalChannel.Control, "hello", new { host = "BackThread.Host", protocol = 1 }, session.Token);
                            lock(stateGate)audioError="";
                            phonePlayback=hello.Body.TryGetProperty("playbackOwner",out var owner)&&owner.GetString()=="phone";
                            supportsApps=hello.Body.TryGetProperty("capabilities",out var caps)&&caps.EnumerateArray().Any(x=>x.GetString()=="apps");
                            SetState("connected"); Log?.Invoke("Agent capabilities: " + hello.Body.GetRawText()); attempt = 0;
                            await c.RequestAsync(LogicalChannel.Control, "action.registry", new { actions = config.Actions.Select(a => new { a.Id, a.Title, icon=a.Kind.StartsWith("media.",StringComparison.Ordinal)?"play":"actions" }) }, session.Token);
                            if(supportsApps)await PublishCatalogAsync(c,session.Token);
                            heartbeat = HeartbeatAsync(c, session.Token); mediaLoop = MediaLoopAsync(c, session.Token);
                            notificationLoop = NotificationLoopAsync(session.Token);
                            audioLoop=AudioLoopAsync(c,session.Token);
                            if(activeTransport=="wifi"&&wifi.PreferUsb)usbReturn=WaitForUsbAsync(session.Token);
                            var completed=await Task.WhenAny(reader,heartbeat,audioLoop,apiTask,usbReturn??Task.Delay(Timeout.Infinite,session.Token));
                            if(completed==usbReturn)prefetchedUsb=await usbReturn!;else await completed;
                        }
                        finally
                        {
                            session.Cancel(); worker = null; connection = null;
                            if(usbReturn!=null)try{var returning=await usbReturn;if(prefetchedUsb!=returning){if(ct.IsCancellationRequested)returning.Stream.Dispose();else prefetchedUsb=returning;}}catch(Exception ex)when(ex is IOException or OperationCanceledException or SocketException){ }
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
        finally { Apps.Changed-=CatalogChanged;wifi.Changed-=WifiChanged;wifi.Dispose();prefetchedUsb?.Stream.Dispose();api.Stop(); try { await apiTask; } catch (Exception) when (ct.IsCancellationRequested) { } SetState("stopped"); }
    }
    private async Task<TransportLease> ConnectTransportAsync(CancellationToken ct)
    {
        if(prefetchedUsb is{} cached){prefetchedUsb=null;return cached;}
        if(config.Transport=="localhost")return await new SimulatorTransport(config.DevicePort).ConnectAsync(ct);
        if(wifi.Enabled&&!wifi.PreferUsb)return await wifi.ConnectAsync(ct);
        try{return await new UsbTransport(config.DevicePort,config.DeviceSerial).ConnectAsync(ct);}
        catch(Exception ex)when(wifi.Enabled&&(ex is IOException or SocketException)){return await wifi.ConnectAsync(ct);}
    }
    private async Task<TransportLease> WaitForUsbAsync(CancellationToken ct)
    {
        while(true)
        {
            await Task.Delay(2000,ct);Stream? stream=null;
            try{using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(3));var lease=await new UsbTransport(config.DevicePort,config.DeviceSerial).ConnectAsync(timeout.Token);stream=lease.Stream;await Authentication.ClientAsync(stream,key,timeout.Token);return lease with{Authenticated=true};}
            catch(Exception ex)when(!ct.IsCancellationRequested&&(ex is IOException or SocketException or OperationCanceledException)){stream?.Dispose();}
            catch{stream?.Dispose();throw;}
        }
    }
    public async Task<object> EnableWifiAsync(string address,int minutes,bool fallback,CancellationToken ct)
    {
        if(minutes is not(0 or 15 or 30))throw new InvalidDataException("Choose 15 minutes, 30 minutes or until disconnected");
        await wifiChanges.WaitAsync(ct);
        try
        {
            var c=connection??throw new IOException("Authenticated USB is required for initial Wi-Fi enrollment");
            if(activeTransport!="usb")throw new IOException("Enroll temporary Wi-Fi over authenticated USB");
            var enrollment=wifi.Enable(address,TimeSpan.FromMinutes(minutes),fallback);
            try{var reply=await c.RequestAsync(LogicalChannel.Control,"wifi.enroll",enrollment,ct);if(!reply.Body.TryGetProperty("authorized",out var accepted)||!accepted.GetBoolean())throw new IOException("Phone did not authorize the temporary session");return new{success=true,authorized=true,enrollment};}
            catch{wifi.Disable();throw;}
        }
        finally{wifiChanges.Release();}
    }
    public async Task DisableWifiAsync(CancellationToken ct)
    {
        await wifiChanges.WaitAsync(ct);
        try{var c=connection;if(c!=null)try{await c.RequestAsync(LogicalChannel.Control,"wifi.revoke",new{},ct,TimeSpan.FromSeconds(2));}catch(Exception ex)when(ex is IOException or OperationCanceledException){ }wifi.Disable();}
        finally{wifiChanges.Release();}
    }
    private void WifiChanged(){_ = SendWifiStatusAsync();}
    private async Task SendWifiStatusAsync(){try{var c=connection;if(c!=null)await c.SendAsync(LogicalChannel.Control,Message.Create("wifi.state",wifi.Status),CancellationToken.None);}catch(Exception ex)when(ex is IOException or OperationCanceledException){ }}
    private async Task AudioLoopAsync(Connection c,CancellationToken ct)
    {
        if(!config.StreamAudio||activeTransport=="wifi"){audio=null;await Task.Delay(Timeout.Infinite,ct);return;}
        while(!ct.IsCancellationRequested)
        {
            if(phonePlayback){await audioModeChanged.WaitAsync(ct);continue;}
            using var mode=CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock(stateGate)activeAudio=mode;
            audio=new LoopbackBridge();
            try
            {
                if(phonePlayback)continue;
                if(activeTransport=="usb")await c.RequestAsync(LogicalChannel.Control,"audio.volume",new{value=config.AudioGain},mode.Token);
                await audio.RunAsync(c,config.FixtureAudio,mode.Token);
            }
            catch(OperationCanceledException)when(mode.IsCancellationRequested&&!ct.IsCancellationRequested) { }
            catch(Exception ex)when(!ct.IsCancellationRequested&&(ex is IOException or OperationCanceledException))
            {
                lock(stateGate)audioError=ex.Message;
                Log?.Invoke("Audio unavailable; control remains connected: "+ex.Message);
                await audioModeChanged.WaitAsync(ct);
            }
            finally {lock(stateGate)if(activeAudio==mode)activeAudio=null;}
        }
        ct.ThrowIfCancellationRequested();
    }
    private void ChangePlaybackOwner(string owner)
    {
        if(owner is not("phone" or "pc"))throw new InvalidDataException("Unknown playback owner");
        CancellationTokenSource? stop;lock(stateGate){phonePlayback=owner=="phone";audioError="";stop=activeAudio;}try{stop?.Cancel();}catch(ObjectDisposedException){ }
        if(audioModeChanged.CurrentCount==0)audioModeChanged.Release();
    }
    private void CatalogChanged(){Interlocked.Exchange(ref catalogDirty,1);if(Interlocked.CompareExchange(ref catalogPublishing,1,0)==0)_=PublishChangedCatalogAsync();}
    private async Task PublishChangedCatalogAsync(){try{while(Interlocked.Exchange(ref catalogDirty,0)!=0){var c=connection;if(c!=null&&supportsApps)await PublishCatalogAsync(c,CancellationToken.None);}}catch(Exception ex)when(ex is IOException or OperationCanceledException){Log?.Invoke("App catalog update unavailable");}finally{Interlocked.Exchange(ref catalogPublishing,0);if(Volatile.Read(ref catalogDirty)!=0)CatalogChanged();}}
    private async Task PublishCatalogAsync(Connection c,CancellationToken ct)
    {
        await catalogSend.WaitAsync(ct);try{var entries=await Apps.WireCatalogAsync();
        if(entries.Length==0)await c.SendAsync(LogicalChannel.Control,Message.Create("apps.catalog",new{reset=true,apps=Array.Empty<object>()}),ct);
        for(int i=0;i<entries.Length;i+=4)await c.SendAsync(LogicalChannel.Control,Message.Create("apps.catalog",new{reset=i==0,apps=entries.Skip(i).Take(4).ToArray()}),ct);
        }finally{catalogSend.Release();}
    }
    public async Task<JsonElement> InspectUiAsync(JsonElement body,CancellationToken ct)=>(await (connection??throw new IOException("Agent is not connected")).RequestAsync(LogicalChannel.Diagnostics,"ui.inspect",body,ct)).Body;
    private static async Task HeartbeatAsync(Connection c, CancellationToken ct)
    {
        while (true) { await Task.Delay(3000, ct); await c.RequestAsync(LogicalChannel.Control, "ping", new { }, ct, TimeSpan.FromSeconds(5)); }
    }
    private async Task MediaLoopAsync(Connection c, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Metrics=metricsCounter.Sample();await c.SendAsync(LogicalChannel.Media, Message.Create("media.state", new { session = await media.SnapshotAsync(),system=Metrics }), ct); }
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
                if(collectNotifications)await collector.CollectAsync(async (e, token) => { await PublishAsync(e, token); }, ct);
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
        if(m.Type=="media.owner"){ChangePlaybackOwner(m.Body.GetProperty("owner").GetString()!);await connection!.SendAsync(f.Channel,Message.Create("ok",new{success=true,owner=phonePlayback?"phone":"pc"},replyTo:m.Id),ct);return;}
        if(m.Type=="media.playback"){await connection!.SendAsync(f.Channel,Message.Create("ok",new{success=true},replyTo:m.Id),ct);return;}
        if (m.Type is not ("media.command" or "action.invoke" or "apps.launch" or "apps.order" or "apps.unpin" or "apps.refresh" or "wifi.enable" or "wifi.disable")) return;
        // Dispatch without blocking the protocol reader, since actions can issue agent requests.
        if (!await actions.WaitAsync(0, ct)) { await connection!.SendAsync(f.Channel, Message.Create("error", new { error = "An action is already running" }, replyTo: m.Id), ct); return; }
        _ = ExecuteAndReplyAsync();
        async Task ExecuteAndReplyAsync()
        {
            var c = connection;
            try
            {
                object result;
                if(m.Type=="wifi.enable")result=await EnableWifiAsync(m.Body.GetProperty("address").GetString()!,m.Body.GetProperty("minutes").GetInt32(),m.Body.TryGetProperty("fallback",out var fallback)&&fallback.GetBoolean(),ct);
                else if(m.Type=="wifi.disable"){await DisableWifiAsync(ct);result=new{success=true};}
                else if(m.Type=="apps.launch")result=await Apps.LaunchRequestAsync(m.Body,ct);
                else if(m.Type=="apps.order"){if(m.Body.EnumerateObject().Any(p=>p.Name!="appIds"))throw new UnauthorizedAccessException("Only pinned IDs may be reordered");Apps.Reorder(m.Body.GetProperty("appIds").Deserialize<string[]>(Message.Json)??throw new InvalidDataException());result=new{success=true};}
                else if(m.Type=="apps.unpin"){if(m.Body.EnumerateObject().Any(p=>p.Name!="appId"))throw new UnauthorizedAccessException();Apps.Remove(m.Body.GetProperty("appId").GetString()!);result=new{success=true};}
                else if(m.Type=="apps.refresh"){if(c!=null)await PublishCatalogAsync(c,ct);result=new{success=true};}
                else if(m.Type=="media.command"){bool success=await media.CommandAsync(m.Body.GetProperty("command").GetString()!,m.Body.TryGetProperty("seconds",out var seconds)?seconds.GetDouble():0,m.Body.TryGetProperty("source",out var source)?source.GetString():null);result=new{success,error=success?"":"The Windows player did not accept this command. For YouTube, Next requires browser/player support or a playlist."};}
                else result=await ActionAsync(m.Body.GetProperty("actionId").GetString()!,ct);
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
            case "media.next":case "media.previous":case "media.play":case "media.pause":return new{success=await media.CommandAsync(action.Kind[6..])};
            case "media.toggle": return new { success = await media.CommandAsync("toggle") };
            case "microphone.toggleMute":
                using (var enumerator = new MMDeviceEnumerator()) using (var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) { mic.AudioEndpointVolume.Mute = !mic.AudioEndpointVolume.Mute; return new { muted = mic.AudioEndpointVolume.Mute }; }
            case "launch":
                if (action.Executable is null || !Path.IsPathFullyQualified(action.Executable) || !File.Exists(action.Executable) || !action.Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Configure a full path to an executable");
                var start = new ProcessStartInfo(action.Executable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in action.Arguments ?? []) start.ArgumentList.Add(arg);
                using (var process = Process.Start(start)) return new { launched = process != null };
            default: throw new InvalidDataException("Unsupported action kind");
        }
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
