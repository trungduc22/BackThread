using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Diagnostics;
using System.Net.Sockets;

namespace BackThread.Transport;

/// <summary>One reader and one writer. Bulk is backpressured; audio discards old frames to bound latency.</summary>
public sealed class Connection : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly record struct QueuedFrame(Frame Frame, long EnqueuedAt);
    private readonly Channel<QueuedFrame> control = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait });
    private readonly Channel<QueuedFrame> audio;
    private readonly Channel<QueuedFrame> bulk = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait });
    private readonly SemaphoreSlim ready = new(0);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> pending = new();
    private readonly uint[] sequences = new uint[8];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task writer;
    private long dropped;
    private long audioWriteMaximumUs,audioSlowWrites,audioQueueMaximumUs,handlerMaximumUs;
    private long lastReceive = Environment.TickCount64;
    public long DroppedAudio => Interlocked.Read(ref dropped);
    public double AudioWriteMaximumMilliseconds=>Interlocked.Read(ref audioWriteMaximumUs)/1000d;
    public long AudioSlowWrites=>Interlocked.Read(ref audioSlowWrites);
    public double AudioQueueMaximumMilliseconds=>Interlocked.Read(ref audioQueueMaximumUs)/1000d;
    public double HandlerMaximumMilliseconds=>Interlocked.Read(ref handlerMaximumUs)/1000d;
    public TimeSpan ReceiveAge => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref lastReceive));
    public Connection(Stream stream)
    {
        this.stream = stream;
        audio = Channel.CreateBounded<QueuedFrame>(new BoundedChannelOptions(6) { FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref dropped));
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
        lifetime.Token.ThrowIfCancellationRequested();
        uint seq;
        lock (sequences) seq = ++sequences[(int)channel];
        var f = new Frame(channel, seq, timestampUs, payload);
        var queue = channel == LogicalChannel.Audio ? audio : channel == LogicalChannel.Files ? bulk : control;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        await queue.Writer.WriteAsync(new(f, Stopwatch.GetTimestamp()), linked.Token);
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
        try
        {
            while (true)
            {
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(linked.Token); readTimeout.CancelAfter(TimeSpan.FromSeconds(12));
                var frame = await Wire.ReadAsync(stream, readTimeout.Token);
                Interlocked.Exchange(ref lastReceive, Environment.TickCount64);
                Message? message = frame.Channel is LogicalChannel.Audio or LogicalChannel.Files ? null : Message.Decode(frame.Payload);
                if (message?.ReplyTo is { } id && pending.TryGetValue(id, out var p)) { p.TrySetResult(message); continue; }
                if (message?.Type == "ping") { await SendAsync(LogicalChannel.Control, Message.Create("pong", replyTo: message.Id), linked.Token); continue; }
                long started=Stopwatch.GetTimestamp();
                await handler(frame, message, linked.Token);
                RecordMaximum(ref handlerMaximumUs,Stopwatch.GetElapsedTime(started).Ticks/10);
            }
        }
        finally { foreach (var p in pending.Values) p.TrySetException(new IOException("Connection closed")); lifetime.Cancel(); }
    }
    private async Task WriteLoopAsync()
    {
        try
        {
            while (true)
            {
                await ready.WaitAsync(lifetime.Token);
                while (control.Reader.TryRead(out var queued) || audio.Reader.TryRead(out queued) || bulk.Reader.TryRead(out queued))
                {
                    var f=queued.Frame;RecordQueueWait(queued);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    long started=Stopwatch.GetTimestamp();
                    await Wire.WriteAsync(stream, f, timeout.Token);
                    RecordWrite(f,started);
                }
            }
        }
        catch (Exception) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lifetime.Cancel(); stream.Dispose();
            foreach (var p in pending.Values) p.TrySetException(ex);
        }
    }
    private void RecordWrite(Frame frame,long started)
    {
        if(frame.Channel!=LogicalChannel.Audio)return;
        long elapsed=Stopwatch.GetElapsedTime(started).Ticks/10;
        RecordMaximum(ref audioWriteMaximumUs,elapsed);
        if(elapsed>=50000)Interlocked.Increment(ref audioSlowWrites);
    }
    private static void RecordMaximum(ref long maximum,long value)
    {
        long old=Interlocked.Read(ref maximum);
        while(value>old){long observed=Interlocked.CompareExchange(ref maximum,value,old);if(observed==old)break;old=observed;}
    }
    private void RecordQueueWait(QueuedFrame queued)
    {
        if(queued.Frame.Channel==LogicalChannel.Audio)
            RecordMaximum(ref audioQueueMaximumUs,Stopwatch.GetElapsedTime(queued.EnqueuedAt).Ticks/10);
    }
    private void WriteNetworkLoop()
    {
        Thread.CurrentThread.Name="BackThread socket writer";
        Thread.CurrentThread.Priority=ThreadPriority.AboveNormal;
        try {
            while(true){
                ready.Wait(lifetime.Token);
                while(control.Reader.TryRead(out var queued)||audio.Reader.TryRead(out queued)||bulk.Reader.TryRead(out queued)){
                    lifetime.Token.ThrowIfCancellationRequested();
                    var frame=queued.Frame;RecordQueueWait(queued);
                    long started=Stopwatch.GetTimestamp();Wire.Write(stream,frame);RecordWrite(frame,started);
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
