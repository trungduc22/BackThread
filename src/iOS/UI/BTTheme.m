#import "BTTheme.h"
#import <SystemConfiguration/SystemConfiguration.h>
#import <ifaddrs.h>
#import <net/if.h>
#import <netinet/in.h>
#import <sys/socket.h>

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

+ (BOOL)isReduceMotionEnabled {
    return UIAccessibilityIsReduceMotionEnabled();
}

+ (void)pulseView:(UIView *)view accent:(UIColor *)accent {
    if (!view) return;
    if ([self isReduceMotionEnabled]) {
        view.layer.borderColor = (accent ?: [self accentTeal]).CGColor;
        return;
    }
    UIColor *targetColor = accent ?: [self accentTeal];
    CGColorRef originalBorder = view.layer.borderColor;
    UIColor *origCopy = originalBorder ? [UIColor colorWithCGColor:originalBorder] : [self hairlineColor];
    view.layer.borderColor = targetColor.CGColor;
    [UIView animateWithDuration:0.16 delay:0 options:UIViewAnimationOptionAllowUserInteraction | UIViewAnimationOptionCurveEaseOut animations:^{
        view.transform = CGAffineTransformMakeScale(1.015, 1.015);
    } completion:^(BOOL finished) {
        (void)finished;
        [UIView animateWithDuration:0.22 delay:0 options:UIViewAnimationOptionAllowUserInteraction | UIViewAnimationOptionCurveEaseInOut animations:^{
            view.transform = CGAffineTransformIdentity;
            view.layer.borderColor = origCopy.CGColor;
        } completion:nil];
    }];
}

+ (NSDictionary *)deviceTelemetrySnapshot {
    UIDevice *device = [UIDevice currentDevice];
    if (!device.isBatteryMonitoringEnabled) {
        device.batteryMonitoringEnabled = YES;
    }
    float level = device.batteryLevel;
    NSInteger batteryPct = (level >= 0.0f && level <= 1.0f) ? (NSInteger)llroundf(level * 100.0f) : -1;
    UIDeviceBatteryState state = device.batteryState;
    BOOL charging = (state == UIDeviceBatteryStateCharging || state == UIDeviceBatteryStateFull);

    BOOL wifiAddressUp = NO;
    struct ifaddrs *addrs = NULL;
    if (getifaddrs(&addrs) == 0 && addrs != NULL) {
        for (struct ifaddrs *cur = addrs; cur != NULL; cur = cur->ifa_next) {
            if (!cur->ifa_addr || !cur->ifa_name) continue;
            unsigned int flags = cur->ifa_flags;
            if ((flags & (IFF_UP | IFF_RUNNING)) != (IFF_UP | IFF_RUNNING)) continue;
            if (flags & IFF_LOOPBACK) continue;
            if (strcmp(cur->ifa_name, "en0") == 0 &&
                (cur->ifa_addr->sa_family == AF_INET || cur->ifa_addr->sa_family == AF_INET6)) {
                wifiAddressUp = YES;
                break;
            }
        }
        freeifaddrs(addrs);
    }

    BOOL reachableWifi = NO;
    struct sockaddr_in zeroAddr;
    memset(&zeroAddr, 0, sizeof(zeroAddr));
    zeroAddr.sin_len = sizeof(zeroAddr);
    zeroAddr.sin_family = AF_INET;
    SCNetworkReachabilityRef reach = SCNetworkReachabilityCreateWithAddress(kCFAllocatorDefault, (const struct sockaddr *)&zeroAddr);
    if (reach) {
        SCNetworkReachabilityFlags flags = 0;
        if (SCNetworkReachabilityGetFlags(reach, &flags)) {
            BOOL reachable = (flags & kSCNetworkReachabilityFlagsReachable) != 0;
            BOOL wwan = (flags & kSCNetworkReachabilityFlagsIsWWAN) != 0;
            reachableWifi = reachable && !wwan;
        }
        CFRelease(reach);
    }

    return @{
        @"batteryPercent": @(batteryPct),
        @"charging": @(charging),
        @"wifiConnected": @(wifiAddressUp && reachableWifi),
        @"wifiInterfaceUp": @(wifiAddressUp)
    };
}

@end

