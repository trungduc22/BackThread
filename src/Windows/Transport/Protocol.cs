using System.Buffers.Binary;
using System.Text.Json;

namespace BackThread.Transport;

public enum LogicalChannel : ushort { Control = 1, Audio, Media, Notifications, Jobs, Files, Diagnostics }
public sealed record Frame(LogicalChannel Channel, uint Sequence, ulong TimestampUs, byte[] Payload);
public sealed record Message(string Type, string? Id, string? ReplyTo, JsonElement Body)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 16 };
    public static Message Create(string type, object? body = null, string? id = null, string? replyTo = null)
        => new(type, id, replyTo, JsonSerializer.SerializeToElement(body ?? new { }, Json));
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this, Json);
    public static Message Decode(byte[] bytes)
    {
        var m = JsonSerializer.Deserialize<Message>(bytes, Json) ?? throw new InvalidDataException("Empty message");
        if (string.IsNullOrWhiteSpace(m.Type) || m.Type.Length > 80 || m.Id?.Length > 64 || m.ReplyTo?.Length > 64 || m.Body.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid message envelope");
        return m;
    }
}

public static class Wire
{
    public const int HeaderSize = 24;
    public const uint Magic = 0x31485442; // BTH1
    public const ushort Version = 1;
    public static int Limit(LogicalChannel channel) => channel switch
    {
        LogicalChannel.Audio => 8192,
        LogicalChannel.Files => 32808,
        >= LogicalChannel.Control and <= LogicalChannel.Diagnostics => 65536,
        _ => throw new InvalidDataException("Unknown logical channel")
    };
    public static byte[] Header(Frame f)
    {
        if (f.Payload.Length > Limit(f.Channel)) throw new InvalidDataException("Frame too large");
        var h = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(h, (uint)f.Payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(4), Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(8), Version);
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(10), (ushort)f.Channel);
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(12), f.Sequence);
        BinaryPrimitives.WriteUInt64LittleEndian(h.AsSpan(16), f.TimestampUs);
        return h;
    }
    public static async ValueTask<Frame> ReadAsync(Stream stream, CancellationToken ct)
    {
        byte[] h = new byte[HeaderSize];
        await stream.ReadExactlyAsync(h, ct);
        if (BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(4)) != Magic || BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(8)) != Version)
            throw new InvalidDataException("Unsupported BackThread protocol");
        var channel = (LogicalChannel)BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(10));
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(h);
        if (size > Limit(channel)) throw new InvalidDataException("Frame too large");
        byte[] payload = new byte[checked((int)size)];
        await stream.ReadExactlyAsync(payload, ct);
        return new(channel, BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(12)), BinaryPrimitives.ReadUInt64LittleEndian(h.AsSpan(16)), payload);
    }
    public static async ValueTask WriteAsync(Stream stream, Frame frame, CancellationToken ct)
    {
        await stream.WriteAsync(Header(frame), ct);
        await stream.WriteAsync(frame.Payload, ct);
    }
}
