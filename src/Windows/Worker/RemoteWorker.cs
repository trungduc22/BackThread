using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using BackThread.Transport;

namespace BackThread.Worker;

public sealed class RemoteWorker(Connection connection)
{
    private readonly SemaphoreSlim uploadGate = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<long>> acknowledgements = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> downloads = new();
    public void Receive(Frame frame, Message? message)
    {
        if (message?.Type == "file.ack")
        {
            string id = message.Body.GetProperty("transferId").GetString()!;
            if (acknowledgements.TryRemove(id, out var tcs)) tcs.TrySetResult(message.Body.GetProperty("offset").GetInt64());
        }
        if (frame.Channel == LogicalChannel.Files && frame.Payload.Length >= 40)
        {
            string id = Encoding.ASCII.GetString(frame.Payload, 0, 32);
            if (downloads.TryRemove(id, out var tcs)) tcs.TrySetResult(frame.Payload[40..]);
        }
    }
    public Task<Message> SubmitAsync(JobSpec job, CancellationToken ct) => connection.RequestAsync(LogicalChannel.Jobs, "job.submit", job, ct);
    public Task<Message> CancelAsync(string id, CancellationToken ct) => connection.RequestAsync(LogicalChannel.Jobs, "job.cancel", new { jobId = id }, ct);
    public Task<Message> QueryAsync(string id, CancellationToken ct) => connection.RequestAsync(LogicalChannel.Jobs, "job.query", new { jobId = id }, ct);
    public async Task<Message> UploadAsync(string path, string name, CancellationToken ct)
    {
        await uploadGate.WaitAsync(ct);
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true);
            string sha = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant(); file.Position = 0;
            string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name + ":" + sha))).ToLowerInvariant()[..32];
            var begin = await connection.RequestAsync(LogicalChannel.Jobs, "file.begin", new TransferSpec(id, name, file.Length, sha), ct);
            long offset = begin.Body.GetProperty("offset").GetInt64(); if (offset < 0 || offset > file.Length) throw new InvalidDataException("Invalid resume offset"); file.Position = offset;
            byte[] buffer = new byte[32768]; int n;
            while ((n = await file.ReadAsync(buffer, ct)) > 0)
            {
                var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously); acknowledgements[id] = tcs;
                try
                {
                    await connection.SendFrameAsync(LogicalChannel.Files, Storage.Chunk(id, offset, buffer.AsSpan(0, n)), 0, ct);
                    long ack = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
                    if (ack != offset + n) throw new InvalidDataException("Unexpected file acknowledgement"); offset = ack;
                }
                finally { acknowledgements.TryRemove(id, out _); }
            }
            return await connection.RequestAsync(LogicalChannel.Jobs, "file.commit", new { transferId = id }, ct, TimeSpan.FromMinutes(5));
        }
        finally { uploadGate.Release(); }
    }
    public async Task<byte[]> ReadArtifactAsync(string name, long offset, int count, CancellationToken ct)
    {
        if (downloads.Count >= 4) throw new IOException("Too many downloads");
        string id = Guid.NewGuid().ToString("N"); var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); downloads[id] = tcs;
        try
        {
            await connection.RequestAsync(LogicalChannel.Jobs, "file.read", new { name, offset, count = Math.Clamp(count, 1, 32768), transferId = id }, ct);
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        finally { downloads.TryRemove(id, out _); }
    }
}
