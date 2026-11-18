#import "BTAudioSessionCoordinator.h"

@implementation BTAudioSessionCoordinator {
    NSRecursiveLock *_lock;
    BOOL _pcmActive;
    BOOL _independentActive;
    NSString *_focusPolicy;
    BOOL _muteIndependent;
    BOOL _duckIndependent;
    float _independentVolume;
}

+ (instancetype)sharedCoordinator {
    static BTAudioSessionCoordinator *instance = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        instance = [[BTAudioSessionCoordinator alloc] init];
    });
    return instance;
}

- (instancetype)init {
    if ((self = [super init])) {
        _lock = [NSRecursiveLock new];
        _focusPolicy = @"Auto";
        _independentVolume = 1.0f;
    }
    return self;
}

- (BOOL)pcmReceiverActive {
    [_lock lock];
    BOOL v = _pcmActive;
    [_lock unlock];
    return v;
}

- (BOOL)independentMediaActive {
    [_lock lock];
    BOOL v = _independentActive;
    [_lock unlock];
    return v;
}

- (NSString *)focusPolicy {
    [_lock lock];
    NSString *v = _focusPolicy;
    [_lock unlock];
    return v;
}

- (BOOL)muteIndependentAudio {
    [_lock lock];
    BOOL v = _muteIndependent;
    [_lock unlock];
    return v;
}

- (BOOL)duckIndependentAudio {
    [_lock lock];
    BOOL v = _duckIndependent;
    [_lock unlock];
    return v;
}

- (float)independentVolume {
    [_lock lock];
    float v = _independentVolume;
    [_lock unlock];
    return v;
}

- (BOOL)configureAndActivateLocked:(NSError **)error {
    AVAudioSession *session = [AVAudioSession sharedInstance];
    // Exclusive AVAudioSessionCategoryPlayback ensures lock-screen & background audio reliability on iOS 12;
    // Live PCM AudioQueue + in-process AVPlayer share the process graph naturally without dropping category ownership.
    if (![session setCategory:AVAudioSessionCategoryPlayback error:error]) {
        return NO;
    }
    if (_pcmActive) {
        [session setPreferredSampleRate:48000.0 error:NULL];
        [session setPreferredIOBufferDuration:0.01 error:NULL];
    }
    if (![session setActive:YES error:error]) {
        return NO;
    }
    return YES;
}

- (BOOL)activateForPcmReceiver:(NSError **)error {
    [_lock lock];
    _pcmActive = YES;
    BOOL ok = [self configureAndActivateLocked:error];
    if (!ok) {
        _pcmActive = NO;
    }
    [_lock unlock];
    return ok;
}

- (void)deactivatePcmReceiver {
    [_lock lock];
    _pcmActive = NO;
    // CRITICAL: Stopping PCM receiver (for example on USB disconnect) must NEVER tear down AVAudioSession
    // while independent Wi-Fi/Buffered AVPlayer media is active!
    if (!_independentActive) {
        [[AVAudioSession sharedInstance] setActive:NO
                                       withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation
                                             error:NULL];
    }
    [_lock unlock];
}

- (BOOL)activateForIndependentMedia:(NSError **)error {
    [_lock lock];
    _independentActive = YES;
    BOOL ok = [self configureAndActivateLocked:error];
    if (!ok) {
        _independentActive = NO;
    }
    [_lock unlock];
    return ok;
}

- (void)deactivateIndependentMedia {
    [_lock lock];
    _independentActive = NO;
    if (!_pcmActive) {
        [[AVAudioSession sharedInstance] setActive:NO
                                       withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation
                                             error:NULL];
    }
    [_lock unlock];
}

- (BOOL)reactivateIfNeeded:(NSError **)error {
    [_lock lock];
    BOOL needed = _pcmActive || _independentActive;
    BOOL ok = needed ? [self configureAndActivateLocked:error] : YES;
    [_lock unlock];
    return ok;
}

- (void)updateFocusPolicy:(NSString *)policy
          muteIndependent:(BOOL)mute
        independentVolume:(float)volume
                     duck:(BOOL)duck {
    [_lock lock];
    _focusPolicy = policy.length ? [policy copy] : @"Auto";
    _muteIndependent = mute;
    _duckIndependent = duck;
    _independentVolume = fmaxf(0.0f, fminf(1.0f, isfinite(volume) ? volume : 1.0f));
    [_lock unlock];
}

- (NSDictionary *)statusSnapshot {
    [_lock lock];
    AVAudioSession *session = [AVAudioSession sharedInstance];
    NSDictionary *snap = @{
        @"pcmActive": @(_pcmActive),
        @"independentActive": @(_independentActive),
        @"focusPolicy": _focusPolicy ?: @"Auto",
        @"muteIndependent": @(_muteIndependent),
        @"duckIndependent": @(_duckIndependent),
        @"independentVolume": @(_independentVolume),
        @"sampleRate": @(session.sampleRate),
        @"category": session.category ?: @""
    };
    [_lock unlock];
    return snap;
}

@end
