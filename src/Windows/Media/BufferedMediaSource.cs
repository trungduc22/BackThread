using BackThread.Worker;

namespace BackThread.Media;

/// <summary>
/// Bounded USB chunk reader for Mode B PC-Assisted Buffered Encoded Media.
/// Serves only requested offsets on demand (pull-based backpressure) using Storage.Chunk Channel 6 framing.
/// </summary>
public sealed class BufferedMediaSource : IDisposable
{
    public const int MaxChunkBytes = 32768;
    private static readonly Dictionary<string, (string Mime, bool Video)> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = ("video/mp4", true),
        [".m4v"] = ("video/x-m4v", true),
        [".mov"] = ("video/quicktime", true),
        [".mp3"] = ("audio/mpeg", false),
        [".m4a"] = ("audio/mp4", false),
        [".aac"] = ("audio/aac", false),
        [".wav"] = ("audio/wav", false)
    };

    private readonly FileStream stream;
    private readonly SemaphoreSlim ioGate = new(1, 1);
    private bool disposed;

    public string StreamId { get; }
    public long Generation { get; }
    public string FilePath { get; }
    public string MimeType { get; }
    public bool SupportsVideo { get; }
    public long TotalBytes { get; }
    public long BytesServed { get; private set; }
    public long ChunksServed { get; private set; }

    private BufferedMediaSource(string streamId, long generation, string filePath, string mimeType, bool supportsVideo, FileStream stream)
    {
        StreamId = streamId;
        Generation = generation;
        FilePath = filePath;
        MimeType = mimeType;
        SupportsVideo = supportsVideo;
        this.stream = stream;
        TotalBytes = stream.Length;
    }

    public static bool CanServeFile(string? path, out string mimeType, out bool supportsVideo, out string reason)
    {
        mimeType = "";
        supportsVideo = false;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            reason = "Select an existing local encoded media file with a full path";
            return false;
        }
        if (!File.Exists(path))
        {
            reason = "Selected local media file does not exist";
            return false;
        }
        string ext = Path.GetExtension(path);
        if (!SupportedExtensions.TryGetValue(ext, out var info))
        {
            reason = $"Unsupported buffered container extension ({ext}); supported: .mp4, .m4v, .mov, .mp3, .m4a, .aac, .wav";
            return false;
        }
        long length = new FileInfo(path).Length;
        if (length <= 0 || length > Storage.MaxFileSize)
        {
            reason = "Local media file must be between 1 byte and 2 GiB";
            return false;
        }
        mimeType = info.Mime;
        supportsVideo = info.Video;
        reason = $"Ready for bounded USB buffered streaming ({info.Mime}, {length} bytes)";
        return true;
    }

    public static BufferedMediaSource Open(string streamId, long generation, string filePath)
    {
        if (streamId is not { Length: 32 } || !streamId.All(Uri.IsHexDigit))
            throw new InvalidDataException("Stream ID must be 32 hex characters");
        if (!CanServeFile(filePath, out string mime, out bool video, out string reason))
            throw new InvalidDataException(reason);
        var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
        return new BufferedMediaSource(streamId.ToLowerInvariant(), generation, filePath, mime, video, fs);
    }

    public async Task<(byte[] FramePayload, int BytesRead, bool Eof)> ReadChunkAsync(long offset, int requestedBytes, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (offset < 0 || requestedBytes <= 0) throw new InvalidDataException("Invalid buffered media byte range");
        int capped = Math.Min(requestedBytes, MaxChunkBytes);

        await ioGate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (offset >= TotalBytes)
                return (Storage.Chunk(StreamId, offset, []), 0, true);
            int toRead = (int)Math.Min(capped, TotalBytes - offset);
            byte[] data = new byte[toRead];
            stream.Position = offset;
            int read = 0;
            while (read < toRead)
            {
                int n = await stream.ReadAsync(data.AsMemory(read, toRead - read), ct);
                if (n == 0) break;
                read += n;
            }
            BytesServed += read;
            ChunksServed++;
            bool eof = offset + read >= TotalBytes;
            return (Storage.Chunk(StreamId, offset, data.AsSpan(0, read)), read, eof);
        }
        finally
        {
            ioGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stream.Dispose();
        ioGate.Dispose();
    }
}
