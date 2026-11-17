using System.Collections.Concurrent;
using System.Threading.Channels;

namespace BackThread.Transport;

/// <summary>One reader and one writer. Bulk is backpressured; audio discards old frames to bound latency.</summary>
public sealed class Connection : IAsyncDisposable
{
    private readonly Stream stream;
    private readonly Channel<Frame> control = Channel.CreateBounded<Frame>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.Wait });
    private readonly Channel<Frame> audio;
    private readonly Channel<Frame> bulk = Channel.CreateBounded<Frame>(new BoundedChannelOptions(2) { FullMode = BoundedChannelFullMode.Wait });
    private readonly SemaphoreSlim ready = new(0);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Message>> pending = new();
    private readonly uint[] sequences = new uint[8];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task writer;
    private long dropped;
    private long lastReceive = Environment.TickCount64;
    public long DroppedAudio => Interlocked.Read(ref dropped);
    public TimeSpan ReceiveAge => TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref lastReceive));
    public Connection(Stream stream)
    {
        this.stream = stream;
        audio = Channel.CreateBounded<Frame>(new BoundedChannelOptions(6) { FullMode = BoundedChannelFullMode.DropOldest }, _ => Interlocked.Increment(ref dropped));
        writer = WriteLoopAsync();
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
        await queue.Writer.WriteAsync(f, linked.Token);
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
                await handler(frame, message, linked.Token);
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
                while (control.Reader.TryRead(out var f) || audio.Reader.TryRead(out f) || bulk.Reader.TryRead(out f))
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    await Wire.WriteAsync(stream, f, timeout.Token);
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
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); stream.Dispose(); control.Writer.TryComplete(); audio.Writer.TryComplete(); bulk.Writer.TryComplete();
        foreach (var p in pending.Values) p.TrySetException(new IOException("Connection disposed"));
        await writer;
        ready.Dispose(); lifetime.Dispose();
    }
}
