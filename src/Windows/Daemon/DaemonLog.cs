namespace BackThread.Daemon;

/// <summary>Bounded in-memory log ring (no disk writes by default; the dashboard reads it via <c>logs.tail</c>).</summary>
public sealed class DaemonLog
{
    private readonly Queue<string> entries = new();
    private readonly Lock gate = new();
    private readonly int capacity;

    public DaemonLog(int capacity = 200) => this.capacity = Math.Max(10, capacity);

    public event Action<string>? Written;

    public void Write(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        string line = DateTimeOffset.Now.ToString("HH:mm:ss") + " " + message.Trim();
        lock (gate)
        {
            entries.Enqueue(line);
            while (entries.Count > capacity)
                entries.Dequeue();
        }
        Written?.Invoke(line);
    }

    public string[] Tail(int count)
    {
        lock (gate)
        {
            int take = Math.Clamp(count, 1, capacity);
            return entries.Skip(Math.Max(0, entries.Count - take)).ToArray();
        }
    }
}
