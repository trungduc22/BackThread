using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BackThread.Worker;

public sealed record TransferSpec(string TransferId, string Name, long Size, string Sha256);
public sealed class Storage
{
    public const long MaxFileSize = 2L * 1024 * 1024 * 1024;
    private readonly string root;
    private readonly long quota;
    public Storage(string root, long quota = MaxFileSize) { if (quota < 1) throw new ArgumentOutOfRangeException(nameof(quota)); this.quota = quota; this.root = Path.GetFullPath(root); Directory.CreateDirectory(this.root); }
    public string Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name is "." or ".." || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_') || name.EndsWith('.') || name.StartsWith('.'))
            throw new InvalidDataException("Use a simple storage name: ASCII letters, digits, dot, underscore, dash");
        // Reject Windows device names even on other operating systems.
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9'))
            throw new InvalidDataException("Reserved storage name");
        return Path.Combine(root, name);
    }
    private string Part(string id)
    {
        if (id is not { Length: 32 } || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid transfer identifier");
        return Path.Combine(root, "." + id.ToLowerInvariant() + ".part");
    }
    public async Task<long> BeginAsync(TransferSpec spec, CancellationToken ct)
    {
        Resolve(spec.Name);
        if (spec.Size < 0 || spec.Size > MaxFileSize || spec.Sha256 is not { Length: 64 } || !spec.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid size or checksum");
        string part = Part(spec.TransferId), meta = part + ".json";
        if (File.Exists(meta))
        {
            var old = JsonSerializer.Deserialize<TransferSpec>(await File.ReadAllTextAsync(meta, ct));
            if (old != spec) throw new InvalidDataException("Transfer identifier is already bound to different content");
        }
        else
        {
            if (Directory.GetFiles(root, ".*.part.json").Length >= 16) throw new IOException("Too many unfinished transfers; clean up or complete existing transfers");
            long reserved = 0; int completed = 0;
            foreach (string path in Directory.EnumerateFiles(root))
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".part.json", StringComparison.Ordinal)) { var existing = JsonSerializer.Deserialize<TransferSpec>(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidDataException("Invalid transfer journal"); reserved = checked(reserved + existing.Size); }
                else if (!name.StartsWith('.')) { reserved = checked(reserved + new FileInfo(path).Length); completed++; }
            }
            if (reserved > quota - spec.Size) throw new IOException("Application storage quota exceeded (2 GiB by default), including reserved partial transfers");
            if (completed >= 256 && !File.Exists(Resolve(spec.Name))) throw new IOException("Stored file count limit reached");
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            if (drive.AvailableFreeSpace < spec.Size + 64L * 1024 * 1024) throw new IOException("Insufficient storage: preserve at least 64 MiB free");
            await File.WriteAllTextAsync(meta + ".tmp", JsonSerializer.Serialize(spec), ct); File.Move(meta + ".tmp", meta, true);
        }
        using var file = new FileStream(part, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        if (file.Length > spec.Size) throw new InvalidDataException("Partial transfer exceeds declared length");
        return file.Length;
    }
    public async Task<(string Id, long Offset)> AppendAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length is < 40 or > 32808) throw new InvalidDataException("Invalid file chunk");
        string id = Encoding.ASCII.GetString(payload, 0, 32), part = Part(id);
        long offset = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(32));
        var spec = JsonSerializer.Deserialize<TransferSpec>(await File.ReadAllTextAsync(part + ".json", ct)) ?? throw new InvalidDataException("Missing transfer");
        await using var file = new FileStream(part, FileMode.Open, FileAccess.Write, FileShare.Read, 32768, true);
        if (offset < 0 || offset > file.Length || offset + payload.Length - 40 > spec.Size) throw new InvalidDataException("Invalid transfer offset");
        if (offset < file.Length)
        {
            if (offset + payload.Length - 40 > file.Length) throw new InvalidDataException("Overlapping file chunk");
            return (id, file.Length); // Retry of acknowledged content; final checksum remains authoritative.
        }
        file.Position = offset; await file.WriteAsync(payload.AsMemory(40), ct); await file.FlushAsync(ct);
        return (id, file.Position);
    }
    public async Task<object> CommitAsync(string id, CancellationToken ct)
    {
        string part = Part(id), meta = part + ".json";
        var spec = JsonSerializer.Deserialize<TransferSpec>(await File.ReadAllTextAsync(meta, ct)) ?? throw new InvalidDataException("Missing transfer");
        string hash;
        await using (var file = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.Read, 32768, true))
        {
            if (file.Length != spec.Size) throw new InvalidDataException("Transfer incomplete");
            hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).ToLowerInvariant();
        }
        if (!hash.Equals(spec.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checksum mismatch; partial file retained for diagnosis");
        File.Move(part, Resolve(spec.Name), true); File.Delete(meta);
        return new { name = spec.Name, size = spec.Size, sha256 = hash };
    }
    public static byte[] Chunk(string id, long offset, ReadOnlySpan<byte> data)
    {
        byte[] b = new byte[40 + data.Length]; Encoding.ASCII.GetBytes(id, b); BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(32), offset); data.CopyTo(b.AsSpan(40)); return b;
    }
    public IEnumerable<object> List() => Directory.EnumerateFiles(root).Where(p => !Path.GetFileName(p).StartsWith('.')).Take(256).Select(p => { var f = new FileInfo(p); return new { name = f.Name, size = f.Length, modifiedUtc = f.LastWriteTimeUtc }; });
}
