#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>

@class BTAgent;
@class BTBufferedMediaEngine;

NS_ASSUME_NONNULL_BEGIN

@interface BTDirectMediaPlayer : NSObject
@property(nonatomic, readonly) AVPlayer *player;
@property(nonatomic, readonly) AVPlayerLayer *playerLayer;
@property(nonatomic, readonly) BTBufferedMediaEngine *bufferedEngine;
@property(nonatomic, readonly) BOOL independentActive;
@property(nonatomic, readonly) BOOL isPlaying;
@property(nonatomic, readonly) BOOL hasVideoTrack;
@property(nonatomic, copy, readonly, nullable) NSString *activeSessionId;
@property(nonatomic, readonly) uint64_t activeGeneration;
@property(nonatomic, copy, readonly) NSString *activeMode;
@property(nonatomic, copy, readonly) NSString *activeStage;
@property(nonatomic, copy, readonly) NSString *activeTitle;
@property(nonatomic, copy, readonly) NSString *activeArtist;
@property(nonatomic, copy, readonly, nullable) NSString *activeUrl;
@property(nonatomic, copy, readonly) NSString *qualityCap;
@property(nonatomic) BOOL keepScreenAwake;

@property(nonatomic, copy, nullable) void (^stateUpdated)(NSDictionary *snapshot);

- (instancetype)initWithAgent:(nullable BTAgent *)agent;

- (void)startHandoffWithPayload:(NSDictionary *)payload;
- (void)prepareBufferedStream:(NSDictionary *)payload;
- (void)cancelHandoffWithReason:(nullable NSString *)reason;
- (double)stopAndReturnToWindows;

- (void)play;
- (void)pause;
- (void)togglePlayPause;
- (void)seekToSeconds:(double)seconds;
- (void)applyQualityCap:(NSString *)qualityCap;
- (void)applyAudioFocusMute:(BOOL)mute volume:(float)volume;
- (void)applyRemoteCommand:(NSDictionary *)command;

- (void)setUsbConnected:(BOOL)connected;
- (void)handleMemoryPressure;
- (NSDictionary *)syncSnapshot;
@end

NS_ASSUME_NONNULL_END
