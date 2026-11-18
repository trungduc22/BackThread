#import "BTDirectMediaPlayer.h"
#import "BTBufferedMediaEngine.h"
#import "../Agent/BTAgent.h"
#import "../AudioReceiver/BTAudioSessionCoordinator.h"
#import <UIKit/UIKit.h>
#import <MediaPlayer/MediaPlayer.h>

static void *BTPlayerItemContext = &BTPlayerItemContext;
static void *BTPlayerLayerContext = &BTPlayerLayerContext;
static void *BTPlayerRateContext = &BTPlayerRateContext;

static NSString *BTPlayerStr(id value, NSString *fallback) {
    return [value isKindOfClass:NSString.class] ? value : fallback;
}

@implementation BTDirectMediaPlayer {
    __weak BTAgent *_agent;
    AVPlayer *_player;
    AVPlayerLayer *_playerLayer;
    AVPlayerItem *_observedItem;
    BTBufferedMediaEngine *_bufferedEngine;
    id _timeObserver;
    NSTimer *_startupTimer;
    NSLock *_snapshotLock;
    NSDictionary *_threadSafeSnapshot;
    BOOL _observingLayer;
    BOOL _observingRate;
    BOOL _independentActive;
    BOOL _confirmed;
    BOOL _expectsVideo;
    BOOL _usbConnected;
    BOOL _commandsRegistered;
    double _pendingSeekSeconds;
    BOOL _seekApplied;
    double _baselinePosSeconds;
    NSInteger _progressTicks;
    NSDate *_lastTelemetrySent;
    double _positionSeconds;
    double _durationSeconds;
    double _bufferedSeconds;
    int _videoWidth;
    int _videoHeight;
    NSString *_lastError;
}

- (instancetype)initWithAgent:(BTAgent *)agent {
    if ((self = [super init])) {
        _agent = agent;
        _snapshotLock = [NSLock new];
        _player = [AVPlayer new];
        _player.actionAtItemEnd = AVPlayerActionAtItemEndPause;
        _playerLayer = [AVPlayerLayer playerLayerWithPlayer:_player];
        _playerLayer.videoGravity = AVLayerVideoGravityResizeAspect;
        _bufferedEngine = [BTBufferedMediaEngine new];
        _bufferedEngine.agent = agent;
        _activeMode = @"LivePcm";
        _activeStage = @"idle";
        _activeTitle = @"";
        _activeArtist = @"";
        _qualityCap = @"auto";
        _keepScreenAwake = YES;
        _threadSafeSnapshot = @{@"independentActive": @NO};

        [_playerLayer addObserver:self
                       forKeyPath:@"readyForDisplay"
                          options:NSKeyValueObservingOptionNew
                          context:BTPlayerLayerContext];
        _observingLayer = YES;

        [_player addObserver:self
                  forKeyPath:@"rate"
                     options:NSKeyValueObservingOptionNew
                     context:BTPlayerRateContext];
        _observingRate = YES;

        __weak typeof(self) weakSelf = self;
        _timeObserver = [_player addPeriodicTimeObserverForInterval:CMTimeMakeWithSeconds(0.4, 600)
                                                              queue:dispatch_get_main_queue()
                                                         usingBlock:^(CMTime time) {
            [weakSelf handlePeriodicTime:time];
        }];

        [[NSNotificationCenter defaultCenter] addObserver:self
                                                 selector:@selector(handleAudioInterruption:)
                                                     name:AVAudioSessionInterruptionNotification
                                                   object:nil];

        [self setupRemoteCommands];
    }
    return self;
}

- (AVPlayer *)player { return _player; }
- (AVPlayerLayer *)playerLayer { return _playerLayer; }
- (BTBufferedMediaEngine *)bufferedEngine { return _bufferedEngine; }
- (BOOL)independentActive { return _independentActive; }
- (BOOL)isPlaying { return _player.rate > 0.01f; }
- (BOOL)hasVideoTrack { return _expectsVideo || _videoWidth > 0 || _playerLayer.readyForDisplay; }

