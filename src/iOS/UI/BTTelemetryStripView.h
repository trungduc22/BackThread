#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTTelemetryStripView : UIView
- (void)updateWithHostTelemetry:(nullable NSDictionary *)hostTelemetry
                   usbConnected:(BOOL)usbConnected
                audioOutputMode:(NSString *)audioOutputMode;
@end

NS_ASSUME_NONNULL_END
