#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTAudioSessionCoordinator : NSObject
@property(nonatomic, readonly) BOOL pcmReceiverActive;
@property(nonatomic, readonly) BOOL independentMediaActive;
@property(nonatomic, copy, readonly) NSString *focusPolicy;
@property(nonatomic, readonly) BOOL muteIndependentAudio;
@property(nonatomic, readonly) BOOL duckIndependentAudio;
@property(nonatomic, readonly) float independentVolume;

+ (instancetype)sharedCoordinator;

- (BOOL)activateForPcmReceiver:(NSError * _Nullable * _Nullable)error;
- (void)deactivatePcmReceiver;

- (BOOL)activateForIndependentMedia:(NSError * _Nullable * _Nullable)error;
- (void)deactivateIndependentMedia;

- (BOOL)reactivateIfNeeded:(NSError * _Nullable * _Nullable)error;

- (void)updateFocusPolicy:(NSString *)policy
          muteIndependent:(BOOL)mute
        independentVolume:(float)volume
                     duck:(BOOL)duck;

- (NSDictionary *)statusSnapshot;
@end

NS_ASSUME_NONNULL_END
