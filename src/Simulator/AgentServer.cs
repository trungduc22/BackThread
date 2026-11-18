using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Channels;
using BackThread.Audio;
using BackThread.Events;
using BackThread.Transport;
using BackThread.Worker;

namespace BackThread.Simulator;

public sealed class AgentServer : IAsyncDisposable
{
    private readonly TcpListener listener;
    private readonly byte[] key;
    private readonly CancellationTokenSource stop = new();
    private readonly Storage storage;
    public JobScheduler Jobs { get; }
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public int DelayMs { get; set; }
    public int FileIoDelayMs { get; set; }
    private int fileOperationsInFlight;
    public int FileOperationsInFlight=>Volatile.Read(ref fileOperationsInFlight);
    public int DisconnectAfterAudioPackets { get; set; }
    public int AuthenticatedConnections { get; private set; }
    public bool SimulateHandoffShouldFail { get; set; }
    public string SimulateHandoffFailureReason { get; set; } = "Simulated AVPlayer decode error";
    public int SimulateHandoffStepDelayMs { get; set; } = 15;
    public string LastFocusPolicy { get; private set; } = "Auto";
    public bool LastMuteVideoAudio { get; private set; }
    public double LastIndependentVolume { get; private set; } = 1.0;
    public double LastAppGainVolume { get; private set; } = 1.0;
    public string LastAudioOutputMode { get; private set; } = "Both";
    public int TelemetryReceivedCount { get; private set; }
    public JsonElement? LastTelemetryReceived { get; private set; }
    public JsonElement? LastAudioControlState { get; private set; }
    public int ReceivedMediaStateCount { get; private set; }
    public long LastBufferedBytesReceived { get; private set; }
    public string? LastBufferedSha256 { get; private set; }
    public event Action<string>? Log;
    private Task? serving;
    private readonly EventRouter events = new();

    // Persistent independent playback state across USB disconnects!
    private readonly object playerGate = new();
    private string? activeSessionId, activeMode, activeUrl, activeTitle;
    private long activeGeneration;
    private bool independentActive, isPlaying;
    private double positionSeconds, durationSeconds = 180, bufferedSeconds = 12;

    public AgentServer(int port, byte[] key, string stateRoot)
    {
        this.key = key;
        listener = new(IPAddress.Loopback, port);
        storage = new(Path.Combine(stateRoot, "files"));
        Jobs = new(storage, Path.Combine(stateRoot, "jobs.json"));
    }

    public void Start() { listener.Start(2); serving = ServeAsync(); }

