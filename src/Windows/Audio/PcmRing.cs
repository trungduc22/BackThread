namespace BackThread.Audio;

/// <summary>Interleaved signed 16-bit stereo, 48 kHz. Drop oldest complete stereo samples on overflow.</summary>
public sealed class PcmRing
{
    public const int SampleRate = 48000, Channels = 2, BytesPerSample = 4, BytesPer10Ms = 1920;
    private readonly byte[] buffer;
    private int head, count;
    private readonly object gate = new();
    public long DroppedBytes { get; private set; }
    public long SilenceBytes { get; private set; }
    public int Count { get { lock (gate) return count; } }
    public PcmRing(int capacity = BytesPer10Ms * 10)
    {
        if (capacity <= 0 || capacity % 4 != 0) throw new ArgumentOutOfRangeException(nameof(capacity)); buffer = new byte[capacity];
    }
    public void Clear() { lock (gate) { head = 0; count = 0; } }
    public void Write(ReadOnlySpan<byte> input)
    {
        if (input.Length % 4 != 0) throw new InvalidDataException("PCM must contain complete stereo samples");
        lock (gate)
        {
            if (input.Length > buffer.Length) { DroppedBytes += input.Length - buffer.Length; input = input[^buffer.Length..]; }
            int drop = Math.Max(0, count + input.Length - buffer.Length); head = (head + drop) % buffer.Length; count -= drop; DroppedBytes += drop;
            int tail = (head + count) % buffer.Length, first = Math.Min(input.Length, buffer.Length - tail);
            input[..first].CopyTo(buffer.AsSpan(tail)); input[first..].CopyTo(buffer); count += input.Length;
        }
    }
    public int Read(Span<byte> output, bool zeroFill = true)
    {
        if (output.Length % 4 != 0) throw new InvalidDataException("PCM read must be sample aligned");
        lock (gate)
        {
            int n = Math.Min(output.Length, count), first = Math.Min(n, buffer.Length - head);
            buffer.AsSpan(head, first).CopyTo(output); buffer.AsSpan(0, n - first).CopyTo(output[first..]); head = (head + n) % buffer.Length; count -= n;
            if (zeroFill) { output[n..].Clear(); SilenceBytes += output.Length - n; }
            return n;
        }
    }
    public static byte[] Sine(long firstSample, int frames = 480)
    {
        byte[] pcm = new byte[frames * 4];
        for (int i = 0; i < frames; i++)
        {
            short s = (short)(Math.Sin(2 * Math.PI * 440 * (firstSample + i) / SampleRate) * 4096);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4), s);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 4 + 2), s);
        }
        return pcm;
    }
    public static void FloatToPcm16(ReadOnlySpan<float> input, Span<byte> output)
    {
        if (output.Length != input.Length * 2) throw new ArgumentException("PCM buffer size");
        for (int i = 0; i < input.Length; i++)
        {
            float v = float.IsNaN(input[i]) ? 0 : Math.Clamp(input[i], -1f, 1f);
            short s = v >= 1 ? short.MaxValue : (short)(v * 32768f);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(output[(i * 2)..], s);
        }
    }
}
