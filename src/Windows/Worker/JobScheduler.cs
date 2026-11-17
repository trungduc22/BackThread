using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BackThread.Transport;

namespace BackThread.Worker;

public sealed record JobSpec(string JobId, string Kind, string Name, string? ExpectedSha256 = null);
public sealed record JobState(string JobId, string State, double Progress, object? Result = null, string? Error = null);
public sealed class JobScheduler : IAsyncDisposable
{
    private readonly Storage storage;
    private readonly string journal;
    private readonly Channel<JobSpec> queue = Channel.CreateBounded<JobSpec>(8);
    private readonly Dictionary<string, JobState> states = [];
    private readonly Dictionary<string, JobSpec> specs = [];
    private readonly Dictionary<string, CancellationTokenSource> cancellations = [];
    private readonly CancellationTokenSource stop = new();
    private readonly Task runner;
    private readonly object gate = new();
    public bool MemoryPressure { get; set; }
    public event Action<JobState>? Changed;
    public JobScheduler(Storage storage, string journal)
    {
        this.storage = storage; this.journal = journal;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(journal))!);
        if (File.Exists(journal))
        {
            try
            {
                foreach (var s in JsonSerializer.Deserialize<JobState[]>(File.ReadAllText(journal)) ?? []) states[s.JobId] = s.State is "queued" or "running" ? s with { State = "interrupted", Error = "Agent restarted; submit a new job ID to retry" } : s;
            }
            catch (JsonException) { File.Move(journal, journal + ".corrupt", true); }
        }
        runner = RunAsync();
    }
    public JobState Submit(JobSpec job)
    {
        if (job.JobId is not { Length: > 0 and <= 64 } || job.Name is null || job.Name.Length > 120 || job.Kind is not ("sha256" or "verify" or "metadata" or "text" or "diagnostic") || (job.Kind == "verify" && (job.ExpectedSha256 is not { Length: 64 } || !job.ExpectedSha256.All(Uri.IsHexDigit)))) throw new InvalidDataException("Invalid or unsupported job");
        lock (gate)
        {
            if (states.TryGetValue(job.JobId, out var prior))
            {
                if (specs.TryGetValue(job.JobId, out var spec) && spec != job) throw new InvalidDataException("Duplicate job ID has different arguments");
                return prior;
            }
            if (MemoryPressure) throw new IOException("Worker paused under memory pressure");
            if (states.Count >= 64)
            {
                string? old = states.FirstOrDefault(p => p.Value.State is not ("running" or "queued")).Key;
                if (old is null) throw new IOException("Job history full");
                states.Remove(old); specs.Remove(old);
            }
            var state = new JobState(job.JobId, "queued", 0);
            states.Add(job.JobId, state); specs[job.JobId] = job; cancellations[job.JobId] = new();
            if (!queue.Writer.TryWrite(job)) { states.Remove(job.JobId); specs.Remove(job.JobId); cancellations.Remove(job.JobId, out var c); c?.Dispose(); throw new IOException("Worker queue full"); }
            Persist(); return state;
        }
    }
    public JobState? Query(string id) { lock (gate) return states.GetValueOrDefault(id); }
    public bool Cancel(string id) { lock (gate) { if (!cancellations.TryGetValue(id, out var c)) return false; c.Cancel(); return true; } }
    private void Persist()
    {
        File.WriteAllText(journal + ".tmp", JsonSerializer.Serialize(states.Values)); File.Move(journal + ".tmp", journal, true);
    }
    private void Update(JobState s)
    {
        lock (gate) { string? previousState = states.GetValueOrDefault(s.JobId)?.State; states[s.JobId] = s; if (previousState != s.State) Persist(); }
        Changed?.Invoke(s);
    }
    private async Task RunAsync()
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stop.Token))
            {
                CancellationTokenSource jobCancel; lock (gate) jobCancel = cancellations[job.JobId];
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, jobCancel.Token);
                try
                {
                    while (MemoryPressure) await Task.Delay(100, linked.Token);
                    linked.Token.ThrowIfCancellationRequested(); Update(new(job.JobId, "running", 0));
                    object result = await ExecuteAsync(job, linked.Token);
                    Update(new(job.JobId, "completed", 1, result));
                }
                catch (OperationCanceledException) { Update(new(job.JobId, "cancelled", 0)); }
                catch (Exception ex) { Update(new(job.JobId, "failed", 0, Error: ex.Message)); }
                finally { lock (gate) { cancellations.Remove(job.JobId); jobCancel.Dispose(); } }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private async Task<object> ExecuteAsync(JobSpec job, CancellationToken ct)
    {
        if (job.Kind == "diagnostic") return job.Name switch
        {
            "memory" => new { managedBytes = GC.GetTotalMemory(false), workingSetBytes = Environment.WorkingSet },
            "version" => new { agent = "BackThread.Simulator", protocol = 1 },
            _ => throw new InvalidDataException("Only memory and version diagnostics are authorized")
        };
        string path = storage.Resolve(job.Name);
        if (job.Kind == "metadata") { var f = new FileInfo(path); if (!f.Exists) throw new FileNotFoundException(); return new { name = f.Name, size = f.Length, modifiedUtc = f.LastWriteTimeUtc }; }
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true);
        byte[] buffer = new byte[32768]; long processed = 0, lines = 0, words = 0; bool inWord = false; int last = -1;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var decoder = Encoding.UTF8.GetDecoder(); char[] chars = new char[32768];
        int n; double lastProgress = 0;
        while ((n = await file.ReadAsync(buffer, ct)) > 0)
        {
            while (MemoryPressure) await Task.Delay(100, ct);
            if (job.Kind == "text")
            {
                int count = decoder.GetChars(buffer, 0, n, chars, 0, file.Position == file.Length);
                for (int i = 0; i < count; i++) { char ch = chars[i]; if (ch == '\n') lines++; if (!char.IsWhiteSpace(ch) && !inWord) words++; inWord = !char.IsWhiteSpace(ch); last = ch; }
            }
            else hash.AppendData(buffer, 0, n);
            processed += n; double progress = file.Length == 0 ? 1 : (double)processed / file.Length;
            if (progress - lastProgress >= .1) { Update(new(job.JobId, "running", progress)); lastProgress = progress; }
            await Task.Yield();
        }
        if (job.Kind == "text") return new { bytes = processed, lines = lines + (last is >= 0 and not 10 ? 1 : 0), words };
        string sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (job.Kind == "verify" && !sha.Equals(job.ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Integrity verification failed");
        return new { sha256 = sha, bytes = processed, verified = job.Kind == "verify" };
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); queue.Writer.TryComplete(); await runner;
        lock (gate) { foreach (var c in cancellations.Values) c.Dispose(); cancellations.Clear(); }
        stop.Dispose();
    }
}