    private async Task ServeAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token); client.NoDelay = true;
                await Authentication.ServerAsync(client.GetStream(), key, stop.Token);
                AuthenticatedConnections++;
                await using var c = new Connection(client.GetStream());
                using var session = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var ring = new PcmRing(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                bool audioOn = false; long packets = 0, bytes = 0, gaps = 0; uint previous = 0; long framesRead = 0, playbackEpoch = 0;
                long lastArrival=0,maximumArrivalGapUs=0;
                var pendingTasks = new List<Task>();
                // Simulated Mode B buffered stream state
                string? bufStreamId = null; long bufGen = 0, bufTotal = 0, bufReceived = 0;
                using var bufHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var bufCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                using var fileSlots=new SemaphoreSlim(2,2);
                var fileWork=Channel.CreateBounded<(Func<Task> Run,Message? Request,LogicalChannel Channel)>(2);
                void QueueFileOperation(Func<Task> operation,Message? request=null,LogicalChannel channel=LogicalChannel.Files)
                {
                    // Match the native serial, two-slot file queue. Disk latency must not
                    // hold the PCM/control reader; the acknowledgement provides backpressure.
                    if(!fileSlots.Wait(0))throw new IOException("File I/O queue is busy");
                    if(!fileWork.Writer.TryWrite((operation,request,channel)))
                    {
                        fileSlots.Release();throw new IOException("File I/O queue is closed");
                    }
                }
                var fileIo=Task.Run(async()=>
                {
                    try
                    {
                        await foreach(var operation in fileWork.Reader.ReadAllAsync(session.Token))
                        {
                            int delay=FileIoDelayMs;
                            Interlocked.Increment(ref fileOperationsInFlight);
                            try
                            {
                                if(delay>0)await Task.Delay(delay,session.Token);
                                await operation.Run();
                            }
                            catch(Exception ex)when(!session.IsCancellationRequested)
                            {
                                if(operation.Request is not null)
                                    await c.SendAsync(operation.Channel,Message.Create("error",new{error=ex.Message},replyTo:operation.Request.Id),session.Token);
                                else {Log?.Invoke("File I/O failed: "+ex.Message);session.Cancel();}
                            }
                            finally
                            {
                                Interlocked.Decrement(ref fileOperationsInFlight);fileSlots.Release();
                            }
                        }
                    }
                    catch(Exception)when(session.IsCancellationRequested) { }
                    catch(Exception ex){Log?.Invoke("File worker failed: "+ex.Message);session.Cancel();}
                });
                void JobChanged(JobState s)
                {
                    lock (pendingTasks) { pendingTasks.RemoveAll(t => t.IsCompleted); if (pendingTasks.Count < 16) pendingTasks.Add(SafeSendAsync("job.progress", s)); }
                }
                async Task SafeSendAsync(string type, object body)
                {
                    try { await c.SendAsync(LogicalChannel.Jobs, Message.Create(type, body), session.Token); } catch (Exception) when (session.IsCancellationRequested) { }
                }
                Jobs.Changed += JobChanged;
                var playback = Task.Factory.StartNew(() =>
                {
                    Thread.CurrentThread.Name = "BackThread simulated DAC"; Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                    byte[] sink = new byte[1920]; long epoch = 0, consumed = 0;
                    while (!session.Token.WaitHandle.WaitOne(Volatile.Read(ref audioOn) ? 5 : 20))
                    {
                        if (!Volatile.Read(ref audioOn)) continue;
                        long currentEpoch = Interlocked.Read(ref playbackEpoch);
                        if (currentEpoch == 0) continue;
                        if (currentEpoch != epoch) { epoch = currentEpoch; consumed = 0; }
                        long due = Math.Max(0, (long)(Stopwatch.GetElapsedTime(epoch).TotalMilliseconds - 30) / 10);
                        if (due - consumed > 10) consumed = due - 10;
                        while (consumed < due) { ring.Read(sink); Interlocked.Increment(ref framesRead); consumed++; }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    await c.RunAsync(async (frame, message, ct) =>
                    {
                        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
                        if (frame.Channel == LogicalChannel.Audio)
                        {
                            if (!audioOn || frame.Payload.Length % 4 != 0) throw new InvalidDataException("Invalid PCM stream");
                            long arrived=Stopwatch.GetTimestamp();
                            if(lastArrival!=0)maximumArrivalGapUs=Math.Max(maximumArrivalGapUs,Stopwatch.GetElapsedTime(lastArrival,arrived).Ticks/10);
                            lastArrival=arrived;
                            if (previous != 0 && frame.Sequence != previous + 1) gaps++;
                            previous = frame.Sequence; hash.AppendData(frame.Payload); ring.Write(frame.Payload);
                            Interlocked.CompareExchange(ref playbackEpoch, Stopwatch.GetTimestamp(), 0);
                            packets++; bytes += frame.Payload.Length;
                            if (DisconnectAfterAudioPackets > 0 && packets >= DisconnectAfterAudioPackets) throw new IOException("Injected disconnect");
                            return;
                        }
                        if (frame.Channel == LogicalChannel.Files)
                        {
                            if (frame.Payload.Length >= 40)
                            {
                                string chunkId = Encoding.ASCII.GetString(frame.Payload, 0, 32);
                                if (bufStreamId != null && string.Equals(chunkId, bufStreamId, StringComparison.OrdinalIgnoreCase))
                                {
                                    int dataLen = frame.Payload.Length - 40;
                                    bufHash.AppendData(frame.Payload, 40, dataLen);
                                    bufReceived += dataLen;
                                    LastBufferedBytesReceived = bufReceived;
                                    if (bufReceived >= bufTotal)
                                    {
                                        LastBufferedSha256 = Convert.ToHexString(bufHash.GetCurrentHash()).ToLowerInvariant();
                                        bufCompletion.TrySetResult();
                                    }
                                    return;
                                }
                            }
                            QueueFileOperation(async()=>
                            {
                                var ack=await storage.AppendAsync(frame.Payload,session.Token);
                                await c.SendAsync(LogicalChannel.Jobs,Message.Create("file.ack",new{transferId=ack.Id,offset=ack.Offset}),session.Token);
                            });
                            return;
                        }
                        if (message is null) return;
                        async ValueTask Reply(object body, string type = "ok") => await c.SendAsync(frame.Channel, Message.Create(type, body, replyTo: message.Id), ct);
                        try
                        {
                            var b = message.Body;
                            switch (message.Type)
                            {
                                case "hello":
                                    await Reply(new { agent = "BackThread.Simulator", simulated = true, capabilities = new[] { "pcm", "jobs", "files", "events", "faults", "hybrid-media", "buffered-media", "top-bar-notifications", "telemetry", "audio-modes", "sync" } });
                                    break;
                                case "audio.start":
                                    if (b.GetProperty("sampleRate").GetInt32() != 48000 || b.GetProperty("channels").GetInt32() != 2 || b.GetProperty("bits").GetInt32() != 16) throw new InvalidDataException("Only 48 kHz stereo PCM16 supported");
                                    Volatile.Write(ref audioOn, false); ring.Clear(); hash.GetHashAndReset(); packets = bytes = gaps = 0; lastArrival=maximumArrivalGapUs=0; previous = 0; Interlocked.Exchange(ref playbackEpoch, 0); Volatile.Write(ref audioOn, true); await Reply(new { ready = true }); break;
                                case "audio.stop": audioOn = false; ring.Clear(); await Reply(new { stopped = true }); break;
                                case "audio.volume":
                                    LastAppGainVolume = b.TryGetProperty("value", out var av) && av.ValueKind == JsonValueKind.Number ? Math.Clamp(av.GetDouble(), 0.0, 1.0) : 1.0;
                                    await Reply(new { value = LastAppGainVolume });
                                    break;
                                case "audio.control.state":
                                    LastAudioControlState = b.Clone();
                                    if (b.TryGetProperty("mode", out var om)) LastAudioOutputMode = om.GetString() ?? LastAudioOutputMode;
                                    break;
                                case "system.telemetry":
                                    TelemetryReceivedCount++;
                                    LastTelemetryReceived = b.Clone();
                                    break;
                                case "audio.stats": await Reply(new { packets, bytes, gaps, bufferedBytes = ring.Count, droppedBytes = ring.DroppedBytes, silenceBytes = ring.SilenceBytes, playbackTicks = framesRead, maximumArrivalGapMilliseconds=maximumArrivalGapUs/1000d,handlerMaximumMilliseconds=c.HandlerMaximumMilliseconds, sha256 = Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant() }); break;
                                case "job.submit": await Reply(Jobs.Submit(b.Deserialize<JobSpec>(Message.Json) ?? throw new InvalidDataException())); break;
                                case "job.query": await Reply(Jobs.Query(b.GetProperty("jobId").GetString()!) ?? throw new FileNotFoundException("Unknown job")); break;
                                case "job.cancel": await Reply(new { cancelled = Jobs.Cancel(b.GetProperty("jobId").GetString()!) }); break;
                                case "file.begin": QueueFileOperation(async()=>await Reply(new{offset=await storage.BeginAsync(b.Deserialize<TransferSpec>(Message.Json)??throw new InvalidDataException(),session.Token)}),message,frame.Channel);break;
                                case "file.commit":
                                    lock (pendingTasks)
                                    {
                                        pendingTasks.RemoveAll(t => t.IsCompleted);
                                        if (pendingTasks.Count >= 16) throw new IOException("Background operation limit");
                                        pendingTasks.Add(Task.Run(async () =>
                                        {
                                            try { await Reply(await storage.CommitAsync(b.GetProperty("transferId").GetString()!, session.Token)); }
                                            catch (Exception ex) when (!session.IsCancellationRequested) { await Reply(new { error = ex.Message }, "error"); }
                                        }, session.Token));
                                    }
                                    break;
                                case "file.list": await Reply(new { files = storage.List() }); break;
                                case "file.read":
                                    string name = b.GetProperty("name").GetString()!, id = b.GetProperty("transferId").GetString()!;
                                    if (id.Length != 32 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid download ID");
                                    long offset = b.GetProperty("offset").GetInt64(); int count = b.GetProperty("count").GetInt32();
                                    if (offset < 0 || count is < 1 or > 32768) throw new InvalidDataException("Invalid read range");
                                    QueueFileOperation(async()=>
                                    {
                                        await using var file=File.OpenRead(storage.Resolve(name));
                                        if (offset > file.Length) throw new InvalidDataException("Read beyond file");
                                        file.Position = offset; byte[] data = new byte[Math.Min(count, file.Length - offset)]; await file.ReadExactlyAsync(data, session.Token);
                                        await c.SendFrameAsync(LogicalChannel.Files, Storage.Chunk(id, offset, data), 0, session.Token); await Reply(new { length = data.Length });
                                    },message,frame.Channel);
                                    break;
                                case "event.publish":
                                    var accepted = events.Accept(b.Deserialize<CompanionEvent>(Message.Json) ?? throw new InvalidDataException()); await Reply(new { accepted = accepted != null }); break;
                                case "event.history": await Reply(new { events = events.History() }); break;
                                case "media.state":
                                    ReceivedMediaStateCount++;
                                    await Reply(new { received = true });
                                    break;
                                case "media.buffered.start":
                                    bufStreamId = b.GetProperty("streamId").GetString()!;
                                    bufGen = b.GetProperty("generation").GetInt64();
                                    bufTotal = b.GetProperty("totalBytes").GetInt64();
                                    bufReceived = 0; LastBufferedBytesReceived = 0; LastBufferedSha256 = null;
                                    bufCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                                    bufHash.GetHashAndReset();
                                    await Reply(new { accepted = true });
                                    break;
                                case "media.buffered.eof":
                                    await Reply(new { acknowledged = true });
                                    break;
                                case "media.handoff":
                                    string sid = b.GetProperty("sessionId").GetString()!;
                                    long gen = b.GetProperty("generation").GetInt64();
                                    string mode = b.GetProperty("mode").GetString() ?? "DirectInternet";
                                    string? url = b.TryGetProperty("url", out var u) ? u.GetString() : null;
                                    string? title = b.TryGetProperty("title", out var t) ? t.GetString() : null;
                                    double startPos = b.TryGetProperty("seekSeconds", out var sk) ? sk.GetDouble() : 0;
                                    var completionForStream = bufCompletion;
                                    await Reply(new { accepted = true, sessionId = sid, generation = gen });
                                    lock (pendingTasks)
                                    {
                                        pendingTasks.Add(Task.Run(async () =>
                                        {
                                            try
                                            {
                                                if (mode == "BufferedUsb" && bufStreamId != null)
                                                {
                                                    long pullOffset = 0;
                                                    while (pullOffset < bufTotal && !session.IsCancellationRequested)
                                                    {
                                                        int len = (int)Math.Min(16384, bufTotal - pullOffset);
                                                        await c.SendAsync(LogicalChannel.Media, Message.Create("media.buffered.request", new { streamId = bufStreamId, generation = bufGen, offset = pullOffset, length = len }), session.Token);
                                                        pullOffset += len;
                                                        await Task.Delay(5, session.Token);
                                                    }
                                                    await completionForStream.Task.WaitAsync(TimeSpan.FromSeconds(5), session.Token);
                                                }
                                                foreach (string st in SimulateHandoffShouldFail ? new[] { "validating", "connecting", "failed" } : new[] { "validating", "connecting", "buffering", "ready", "playing", "confirmed" })
                                                {
                                                    await Task.Delay(SimulateHandoffStepDelayMs, session.Token);
                                                    bool activeNow;
                                                    lock (playerGate)
                                                    {
                                                        if (st == "confirmed")
                                                        {
                                                            activeSessionId = sid; activeGeneration = gen; activeMode = mode; activeUrl = url; activeTitle = title;
                                                            independentActive = true; isPlaying = true; positionSeconds = startPos + 0.5;
                                                        }
                                                        else if (st == "failed")
                                                        {
                                                            independentActive = false; isPlaying = false;
                                                        }
                                                        activeNow = isPlaying;
                                                    }
                                                    await c.SendAsync(LogicalChannel.Media, Message.Create("media.handoff.status", new
                                                    {
                                                        sessionId = sid,
                                                        generation = gen,
                                                        stage = st,
                                                        positionSeconds = st == "confirmed" ? startPos + 0.5 : startPos,
                                                        durationSeconds,
                                                        bufferedSeconds,
                                                        isPlaying = activeNow,
                                                        readyForDisplay = st is "ready" or "playing" or "confirmed",
                                                        videoWidth = 1280,
                                                        videoHeight = 720,
                                                        error = st == "failed" ? SimulateHandoffFailureReason : null
                                                    }), session.Token);
                                                }
                                            }
                                            catch { }
                                        }, session.Token));
                                    }
                                    break;
                                case "media.handoff.cancel":
                                    lock (playerGate) { independentActive = false; isPlaying = false; activeSessionId = null; }
                                    await Reply(new { cancelled = true });
                                    break;
                                case "media.return_to_windows":
                                    double posSnap;
                                    lock (playerGate) { posSnap = positionSeconds; independentActive = false; isPlaying = false; activeSessionId = null; }
                                    await Reply(new { returned = true, positionSeconds = posSnap });
                                    break;
                                case "media.command":
                                    string cmd = b.GetProperty("command").GetString()!;
                                    bool cmdPlaying; double cmdPos;
                                    lock (playerGate)
                                    {
                                        if (cmd == "pause") isPlaying = false;
                                        else if (cmd == "play") isPlaying = true;
                                        else if (cmd == "toggle") isPlaying = !isPlaying;
                                        else if (cmd == "seek" && b.TryGetProperty("seconds", out var secEl)) positionSeconds = Math.Clamp(secEl.GetDouble(), 0, durationSeconds);
                                        cmdPlaying = isPlaying; cmdPos = positionSeconds;
                                    }
                                    await Reply(new { success = true, isPlaying = cmdPlaying, positionSeconds = cmdPos });
                                    break;
                                case "media.focus.set":
                                    LastFocusPolicy = b.TryGetProperty("policy", out var pol) ? pol.GetString() ?? "Auto" : "Auto";
                                    LastMuteVideoAudio = b.TryGetProperty("muteVideoAudio", out var mv) && mv.GetBoolean();
                                    LastIndependentVolume = b.TryGetProperty("volume", out var vol) ? vol.GetDouble() : 1.0;
                                    await Reply(new { applied = true });
                                    break;
                                case "media.volume.set":
                                    LastIndependentVolume = b.TryGetProperty("value", out var mvVal) && mvVal.ValueKind == JsonValueKind.Number ? Math.Clamp(mvVal.GetDouble(), 0.0, 1.0) : 1.0;
                                    await Reply(new { value = LastIndependentVolume });
                                    break;
                                case "media.mode.select":
                                    await Reply(new { received = true });
                                    break;
                                case "media.sync":
                                    object syncPayload;
                                    lock (playerGate)
                                    {
                                        syncPayload = new
                                        {
                                            sessionId = activeSessionId,
                                            generation = activeGeneration,
                                            mode = activeMode,
                                            independentActive,
                                            isPlaying,
                                            positionSeconds,
                                            durationSeconds,
                                            bufferedSeconds,
                                            url = activeUrl,
                                            title = activeTitle
                                        };
                                    }
                                    await Reply(syncPayload);
                                    break;
                                case "action.registry": await Reply(new { received = true }); break;
                                case "memory.pressure": Jobs.MemoryPressure = b.GetProperty("active").GetBoolean(); await Reply(new { paused = Jobs.MemoryPressure }); break;
                                case "fault.disconnect": throw new SocketException((int)SocketError.ConnectionReset);
                                default: await Reply(new { error = "Unsupported operation: " + message.Type }, "error"); break;
                            }
                        }
                        catch (SocketException) { throw; }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or KeyNotFoundException or JsonException)
                        { await Reply(new { error = ex.Message }, "error"); }
                    }, session.Token);
                }
                finally
                {
                    Jobs.Changed -= JobChanged; fileWork.Writer.TryComplete(); session.Cancel(); await playback; await fileIo;
                    Task[] remaining; lock (pendingTasks) remaining = pendingTasks.ToArray();
                    try { await Task.WhenAll(remaining); } catch (Exception) when (session.IsCancellationRequested) { }
                }
            }
            catch (Exception ex) when (!stop.IsCancellationRequested) { Log?.Invoke(ex.GetType().Name + ": " + ex.Message); }
            catch (Exception) when (stop.IsCancellationRequested) { break; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop(); if (serving != null) await serving; await Jobs.DisposeAsync(); stop.Dispose();
    }
}
