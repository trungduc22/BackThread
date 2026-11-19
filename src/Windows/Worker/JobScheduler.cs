using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using BackThread.Transport;

namespace BackThread.Worker;

public sealed record JobSpec(
    string JobId,
    string Kind,
    string Name,
    string? ExpectedSha256 = null,
    Dictionary<string, string>? Parameters = null);

public sealed record JobState(string JobId, string State, double Progress, object? Result = null, string? Error = null);

public sealed class JobScheduler : IAsyncDisposable
{
    public static readonly HashSet<string> SupportedKinds = new(StringComparer.Ordinal)
    {
        "sha256",
        "verify",
        "metadata",
        "text",
        "diagnostic",
        "compress",
        "decompress",
        "log.scan",
        "image.resize",
        "pdf.extract",
        "feed.poll",
        "ocr.extract",
        "speech.transcribe"
    };

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
                foreach (var s in JsonSerializer.Deserialize<JobState[]>(File.ReadAllText(journal)) ?? [])
                    states[s.JobId] = s.State is "queued" or "running" ? s with { State = "interrupted", Error = "Agent restarted; submit a new job ID to retry" } : s;
            }
            catch (JsonException) { File.Move(journal, journal + ".corrupt", true); }
        }
        runner = RunAsync();
    }

    public JobState Submit(JobSpec job)
    {
        if (job.JobId is not { Length: > 0 and <= 64 }
            || job.Name is null
            || job.Name.Length > 120
            || !SupportedKinds.Contains(job.Kind)
            || (job.Kind == "verify" && (job.ExpectedSha256 is not { Length: 64 } || !job.ExpectedSha256.All(Uri.IsHexDigit))))
            throw new InvalidDataException("Invalid or unsupported job");

        lock (gate)
        {
            if (states.TryGetValue(job.JobId, out var prior))
            {
                if (specs.TryGetValue(job.JobId, out var spec) && !JobSpecsEqual(spec, job))
                    throw new InvalidDataException("Duplicate job ID has different arguments");
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
            if (!queue.Writer.TryWrite(job))
            {
                states.Remove(job.JobId); specs.Remove(job.JobId); cancellations.Remove(job.JobId, out var c); c?.Dispose();
                throw new IOException("Worker queue full");
            }
            Persist();
            return state;
        }
    }

    private static bool JobSpecsEqual(JobSpec a, JobSpec b)
    {
        if (a.JobId != b.JobId || a.Kind != b.Kind || a.Name != b.Name || a.ExpectedSha256 != b.ExpectedSha256) return false;
        int ac = a.Parameters?.Count ?? 0;
        int bc = b.Parameters?.Count ?? 0;
        if (ac != bc) return false;
        if (ac == 0) return true;
        foreach (var kv in a.Parameters!)
            if (!b.Parameters!.TryGetValue(kv.Key, out var bv) || bv != kv.Value) return false;
        return true;
    }

    public JobState? Query(string id) { lock (gate) return states.GetValueOrDefault(id); }
    public bool Cancel(string id) { lock (gate) { if (!cancellations.TryGetValue(id, out var c)) return false; c.Cancel(); return true; } }

    private void Persist()
    {
        File.WriteAllText(journal + ".tmp", JsonSerializer.Serialize(states.Values));
        File.Move(journal + ".tmp", journal, true);
    }

    private void Update(JobState s)
    {
        lock (gate)
        {
            string? previousState = states.GetValueOrDefault(s.JobId)?.State;
            states[s.JobId] = s;
            if (previousState != s.State) Persist();
        }
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
            "version" => new { agent = "BackThread.Simulator", protocol = 2 },
            _ => throw new InvalidDataException("Only memory and version diagnostics are authorized")
        };

        if (job.Kind == "feed.poll")
        {
            string feedName = job.Name;
            string url = job.Parameters?.GetValueOrDefault("url") ?? "";
            return new { feed = feedName, url, polledUtc = DateTimeOffset.UtcNow, simulated = true, status = "ok" };
        }

        string path = storage.Resolve(job.Name);
        if (job.Kind == "metadata")
        {
            var f = new FileInfo(path);
            if (!f.Exists) throw new FileNotFoundException();
            return new { name = f.Name, size = f.Length, modifiedUtc = f.LastWriteTimeUtc };
        }

        if (job.Kind == "compress")
            return await ExecuteCompressAsync(job, path, ct);

        if (job.Kind == "decompress")
            return await ExecuteDecompressAsync(job, path, ct);

        if (job.Kind == "log.scan")
            return await ExecuteLogScanAsync(job, path, ct);

        if (job.Kind is "image.resize" or "pdf.extract" or "ocr.extract" or "speech.transcribe")
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) throw new FileNotFoundException();
            return new
            {
                kind = job.Kind,
                name = fi.Name,
                bytes = fi.Length,
                simulated = true,
                text = job.Kind is "ocr.extract" or "pdf.extract" or "speech.transcribe"
                    ? $"[simulated {job.Kind} for {fi.Name} ({fi.Length} bytes)]"
                    : null
            };
        }

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

    private async Task<object> ExecuteCompressAsync(JobSpec job, string inputPath, CancellationToken ct)
    {
        string algorithm = (job.Parameters?.GetValueOrDefault("algorithm") ?? "gzip").ToLowerInvariant();
        string outputName = job.Parameters?.GetValueOrDefault("outputName") ?? $"{job.Name}.{(algorithm == "zlib" ? "zlib" : "gz")}";
        string outputPath = storage.Resolve(outputName);

        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true);
        await using var output = new FileStream(outputPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 32768, true);
        await using (Stream codec = algorithm switch
        {
            "zlib" => new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true),
            "deflate" => new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true),
            _ => new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)
        })
        {
            byte[] buf = new byte[32768];
            long processed = 0;
            double lastProgress = 0;
            int n;
            while ((n = await input.ReadAsync(buf, ct)) > 0)
            {
                while (MemoryPressure) await Task.Delay(100, ct);
                await codec.WriteAsync(buf.AsMemory(0, n), ct);
                processed += n;
                double progress = input.Length == 0 ? 1 : (double)processed / input.Length;
                if (progress - lastProgress >= 0.1) { Update(new(job.JobId, "running", progress)); lastProgress = progress; }
            }
        }
        long inputBytes = input.Length;
        long outputBytes = output.Length;
        output.Close();
        File.Move(outputPath + ".tmp", outputPath, overwrite: true);
        double ratio = inputBytes > 0 ? Math.Round((double)outputBytes / inputBytes, 4) : 1.0;
        return new { outputName, algorithm, inputBytes, outputBytes, ratio };
    }

    private async Task<object> ExecuteDecompressAsync(JobSpec job, string inputPath, CancellationToken ct)
    {
        string algorithm = (job.Parameters?.GetValueOrDefault("algorithm") ?? "gzip").ToLowerInvariant();
        string defaultOut = job.Name.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
            ? job.Name[..^3]
            : job.Name.EndsWith(".zlib", StringComparison.OrdinalIgnoreCase) ? job.Name[..^5] : job.Name + ".out";
        string outputName = job.Parameters?.GetValueOrDefault("outputName") ?? defaultOut;
        string outputPath = storage.Resolve(outputName);

        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true);
        await using var output = new FileStream(outputPath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 32768, true);
        await using Stream codec = algorithm switch
        {
            "zlib" => new ZLibStream(input, CompressionMode.Decompress, leaveOpen: true),
            "deflate" => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true),
            _ => new GZipStream(input, CompressionMode.Decompress, leaveOpen: true)
        };
        byte[] buf = new byte[32768];
        long totalOut = 0;
        const long MaxDecompressBytes = 256L * 1024 * 1024;
        int n;
        while ((n = await codec.ReadAsync(buf, ct)) > 0)
        {
            while (MemoryPressure) await Task.Delay(100, ct);
            totalOut += n;
            if (totalOut > MaxDecompressBytes) throw new InvalidDataException("Decompressed output exceeds 256 MiB safety cap");
            await output.WriteAsync(buf.AsMemory(0, n), ct);
        }
        long inputBytes = input.Length;
        output.Close();
        File.Move(outputPath + ".tmp", outputPath, overwrite: true);
        return new { outputName, algorithm, inputBytes, outputBytes = totalOut };
    }

    private async Task<object> ExecuteLogScanAsync(JobSpec job, string inputPath, CancellationToken ct)
    {
        string pattern = job.Parameters?.GetValueOrDefault("pattern") ?? "ERROR";
        int maxSamples = int.TryParse(job.Parameters?.GetValueOrDefault("maxSamples"), out int ms) ? Math.Clamp(ms, 1, 50) : 20;
        var samples = new List<string>(maxSamples);
        long totalLines = 0, matchedLines = 0;

        using var reader = new StreamReader(inputPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 32768);
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            while (MemoryPressure) await Task.Delay(100, ct);
            totalLines++;
            if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                matchedLines++;
                if (samples.Count < maxSamples)
                    samples.Add(line.Length > 240 ? line[..240] : line);
            }
        }
        return new { pattern, totalLines, matchedLines, samples };
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); queue.Writer.TryComplete(); await runner;
        lock (gate) { foreach (var c in cancellations.Values) c.Dispose(); cancellations.Clear(); }
        stop.Dispose();
    }
}
