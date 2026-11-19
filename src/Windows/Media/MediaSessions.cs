using System.Runtime.CompilerServices;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace BackThread.Media;

public sealed record MediaSnapshot(string Source, string Title, string Artist, string Album, string State, double PositionSeconds, double DurationSeconds, bool CanSeek, string? ArtworkBase64, bool CanPlay, bool CanPause, bool CanNext, bool CanPrevious);
public sealed class MediaSessions : IDisposable
{
    private readonly Lock hookGate = new();
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private GlobalSystemMediaTransportControlsSession? hookedSession;
    private string? artworkKey, artwork;

    public event Action? Changed;

    public Task InitializeAsync() => OperatingSystem.IsWindows() ? InitializeWindowsAsync() : Task.CompletedTask;
    public Task<MediaSnapshot?> SnapshotAsync() => OperatingSystem.IsWindows() ? SnapshotWindowsAsync() : Task.FromResult<MediaSnapshot?>(null);
    public Task<bool> CommandAsync(string command, double seconds = 0, string? source = null) => OperatingSystem.IsWindows() ? CommandWindowsAsync(command, seconds, source) : Task.FromResult(false);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task InitializeWindowsAsync()
    {
        manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        if (manager is not null)
        {
            manager.CurrentSessionChanged += OnCurrentSessionChanged;
            HookCurrentSession();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        HookCurrentSession();
        Changed?.Invoke();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnSessionPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => Changed?.Invoke();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnSessionPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => Changed?.Invoke();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void HookCurrentSession()
    {
        lock (hookGate)
        {
            var next = manager?.GetCurrentSession();
            if (ReferenceEquals(hookedSession, next)) return;
            if (hookedSession is not null)
            {
                try
                {
                    hookedSession.MediaPropertiesChanged -= OnSessionPropertiesChanged;
                    hookedSession.PlaybackInfoChanged -= OnSessionPlaybackChanged;
                }
                catch { }
            }
            hookedSession = next;
            if (hookedSession is not null)
            {
                try
                {
                    hookedSession.MediaPropertiesChanged += OnSessionPropertiesChanged;
                    hookedSession.PlaybackInfoChanged += OnSessionPlaybackChanged;
                }
                catch { }
            }
        }
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        DisposeWindowsResources();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisposeWindowsResources()
    {
        lock (hookGate)
        {
            if (hookedSession is not null)
            {
                try
                {
                    hookedSession.MediaPropertiesChanged -= OnSessionPropertiesChanged;
                    hookedSession.PlaybackInfoChanged -= OnSessionPlaybackChanged;
                }
                catch { }
                hookedSession = null;
            }
            if (manager is not null)
            {
                try { manager.CurrentSessionChanged -= OnCurrentSessionChanged; } catch { }
                manager = null;
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<MediaSnapshot?> SnapshotWindowsAsync()
    {
        var session = manager?.GetCurrentSession(); if (session is null) { artworkKey = null; artwork = null; return null; }
        var info = await session.TryGetMediaPropertiesAsync(); var playback = session.GetPlaybackInfo(); var timeline = session.GetTimelineProperties();
        string key = session.SourceAppUserModelId + "|" + info.Title + "|" + info.Artist + "|" + info.AlbumTitle;
        if (key != artworkKey)
        {
            artworkKey = key; artwork = null;
            if (info.Thumbnail != null)
            {
                using var stream = await info.Thumbnail.OpenReadAsync();
                // Decode on Windows and send a bounded thumbnail to the A8 device.
                if (stream.Size <= 2 * 1024 * 1024)
                {
                    var decoder = await BitmapDecoder.CreateAsync(stream);
                    if (decoder.PixelWidth is > 0 and <= 8192 && decoder.PixelHeight is > 0 and <= 8192)
                    {
                        double scale = Math.Min(1d, 128d / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
                        uint width = Math.Max(1u, (uint)(decoder.PixelWidth * scale)), height = Math.Max(1u, (uint)(decoder.PixelHeight * scale));
                        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform { ScaledWidth = width, ScaledHeight = height }, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
                        using var thumbnail = new InMemoryRandomAccessStream(); var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, thumbnail);
                        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels.DetachPixelData()); await encoder.FlushAsync();
                        if (thumbnail.Size <= 24000) { using var reader = new DataReader(thumbnail.GetInputStreamAt(0)); await reader.LoadAsync((uint)thumbnail.Size); byte[] bytes = new byte[(int)thumbnail.Size]; reader.ReadBytes(bytes); artwork = Convert.ToBase64String(bytes); }
                    }
                }
            }
        }
        var c = playback.Controls;
        return new(session.SourceAppUserModelId, info.Title ?? "", info.Artist ?? "", info.AlbumTitle ?? "", playback.PlaybackStatus.ToString(), Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds), Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds), c.IsPlaybackPositionEnabled, artwork, c.IsPlayEnabled, c.IsPauseEnabled, c.IsNextEnabled, c.IsPreviousEnabled);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<bool> CommandWindowsAsync(string command, double seconds, string? source)
    {
        var s = source is null ? manager?.GetCurrentSession() : manager?.GetSessions().FirstOrDefault(x => x.SourceAppUserModelId == source);
        if (s is null) return false;
        var c = s.GetPlaybackInfo().Controls;
        return command switch
        {
            "toggle" => await s.TryTogglePlayPauseAsync(),
            "play" when c.IsPlayEnabled => await s.TryPlayAsync(),
            "pause" when c.IsPauseEnabled => await s.TryPauseAsync(),
            "next" when c.IsNextEnabled => await s.TrySkipNextAsync(),
            "previous" when c.IsPreviousEnabled => await s.TrySkipPreviousAsync(),
            "seek" when c.IsPlaybackPositionEnabled && double.IsFinite(seconds) && seconds >= 0 => await s.TryChangePlaybackPositionAsync((s.GetTimelineProperties().StartTime + TimeSpan.FromSeconds(Math.Min(seconds, Math.Max(0, (s.GetTimelineProperties().EndTime - s.GetTimelineProperties().StartTime).TotalSeconds)))).Ticks),
            _ => false
        };
    }
}
