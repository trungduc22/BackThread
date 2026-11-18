using System.Diagnostics;

namespace BackThread.DeviceTests;

public sealed record TimedAudioSample<T>(double ElapsedSeconds, T Value);
public sealed record AudioMeasurementResult<T>(double RequestedSeconds, IReadOnlyList<TimedAudioSample<T>> Samples);

/// <summary>Source deadline and sample admission share the first PCM pacing timestamp.</summary>
public static class AudioMeasurementWindow
{
    public static async Task<AudioMeasurementResult<T>> RunAsync<T>(TimeSpan duration,
        Func<Action, CancellationToken, Task> runSource,
        Func<TimeSpan, CancellationToken, Task<T>> readSample,
        CancellationToken ct, TimeSpan? sampleInterval = null)
    {
        TimeSpan interval = sampleInterval ?? TimeSpan.FromSeconds(1);
        if (duration <= TimeSpan.Zero || interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        using var sourceStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startup.CancelAfter(TimeSpan.FromSeconds(20));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clock = new Stopwatch();
        void Start()
        {
            clock.Start();
            sourceStop.CancelAfter(duration);
            started.TrySetResult();
        }
        Task source = runSource(Start, sourceStop.Token);
        var samples = new List<TimedAudioSample<T>>();
        try
        {
            await Task.WhenAny(started.Task, source).WaitAsync(startup.Token);
            if (!started.Task.IsCompleted)
            {
                await source;
                throw new IOException("Audio source ended before PCM pacing started");
            }
            // Leave time for the final request. A slow reply crossing the deadline is excluded.
            TimeSpan finalTarget = duration - TimeSpan.FromTicks(Math.Min(interval.Ticks / 4, duration.Ticks / 4));
            TimeSpan target = interval < finalTarget ? interval : finalTarget;
            try
            {
                while (true)
                {
                    TimeSpan delay = target - clock.Elapsed;
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, sourceStop.Token);
                    if (source.IsCompleted) { await source; throw new IOException("PCM source ended before its measurement deadline"); }
                    sourceStop.Token.ThrowIfCancellationRequested();
                    T value = await readSample(clock.Elapsed, sourceStop.Token).WaitAsync(sourceStop.Token);
                    TimeSpan completed = clock.Elapsed;
                    if (!sourceStop.IsCancellationRequested && completed < duration && !source.IsCompleted)
                        samples.Add(new(completed.TotalSeconds, value));
                    if (target == finalTarget || completed >= finalTarget) break;
                    // Missed polls are skipped, not added to the duration of the next poll.
                    long nextTicks = (completed.Ticks / interval.Ticks + 1) * interval.Ticks;
                    target = TimeSpan.FromTicks(Math.Min(nextTicks, finalTarget.Ticks));
                }
                await source;
                if (!sourceStop.IsCancellationRequested) throw new IOException("PCM source ended before its measurement deadline");
            }
            catch (OperationCanceledException) when (sourceStop.IsCancellationRequested && !ct.IsCancellationRequested) { }
            ct.ThrowIfCancellationRequested();
            return new(duration.TotalSeconds, samples);
        }
        finally
        {
            sourceStop.Cancel();
            try { await source; } catch (OperationCanceledException) when (sourceStop.IsCancellationRequested) { }
        }
    }
}
