using System.Collections.Concurrent;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using BackThread.Transport;

namespace BackThread.Worker;

public sealed class RemoteWorker(Connection connection)
{
    private readonly SemaphoreSlim uploadGate = new(1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<long>> acknowledgements = new();
    private sealed record Download(TaskCompletionSource<byte[]> Completion, long Offset, int Count);
    private readonly ConcurrentDictionary<string, Download> downloads = new();
    private readonly SemaphoreSlim downloadSlots = new(4, 4);
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
            if (downloads.TryRemove(id, out var request))
            {
                if (BinaryPrimitives.ReadInt64LittleEndian(frame.Payload.AsSpan(32)) != request.Offset || frame.Payload.Length - 40 > request.Count)
                    request.Completion.TrySetException(new InvalidDataException("Artifact offset or length does not match the request"));
                else request.Completion.TrySetResult(frame.Payload[40..]);
            }
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
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (!await downloadSlots.WaitAsync(0, ct)) throw new IOException("Too many downloads");
        count = Math.Clamp(count, 1, 32768);
        string id = Guid.NewGuid().ToString("N"); var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously); downloads[id] = new(tcs, offset, count);
        try
        {
            var response = await connection.RequestAsync(LogicalChannel.Jobs, "file.read", new { name, offset, count, transferId = id }, ct);
            byte[] data = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            if (!response.Body.TryGetProperty("length", out var length) || !length.TryGetInt32(out int declared) || declared != data.Length)
                throw new InvalidDataException("Artifact reply length does not match received data");
            return data;
        }
        finally { downloads.TryRemove(id, out _); downloadSlots.Release(); }
    }
}
