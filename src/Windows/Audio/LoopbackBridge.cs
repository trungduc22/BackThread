using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BackThread.Transport;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BackThread.Audio;

public sealed class LoopbackBridge
{
    public const long SilenceSleepThresholdMs = 10_000;

    // Windows WASAPI shared loopback delivers bursts up to ~100ms; give the capture ring 250ms headroom.
    public PcmRing Ring { get; } = new(PcmRing.BytesPer10Ms * 25);
    public string? DeviceName { get; private set; }
    public string? LastError { get; private set; }
    public long FramesSent { get; private set; }
    public int ActivePacketMs { get; private set; } = 10;
    public bool IsInSilenceSleep => Volatile.Read(ref silenceSleepActive) != 0;
    public long SilenceSleepTransitions => Interlocked.Read(ref silenceSleepTransitions);

    private long lastCapture;
    private long maximumLatenessMs, lateFrames, pacingResets;
    private long nonZeroPackets, zeroPackets, lastNonZeroTick;
    private int lastPeakSample;
    private int silenceSleepActive;
    private long silenceSleepTransitions;

    public long PacingMaximumLatenessMilliseconds => Interlocked.Read(ref maximumLatenessMs);
    public long PacingLateFrames => Interlocked.Read(ref lateFrames);
    public long PacingResets => Interlocked.Read(ref pacingResets);
    public long NonZeroCapturePackets => Interlocked.Read(ref nonZeroPackets);
    public long ZeroCapturePackets => Interlocked.Read(ref zeroPackets);
    public int PeakSampleLevel => Volatile.Read(ref lastPeakSample);

    public string GetCaptureSignalStatus(bool fixture, bool streamActive, bool pcEndpointMuted)
    {
        if (!streamActive) return "stopped";
        if (fixture) return "fixture";
        if (IsInSilenceSleep) return "silence-sleep";
        long now = Environment.TickCount64;
        long lastNz = Interlocked.Read(ref lastNonZeroTick);
        if (Interlocked.Read(ref nonZeroPackets) > 0 && lastNz != 0 && now - lastNz <= 3000)
            return "signal-present";
        if (pcEndpointMuted && Interlocked.Read(ref zeroPackets) > 0)
            return "silent-driver-coupled";
        return "silent-idle";
    }

