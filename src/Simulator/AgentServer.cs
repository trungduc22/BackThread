using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
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
    public event Action<string>? Log;
    private Task? serving;
    private readonly EventRouter events = new();
    public AgentServer(int port, byte[] key, string stateRoot, Action? beforeJobPersist = null)
    {
        this.key = key; listener = new(IPAddress.Loopback, port); storage = new(Path.Combine(stateRoot, "files")); Jobs = new(storage, Path.Combine(stateRoot, "jobs.json"), beforeJobPersist);
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
                bool audioOn = false; long packets = 0, bytes = 0, gaps = 0; uint previous = 0; long framesRead = 0, playbackEpoch = 0, skippedPlaybackTicks = 0, maxPlaybackWakeUs = 0;
                var playbackGate = new object();
                long lastArrival = 0, maximumArrivalGapUs = 0;
                uint maximumArrivalGapSequence = 0;
                var pendingTasks = new List<Task>();
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
                // One executing command plus at most seven waiting. Journal writes and
                // scheduler locks stay off the reader while preserving job command order.
                var jobCommands = Channel.CreateBounded<(LogicalChannel Channel, Message Request)>(7);
                var jobIo = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var (channel, request) in jobCommands.Reader.ReadAllAsync(session.Token))
                        {
                            session.Token.ThrowIfCancellationRequested();
                            Message response;
                            try
                            {
                                var body = request.Body;
                                object result = request.Type switch
                                {
                                    "job.submit" => Jobs.Submit(body.Deserialize<JobSpec>(Message.Json) ?? throw new InvalidDataException()),
                                    "job.query" => Jobs.Query(body.GetProperty("jobId").GetString()!) ?? throw new FileNotFoundException("Unknown job"),
                                    "job.cancel" => new { cancelled = Jobs.Cancel(body.GetProperty("jobId").GetString()!) },
                                    _ => throw new InvalidDataException("Unsupported job command")
                                };
                                response = Message.Create("ok", result, replyTo: request.Id);
                            }
                            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException
                                or InvalidOperationException or KeyNotFoundException or JsonException)
                            {
                                response = Message.Create("error", new { error = error.Message }, replyTo: request.Id);
                            }
                            await c.SendAsync(channel, response, session.Token);
                        }
                    }
                    catch (Exception) when (session.IsCancellationRequested) { }
                    catch (Exception error) { Log?.Invoke("Job command queue failed: " + error.GetType().Name); session.Cancel(); }
                });
                void JobChanged(JobState s)
                {
                    // Progress is advisory and bounded by the scheduler's ten percent update interval.
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
                    long lastWake = Stopwatch.GetTimestamp();
                    while (!session.Token.WaitHandle.WaitOne(Volatile.Read(ref audioOn) ? 5 : 20))
                    {
                        long wake = Stopwatch.GetTimestamp();
                        long wakeUs = Stopwatch.GetElapsedTime(lastWake, wake).Ticks / 10;
                        lastWake = wake;
                        lock (playbackGate)
                        {
                            if (!audioOn || playbackEpoch == 0) continue;
                            if (playbackEpoch != epoch) { epoch = playbackEpoch; consumed = 0; }
                            maxPlaybackWakeUs = Math.Max(maxPlaybackWakeUs, wakeUs);
                            long due = Math.Max(0, (long)(Stopwatch.GetElapsedTime(epoch).TotalMilliseconds - 30) / 10);
                            long skipped = Math.Max(0, due - consumed - 10);
                            skippedPlaybackTicks += skipped;
                            consumed += skipped;
                            while (consumed < due) { ring.Read(sink); framesRead++; consumed++; }
                        }
                    }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                try
                {
                    await c.RunAsync(async (frame, message, ct) =>
                    {
                        if (DelayMs > 0) await Task.Delay(DelayMs, ct);
                        if (frame.Channel == LogicalChannel.Audio)
                        {
                            if (frame.Payload.Length % 4 != 0) throw new InvalidDataException("Invalid PCM stream");
                            if (!audioOn) return; // A PCM frame queued before Stop can arrive afterwards.
                            long arrived=Stopwatch.GetTimestamp();
                            if (lastArrival != 0)
                            {
                                long gapUs = Stopwatch.GetElapsedTime(lastArrival, arrived).Ticks / 10;
                                if (gapUs > maximumArrivalGapUs)
                                {
                                    maximumArrivalGapUs = gapUs;
                                    maximumArrivalGapSequence = frame.Sequence;
                                }
                            }
                            lastArrival=arrived;
                            if (previous != 0 && frame.Sequence != previous + 1) gaps++;
                            previous = frame.Sequence; hash.AppendData(frame.Payload); ring.Write(frame.Payload);
                            lock (playbackGate) { if (playbackEpoch == 0) playbackEpoch = arrived; }
                            packets++; bytes += frame.Payload.Length;
                            if (DisconnectAfterAudioPackets > 0 && packets >= DisconnectAfterAudioPackets) throw new IOException("Injected disconnect");
                            return;
                        }
                        if (frame.Channel == LogicalChannel.Files)
                        {
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
                                case "hello": await Reply(new { agent = "BackThread.Simulator", simulated = true, capabilities = new[] { "pcm", "jobs", "files", "events", "faults" } }); break;
                                case "audio.start":
                                    if (b.GetProperty("sampleRate").GetInt32() != 48000 || b.GetProperty("channels").GetInt32() != 2 || b.GetProperty("bits").GetInt32() != 16) throw new InvalidDataException("Only 48 kHz stereo PCM16 supported");
                                    lock (playbackGate)
                                    {
                                        ring.Clear();
                                        playbackEpoch = framesRead = skippedPlaybackTicks = maxPlaybackWakeUs = 0;
                                        Volatile.Write(ref audioOn, true);
                                    }
                                    hash.GetHashAndReset(); packets = bytes = gaps = 0; lastArrival = maximumArrivalGapUs = 0;
                                    previous = maximumArrivalGapSequence = 0;
                                    await Reply(new { ready = true }); break;
                                case "audio.stop":
                                    lock (playbackGate) { Volatile.Write(ref audioOn, false); ring.Clear(); }
                                    await Reply(new { stopped = true }); break;
                                case "audio.stats":
                                    object audioStats;
                                    lock (playbackGate)
                                        audioStats = new { packets, bytes, gaps, bufferedBytes = ring.Count, droppedBytes = ring.DroppedBytes,
                                            silenceBytes = ring.SilenceBytes, playbackTicks = framesRead, skippedPlaybackTicks,
                                            maximumPlaybackWakeMilliseconds = maxPlaybackWakeUs / 1000d,
                                            maximumArrivalGapMilliseconds = maximumArrivalGapUs / 1000d, maximumArrivalGapSequence,
                                            handlerMaximumMilliseconds = c.HandlerMaximumMilliseconds, sha256 = Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant() };
                                    await Reply(audioStats); break;
                                case "job.submit":
                                case "job.query":
                                case "job.cancel":
                                    if (!jobCommands.Writer.TryWrite((frame.Channel, message))) throw new IOException("Job command queue is busy");
                                    break;
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
                                case "file.list": QueueFileOperation(() => Reply(new { files = storage.List() }).AsTask(), message, frame.Channel); break;
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
                                case "media.state": case "action.registry": await Reply(new { received = true }); break;
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
                    Jobs.Changed -= JobChanged;
                    fileWork.Writer.TryComplete();
                    jobCommands.Writer.TryComplete();
                    session.Cancel();
                    await Task.WhenAll(playback, fileIo, jobIo);
                    Task[] remaining; lock (pendingTasks) remaining = pendingTasks.ToArray();
                    try { await Task.WhenAll(remaining); } catch (Exception) when(session.IsCancellationRequested) { }
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
