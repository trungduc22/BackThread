#import "BTTheme.h"

@implementation BTTheme

+ (UIColor *)canvasColor {
    return [UIColor colorWithRed:0.043 green:0.055 blue:0.078 alpha:1.0];
}

+ (UIColor *)surfaceColor {
    return [UIColor colorWithRed:0.078 green:0.102 blue:0.141 alpha:1.0];
}

+ (UIColor *)elevatedColor {
    return [UIColor colorWithRed:0.110 green:0.141 blue:0.196 alpha:1.0];
}

+ (UIColor *)pillColor {
    return [UIColor colorWithRed:0.145 green:0.184 blue:0.251 alpha:1.0];
}

+ (UIColor *)hairlineColor {
    return [UIColor colorWithRed:0.165 green:0.212 blue:0.286 alpha:1.0];
}

+ (UIColor *)accentTeal {
    return [UIColor colorWithRed:0.239 green:0.839 blue:0.776 alpha:1.0];
}

+ (UIColor *)accentBlue {
    return [UIColor colorWithRed:0.322 green:0.596 blue:1.000 alpha:1.0];
}

+ (UIColor *)accentAmber {
    return [UIColor colorWithRed:0.961 green:0.651 blue:0.137 alpha:1.0];
}

+ (UIColor *)accentGreen {
    return [UIColor colorWithRed:0.212 green:0.827 blue:0.486 alpha:1.0];
}

+ (UIColor *)accentRed {
    return [UIColor colorWithRed:0.941 green:0.353 blue:0.400 alpha:1.0];
}

+ (UIColor *)textPrimary {
    return [UIColor colorWithRed:0.957 green:0.969 blue:0.984 alpha:1.0];
}

+ (UIColor *)textSecondary {
    return [UIColor colorWithRed:0.604 green:0.651 blue:0.722 alpha:1.0];
}

+ (UIColor *)textMuted {
    return [UIColor colorWithRed:0.412 green:0.459 blue:0.541 alpha:1.0];
}

+ (UILabel *)labelWithSize:(CGFloat)size weight:(UIFontWeight)weight color:(UIColor *)color {
    UILabel *label = [UILabel new];
    label.font = [UIFont systemFontOfSize:size weight:weight];
    label.textColor = color ?: [self textPrimary];
    label.numberOfLines = 1;
    return label;
}

+ (UIButton *)pillButtonWithTitle:(NSString *)title target:(id)target action:(SEL)action {
    UIButton *button = [UIButton buttonWithType:UIButtonTypeSystem];
    [button setTitle:title forState:UIControlStateNormal];
    button.titleLabel.font = [UIFont systemFontOfSize:13 weight:UIFontWeightSemibold];
    [button setTitleColor:[self textPrimary] forState:UIControlStateNormal];
    [button setTitleColor:[self textMuted] forState:UIControlStateDisabled];
    button.backgroundColor = [self pillColor];
    button.layer.cornerRadius = 10;
    button.layer.borderWidth = 1.0 / UIScreen.mainScreen.scale;
    button.layer.borderColor = [self hairlineColor].CGColor;
    button.contentEdgeInsets = UIEdgeInsetsMake(8, 12, 8, 12);
    [button.heightAnchor constraintGreaterThanOrEqualToConstant:44].active = YES;
    if (target && action) {
        [button addTarget:target action:action forControlEvents:UIControlEventTouchUpInside];
    }
    return button;
}

+ (UIButton *)primaryButtonWithTitle:(NSString *)title target:(id)target action:(SEL)action {
    UIButton *button = [UIButton buttonWithType:UIButtonTypeSystem];
    [button setTitle:title forState:UIControlStateNormal];
    button.titleLabel.font = [UIFont systemFontOfSize:14 weight:UIFontWeightBold];
    [button setTitleColor:[self canvasColor] forState:UIControlStateNormal];
    [button setTitleColor:[self textMuted] forState:UIControlStateDisabled];
    button.backgroundColor = [self accentTeal];
    button.layer.cornerRadius = 10;
    button.contentEdgeInsets = UIEdgeInsetsMake(8, 14, 8, 14);
    [button.heightAnchor constraintGreaterThanOrEqualToConstant:44].active = YES;
    if (target && action) {
        [button addTarget:target action:action forControlEvents:UIControlEventTouchUpInside];
    }
    return button;
}

+ (UIView *)cardView {
    UIView *view = [UIView new];
    view.backgroundColor = [self surfaceColor];
    view.layer.cornerRadius = 12;
    view.layer.borderWidth = 1.0 / UIScreen.mainScreen.scale;
    view.layer.borderColor = [self hairlineColor].CGColor;
    return view;
}

+ (void)styleChipLabel:(UILabel *)label accent:(UIColor *)accent {
    label.font = [UIFont systemFontOfSize:11 weight:UIFontWeightBold];
    label.textColor = accent;
    label.backgroundColor = [accent colorWithAlphaComponent:0.15];
    label.layer.cornerRadius = 6;
    label.clipsToBounds = YES;
    label.textAlignment = NSTextAlignmentCenter;
}

+ (NSString *)formatDuration:(double)seconds {
    if (!isfinite(seconds) || seconds < 0) return @"0:00";
    long total = (long)llround(seconds);
    long hours = total / 3600;
    long mins = (total % 3600) / 60;
    long secs = total % 60;
    if (hours > 0) {
        return [NSString stringWithFormat:@"%ld:%02ld:%02ld", hours, mins, secs];
    }
    return [NSString stringWithFormat:@"%ld:%02ld", mins, secs];
}

+ (NSString *)displayModeName:(NSString *)mode {
    if ([mode isEqualToString:@"DirectInternet"]) return @"Mode A · Direct Wi-Fi";
    if ([mode isEqualToString:@"BufferedUsb"]) return @"Mode B · Buffered USB";
    if ([mode isEqualToString:@"LivePcm"]) return @"Mode C · Live USB PCM";
    if ([mode isEqualToString:@"RemoteControl"]) return @"Mode D · Remote Control";
    return mode.length ? mode : @"Mode C · Live USB PCM";
}

+ (NSString *)displayOwnerName:(NSString *)owner stage:(NSString *)stage {
    if ([owner isEqualToString:@"iPhoneOwned"] || [stage isEqualToString:@"confirmed"]) return @"Independent on iPhone";
    if ([owner isEqualToString:@"TransferPending"] || [stage isEqualToString:@"connecting"] || [stage isEqualToString:@"buffering"]) return @"Handing off...";
    if ([owner isEqualToString:@"TransferFailed"] || [stage isEqualToString:@"failed"]) return @"Handoff failed · Windows kept";
    if ([owner isEqualToString:@"ReturningToWindows"]) return @"Returning to Windows";
    return @"Windows Host Active";
}

@end
