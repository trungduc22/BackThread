using System.Diagnostics;
using System.Text.Json;

namespace BackThread.Host;

public sealed record DevMonitorConfig
{
    public bool Enabled { get; init; }
    public string OllamaEndpoint { get; init; } = "http://127.0.0.1:11434";
    public string[] WatchProcesses { get; init; } = ["ollama", "claude", "codex", "dotnet", "cargo", "zig"];
}

public sealed record DevSessionSnapshot(
    bool Enabled,
    bool OllamaReachable,
    string[] LoadedOllamaModels,
    string[] ActiveDevProcesses,
    long SampledUnixMs);

/// <summary>
/// Lightweight opt-in monitor for local AI / dev workflows (Ollama / ed-local-ai, Claude Code, Codex, build tools).
/// Does zero polling unless explicitly enabled and requested.
/// </summary>
public sealed class DevSessionMonitor : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsHttp;
    private readonly Lock gate = new();
    private DevSessionSnapshot? cached;
    private long cachedAt;

    public DevSessionMonitor(HttpMessageHandler? handler = null)
    {
        if (handler is not null) { http = new HttpClient(handler, disposeHandler: false); ownsHttp = false; }
        else { http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) }; ownsHttp = true; }
    }

    public async Task<DevSessionSnapshot> SampleAsync(DevMonitorConfig config, CancellationToken ct)
    {
        if (!config.Enabled)
            return new DevSessionSnapshot(false, false, [], [], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        lock (gate)
        {
            if (cached is not null && Environment.TickCount64 - cachedAt < 10_000)
                return cached;
        }

        var activeProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var watched = new HashSet<string>(config.WatchProcesses, StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (watched.Contains(p.ProcessName))
                    activeProcs.Add(p.ProcessName);
            }
            catch { }
            finally { p.Dispose(); }
        }

        bool ollamaOk = false;
        var models = new List<string>();
        if (!string.IsNullOrWhiteSpace(config.OllamaEndpoint) && Uri.TryCreate(config.OllamaEndpoint, UriKind.Absolute, out var baseUri)
            && baseUri.IsLoopback && baseUri.Scheme is "http" or "https")
        {
            try
            {
                using var res = await http.GetAsync(new Uri(baseUri, "/api/ps"), ct);
                if (res.IsSuccessStatusCode)
                {
                    ollamaOk = true;
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                    if (doc.RootElement.TryGetProperty("models", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                        {
                            if (item.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                                models.Add(name);
                        }
                    }
                }
            }
            catch
            {
                // Ollama not running locally.
            }
        }

        var snap = new DevSessionSnapshot(
            Enabled: true,
            OllamaReachable: ollamaOk,
            LoadedOllamaModels: [.. models],
            ActiveDevProcesses: activeProcs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            SampledUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        lock (gate)
        {
            cached = snap;
            cachedAt = Environment.TickCount64;
        }
        return snap;
    }

    public void Dispose()
    {
        if (ownsHttp) http.Dispose();
    }
}