- (void)setupRemoteCommands {
    if (_commandsRegistered) return;
    _commandsRegistered = YES;
    MPRemoteCommandCenter *cc = [MPRemoteCommandCenter sharedCommandCenter];
    __weak typeof(self) weakSelf = self;
    [cc.playCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) {
        (void)event;
        BTDirectMediaPlayer *strong = weakSelf;
        if (!strong || !strong->_independentActive) return MPRemoteCommandHandlerStatusCommandFailed;
        [strong play];
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    [cc.pauseCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) {
        (void)event;
        BTDirectMediaPlayer *strong = weakSelf;
        if (!strong || !strong->_independentActive) return MPRemoteCommandHandlerStatusCommandFailed;
        [strong pause];
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    [cc.togglePlayPauseCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) {
        (void)event;
        BTDirectMediaPlayer *strong = weakSelf;
        if (!strong || !strong->_independentActive) return MPRemoteCommandHandlerStatusCommandFailed;
        [strong togglePlayPause];
        return MPRemoteCommandHandlerStatusSuccess;
    }];
    [cc.changePlaybackPositionCommand addTargetWithHandler:^MPRemoteCommandHandlerStatus(MPRemoteCommandEvent *event) {
        BTDirectMediaPlayer *strong = weakSelf;
        if (!strong || !strong->_independentActive) return MPRemoteCommandHandlerStatusCommandFailed;
        MPChangePlaybackPositionCommandEvent *posEvent = (MPChangePlaybackPositionCommandEvent *)event;
        [strong seekToSeconds:posEvent.positionTime];
        return MPRemoteCommandHandlerStatusSuccess;
    }];
}

- (void)detachObservedItem {
    if (_observedItem) {
        [_observedItem removeObserver:self forKeyPath:@"status" context:BTPlayerItemContext];
        [_observedItem removeObserver:self forKeyPath:@"playbackLikelyToKeepUp" context:BTPlayerItemContext];
        [_observedItem removeObserver:self forKeyPath:@"playbackBufferEmpty" context:BTPlayerItemContext];
        [_observedItem removeObserver:self forKeyPath:@"loadedTimeRanges" context:BTPlayerItemContext];
        [[NSNotificationCenter defaultCenter] removeObserver:self name:AVPlayerItemDidPlayToEndTimeNotification object:_observedItem];
        [[NSNotificationCenter defaultCenter] removeObserver:self name:AVPlayerItemFailedToPlayToEndTimeNotification object:_observedItem];
        _observedItem = nil;
    }
}

- (void)invalidateStartupTimer {
    [_startupTimer invalidate];
    _startupTimer = nil;
}

- (double)peakBitRateForQualityCap:(NSString *)cap {
    NSString *lower = cap.lowercaseString;
    if ([lower isEqual:@"480p"]) return 1500000.0;
    if ([lower isEqual:@"720p"]) return 3500000.0;
    if ([lower isEqual:@"audio-only"]) return 192000.0;
    return 0.0;
}

- (void)updateIdleTimerAndNowPlaying {
    BOOL shouldStayAwake = _independentActive && _keepScreenAwake && self.hasVideoTrack && self.isPlaying;
    [UIApplication sharedApplication].idleTimerDisabled = shouldStayAwake;

    if (_independentActive) {
        NSMutableDictionary *info = [NSMutableDictionary new];
        info[MPMediaItemPropertyTitle] = _activeTitle.length ? _activeTitle : @"BackThread Media";
        if (_activeArtist.length) info[MPMediaItemPropertyArtist] = _activeArtist;
        if (_durationSeconds > 0 && isfinite(_durationSeconds)) {
            info[MPMediaItemPropertyPlaybackDuration] = @(_durationSeconds);
        }
        info[MPNowPlayingInfoPropertyElapsedPlaybackTime] = @(_positionSeconds);
        info[MPNowPlayingInfoPropertyPlaybackRate] = @(self.isPlaying ? 1.0 : 0.0);
        [MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo = info;
    } else {
        [MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo = nil;
    }
}

- (NSDictionary *)buildCurrentSnapshot {
    return @{
        @"sessionId": _activeSessionId ?: @"",
        @"generation": @(_activeGeneration),
        @"mode": _activeMode ?: @"LivePcm",
        @"stage": _activeStage ?: @"idle",
        @"title": _activeTitle ?: @"",
        @"artist": _activeArtist ?: @"",
        @"url": _activeUrl ?: @"",
        @"independentActive": @(_independentActive),
        @"isPlaying": @(self.isPlaying),
        @"positionSeconds": @(isfinite(_positionSeconds) ? _positionSeconds : 0),
        @"durationSeconds": @(isfinite(_durationSeconds) ? _durationSeconds : 0),
        @"bufferedSeconds": @(isfinite(_bufferedSeconds) ? _bufferedSeconds : 0),
        @"readyForDisplay": @(_playerLayer.readyForDisplay),
        @"videoWidth": @(_videoWidth),
        @"videoHeight": @(_videoHeight),
        @"qualityCap": _qualityCap ?: @"auto",
        @"keepScreenAwake": @(_keepScreenAwake),
        @"error": _lastError ?: @""
    };
}

- (void)refreshThreadSafeSnapshotAndNotifyUI {
    NSDictionary *snap = [self buildCurrentSnapshot];
    [_snapshotLock lock];
    _threadSafeSnapshot = snap;
    [_snapshotLock unlock];
    [self updateIdleTimerAndNowPlaying];
    void (^cb)(NSDictionary *) = self.stateUpdated;
    if (cb) cb(snap);
}

- (void)emitHandoffStage:(NSString *)stage error:(NSString * _Nullable)error {
    _activeStage = [stage copy];
    _lastError = [error copy];
    [self refreshThreadSafeSnapshotAndNotifyUI];

    if (_activeSessionId.length && _activeGeneration > 0) {
        NSMutableDictionary *msg = [@{
            @"sessionId": _activeSessionId,
            @"generation": @(_activeGeneration),
            @"stage": stage,
            @"positionSeconds": @(isfinite(_positionSeconds) ? _positionSeconds : 0),
            @"durationSeconds": @(isfinite(_durationSeconds) ? _durationSeconds : 0),
            @"bufferedSeconds": @(isfinite(_bufferedSeconds) ? _bufferedSeconds : 0),
            @"isPlaying": @(self.isPlaying),
            @"readyForDisplay": @(_playerLayer.readyForDisplay),
            @"videoWidth": @(_videoWidth),
            @"videoHeight": @(_videoHeight)
        } mutableCopy];
        if (error.length) {
            msg[@"error"] = error;
            msg[@"reason"] = error;
        }
        [_agent sendMediaMessage:@"media.handoff.status" body:msg];
    }
}

- (void)emitPeriodicTelemetryIfNeeded:(BOOL)force {
    if (!_independentActive || !_activeSessionId.length) return;
    if (!force && _lastTelemetrySent && -[_lastTelemetrySent timeIntervalSinceNow] < 0.9) return;
    _lastTelemetrySent = [NSDate date];
    [_agent sendMediaMessage:@"media.player.state" body:@{
        @"sessionId": _activeSessionId,
        @"generation": @(_activeGeneration),
        @"isPlaying": @(self.isPlaying),
        @"positionSeconds": @(isfinite(_positionSeconds) ? _positionSeconds : 0),
        @"durationSeconds": @(isfinite(_durationSeconds) ? _durationSeconds : 0),
        @"bufferedSeconds": @(isfinite(_bufferedSeconds) ? _bufferedSeconds : 0),
        @"readyForDisplay": @(_playerLayer.readyForDisplay),
        @"videoWidth": @(_videoWidth),
        @"videoHeight": @(_videoHeight)
    }];
}

- (void)prepareBufferedStream:(NSDictionary *)payload {
    NSString *streamId = BTPlayerStr(payload[@"streamId"], @"");
    if (streamId.length != 32) return;
    [_agent registerBufferedStreamId:streamId];
}

- (void)startHandoffWithPayload:(NSDictionary *)payload {
    [self invalidateStartupTimer];
    [self detachObservedItem];
    [_player pause];
    [_player replaceCurrentItemWithPlayerItem:nil];

    _activeSessionId = [BTPlayerStr(payload[@"sessionId"], @"") copy];
    _activeGeneration = [payload[@"generation"] unsignedLongLongValue];
    _activeMode = [BTPlayerStr(payload[@"mode"], @"DirectInternet") copy];
    _activeTitle = [BTPlayerStr(payload[@"title"], @"External PiP Media") copy];
    _activeArtist = [BTPlayerStr(payload[@"artist"], @"") copy];
    _activeUrl = [BTPlayerStr(payload[@"url"], @"") copy];
    _qualityCap = [BTPlayerStr(payload[@"qualityCap"], @"auto") copy];
    _keepScreenAwake = payload[@"keepScreenAwake"] ? [payload[@"keepScreenAwake"] boolValue] : YES;
    _pendingSeekSeconds = [payload[@"seekSeconds"] doubleValue];
    _seekApplied = _pendingSeekSeconds <= 0.1;
    _confirmed = NO;
    _independentActive = NO;
    _positionSeconds = MAX(0, _pendingSeekSeconds);
    _durationSeconds = 0;
    _bufferedSeconds = 0;
    _baselinePosSeconds = _positionSeconds;
    _progressTicks = 0;
    _videoWidth = 0;
    _videoHeight = 0;
    _lastError = nil;

    [self emitHandoffStage:@"validating" error:nil];

    AVAsset *asset = nil;
    if ([_activeMode isEqual:@"BufferedUsb"]) {
        NSDictionary *bufInfo = [payload[@"bufferedTransfer"] isKindOfClass:NSDictionary.class] ? payload[@"bufferedTransfer"] : @{};
        NSString *streamId = BTPlayerStr(bufInfo[@"streamId"], @"");
        uint64_t totalBytes = [bufInfo[@"totalBytes"] unsignedLongLongValue];
        NSString *mime = BTPlayerStr(bufInfo[@"mimeType"], @"video/mp4");
        _expectsVideo = bufInfo[@"supportsVideo"] ? [bufInfo[@"supportsVideo"] boolValue] : YES;

        asset = [_bufferedEngine prepareStreamWithId:streamId
                                          generation:_activeGeneration
                                          totalBytes:totalBytes
                                            mimeType:mime];
        if (!asset) {
            [self emitHandoffStage:@"failed" error:@"Invalid buffered USB media stream"];
            return;
        }
    } else {
        [_bufferedEngine closeStream];
        NSURL *url = [NSURL URLWithString:_activeUrl ?: @""];
        if (!url || ![url.scheme.lowercaseString isEqual:@"https"] || url.user || url.password) {
            [self emitHandoffStage:@"failed" error:@"Only credential-free HTTPS URLs are allowed for Mode A"];
            return;
        }
        NSString *mime = BTPlayerStr(payload[@"mimeType"], @"");
        _expectsVideo = [mime isEqual:@""] || [mime hasPrefix:@"video/"] || [mime containsString:@"mpegurl"];
        asset = [AVURLAsset URLAssetWithURL:url options:nil];
    }

    NSError *sessionError = nil;
    if (![[BTAudioSessionCoordinator sharedCoordinator] activateForIndependentMedia:&sessionError]) {
        [self emitHandoffStage:@"failed" error:sessionError.localizedDescription ?: @"Could not activate AVAudioSession"];
        return;
    }

    [self emitHandoffStage:@"connecting" error:nil];

    AVPlayerItem *item = [AVPlayerItem playerItemWithAsset:asset];
    item.preferredPeakBitRate = [self peakBitRateForQualityCap:_qualityCap];

    _observedItem = item;
    [item addObserver:self forKeyPath:@"status" options:NSKeyValueObservingOptionNew context:BTPlayerItemContext];
    [item addObserver:self forKeyPath:@"playbackLikelyToKeepUp" options:NSKeyValueObservingOptionNew context:BTPlayerItemContext];
    [item addObserver:self forKeyPath:@"playbackBufferEmpty" options:NSKeyValueObservingOptionNew context:BTPlayerItemContext];
    [item addObserver:self forKeyPath:@"loadedTimeRanges" options:NSKeyValueObservingOptionNew context:BTPlayerItemContext];
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(playerItemDidEnd:) name:AVPlayerItemDidPlayToEndTimeNotification object:item];
    [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(playerItemFailed:) name:AVPlayerItemFailedToPlayToEndTimeNotification object:item];

    BTAudioSessionCoordinator *coord = [BTAudioSessionCoordinator sharedCoordinator];
    _player.muted = coord.muteIndependentAudio;
    _player.volume = coord.independentVolume;
    [_player replaceCurrentItemWithPlayerItem:item];

    __weak typeof(self) weakSelf = self;
    uint64_t expectedGen = _activeGeneration;
    _startupTimer = [NSTimer scheduledTimerWithTimeInterval:15.0 repeats:NO block:^(NSTimer *timer) {
        (void)timer;
        BTDirectMediaPlayer *strong = weakSelf;
        if (!strong || strong->_activeGeneration != expectedGen || strong->_confirmed) return;
        [strong failCurrentSessionWithError:@"Timed out waiting for iOS playback frames to advance"];
    }];

    [self emitHandoffStage:@"buffering" error:nil];
    [_player play];
}

- (void)failCurrentSessionWithError:(NSString *)message {
    [self invalidateStartupTimer];
    [self detachObservedItem];
    [_player pause];
    [_player replaceCurrentItemWithPlayerItem:nil];
    [_bufferedEngine closeStream];
    _independentActive = NO;
    _confirmed = NO;
    [[BTAudioSessionCoordinator sharedCoordinator] deactivateIndependentMedia];
    [self emitHandoffStage:@"failed" error:message];
}

- (void)cancelHandoffWithReason:(NSString *)reason {
    [self invalidateStartupTimer];
    [self detachObservedItem];
    [_player pause];
    [_player replaceCurrentItemWithPlayerItem:nil];
    [_bufferedEngine closeStream];
    _independentActive = NO;
    _confirmed = NO;
    [[BTAudioSessionCoordinator sharedCoordinator] deactivateIndependentMedia];
    _activeStage = @"cancelled";
    _lastError = [reason copy];
    [self refreshThreadSafeSnapshotAndNotifyUI];
}

- (double)stopAndReturnToWindows {
    double pos = isfinite(_positionSeconds) ? _positionSeconds : 0;
    [self invalidateStartupTimer];
    [self detachObservedItem];
    [_player pause];
    [_player replaceCurrentItemWithPlayerItem:nil];
    [_bufferedEngine closeStream];
    _independentActive = NO;
    _confirmed = NO;
    _activeMode = @"LivePcm";
    _activeStage = @"returned";
    [[BTAudioSessionCoordinator sharedCoordinator] deactivateIndependentMedia];
    [self refreshThreadSafeSnapshotAndNotifyUI];
    return pos;
}

- (void)play {
    [[BTAudioSessionCoordinator sharedCoordinator] activateForIndependentMedia:nil];
    [_player play];
    [self refreshThreadSafeSnapshotAndNotifyUI];
    [self emitPeriodicTelemetryIfNeeded:YES];
}

- (void)pause {
    [_player pause];
    [self refreshThreadSafeSnapshotAndNotifyUI];
    [self emitPeriodicTelemetryIfNeeded:YES];
}

- (void)togglePlayPause {
    if (self.isPlaying) [self pause];
    else [self play];
}

- (void)seekToSeconds:(double)seconds {
    if (!_observedItem || !isfinite(seconds) || seconds < 0) return;
    CMTime target = CMTimeMakeWithSeconds(seconds, 600);
    __weak typeof(self) weakSelf = self;
    [_player seekToTime:target toleranceBefore:kCMTimeZero toleranceAfter:kCMTimeZero completionHandler:^(BOOL finished) {
        (void)finished;
        BTDirectMediaPlayer *strong = weakSelf;
        if (!strong) return;
        strong->_positionSeconds = seconds;
        [strong refreshThreadSafeSnapshotAndNotifyUI];
        [strong emitPeriodicTelemetryIfNeeded:YES];
    }];
}

- (void)applyQualityCap:(NSString *)qualityCap {
    _qualityCap = [qualityCap.length ? qualityCap : @"auto" copy];
    if (_observedItem) {
        _observedItem.preferredPeakBitRate = [self peakBitRateForQualityCap:_qualityCap];
    }
    [self refreshThreadSafeSnapshotAndNotifyUI];
}

- (void)applyAudioFocusMute:(BOOL)mute volume:(float)volume {
    _player.muted = mute;
    _player.volume = fmaxf(0.0f, fminf(1.0f, isfinite(volume) ? volume : 1.0f));
    [self refreshThreadSafeSnapshotAndNotifyUI];
}

- (void)applyRemoteCommand:(NSDictionary *)command {
    NSString *cmd = BTPlayerStr(command[@"command"], @"");
    NSString *qCap = BTPlayerStr(command[@"qualityCap"], @"");
    if (qCap.length) [self applyQualityCap:qCap];
    if (command[@"keepScreenAwake"]) {
        self.keepScreenAwake = [command[@"keepScreenAwake"] boolValue];
        [self updateIdleTimerAndNowPlaying];
    }
    if ([cmd isEqual:@"play"]) [self play];
    else if ([cmd isEqual:@"pause"]) [self pause];
    else if ([cmd isEqual:@"toggle"]) [self togglePlayPause];
    else if ([cmd isEqual:@"seek"]) [self seekToSeconds:[command[@"seconds"] doubleValue]];
}

- (void)setUsbConnected:(BOOL)connected {
    _usbConnected = connected;
    if (connected && _independentActive) {
        [self emitPeriodicTelemetryIfNeeded:YES];
    }
}

- (void)handleMemoryPressure {
    [_bufferedEngine handleMemoryPressure];
    [MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo = _independentActive ? [MPNowPlayingInfoCenter defaultCenter].nowPlayingInfo : nil;
}

- (NSDictionary *)syncSnapshot {
    [_snapshotLock lock];
    NSDictionary *snap = _threadSafeSnapshot ?: @{@"independentActive": @NO};
    [_snapshotLock unlock];
    return snap;
}

- (void)observeValueForKeyPath:(NSString *)keyPath
                      ofObject:(id)object
                        change:(NSDictionary<NSKeyValueChangeKey,id> *)change
                       context:(void *)context {
    (void)change;
    if (context == BTPlayerItemContext && object == _observedItem) {
        dispatch_async(dispatch_get_main_queue(), ^{
            if (object != self->_observedItem) return;
            if ([keyPath isEqual:@"status"]) {
                if (self->_observedItem.status == AVPlayerItemStatusReadyToPlay) {
                    CMTime dur = self->_observedItem.duration;
                    if (CMTIME_IS_NUMERIC(dur) && CMTIME_IS_VALID(dur)) {
                        self->_durationSeconds = MAX(0, CMTimeGetSeconds(dur));
                    }
                    CGSize nat = self->_observedItem.presentationSize;
                    if (nat.width > 0 && nat.height > 0) {
                        self->_videoWidth = (int)nat.width;
                        self->_videoHeight = (int)nat.height;
                    }
                    if (!self->_seekApplied && self->_pendingSeekSeconds > 0.1) {
                        self->_seekApplied = YES;
                        [self seekToSeconds:self->_pendingSeekSeconds];
                    }
                    if (!self->_confirmed) {
                        [self emitHandoffStage:@"ready" error:nil];
                        self->_baselinePosSeconds = CMTimeGetSeconds(self->_player.currentTime);
                        if (!isfinite(self->_baselinePosSeconds)) self->_baselinePosSeconds = 0;
                    }
                    [self->_player play];
                } else if (self->_observedItem.status == AVPlayerItemStatusFailed) {
                    [self failCurrentSessionWithError:self->_observedItem.error.localizedDescription ?: @"AVPlayer failed to load media"];
                }
            } else if ([keyPath isEqual:@"loadedTimeRanges"]) {
                NSArray<NSValue *> *ranges = self->_observedItem.loadedTimeRanges;
                if (ranges.count > 0) {
                    CMTimeRange tr = [ranges.firstObject CMTimeRangeValue];
                    double end = CMTimeGetSeconds(CMTimeRangeGetEnd(tr));
                    if (isfinite(end)) self->_bufferedSeconds = MAX(0, end);
                }
                [self refreshThreadSafeSnapshotAndNotifyUI];
            } else if ([keyPath isEqual:@"playbackBufferEmpty"]) {
                if (self->_observedItem.isPlaybackBufferEmpty && self->_confirmed) {
                    [self refreshThreadSafeSnapshotAndNotifyUI];
                }
            } else if ([keyPath isEqual:@"playbackLikelyToKeepUp"]) {
                if (self->_observedItem.isPlaybackLikelyToKeepUp && !self->_confirmed && self.isPlaying) {
                    [self emitHandoffStage:@"playing" error:nil];
                }
            }
        });
        return;
    }

    if (context == BTPlayerLayerContext && object == _playerLayer) {
        dispatch_async(dispatch_get_main_queue(), ^{
            CGSize nat = self->_observedItem.presentationSize;
            if (nat.width > 0 && nat.height > 0) {
                self->_videoWidth = (int)nat.width;
                self->_videoHeight = (int)nat.height;
            }
            [self evaluateConfirmation];
        });
        return;
    }

    if (context == BTPlayerRateContext && object == _player) {
        dispatch_async(dispatch_get_main_queue(), ^{
            [self evaluateConfirmation];
            [self refreshThreadSafeSnapshotAndNotifyUI];
        });
        return;
    }

    [super observeValueForKeyPath:keyPath ofObject:object change:change context:context];
}

- (void)handlePeriodicTime:(CMTime)time {
    if (!_observedItem) return;
    double sec = CMTIME_IS_NUMERIC(time) ? CMTimeGetSeconds(time) : 0;
    if (isfinite(sec) && sec >= 0) _positionSeconds = sec;

    CMTime dur = _observedItem.duration;
    if (CMTIME_IS_NUMERIC(dur) && CMTIME_IS_VALID(dur)) {
        double d = CMTimeGetSeconds(dur);
        if (isfinite(d) && d > 0) _durationSeconds = d;
    }
    CGSize nat = _observedItem.presentationSize;
    if (nat.width > 0 && nat.height > 0) {
        _videoWidth = (int)nat.width;
        _videoHeight = (int)nat.height;
    }

    [self evaluateConfirmation];
    [self refreshThreadSafeSnapshotAndNotifyUI];
    [self emitPeriodicTelemetryIfNeeded:NO];
}

- (void)evaluateConfirmation {
    if (!_observedItem || _observedItem.status != AVPlayerItemStatusReadyToPlay) return;
    if (_player.rate <= 0.01f || _player.error) return;

    double advanced = _positionSeconds - _baselinePosSeconds;
    if (advanced >= 0.20) {
        _progressTicks++;
    } else if (_positionSeconds > 0.1 && _confirmed) {
        _progressTicks++;
    }

    BOOL videoCondition = !_expectsVideo || _playerLayer.readyForDisplay || _videoWidth > 0;
    BOOL timeCondition = advanced >= 0.25 || _progressTicks >= 2;

    if (!_confirmed && timeCondition && videoCondition) {
        _confirmed = YES;
        _independentActive = YES;
        [self invalidateStartupTimer];
        [self emitHandoffStage:@"confirmed" error:nil];
        [self emitPeriodicTelemetryIfNeeded:YES];
    } else if (!_confirmed && [_activeStage isEqual:@"ready"]) {
        [self emitHandoffStage:@"playing" error:nil];
    }
}

- (void)playerItemDidEnd:(NSNotification *)note {
    (void)note;
    if (!_independentActive) return;
    _activeStage = @"returned";
    [self refreshThreadSafeSnapshotAndNotifyUI];
    [self emitPeriodicTelemetryIfNeeded:YES];
}

- (void)playerItemFailed:(NSNotification *)note {
    NSError *err = note.userInfo[AVPlayerItemFailedToPlayToEndTimeErrorKey];
    [self failCurrentSessionWithError:err.localizedDescription ?: @"Playback failed before completion"];
}

- (void)handleAudioInterruption:(NSNotification *)note {
    if (!_independentActive) return;
    NSUInteger type = [note.userInfo[AVAudioSessionInterruptionTypeKey] unsignedIntegerValue];
    if (type == AVAudioSessionInterruptionTypeEnded) {
        NSUInteger opt = [note.userInfo[AVAudioSessionInterruptionOptionKey] unsignedIntegerValue];
        if (opt & AVAudioSessionInterruptionOptionShouldResume) {
            [[BTAudioSessionCoordinator sharedCoordinator] reactivateIfNeeded:nil];
            [_player play];
        }
    }
}

- (void)dealloc {
    [[NSNotificationCenter defaultCenter] removeObserver:self];
    [_startupTimer invalidate];
    if (_timeObserver) [_player removeTimeObserver:_timeObserver];
    [self detachObservedItem];
    if (_observingLayer) [_playerLayer removeObserver:self forKeyPath:@"readyForDisplay" context:BTPlayerLayerContext];
    if (_observingRate) [_player removeObserver:self forKeyPath:@"rate" context:BTPlayerRateContext];
    [_player pause];
}

@end
