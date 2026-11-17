using BackThread.Audio;
using BackThread.Media;
using NAudio.Wave;
using Windows.Media;
using Windows.Media.Core;
using Windows.Storage;

namespace BackThread.Host;

public static class MediaDiagnostics
{
    public static async Task<object> ProbeAsync(CancellationToken ct)
    {
        string path = Path.Combine(Path.GetTempPath(), "BackThread-media-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            using (var writer = new WaveFileWriter(path, new WaveFormat(48000, 16, 2))) { byte[] pcm = PcmRing.Sine(0, 48000 * 5); writer.Write(pcm, 0, pcm.Length); }
            using var player = new Windows.Media.Playback.MediaPlayer { Volume = 0, IsLoopingEnabled = true };
            player.Source = MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(path));
            player.CommandManager.IsEnabled = false;
            var controls = player.SystemMediaTransportControls; controls.IsEnabled = true; controls.IsPlayEnabled = true; controls.IsPauseEnabled = true; controls.DisplayUpdater.Type = MediaPlaybackType.Music;
            int playEvents = 0, pauseEvents = 0, seekEvents = 0;
            controls.ButtonPressed += (_, e) => { if (e.Button == SystemMediaTransportControlsButton.Play) { Interlocked.Increment(ref playEvents); player.Play(); controls.PlaybackStatus = MediaPlaybackStatus.Playing; } if (e.Button == SystemMediaTransportControlsButton.Pause) { Interlocked.Increment(ref pauseEvents); player.Pause(); controls.PlaybackStatus = MediaPlaybackStatus.Paused; } };
            controls.PlaybackPositionChangeRequested += (_, e) => { Interlocked.Increment(ref seekEvents); player.PlaybackSession.Position = e.RequestedPlaybackPosition; };
            controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties { StartTime = TimeSpan.Zero, MinSeekTime = TimeSpan.Zero, Position = TimeSpan.Zero, MaxSeekTime = TimeSpan.FromSeconds(5), EndTime = TimeSpan.FromSeconds(5) });
            controls.DisplayUpdater.MusicProperties.Title = "BackThread diagnostic fixture"; controls.DisplayUpdater.MusicProperties.Artist = "BackThread"; controls.DisplayUpdater.Update();
            player.Play(); controls.PlaybackStatus = MediaPlaybackStatus.Playing; var media = new MediaSessions(); await media.InitializeAsync(); MediaSnapshot? found = null;
            for (int i = 0; i < 40; i++) { ct.ThrowIfCancellationRequested(); await Task.Delay(100, ct); var s = await media.SnapshotAsync(); if (s?.Title == "BackThread diagnostic fixture") { found = s; break; } }
            if (found is null) throw new IOException("The silent fixture did not expose a discoverable SMTC session");
            bool pause = await media.CommandAsync("pause", source: found.Source); await Task.Delay(200, ct); bool play = await media.CommandAsync("play", source: found.Source); await Task.Delay(200, ct);
            bool seek = found.CanSeek && await media.CommandAsync("seek", 1, found.Source);
            player.Pause(); player.Source = null;
            if (!pause || !play || playEvents == 0 || pauseEvents == 0) throw new IOException($"SMTC command verification failed: play={play}, pause={pause}, playEvents={playEvents}, pauseEvents={pauseEvents}");
            return new { realWindowsSession = true, physicalIPhoneTest = false, metadataVerified = true, play, pause, playEvents, pauseEvents, seekSupported = found.CanSeek, seek, seekEvents, source = found.Source };
        }
        finally { File.Delete(path); }
    }
}
