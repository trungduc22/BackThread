using System.Net;
using System.Net.Sockets;
using BackThread.Transport;
using BackThread.Worker;

namespace BackThread.Tests;

internal static class Check
{
    public static void True(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    public static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static async Task Eventually(Func<Task<bool>> check, int milliseconds = 5000)
    {
        long end = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < end) { if (await check()) return; await Task.Delay(25); } throw new TimeoutException("Condition not met");
    }
}
internal sealed class Session : IAsyncDisposable
{
    private readonly TcpClient tcp;
    private readonly CancellationTokenSource stop = new();
    private readonly Task reader;
    public Connection Connection { get; }
    public RemoteWorker Worker { get; }
    public static async Task<Session> Open(int port, byte[] key, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true }; await tcp.ConnectAsync(IPAddress.Loopback, port, ct);
        try { await Authentication.ClientAsync(tcp.GetStream(), key, ct); return new(tcp); } catch { tcp.Dispose(); throw; }
    }
    private Session(TcpClient tcp)
    {
        this.tcp = tcp; Connection = new(tcp.GetStream()); Worker = new(Connection);
        reader = Connection.RunAsync((f, m, _) => { Worker.Receive(f, m); return ValueTask.CompletedTask; }, stop.Token);
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); await Connection.DisposeAsync(); tcp.Dispose(); try { await reader; } catch (Exception) when (stop.IsCancellationRequested) { } stop.Dispose();
    }
}
internal sealed class FragmentedStream(byte[] data) : MemoryStream(data)
{
    private int reads;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(buffer.Length, ++reads % 7 + 1)], ct);
}
internal sealed class DelayedWriteStream(Task gate) : MemoryStream
{
    public TaskCompletionSource Entered {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> data,CancellationToken ct=default)
    {
        Entered.TrySetResult();await gate.WaitAsync(ct);await base.WriteAsync(data,ct);
    }
}
