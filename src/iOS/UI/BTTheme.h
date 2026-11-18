#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTTheme : NSObject
+ (UIColor *)canvasColor;
+ (UIColor *)surfaceColor;
+ (UIColor *)elevatedColor;
+ (UIColor *)pillColor;
+ (UIColor *)hairlineColor;
+ (UIColor *)accentTeal;
+ (UIColor *)accentBlue;
+ (UIColor *)accentAmber;
+ (UIColor *)accentGreen;
+ (UIColor *)accentRed;
+ (UIColor *)textPrimary;
+ (UIColor *)textSecondary;
+ (UIColor *)textMuted;

+ (UILabel *)labelWithSize:(CGFloat)size weight:(UIFontWeight)weight color:(UIColor *)color;
+ (UIButton *)pillButtonWithTitle:(NSString *)title target:(id)target action:(SEL)action;
+ (UIButton *)primaryButtonWithTitle:(NSString *)title target:(id)target action:(SEL)action;
+ (UIView *)cardView;
+ (void)styleChipLabel:(UILabel *)label accent:(UIColor *)accent;
+ (NSString *)formatDuration:(double)seconds;
+ (NSString *)displayModeName:(NSString *)mode;
+ (NSString *)displayOwnerName:(NSString *)owner stage:(NSString *)stage;
+ (BOOL)isReduceMotionEnabled;
+ (void)pulseView:(UIView *)view accent:(UIColor *)accent;
+ (NSDictionary *)deviceTelemetrySnapshot;
@end

@interface BTEqualizerView : UIView
@property(nonatomic, getter=isPlaying) BOOL playing;
@property(nonatomic, strong) UIColor *barColor;
- (void)setPlaying:(BOOL)playing animated:(BOOL)animated;
@end

NS_ASSUME_NONNULL_END

