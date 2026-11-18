#import <UIKit/UIKit.h>
#import <AVFoundation/AVFoundation.h>

NS_ASSUME_NONNULL_BEGIN

typedef NS_ENUM(NSInteger, BTVideoPresentationMode) {
    BTVideoPresentationCompact = 0,
    BTVideoPresentationFocus = 1,
    BTVideoPresentationAmbient = 2
};

@interface BTVideoSurfaceView : UIView
@property(nonatomic) BTVideoPresentationMode presentationMode;
@property(nonatomic, copy, nullable) void (^onPlayPauseTapped)(void);
@property(nonatomic, copy, nullable) void (^onSeekCommitted)(double seconds);
@property(nonatomic, copy, nullable) void (^onReturnToWindowsTapped)(void);
@property(nonatomic, copy, nullable) void (^onExpandFocusTapped)(void);
@property(nonatomic, copy, nullable) void (^onAmbientToggleTapped)(void);
@property(nonatomic, copy, nullable) void (^onCloseFocusTapped)(void);

- (void)attachPlayerLayer:(AVPlayerLayer *)playerLayer;
- (void)updateWithSnapshot:(NSDictionary *)snapshot usbConnected:(BOOL)usbConnected;
- (void)showOverlayTemporarily;
@end

NS_ASSUME_NONNULL_END
