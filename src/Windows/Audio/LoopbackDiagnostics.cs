using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BackThread.Audio;

public static class LoopbackDiagnostics
{
    /// <summary>Plays a quiet deterministic tone for 1.5 seconds and measures the real Windows loopback stream.</summary>
    public static async Task<object> ProbeAsync(CancellationToken ct)
    {
        using var devices = new MMDeviceEnumerator(); using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        using var capture = new WasapiLoopbackCapture(device) { WaveFormat = new WaveFormat(48000, 16, 2) };
        long captured = 0, nonzero = 0; int callbacks = 0; Exception? captureError = null;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.DataAvailable += (_, e) => { Interlocked.Add(ref captured, e.BytesRecorded); Interlocked.Increment(ref callbacks); for (int i = 0; i < e.BytesRecorded; i++) if (e.Buffer[i] != 0) Interlocked.Increment(ref nonzero); };
        capture.RecordingStopped += (_, e) => { captureError = e.Exception; stopped.TrySetResult(); };
        byte[] pcm = PcmRing.Sine(0, 48000 * 2);
        for (int i = 0; i < pcm.Length; i += 2) { short sample = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i)); System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i), (short)(sample / 32)); }
        using var source = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(48000, 16, 2));
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 50); output.Init(source);
        capture.StartRecording();
        try { output.Play(); await Task.Delay(1500, ct); }
        finally { output.Stop(); capture.StopRecording(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None); }
        if (captureError != null) throw new IOException("WASAPI capture failed", captureError);
        if (captured == 0 || nonzero == 0) throw new IOException("WASAPI returned no nonzero loopback data");
        return new { realWindowsCapture = true, physicalIPhoneTest = false, sampleRate = 48000, channels = 2, bits = 16, capturedBytes = captured, nonzeroBytes = nonzero, callbacks, device = device.FriendlyName, humanListeningVerified = false };
    }
}
