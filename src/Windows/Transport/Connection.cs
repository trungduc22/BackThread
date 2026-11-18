using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Diagnostics;
using System.Net.Sockets;

namespace BackThread.Transport;

/// <summary>One reader and one writer. Bulk is backpressured; audio discards old frames to bound latency.</summary>
public sealed class Connection : IAsyncDisposable
{
    private readonly record struct QueuedFrame(Frame Frame, long EnqueuedTimestamp);
    private readonly Stream stream;
    private readonly Channel<QueuedFrame> control = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait });
    private readonly Channel<QueuedFrame> audio;
    private readonly Channel<QueuedFrame> bulk = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait });
    private readonly SemaphoreSlim ready = new(0);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> pending = new();
    private readonly uint[] sequences = new uint[8];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task writer;
    private long dropped;
    private long audioWriteMaximumUs, audioSlowWrites, audioQueueResidenceMaxUs, handlerMaximumUs;
    private long lastReceive = Environment.TickCount64;
    public long DroppedAudio => Interlocked.Read(ref dropped);
    public double AudioWriteMaximumMilliseconds => Interlocked.Read(ref audioWriteMaximumUs) / 1000d;
    public long AudioSlowWrites => Interlocked.Read(ref audioSlowWrites);
    public double AudioQueueResidenceMaximumMilliseconds => Interlocked.Read(ref audioQueueResidenceMaxUs) / 1000d;
    public double AudioQueueMaximumMilliseconds => AudioQueueResidenceMaximumMilliseconds;
    public double HandlerMaximumMilliseconds => Interlocked.Read(ref handlerMaximumUs) / 1000d;
    public TimeSpan ReceiveAge => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref lastReceive));
    public Connection(Stream stream)
    {
        this.stream = stream;
        audio = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref dropped));
        if(stream is NetworkStream network)
        {
            network.WriteTimeout=3000;
            // Native socket writes have a bounded OS timeout. A dedicated writer avoids
            // resuming every 10ms PCM packet on a busy UI/thread-pool scheduler.
            writer=Task.Factory.StartNew(WriteNetworkLoop,CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
        }
        else writer = WriteLoopAsync();
    }
    public async ValueTask SendAsync(LogicalChannel channel, Message message, CancellationToken ct)
        => await SendFrameAsync(channel, message.Encode(), 0, ct);
    public async ValueTask SendFrameAsync(LogicalChannel channel, byte[] payload, ulong timestampUs, CancellationToken ct)
    {
        if (payload.Length > Wire.Limit(channel)) throw new InvalidDataException("Frame too large");
        ct.ThrowIfCancellationRequested();
        lifetime.Token.ThrowIfCancellationRequested();
        uint seq;
        lock (sequences) seq = ++sequences[(int)channel];
        var qf = new QueuedFrame(new Frame(channel, seq, timestampUs, payload), Stopwatch.GetTimestamp());
        var queue = channel == LogicalChannel.Audio ? audio : channel == LogicalChannel.Files ? bulk : control;
        if (!queue.Writer.TryWrite(qf))
        {
            if (!ct.CanBeCanceled || ct == lifetime.Token)
            {
                await queue.Writer.WriteAsync(qf, lifetime.Token);
            }
            else
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
                await queue.Writer.WriteAsync(qf, linked.Token);
            }
        }
        // A coalescing wake-up avoids an unbounded semaphore count when audio is dropped.
        if (ready.CurrentCount == 0) { try { ready.Release(); } catch (SemaphoreFullException) { } }
    }
    public async Task<Message> RequestAsync(LogicalChannel channel, string type, object body, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (pending.Count >= 64) throw new IOException("Too many pending requests");
        string id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<Message>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!pending.TryAdd(id, tcs)) throw new InvalidOperationException();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            await SendAsync(channel, Message.Create(type, body, id), limit.Token);
            var response = await tcs.Task.WaitAsync(limit.Token);
            if (response.Type == "error") throw new IOException(response.Body.GetProperty("error").GetString());
            return response;
        }
        finally { pending.TryRemove(id, out _); }
    }
    public async Task RunAsync(Func<Frame, Message?, CancellationToken, ValueTask> handler, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
        CancellationTokenSource activeReadTimeout = readTimeout;
        try
        {
            while (true)
            {
                activeReadTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                var frame = await Wire.ReadAsync(stream, activeReadTimeout.Token);
                if (!activeReadTimeout.TryReset())
                {
                    activeReadTimeout.Dispose();
                    activeReadTimeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token);
                }
                Interlocked.Exchange(ref lastReceive, Environment.TickCount64);
                Message? message = frame.Channel is LogicalChannel.Audio or LogicalChannel.Files ? null : Message.Decode(frame.Payload);
                if (message?.ReplyTo is { } id && pending.TryGetValue(id, out var p)) { p.TrySetResult(message); continue; }
                if (message?.Type == "ping") { await SendAsync(LogicalChannel.Control, Message.Create("pong", replyTo: message.Id), linked.Token); continue; }
                long started=Stopwatch.GetTimestamp();
                await handler(frame, message, linked.Token);
                UpdateMax(ref handlerMaximumUs, Stopwatch.GetElapsedTime(started).Ticks / 10);
            }
        }
        finally
        {
            if (!ReferenceEquals(activeReadTimeout, readTimeout)) activeReadTimeout.Dispose();
            foreach (var p in pending.Values) p.TrySetException(new IOException("Connection closed"));
            lifetime.Cancel();
        }
    }
    private async Task WriteLoopAsync()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        try
        {
            while (true)
            {
                await ready.WaitAsync(lifetime.Token);
                while (control.Reader.TryRead(out var qf) || audio.Reader.TryRead(out qf) || bulk.Reader.TryRead(out qf))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    long started = Stopwatch.GetTimestamp();
                    await Wire.WriteAsync(stream, qf.Frame, timeout.Token);
                    RecordWrite(qf, started);
                    if (!timeout.TryReset())
                    {
                        timeout.Dispose();
                        timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    }
                }
            }
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lifetime.Cancel(); stream.Dispose();
            foreach (var p in pending.Values) p.TrySetException(ex);
        }
        finally
        {
            timeout.Dispose();
        }
    }
    private static void UpdateMax(ref long location, long candidate)
    {
        long old = Interlocked.Read(ref location);
        while (candidate > old)
        {
            long observed = Interlocked.CompareExchange(ref location, candidate, old);
            if (observed == old) break;
            old = observed;
        }
    }
    private void RecordWrite(QueuedFrame qf, long started)
    {
        if (qf.Frame.Channel != LogicalChannel.Audio) return;
        long residenceUs = Math.Max(0, (started - qf.EnqueuedTimestamp) * 1_000_000L / Stopwatch.Frequency);
        UpdateMax(ref audioQueueResidenceMaxUs, residenceUs);
        long elapsed = Stopwatch.GetElapsedTime(started).Ticks / 10;
        UpdateMax(ref audioWriteMaximumUs, elapsed);
        if (elapsed >= 50000) Interlocked.Increment(ref audioSlowWrites);
    }
    private void WriteNetworkLoop()
    {
        Thread.CurrentThread.Name="BackThread socket writer";
        Thread.CurrentThread.Priority=ThreadPriority.AboveNormal;
        try {
            while(true){
                ready.Wait(lifetime.Token);
                while(control.Reader.TryRead(out var qf)||audio.Reader.TryRead(out qf)||bulk.Reader.TryRead(out qf)){
                    lifetime.Token.ThrowIfCancellationRequested();
                    long started=Stopwatch.GetTimestamp();Wire.Write(stream,qf.Frame);RecordWrite(qf,started);
                }
            }
        }catch(Exception)when(lifetime.IsCancellationRequested){}
        catch(Exception ex){lifetime.Cancel();stream.Dispose();foreach(var pendingRequest in pending.Values)pendingRequest.TrySetException(ex);}
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); stream.Dispose(); control.Writer.TryComplete(); audio.Writer.TryComplete(); bulk.Writer.TryComplete();
        foreach (var p in pending.Values) p.TrySetException(new IOException("Connection disposed"));
        await writer;
        ready.Dispose(); lifetime.Dispose();
    }
}
