using BackThread.Transport;

namespace BackThread.Media;

public sealed record CapabilityEvaluation(
    bool AcceptableForSubmission,
    PlaybackMode SelectedMode,
    string Provider,
    MediaCapabilities Capabilities,
    string? NormalizedUrl,
    string? LocalFilePath,
    string? MimeType,
    string? Title,
    string? Artist,
    int StartSeconds,
    bool IsExperimentalEmbed = false);

public sealed record BufferedChunkResult(bool Found, long Offset, int BytesRead, bool Eof, string? Error = null);

public sealed class MediaRouter(
    DirectMediaProvider? directProvider = null,
    YouTubeProvider? youtubeProvider = null,
    IMediaUrlProber? urlProber = null,
    bool deviceHasInternet = false,
    bool allowExperimentalYouTubeEmbed = false) : IDisposable
{
    private readonly DirectMediaProvider direct = directProvider ?? new DirectMediaProvider(urlProber);
    private readonly YouTubeProvider youtube = youtubeProvider ?? new YouTubeProvider();
    private readonly object gate = new();

    private MediaSessionState state = MediaSessionState.Initial(usbConnected: false);
    private BufferedMediaSource? activeBuffered;
    private bool autoPauseWindowsOnConfirm = true;
    private bool pauseWindowsDispatched;
    private long generationCounter;
    private string? latestWindowsSource;
    private PlaybackMode? manualModeOverride;
    private AudioOutputMode audioOutputMode = AudioOutputMode.Both;

    public bool DeviceHasInternet { get; set; } = deviceHasInternet;
    public bool AllowExperimentalYouTubeEmbed { get; set; } = allowExperimentalYouTubeEmbed;

    public Func<string?, Task>? PauseWindowsMediaAsync { get; set; }
    public Func<string?, double?, Task>? ResumeWindowsMediaAsync { get; set; }
    public Func<string, string?, string?, Task>? WindowsCommandAsync { get; set; }
    public event Action<MediaSessionState>? StateChanged;

    public AudioOutputMode AudioOutputMode
    {
        get { lock (gate) return audioOutputMode; }
    }

    public MediaSessionState SetAudioOutputMode(AudioOutputMode outputMode)
    {
        MediaSessionState next;
        lock (gate)
        {
            audioOutputMode = outputMode;
            var focus = ComputeFocus(state.FocusPolicy, state.Owner, state.Mode, audioOutputMode);
            next = state with
            {
                ShouldStreamLivePcm = focus.StreamLivePcm,
                IndependentVolume = focus.IndependentVolume,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = next;
        }
        StateChanged?.Invoke(next);
        return next;
    }

    public MediaSessionState CurrentState
    {
        get { lock (gate) return state; }
    }

    public bool ShouldSuppressWindowsMediaPush
    {
        get
        {
            lock (gate)
                return state.Owner is PlaybackOwner.iPhoneOwned or PlaybackOwner.TransferPending;
        }
    }

    public bool ShouldStreamLivePcm
    {
        get
        {
            lock (gate)
                return state.ShouldStreamLivePcm;
        }
    }

    public void UpdateWindowsObservation(MediaSnapshot? snapshot)
    {
        MediaSessionState? changed = null;
        lock (gate)
        {
            latestWindowsSource = snapshot?.Source;
            if (state.Owner is PlaybackOwner.WindowsOwned or PlaybackOwner.TransferFailed)
            {
                if (snapshot is null)
                {
                    if (!string.IsNullOrEmpty(state.Title) || state.IsPlaying)
                    {
                        state = state with { Title = "", Artist = "", Album = "", PositionSeconds = 0, DurationSeconds = 0, IsPlaying = false, WindowsSource = null, UpdatedUtc = DateTimeOffset.UtcNow };
                        changed = state;
                    }
                }
                else if (state.Provider is "windows-smtc" or "spotify" or "youtube" || state.Owner == PlaybackOwner.WindowsOwned)
                {
                    state = state with
                    {
                        WindowsSource = snapshot.Source,
                        Title = string.IsNullOrEmpty(snapshot.Title) ? state.Title : snapshot.Title,
                        Artist = string.IsNullOrEmpty(snapshot.Artist) ? state.Artist : snapshot.Artist,
                        Album = snapshot.Album,
                        PositionSeconds = snapshot.PositionSeconds,
                        DurationSeconds = snapshot.DurationSeconds,
                        IsPlaying = string.Equals(snapshot.State, "Playing", StringComparison.OrdinalIgnoreCase),
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                    changed = state;
                }
            }
        }
        if (changed != null) StateChanged?.Invoke(changed);
    }

    public async Task<CapabilityEvaluation> EvaluateCapabilitiesAsync(MediaSubmissionRequest request, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            bool ok = BufferedMediaSource.CanServeFile(request.FilePath, out string mime, out bool video, out string reason);
            var caps = ok
                ? MediaCapabilities.LocalFile(mime, video)
                : new MediaCapabilities(false, false, true, false, false, false, false, false, false, false, false, false, false, PlaybackMode.LivePcm, "buffered-usb", reason, [reason]);
            return new(
                AcceptableForSubmission: ok,
                SelectedMode: ok ? (request.Mode == PlaybackMode.Auto ? PlaybackMode.BufferedUsb : request.Mode) : PlaybackMode.LivePcm,
                Provider: "buffered-usb",
                Capabilities: caps,
                NormalizedUrl: null,
                LocalFilePath: Path.GetFullPath(request.FilePath),
                MimeType: ok ? mime : null,
                Title: request.Title ?? Path.GetFileName(request.FilePath),
                Artist: request.Artist ?? "Local Windows Media",
                StartSeconds: Math.Max(0, (int)request.StartPositionSeconds));
        }

        if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri))
        {
            var caps = new MediaCapabilities(false, false, true, false, false, false, false, false, false, false, false, false, false, PlaybackMode.LivePcm, "direct", "A valid absolute HTTPS URL or full local file path is required", ["Invalid URL"]);
            return new(false, PlaybackMode.LivePcm, "direct", caps, null, null, null, request.Title, request.Artist, 0);
        }

        if (SpotifyProvider.IsSpotifyUrl(uri))
        {
            var caps = SpotifyProvider.BuildCapabilities();
            PlaybackMode mode = request.Mode is PlaybackMode.Auto or PlaybackMode.RemoteControl or PlaybackMode.LivePcm
                ? request.Mode == PlaybackMode.LivePcm ? PlaybackMode.LivePcm : PlaybackMode.RemoteControl
                : request.Mode;
            return new(true, mode, "spotify", caps, uri.AbsoluteUri, null, null, request.Title ?? "Spotify", request.Artist ?? "Spotify Connect", (int)Math.Max(0, request.StartPositionSeconds));
        }

        if (YouTubeProvider.IsYouTubeUrl(uri))
        {
            var meta = await youtube.FetchMetadataAsync(uri, ct);
            var caps = YouTubeProvider.BuildCapabilities(uri, AllowExperimentalYouTubeEmbed);
            bool embedRequested = AllowExperimentalYouTubeEmbed && meta != null && request.Mode != PlaybackMode.RemoteControl && request.Mode != PlaybackMode.LivePcm;
            return new(
                AcceptableForSubmission: true,
                SelectedMode: embedRequested ? PlaybackMode.DirectInternet : (request.Mode == PlaybackMode.LivePcm ? PlaybackMode.LivePcm : PlaybackMode.RemoteControl),
                Provider: "youtube",
                Capabilities: caps,
                NormalizedUrl: embedRequested ? meta!.EmbedUrl : uri.AbsoluteUri,
                LocalFilePath: null,
                MimeType: embedRequested ? "text/html" : null,
                Title: request.Title ?? meta?.Title ?? "YouTube",
                Artist: request.Artist ?? meta?.AuthorName ?? "YouTube",
                StartSeconds: meta?.StartSeconds ?? (int)Math.Max(0, request.StartPositionSeconds),
                IsExperimentalEmbed: embedRequested);
        }

        var directResult = await direct.EvaluateAsync(uri, DeviceHasInternet, ct);
        var effectiveCaps = directResult.Capabilities ?? new MediaCapabilities(directResult.Supported, false, true, false, false, false, false, false, false, directResult.Supported, directResult.Supported, true, false, directResult.Supported ? PlaybackMode.DirectInternet : PlaybackMode.LivePcm, directResult.Provider, directResult.Reason, []);
        PlaybackMode selected = request.Mode switch
        {
            PlaybackMode.Auto => directResult.Supported ? PlaybackMode.DirectInternet : PlaybackMode.LivePcm,
            _ => request.Mode
        };
        return new(
            AcceptableForSubmission: directResult.Supported || selected is PlaybackMode.LivePcm or PlaybackMode.RemoteControl,
            SelectedMode: selected,
            Provider: directResult.Provider,
            Capabilities: effectiveCaps,
            NormalizedUrl: directResult.Url?.AbsoluteUri ?? uri.AbsoluteUri,
            LocalFilePath: null,
            MimeType: directResult.MimeType,
            Title: request.Title ?? (Path.GetFileName(uri.AbsolutePath) is { Length: > 0 } name ? name : uri.IdnHost),
            Artist: request.Artist ?? uri.IdnHost,
            StartSeconds: (int)Math.Max(0, request.StartPositionSeconds));
    }

    public async Task<MediaSessionState> SubmitAsync(MediaSubmissionRequest request, Connection? connection, CancellationToken ct)
    {
        var eval = await EvaluateCapabilitiesAsync(request, ct);
        if (!eval.AcceptableForSubmission && request.Mode == PlaybackMode.DirectInternet)
        {
            return SetFailedState(eval, eval.Capabilities.Reason);
        }
        if (eval.SelectedMode == PlaybackMode.BufferedUsb && !BufferedMediaSource.CanServeFile(eval.LocalFilePath, out _, out _, out string badFileReason))
        {
            return SetFailedState(eval, badFileReason);
        }
        if (eval.SelectedMode == PlaybackMode.DirectInternet && !eval.IsExperimentalEmbed && !eval.Capabilities.SupportsDirectInternet)
        {
            return SetFailedState(eval, eval.Capabilities.Reason);
        }

        // Mode C (Live PCM) or Mode D (Remote Control) stays WindowsOwned with explicit metadata/reason.
        if (eval.SelectedMode is PlaybackMode.LivePcm or PlaybackMode.RemoteControl)
        {
            MediaSessionState next;
            BufferedMediaSource? staleBuffered = null;
            lock (gate)
            {
                staleBuffered = activeBuffered; activeBuffered = null;
                manualModeOverride = eval.SelectedMode;
                long gen = Interlocked.Increment(ref generationCounter);
                var policy = ComputeFocus(request.FocusPolicy == AudioFocusPolicy.Auto ? state.FocusPolicy : request.FocusPolicy, PlaybackOwner.WindowsOwned, eval.SelectedMode, audioOutputMode);
                next = state with
                {
                    SessionId = Guid.NewGuid().ToString("N"),
                    Generation = gen,
                    Mode = eval.SelectedMode,
                    Owner = PlaybackOwner.WindowsOwned,
                    Stage = HandoffStage.Idle,
                    FocusPolicy = policy.EffectivePolicy,
                    Provider = eval.Provider,
                    Title = eval.Title ?? state.Title,
                    Artist = eval.Artist ?? state.Artist,
                    Url = eval.NormalizedUrl,
                    LocalFilePath = eval.LocalFilePath,
                    StreamId = null,
                    MimeType = eval.MimeType,
                    WindowsPausedByHandoff = false,
                    ShouldStreamLivePcm = policy.StreamLivePcm,
                    IndependentVolume = policy.IndependentVolume,
                    Error = null,
                    Capabilities = eval.Capabilities,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
                state = next;
            }
            staleBuffered?.Dispose();
            if (connection != null)
            {
                await connection.SendAsync(LogicalChannel.Media, Message.Create("media.mode.select", new
                {
                    mode = next.Mode.ToString(),
                    provider = next.Provider,
                    title = next.Title,
                    artist = next.Artist,
                    reason = eval.Capabilities.Reason,
                    generation = next.Generation
                }), ct);
            }
            StateChanged?.Invoke(next);
            return next;
        }

        // Independent handoff: Mode A (DirectInternet), Mode B (BufferedUsb), or Experimental Embed
        if (connection is null)
            return SetFailedState(eval, "iPhone USB connection is required to initiate handoff");

        string sessionId = Guid.NewGuid().ToString("N");
        string? streamId = null;
        BufferedMediaSource? newBuffered = null;
        BufferedMediaSource? previousBuffered = null;
        long generation = Interlocked.Increment(ref generationCounter);
        MediaSessionState pendingState;

        if (eval.SelectedMode == PlaybackMode.BufferedUsb)
        {
            streamId = Guid.NewGuid().ToString("N");
            newBuffered = BufferedMediaSource.Open(streamId, generation, eval.LocalFilePath!);
        }

        string quality = NormalizeQualityCap(request.QualityCap);
        bool keepAwake = request.KeepScreenAwake ?? eval.Capabilities.SupportsVideo;

        lock (gate)
        {
            previousBuffered = activeBuffered;
            activeBuffered = newBuffered;
            manualModeOverride = eval.SelectedMode;
            autoPauseWindowsOnConfirm = request.AutoPauseWindowsOnConfirm;
            pauseWindowsDispatched = false;
            var effectiveFocus = request.FocusPolicy == AudioFocusPolicy.Auto ? state.FocusPolicy : request.FocusPolicy;
            var policy = ComputeFocus(effectiveFocus, PlaybackOwner.TransferPending, eval.SelectedMode);

            pendingState = state with
            {
                SessionId = sessionId,
                Generation = generation,
                Mode = eval.SelectedMode,
                Owner = PlaybackOwner.TransferPending,
                Stage = HandoffStage.Requested,
                FocusPolicy = policy.EffectivePolicy,
                Provider = eval.Provider,
                Title = eval.Title ?? "External PiP Media",
                Artist = eval.Artist ?? "",
                Album = "",
                Url = eval.NormalizedUrl,
                LocalFilePath = eval.LocalFilePath,
                StreamId = streamId,
                MimeType = eval.MimeType,
                PositionSeconds = Math.Max(0, request.StartPositionSeconds > 0 ? request.StartPositionSeconds : eval.StartSeconds),
                DurationSeconds = 0,
                BufferedSeconds = 0,
                IsPlaying = false,
                ReadyForDisplay = false,
                VideoWidth = 0,
                VideoHeight = 0,
                QualityCap = quality,
                KeepScreenAwake = keepAwake,
                WindowsPausedByHandoff = false,
                ShouldStreamLivePcm = policy.StreamLivePcm,
                IndependentVolume = policy.IndependentVolume,
                WindowsSource = latestWindowsSource,
                Error = null,
                Capabilities = eval.Capabilities,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = pendingState;
        }
        previousBuffered?.Dispose();
        StateChanged?.Invoke(pendingState);

        try
        {
            if (newBuffered != null)
            {
                await connection.RequestAsync(LogicalChannel.Media, "media.buffered.start", new
                {
                    sessionId,
                    generation,
                    streamId = newBuffered.StreamId,
                    totalBytes = newBuffered.TotalBytes,
                    mimeType = newBuffered.MimeType,
                    supportsVideo = newBuffered.SupportsVideo,
                    title = pendingState.Title,
                    artist = pendingState.Artist,
                    seekable = true
                }, ct);
            }

            await connection.RequestAsync(LogicalChannel.Media, "media.handoff", new
            {
                sessionId,
                generation,
                mode = pendingState.Mode.ToString(),
                provider = pendingState.Provider,
                url = pendingState.Url,
                mimeType = pendingState.MimeType,
                title = pendingState.Title,
                artist = pendingState.Artist,
                seekSeconds = pendingState.PositionSeconds,
                qualityCap = pendingState.QualityCap,
                keepScreenAwake = pendingState.KeepScreenAwake,
                focusPolicy = pendingState.FocusPolicy.ToString(),
                experimentalEmbed = eval.IsExperimentalEmbed,
                bufferedTransfer = newBuffered == null ? null : new
                {
                    streamId = newBuffered.StreamId,
                    totalBytes = newBuffered.TotalBytes,
                    mimeType = newBuffered.MimeType,
                    supportsVideo = newBuffered.SupportsVideo
                }
            }, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return SetFailedForGeneration(generation, ex.Message);
        }

        return CurrentState;
    }

    public async Task<MediaSessionState> ProcessHandoffStatusAsync(HandoffStatusUpdate update, CancellationToken ct)
    {
        bool shouldPauseWindows = false;
        string? windowsSourceToPause = null;
        MediaSessionState? updated = null;
        BufferedMediaSource? disposeBuffered = null;

        lock (gate)
        {
            if (string.IsNullOrEmpty(state.SessionId) || update.SessionId != state.SessionId || update.Generation != state.Generation)
                return state; // Reject stale or superseded handoff updates cleanly.

            HandoffStage stage = ParseStage(update.Stage);
            PlaybackOwner owner = stage switch
            {
                HandoffStage.Confirmed => PlaybackOwner.iPhoneOwned,
                HandoffStage.Failed => PlaybackOwner.TransferFailed,
                HandoffStage.Cancelled => PlaybackOwner.WindowsOwned,
                HandoffStage.Returned => PlaybackOwner.WindowsOwned,
                _ => state.Owner == PlaybackOwner.iPhoneOwned && stage is HandoffStage.Playing or HandoffStage.Buffering or HandoffStage.Ready
                    ? PlaybackOwner.iPhoneOwned
                    : PlaybackOwner.TransferPending
            };

            if (stage is HandoffStage.Failed or HandoffStage.Cancelled or HandoffStage.Returned)
            {
                disposeBuffered = activeBuffered;
                activeBuffered = null;
            }

            bool pauseNow = stage == HandoffStage.Confirmed && autoPauseWindowsOnConfirm && !pauseWindowsDispatched;
            if (pauseNow)
            {
                pauseWindowsDispatched = true;
                shouldPauseWindows = true;
                windowsSourceToPause = state.WindowsSource ?? latestWindowsSource;
            }

            var focus = ComputeFocus(state.FocusPolicy, owner, state.Mode);
            updated = state with
            {
                Owner = owner,
                Stage = stage,
                PositionSeconds = double.IsFinite(update.PositionSeconds) ? Math.Max(0, update.PositionSeconds) : state.PositionSeconds,
                DurationSeconds = double.IsFinite(update.DurationSeconds) ? Math.Max(0, update.DurationSeconds) : state.DurationSeconds,
                BufferedSeconds = double.IsFinite(update.BufferedSeconds) ? Math.Max(0, update.BufferedSeconds) : state.BufferedSeconds,
                IsPlaying = update.IsPlaying,
                ReadyForDisplay = update.ReadyForDisplay || state.ReadyForDisplay,
                VideoWidth = update.VideoWidth > 0 ? update.VideoWidth : state.VideoWidth,
                VideoHeight = update.VideoHeight > 0 ? update.VideoHeight : state.VideoHeight,
                WindowsPausedByHandoff = state.WindowsPausedByHandoff || shouldPauseWindows,
                ShouldStreamLivePcm = focus.StreamLivePcm,
                IndependentVolume = focus.IndependentVolume,
                Error = stage == HandoffStage.Failed ? (update.Error ?? update.Reason ?? "Independent playback failed") : null,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = updated;
        }

        disposeBuffered?.Dispose();
        if (shouldPauseWindows && PauseWindowsMediaAsync != null)
        {
            try { await PauseWindowsMediaAsync(windowsSourceToPause); }
            catch { /* Best-effort host pause after iPhone playback confirmed. */ }
        }
        StateChanged?.Invoke(updated);
        return updated;
    }

    public MediaSessionState ProcessPlayerTelemetry(string sessionId, long generation, bool isPlaying, double positionSeconds, double durationSeconds, double bufferedSeconds, bool readyForDisplay, int videoWidth, int videoHeight)
    {
        MediaSessionState? changed = null;
        lock (gate)
        {
            if (state.SessionId != sessionId || generation != state.Generation) return state;
            if (state.Owner is not (PlaybackOwner.iPhoneOwned or PlaybackOwner.TransferPending)) return state;
            changed = state with
            {
                IsPlaying = isPlaying,
                PositionSeconds = double.IsFinite(positionSeconds) ? Math.Max(0, positionSeconds) : state.PositionSeconds,
                DurationSeconds = double.IsFinite(durationSeconds) ? Math.Max(0, durationSeconds) : state.DurationSeconds,
                BufferedSeconds = double.IsFinite(bufferedSeconds) ? Math.Max(0, bufferedSeconds) : state.BufferedSeconds,
                ReadyForDisplay = readyForDisplay || state.ReadyForDisplay,
                VideoWidth = videoWidth > 0 ? videoWidth : state.VideoWidth,
                VideoHeight = videoHeight > 0 ? videoHeight : state.VideoHeight,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = changed;
        }
        StateChanged?.Invoke(changed!);
        return changed!;
    }

    public async Task<MediaSessionState> CancelHandoffAsync(Connection? connection, string? reason, CancellationToken ct)
    {
        string sid;
        long gen;
        MediaSessionState updated;
        BufferedMediaSource? disposeBuffered;
        lock (gate)
        {
            sid = state.SessionId;
            gen = state.Generation;
            disposeBuffered = activeBuffered; activeBuffered = null;
            var focus = ComputeFocus(state.FocusPolicy, PlaybackOwner.WindowsOwned, PlaybackMode.LivePcm);
            updated = state with
            {
                Generation = Interlocked.Increment(ref generationCounter),
                Owner = PlaybackOwner.WindowsOwned,
                Stage = HandoffStage.Cancelled,
                IsPlaying = false,
                WindowsPausedByHandoff = false,
                ShouldStreamLivePcm = focus.StreamLivePcm,
                IndependentVolume = focus.IndependentVolume,
                Error = reason,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = updated;
        }
        disposeBuffered?.Dispose();
        if (connection != null && !string.IsNullOrEmpty(sid))
        {
            try { await connection.RequestAsync(LogicalChannel.Media, "media.handoff.cancel", new { sessionId = sid, generation = gen, reason }, ct); }
            catch { }
        }
        StateChanged?.Invoke(updated);
        return updated;
    }

    public async Task<MediaSessionState> ReturnToWindowsAsync(Connection? connection, bool resumeWindows, CancellationToken ct)
    {
        string sid;
        long prevGen;
        double position;
        string? windowsSource;
        MediaSessionState returning;
        BufferedMediaSource? disposeBuffered;
        lock (gate)
        {
            sid = state.SessionId;
            prevGen = state.Generation;
            position = state.PositionSeconds;
            windowsSource = state.WindowsSource ?? latestWindowsSource;
            disposeBuffered = activeBuffered; activeBuffered = null;
            returning = state with
            {
                Generation = Interlocked.Increment(ref generationCounter),
                Owner = PlaybackOwner.ReturningToWindows,
                Stage = HandoffStage.Returned,
                IsPlaying = false,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = returning;
        }
        disposeBuffered?.Dispose();
        StateChanged?.Invoke(returning);

        if (connection != null && !string.IsNullOrEmpty(sid))
        {
            try
            {
                await connection.RequestAsync(LogicalChannel.Media, "media.return_to_windows", new
                {
                    sessionId = sid,
                    generation = prevGen,
                    resumeWindows,
                    positionSeconds = position
                }, ct);
            }
            catch { }
        }

        if (resumeWindows && ResumeWindowsMediaAsync != null)
        {
            try { await ResumeWindowsMediaAsync(windowsSource, position > 0 ? position : null); }
            catch { }
        }

        MediaSessionState settled;
        lock (gate)
        {
            manualModeOverride = PlaybackMode.LivePcm;
            var focus = ComputeFocus(state.FocusPolicy == AudioFocusPolicy.IndependentOnly ? AudioFocusPolicy.Auto : state.FocusPolicy, PlaybackOwner.WindowsOwned, PlaybackMode.LivePcm);
            settled = state with
            {
                Owner = PlaybackOwner.WindowsOwned,
                Mode = PlaybackMode.LivePcm,
                FocusPolicy = focus.EffectivePolicy,
                WindowsPausedByHandoff = false,
                ShouldStreamLivePcm = focus.StreamLivePcm,
                IndependentVolume = focus.IndependentVolume,
                StreamId = null,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = settled;
        }
        StateChanged?.Invoke(settled);
        return settled;
    }

    public async Task<MediaSessionState> SetFocusPolicyAsync(AudioFocusPolicy policy, Connection? connection, CancellationToken ct)
    {
        MediaSessionState next;
        FocusOutcome outcome;
        lock (gate)
        {
            outcome = ComputeFocus(policy, state.Owner, state.Mode);
            next = state with
            {
                FocusPolicy = outcome.EffectivePolicy,
                ShouldStreamLivePcm = outcome.StreamLivePcm,
                IndependentVolume = outcome.IndependentVolume,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = next;
        }
        if (connection != null)
        {
            try
            {
                await connection.RequestAsync(LogicalChannel.Media, "media.focus.set", new
                {
                    policy = outcome.EffectivePolicy.ToString(),
                    duckVideo = outcome.EffectivePolicy == AudioFocusPolicy.DuckVideo,
                    muteVideoAudio = outcome.MuteIndependentAudio,
                    volume = outcome.IndependentVolume,
                    generation = next.Generation
                }, ct);
            }
            catch { }
        }
        StateChanged?.Invoke(next);
        return next;
    }

    public async Task<object> DispatchMediaCommandAsync(string command, double seconds, string? qualityCap, bool? keepScreenAwake, string? target, Connection? connection, CancellationToken ct)
    {
        MediaSessionState snap = CurrentState;
        bool targetPhone = string.Equals(target, "iphone", StringComparison.OrdinalIgnoreCase)
            || (!string.Equals(target, "windows", StringComparison.OrdinalIgnoreCase) && snap.Owner == PlaybackOwner.iPhoneOwned);

        if (targetPhone)
        {
            if (connection is null) throw new IOException("iPhone is not connected over USB");
            var res = await connection.RequestAsync(LogicalChannel.Media, "media.command", new
            {
                target = "iphone",
                sessionId = snap.SessionId,
                generation = snap.Generation,
                command,
                seconds,
                qualityCap,
                keepScreenAwake
            }, ct);
            if (!string.IsNullOrWhiteSpace(qualityCap) || keepScreenAwake.HasValue)
            {
                lock (gate)
                {
                    state = state with
                    {
                        QualityCap = string.IsNullOrWhiteSpace(qualityCap) ? state.QualityCap : NormalizeQualityCap(qualityCap),
                        KeepScreenAwake = keepScreenAwake ?? state.KeepScreenAwake,
                        UpdatedUtc = DateTimeOffset.UtcNow
                    };
                }
            }
            return res.Body;
        }

        if (WindowsCommandAsync is null) return new { success = false };
        await WindowsCommandAsync(command, seconds > 0 ? seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) : null, snap.WindowsSource);
        return new { success = true, owner = snap.Owner.ToString(), target = "windows" };
    }

    public async Task<BufferedChunkResult> ServeBufferedChunkAsync(string streamId, long generation, long offset, int length, Connection connection, CancellationToken ct)
    {
        BufferedMediaSource? src;
        lock (gate)
        {
            src = activeBuffered;
            if (src is null || !string.Equals(src.StreamId, streamId, StringComparison.OrdinalIgnoreCase) || src.Generation != generation)
                return new(false, offset, 0, false, "Stale or unknown buffered stream");
        }

        var (frame, read, eof) = await src.ReadChunkAsync(offset, length, ct);
        if (read > 0)
            await connection.SendFrameAsync(LogicalChannel.Files, frame, 0, ct);
        if (eof)
        {
            await connection.SendAsync(LogicalChannel.Media, Message.Create("media.buffered.eof", new
            {
                streamId = src.StreamId,
                generation = src.Generation,
                totalBytes = src.TotalBytes
            }), ct);
        }
        return new(true, offset, read, eof);
    }

    public void OnTransportDisconnected()
    {
        MediaSessionState next;
        BufferedMediaSource? disposeBuffered = null;
        lock (gate)
        {
            disposeBuffered = activeBuffered;
            activeBuffered = null;
            // Independent DirectInternet playback continues over Wi-Fi even if USB drops!
            bool pendingLost = state.Owner == PlaybackOwner.TransferPending;
            next = state with
            {
                UsbConnected = false,
                Owner = pendingLost ? PlaybackOwner.TransferFailed : state.Owner,
                Stage = pendingLost ? HandoffStage.Failed : state.Stage,
                Error = pendingLost ? "USB disconnected before independent playback confirmed" : state.Error,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = next;
        }
        disposeBuffered?.Dispose();
        StateChanged?.Invoke(next);
    }

    public object BuildHostSyncPayload()
    {
        lock (gate)
        {
            return new
            {
                sessionId = state.SessionId,
                generation = state.Generation,
                owner = state.Owner.ToString(),
                mode = state.Mode.ToString(),
                focusPolicy = state.FocusPolicy.ToString(),
                audioOutputMode = audioOutputMode.ToString(),
                url = state.Url,
                title = state.Title
            };
        }
    }

    public MediaSessionState ApplyAgentSync(AgentMediaSync sync)
    {
        MediaSessionState next;
        lock (gate)
        {
            if (sync.IndependentActive && !string.IsNullOrEmpty(sync.SessionId) && sync.Generation > 0)
            {
                long resolvedGen = Math.Max(Interlocked.Read(ref generationCounter), sync.Generation);
                Interlocked.Exchange(ref generationCounter, resolvedGen);
                PlaybackMode mode = Enum.TryParse<PlaybackMode>(sync.Mode, true, out var parsed) ? parsed : PlaybackMode.DirectInternet;
                var focus = ComputeFocus(state.FocusPolicy, PlaybackOwner.iPhoneOwned, mode);
                next = state with
                {
                    UsbConnected = true,
                    SessionId = sync.SessionId,
                    Generation = resolvedGen,
                    Mode = mode,
                    Owner = PlaybackOwner.iPhoneOwned,
                    Stage = HandoffStage.Confirmed,
                    IsPlaying = sync.IsPlaying,
                    PositionSeconds = Math.Max(0, sync.PositionSeconds),
                    DurationSeconds = Math.Max(0, sync.DurationSeconds),
                    BufferedSeconds = Math.Max(0, sync.BufferedSeconds),
                    Url = sync.Url ?? state.Url,
                    Title = string.IsNullOrEmpty(sync.Title) ? state.Title : sync.Title,
                    ShouldStreamLivePcm = focus.StreamLivePcm,
                    IndependentVolume = focus.IndependentVolume,
                    Error = null,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }
            else
            {
                PlaybackOwner resolvedOwner = state.Owner == PlaybackOwner.iPhoneOwned ? PlaybackOwner.WindowsOwned : state.Owner;
                var focus = ComputeFocus(state.FocusPolicy, resolvedOwner, state.Mode);
                next = state with
                {
                    UsbConnected = true,
                    Owner = resolvedOwner,
                    Mode = resolvedOwner == PlaybackOwner.WindowsOwned ? PlaybackMode.LivePcm : state.Mode,
                    ShouldStreamLivePcm = focus.StreamLivePcm,
                    IndependentVolume = focus.IndependentVolume,
                    UpdatedUtc = DateTimeOffset.UtcNow
                };
            }
            state = next;
        }
        StateChanged?.Invoke(next);
        return next;
    }

    public PlaybackMode? ManualModeOverride => manualModeOverride;

    private MediaSessionState SetFailedState(CapabilityEvaluation eval, string reason)
    {
        MediaSessionState failed;
        lock (gate)
        {
            failed = state with
            {
                Generation = Interlocked.Increment(ref generationCounter),
                Owner = PlaybackOwner.TransferFailed,
                Stage = HandoffStage.Failed,
                Provider = eval.Provider,
                Title = eval.Title ?? state.Title,
                Artist = eval.Artist ?? state.Artist,
                Url = eval.NormalizedUrl,
                LocalFilePath = eval.LocalFilePath,
                MimeType = eval.MimeType,
                WindowsPausedByHandoff = false,
                Capabilities = eval.Capabilities,
                Error = reason,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = failed;
        }
        StateChanged?.Invoke(failed);
        return failed;
    }

    private MediaSessionState SetFailedForGeneration(long generation, string reason)
    {
        MediaSessionState failed;
        BufferedMediaSource? disposeBuffered = null;
        lock (gate)
        {
            if (state.Generation != generation) return state;
            disposeBuffered = activeBuffered; activeBuffered = null;
            failed = state with
            {
                Owner = PlaybackOwner.TransferFailed,
                Stage = HandoffStage.Failed,
                WindowsPausedByHandoff = false,
                Error = reason,
                UpdatedUtc = DateTimeOffset.UtcNow
            };
            state = failed;
        }
        disposeBuffered?.Dispose();
        StateChanged?.Invoke(failed);
        return failed;
    }

    private readonly record struct FocusOutcome(AudioFocusPolicy EffectivePolicy, bool StreamLivePcm, float IndependentVolume, bool MuteIndependentAudio);

    private FocusOutcome ComputeFocus(AudioFocusPolicy requested, PlaybackOwner owner, PlaybackMode mode, AudioOutputMode? outputModeOverride = null)
    {
        bool independentPlayback = owner is PlaybackOwner.iPhoneOwned && mode is PlaybackMode.DirectInternet or PlaybackMode.BufferedUsb;
        bool allowLivePcm = (outputModeOverride ?? audioOutputMode) != AudioOutputMode.PcOnly;
        return requested switch
        {
            AudioFocusPolicy.IndependentOnly => new(requested, false, 1f, false),
            AudioFocusPolicy.LivePcmOnly => new(requested, allowLivePcm, 0f, true),
            AudioFocusPolicy.KeepVideoMuteAudio => new(requested, allowLivePcm, 0f, true),
            AudioFocusPolicy.DuckVideo => new(requested, allowLivePcm, 0.25f, false),
            AudioFocusPolicy.LivePcmPrioritized => new(requested, allowLivePcm, independentPlayback ? 0.25f : 1f, false),
            AudioFocusPolicy.IndependentPrioritized => new(requested, allowLivePcm && !independentPlayback, 1f, false),
            _ => new(AudioFocusPolicy.Auto, allowLivePcm && !independentPlayback, 1f, false)
        };
    }

    private static HandoffStage ParseStage(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "requested" => HandoffStage.Requested,
        "validating" => HandoffStage.Validating,
        "connecting" => HandoffStage.Connecting,
        "buffering" => HandoffStage.Buffering,
        "ready" => HandoffStage.Ready,
        "playing" => HandoffStage.Playing,
        "confirmed" => HandoffStage.Confirmed,
        "failed" => HandoffStage.Failed,
        "cancelled" or "canceled" => HandoffStage.Cancelled,
        "returned" or "stopped" => HandoffStage.Returned,
        _ => HandoffStage.Validating
    };

    private static string NormalizeQualityCap(string? raw) => raw?.Trim().ToLowerInvariant() switch
    {
        "480p" => "480p",
        "720p" => "720p",
        "audio-only" => "audio-only",
        _ => "auto"
    };

    public void Dispose()
    {
        BufferedMediaSource? buf;
        lock (gate) { buf = activeBuffered; activeBuffered = null; }
        buf?.Dispose();
    }
}