@implementation BTEqualizerView {
    NSArray<UIView *> *_bars;
    NSArray<NSLayoutConstraint *> *_heightConstraints;
    NSTimer *_timer;
    NSUInteger _tick;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        _barColor = [BTTheme accentTeal];
        NSMutableArray *bars = [NSMutableArray new];
        NSMutableArray *heights = [NSMutableArray new];
        UIStackView *stack = [UIStackView new];
        stack.axis = UILayoutConstraintAxisHorizontal;
        stack.alignment = UIStackViewAlignmentBottom;
        stack.distribution = UIStackViewDistributionFillEqually;
        stack.spacing = 2.0;
        stack.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:stack];

        NSArray<NSNumber *> *initial = @[@0.35, @0.65, @0.45, @0.25];
        for (NSUInteger i = 0; i < 4; i++) {
            UIView *bar = [UIView new];
            bar.backgroundColor = _barColor;
            bar.layer.cornerRadius = 1.5;
            bar.translatesAutoresizingMaskIntoConstraints = NO;
            [stack addArrangedSubview:bar];
            NSLayoutConstraint *h = [bar.heightAnchor constraintEqualToConstant:6.0 + initial[i].doubleValue * 8.0];
            h.active = YES;
            [bars addObject:bar];
            [heights addObject:h];
        }
        _bars = bars;
        _heightConstraints = heights;

        [NSLayoutConstraint activateConstraints:@[
            [stack.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [stack.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [stack.topAnchor constraintEqualToAnchor:self.topAnchor],
            [stack.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],
            [self.widthAnchor constraintGreaterThanOrEqualToConstant:16],
            [self.heightAnchor constraintGreaterThanOrEqualToConstant:14]
        ]];
    }
    return self;
}

- (void)setBarColor:(UIColor *)barColor {
    _barColor = barColor ?: [BTTheme accentTeal];
    for (UIView *bar in _bars) {
        bar.backgroundColor = _barColor;
    }
}

- (void)setPlaying:(BOOL)playing {
    [self setPlaying:playing animated:YES];
}

- (void)setPlaying:(BOOL)playing animated:(BOOL)animated {
    if (_playing == playing && (!playing || _timer != nil || [BTTheme isReduceMotionEnabled])) {
        return;
    }
    _playing = playing;
    [self syncTimerAndBarsAnimated:animated];
}

- (void)didMoveToWindow {
    [super didMoveToWindow];
    [self syncTimerAndBarsAnimated:NO];
}

- (void)setHidden:(BOOL)hidden {
    [super setHidden:hidden];
    [self syncTimerAndBarsAnimated:NO];
}

- (void)syncTimerAndBarsAnimated:(BOOL)animated {
    [_timer invalidate];
    _timer = nil;

    BOOL reduce = [BTTheme isReduceMotionEnabled];
    BOOL canAnimate = _playing && !self.isHidden && self.window != nil && !reduce;
    if (canAnimate) {
        __weak typeof(self) weakSelf = self;
        _timer = [NSTimer scheduledTimerWithTimeInterval:0.22 repeats:YES block:^(NSTimer *timer) {
            (void)timer;
            [weakSelf stepBars];
        }];
        [self stepBars];
        return;
    }

    NSArray<NSNumber *> *ratios = _playing
        ? @[@0.55, @0.88, @0.68, @0.44]
        : @[@0.24, @0.32, @0.26, @0.20];
    CGFloat maxH = MAX(10.0, self.bounds.size.height > 1 ? self.bounds.size.height : 16.0);
    for (NSUInteger i = 0; i < _heightConstraints.count; i++) {
        _heightConstraints[i].constant = MAX(3.0, maxH * ratios[i].doubleValue);
        _bars[i].alpha = _playing ? 0.95 : 0.45;
    }
    if (animated && !reduce) {
        [UIView animateWithDuration:0.18 animations:^{
            [self layoutIfNeeded];
        }];
    }
}

- (void)stepBars {
    if (!_playing || self.isHidden || !self.window || [BTTheme isReduceMotionEnabled]) {
        [_timer invalidate];
        _timer = nil;
        return;
    }
    _tick++;
    static const double kPatterns[4][4] = {
        {0.42, 0.92, 0.60, 0.35},
        {0.84, 0.48, 0.96, 0.56},
        {0.54, 0.80, 0.38, 0.88},
        {0.72, 0.62, 0.82, 0.45}
    };
    const double *row = kPatterns[_tick % 4];
    CGFloat maxH = MAX(12.0, self.bounds.size.height > 1 ? self.bounds.size.height : 16.0);
    for (NSUInteger i = 0; i < _heightConstraints.count; i++) {
        _heightConstraints[i].constant = MAX(3.0, maxH * row[i]);
        _bars[i].alpha = 0.95;
    }
    [UIView animateWithDuration:0.18 delay:0 options:UIViewAnimationOptionAllowUserInteraction | UIViewAnimationOptionCurveEaseInOut animations:^{
        [self layoutIfNeeded];
    } completion:nil];
}

- (void)dealloc {
    [_timer invalidate];
}

@end

