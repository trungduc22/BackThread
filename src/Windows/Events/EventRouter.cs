namespace BackThread.Events;

public sealed record CompanionEvent(string Id, string Source, string Title, string Body, int Priority, DateTimeOffset ExpiresUtc, bool Sensitive = false,string? Category=null,string? IconBase64=null);
public sealed class EventRouter
{
    private readonly Queue<CompanionEvent> history = new();
    private readonly Dictionary<string, DateTimeOffset> seen = [];
    private readonly object gate = new();
    private DateTimeOffset window;
    private int inWindow;
    public bool HideSensitive { get; set; } = true;
    public HashSet<string> BlockedSources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public CompanionEvent? Accept(CompanionEvent e, DateTimeOffset? now = null)
    {
        var time = now ?? DateTimeOffset.UtcNow;
        if(e.Category?.Length>64||e.IconBase64?.Length>12000)return null;
        if (e.Id is not { Length: > 0 and <= 64 } || e.Source is not { Length: > 0 and <= 128 } || e.Title is null || e.Title.Length > 256 || e.Body is null || e.Body.Length > 2048 || e.Priority is < 0 or > 3 || e.ExpiresUtc <= time || e.ExpiresUtc > time.AddDays(1)) return null;
        lock (gate)
        {
            if (BlockedSources.Contains(e.Source)) return null;
            foreach (var id in seen.Where(p => p.Value <= time).Select(p => p.Key).ToArray()) seen.Remove(id);
            if (seen.ContainsKey(e.Id)) return null;
            if (time - window >= TimeSpan.FromSeconds(1)) { window = time; inWindow = 0; }
            if (++inWindow > 10) return null;
            if (seen.Count >= 256) seen.Remove(seen.Keys.First());
            seen[e.Id] = e.ExpiresUtc;
            if (HideSensitive && e.Sensitive) e = e with { Title = "Private notification", Body = "Open the source application on Windows" };
            history.Enqueue(e); while (history.Count > 64) history.Dequeue();
            return e;
        }
    }
    public CompanionEvent[] History() { lock (gate) return history.Where(e => e.ExpiresUtc > DateTimeOffset.UtcNow).ToArray(); }
}
