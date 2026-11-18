#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTNowPlayingView : UIView
@property(nonatomic, readonly) UIView *videoSlotView;
@property(nonatomic, copy, nullable) void (^onPlayPauseTapped)(void);
@property(nonatomic, copy, nullable) void (^onPreviousTapped)(void);
@property(nonatomic, copy, nullable) void (^onNextTapped)(void);
@property(nonatomic, copy, nullable) void (^onSeekCommitted)(double seconds);
@property(nonatomic, copy, nullable) void (^onReturnToWindowsTapped)(void);
@property(nonatomic, copy, nullable) void (^onOpenVideoFocusTapped)(void);
@property(nonatomic, copy, nullable) void (^onFocusPolicySelected)(NSString *policy);
@property(nonatomic, copy, nullable) void (^onQualityCapSelected)(NSString *qualityCap);
@property(nonatomic, copy, nullable) void (^onKeepAwakeToggled)(BOOL keepAwake);

- (void)updateWithHostMediaState:(NSDictionary *)hostState
                   playerSnapshot:(NSDictionary *)playerSnapshot
                     embedActive:(BOOL)embedActive
                     focusPolicy:(NSString *)focusPolicy;
- (void)clearArtworkCache;
@end

NS_ASSUME_NONNULL_END
