using System.Diagnostics;
using BackThread.Transport;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BackThread.Audio;

public sealed class LoopbackBridge
{
    public PcmRing Ring { get; } = new();
    public string? DeviceName { get; private set; }
    public string? LastError { get; private set; }
    public long FramesSent { get; private set; }
    private long lastCapture;
    /// <summary>WASAPI shared-mode PCM conversion is requested from the Windows audio engine.</summary>
    public async Task RunAsync(Connection connection, bool fixture, CancellationToken ct, Action? pacingStarted = null)
    {
        using var scheduling = new AudioTimerScope();
        await connection.RequestAsync(LogicalChannel.Control, "audio.start", new { sampleRate = 48000, channels = 2, bits = 16, packetMs = 10 }, ct);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var captureTask = fixture ? Task.CompletedTask : CaptureLoopAsync(linked.Token);
        try
        {
            await Task.Factory.StartNew(() => SendLoop(connection, fixture, ct, pacingStarted), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        finally { linked.Cancel(); await captureTask; }
    }
    private void SendLoop(Connection connection, bool fixture, CancellationToken ct, Action? pacingStarted)
    {
        // Audio deadlines must not wait for file/hash continuations on the general worker pool.
        Thread.CurrentThread.Name = "BackThread PCM pacing";
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
        long sample = 0; var timer = Stopwatch.StartNew(); long next = 0;
        pacingStarted?.Invoke();
        while (!ct.IsCancellationRequested)
        {
                long delay = next - timer.ElapsedMilliseconds;
                if (delay > 0 && ct.WaitHandle.WaitOne((int)delay)) ct.ThrowIfCancellationRequested();
                // Skip late scheduling slots rather than building up audio latency.
                if (timer.ElapsedMilliseconds - next > 50) { Ring.Clear(); next = timer.ElapsedMilliseconds; }
                byte[] pcm = fixture ? PcmRing.Sine(sample) : new byte[PcmRing.BytesPer10Ms];
                if (!fixture)
                {
                    if (Environment.TickCount64 - Interlocked.Read(ref lastCapture) > 150) Ring.Clear();
                    Ring.Read(pcm);
                }
                // Drop-oldest audio writes complete synchronously; no socket I/O occurs on this thread.
                connection.SendFrameAsync(LogicalChannel.Audio, pcm, (ulong)(sample * 1_000_000 / 48000), ct).GetAwaiter().GetResult();
                sample += 480; FramesSent++; next += 10;
        }
        ct.ThrowIfCancellationRequested();
    }
    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var devices = new MMDeviceEnumerator();
                using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                using var capture = new WasapiLoopbackCapture(device) { WaveFormat = new WaveFormat(48000, 16, 2) };
                DeviceName = device.FriendlyName; string id = device.ID;
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                capture.DataAvailable += (_, e) => { Ring.Write(e.Buffer.AsSpan(0, e.BytesRecorded)); Interlocked.Exchange(ref lastCapture, Environment.TickCount64); };
                capture.RecordingStopped += (_, e) => { if (e.Exception != null) LastError = e.Exception.Message; stopped.TrySetResult(); };
                capture.StartRecording(); LastError = null;
                try
                {
                    while (!ct.IsCancellationRequested && !stopped.Task.IsCompleted)
                    {
                        await Task.Delay(1000, ct);
                        using var current = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (current.ID != id) break;
                    }
                }
                finally { capture.StopRecording(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None); Ring.Clear(); }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { LastError = ex.Message; Ring.Clear(); try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; } }
        }
    }
}