    internal void ObserveCapturedPcm(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2) return;
        Ring.Write(bytes);
        Interlocked.Exchange(ref lastCapture, Environment.TickCount64);
        var samples = MemoryMarshal.Cast<byte, short>(bytes.Slice(0, bytes.Length & ~1));
        int peak = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            short s = samples[i];
            int abs = s == short.MinValue ? 32767 : (s < 0 ? -s : s);
            if (abs > peak) peak = abs;
            if (peak > 16) break;
        }
        Volatile.Write(ref lastPeakSample, peak);
        if (peak > 8)
        {
            Interlocked.Increment(ref nonZeroPackets);
            Interlocked.Exchange(ref lastNonZeroTick, Environment.TickCount64);
        }
        else
        {
            Interlocked.Increment(ref zeroPackets);
        }
    }

    /// <summary>WASAPI shared-mode PCM conversion is requested from the Windows audio engine.</summary>
    public async Task RunAsync(
        Connection connection,
        bool fixture,
        CancellationToken ct,
        Action? pacingStarted = null,
        AdaptiveAudioProfile? audioProfile = null,
        bool enableSilenceSleep = true)
    {
        var profile = audioProfile ?? AdaptiveAudioProfile.ForRoute(TransportRouteKind.Usb);
        int packetMs = profile.PacketMs == 20 ? 20 : 10;
        ActivePacketMs = packetMs;
        Volatile.Write(ref silenceSleepActive, 0);

        await connection.RequestAsync(LogicalChannel.Control, "audio.start", new
        {
            sampleRate = 48000,
            channels = 2,
            bits = 16,
            packetMs,
            primeMs = profile.PrimeMs,
            ringCapacityMs = profile.RingCapacityMs
        }, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var captureTask = fixture ? Task.CompletedTask : CaptureLoopAsync(linked.Token);
        try
        {
            await Task.Factory.StartNew(
                () => SendLoop(connection, fixture, packetMs, enableSilenceSleep, ct, pacingStarted),
                ct,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }
        finally
        {
            Volatile.Write(ref silenceSleepActive, 0);
            linked.Cancel();
            await captureTask;
        }
    }

    private void SendLoop(Connection connection, bool fixture, int packetMs, bool enableSilenceSleep, CancellationToken ct, Action? pacingStarted)
    {
        // Audio deadlines must not wait for file/hash continuations on the general worker pool.
        Thread.CurrentThread.Name = "BackThread PCM pacing";
        Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;

        int framesPerPacket = packetMs * 48; // 480 for 10ms, 960 for 20ms
        int bytesPerPacket = framesPerPacket * PcmRing.BytesPerSample;
        long sample = 0;
        long epoch = Stopwatch.GetTimestamp();
        double nextMs = 0;
        long streamStartTick = Environment.TickCount64;
        AudioTimerScope? timerScope = new();

        try
        {
            pacingStarted?.Invoke();
            while (!ct.IsCancellationRequested)
            {
                // Low-thread / weak-PC Silence Sleep: when real capture has been silent for >10s,
                // release timeBeginPeriod(1) and sleep at 100ms cadence until audio resumes.
                if (!fixture && enableSilenceSleep)
                {
                    long nowTick = Environment.TickCount64;
                    long lastNz = Interlocked.Read(ref lastNonZeroTick);
                    long silentForMs = lastNz == 0 ? nowTick - streamStartTick : nowTick - lastNz;
                    if (silentForMs >= SilenceSleepThresholdMs && Ring.Count == 0)
                    {
                        if (Volatile.Read(ref silenceSleepActive) == 0)
                        {
                            Volatile.Write(ref silenceSleepActive, 1);
                            Interlocked.Increment(ref silenceSleepTransitions);
                            timerScope?.Dispose();
                            timerScope = null;
                        }
                        if (ct.WaitHandle.WaitOne(100)) ct.ThrowIfCancellationRequested();
                        continue;
                    }
                    else if (Volatile.Read(ref silenceSleepActive) != 0)
                    {
                        Volatile.Write(ref silenceSleepActive, 0);
                        timerScope = new AudioTimerScope();
                        epoch = Stopwatch.GetTimestamp();
                        nextMs = 0;
                    }
                }

                double elapsedMs = Stopwatch.GetElapsedTime(epoch).TotalMilliseconds;
                double delayMs = nextMs - elapsedMs;
                if (delayMs >= 1.0 && ct.WaitHandle.WaitOne((int)delayMs)) ct.ThrowIfCancellationRequested();

                elapsedMs = Stopwatch.GetElapsedTime(epoch).TotalMilliseconds;
                long lateness = Math.Max(0L, (long)(elapsedMs - nextMs));
                long old = Interlocked.Read(ref maximumLatenessMs);
                while (lateness > old) { long observed = Interlocked.CompareExchange(ref maximumLatenessMs, lateness, old); if (observed == old) break; old = observed; }
                if (lateness >= packetMs) Interlocked.Increment(ref lateFrames);

                // After a severe stall (> 120ms), skip missed timeline slots without clearing fresh capture samples;
                // the circular Ring already bounds capture age by dropping oldest data.
                if (elapsedMs - nextMs > 120.0)
                {
                    Interlocked.Increment(ref pacingResets);
                    nextMs = elapsedMs;
                }

                int burst = 0;
                do
                {
                    byte[] pcm = fixture ? PcmRing.Sine(sample, framesPerPacket) : new byte[bytesPerPacket];
                    if (!fixture)
                    {
                        if (Ring.Count > 0 && Environment.TickCount64 - Interlocked.Read(ref lastCapture) > 150) Ring.Clear();
                        Ring.Read(pcm);
                    }
                    // Drop-oldest audio writes complete synchronously; no socket I/O occurs on this thread.
                    connection.SendFrameAsync(LogicalChannel.Audio, pcm, (ulong)(sample * 1_000_000 / 48000), ct).GetAwaiter().GetResult();
                    sample += framesPerPacket;
                    FramesSent++;
                    nextMs += packetMs;
                    burst++;
                }
                while (!ct.IsCancellationRequested && burst < 3 && Stopwatch.GetElapsedTime(epoch).TotalMilliseconds >= nextMs + (packetMs / 2.0));
            }
            ct.ThrowIfCancellationRequested();
        }
        finally
        {
            Volatile.Write(ref silenceSleepActive, 0);
            timerScope?.Dispose();
        }
    }

    private Task CaptureLoopAsync(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows()) return CaptureWindowsLoopAsync(ct);
        LastError = "WASAPI loopback capture requires Windows";
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task CaptureWindowsLoopAsync(CancellationToken ct)
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
                capture.DataAvailable += (_, e) => ObserveCapturedPcm(e.Buffer.AsSpan(0, e.BytesRecorded));
                capture.RecordingStopped += (_, e) => { if (e.Exception != null) LastError = e.Exception.Message; stopped.TrySetResult(); };
                capture.StartRecording(); LastError = null;
                try
                {
                    while (!ct.IsCancellationRequested && !stopped.Task.IsCompleted)
                    {
                        await Task.Delay(2000, ct);
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
