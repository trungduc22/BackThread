using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Diagnostics;
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
    public int DisconnectAfterAudioPackets { get; set; }
    public int AuthenticatedConnections { get; private set; }
    public event Action<string>? Log;
    private Task? serving;
    private readonly EventRouter events = new();
    public AgentServer(int port, byte[] key, string stateRoot)
    {
        this.key = key; listener = new(IPAddress.Loopback, port); storage = new(Path.Combine(stateRoot, "files")); Jobs = new(storage, Path.Combine(stateRoot, "jobs.json"));
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
                var pendingTasks = new List<Task>();
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
                    while (!session.Token.WaitHandle.WaitOne(Volatile.Read(ref audioOn) ? 5 : 20))
                    {
                            if (!Volatile.Read(ref audioOn)) continue;
                            long currentEpoch = Interlocked.Read(ref playbackEpoch);
                            if (currentEpoch == 0) continue; // negotiation alone is not the first PCM sample
                            if (currentEpoch != epoch) { epoch = currentEpoch; consumed = 0; }
                            // Windows timer callbacks can coalesce at ~15.6 ms. Model a hardware sample
                            // clock, consuming all elapsed 10 ms blocks instead of one block per wake.
                            long due = Math.Max(0, (long)(Stopwatch.GetElapsedTime(epoch).TotalMilliseconds - 30) / 10);
                            if (due - consumed > 10) consumed = due - 10; // bound an exceptional scheduler stall
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
                            if (previous != 0 && frame.Sequence != previous + 1) gaps++;
                            previous = frame.Sequence; hash.AppendData(frame.Payload); ring.Write(frame.Payload);
                            Interlocked.CompareExchange(ref playbackEpoch, Stopwatch.GetTimestamp(), 0);
                            packets++; bytes += frame.Payload.Length;
                            if (DisconnectAfterAudioPackets > 0 && packets >= DisconnectAfterAudioPackets) throw new IOException("Injected disconnect");
                            return;
                        }
                        if (frame.Channel == LogicalChannel.Files)
                        {
                            var ack = await storage.AppendAsync(frame.Payload, ct);
                            await c.SendAsync(LogicalChannel.Jobs, Message.Create("file.ack", new { transferId = ack.Id, offset = ack.Offset }), ct); return;
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
                                    Volatile.Write(ref audioOn, false); ring.Clear(); hash.GetHashAndReset(); packets = bytes = gaps = 0; previous = 0; Interlocked.Exchange(ref playbackEpoch, 0); Volatile.Write(ref audioOn, true); await Reply(new { ready = true }); break;
                                case "audio.stop": audioOn = false; ring.Clear(); await Reply(new { stopped = true }); break;
                                case "audio.stats": await Reply(new { packets, bytes, gaps, bufferedBytes = ring.Count, droppedBytes = ring.DroppedBytes, silenceBytes = ring.SilenceBytes, playbackTicks = framesRead, sha256 = Convert.ToHexString(hash.GetCurrentHash()).ToLowerInvariant() }); break;
                                case "job.submit": await Reply(Jobs.Submit(b.Deserialize<JobSpec>(Message.Json) ?? throw new InvalidDataException())); break;
                                case "job.query": await Reply(Jobs.Query(b.GetProperty("jobId").GetString()!) ?? throw new FileNotFoundException("Unknown job")); break;
                                case "job.cancel": await Reply(new { cancelled = Jobs.Cancel(b.GetProperty("jobId").GetString()!) }); break;
                                case "file.begin": await Reply(new { offset = await storage.BeginAsync(b.Deserialize<TransferSpec>(Message.Json) ?? throw new InvalidDataException(), ct) }); break;
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
                                    await using (var file = File.OpenRead(storage.Resolve(name)))
                                    {
                                        if (offset > file.Length) throw new InvalidDataException("Read beyond file");
                                        file.Position = offset; byte[] data = new byte[Math.Min(count, file.Length - offset)]; await file.ReadExactlyAsync(data, ct);
                                        await c.SendFrameAsync(LogicalChannel.Files, Storage.Chunk(id, offset, data), 0, ct); await Reply(new { length = data.Length });
                                    }
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
                    Jobs.Changed -= JobChanged; session.Cancel(); await playback;
                    Task[] remaining; lock (pendingTasks) remaining = pendingTasks.ToArray();
                    try { await Task.WhenAll(remaining); } catch (OperationCanceledException) { }
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
