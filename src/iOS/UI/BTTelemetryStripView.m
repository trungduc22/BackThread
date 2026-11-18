#import "BTTelemetryStripView.h"
#import "BTTheme.h"

static NSString *BTTelemStr(id val, NSString *fallback) {
    return [val isKindOfClass:NSString.class] ? val : fallback;
}

@implementation BTTelemetryStripView {
    UILabel *_clockLabel;
    UILabel *_iphoneLabel;
    UILabel *_pcComputeLabel;
    UILabel *_pcPowerWifiLabel;
    UILabel *_btLabel;
    UILabel *_audioModeChip;
    NSDateFormatter *_clockFormatter;
    NSTimer *_clockTimer;
    NSDictionary *_latestHostTelemetry;
    BOOL _usbConnected;
    NSString *_audioOutputMode;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [BTTheme elevatedColor];
        self.layer.borderWidth = 1.0 / UIScreen.mainScreen.scale;
        self.layer.borderColor = [BTTheme hairlineColor].CGColor;
        _audioOutputMode = @"Both";

        _clockFormatter = [NSDateFormatter new];
        _clockFormatter.locale = [NSLocale localeWithLocaleIdentifier:@"en_US_POSIX"];
        _clockFormatter.dateFormat = @"HH:mm:ss";

        _clockLabel = [BTTheme labelWithSize:12 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        _clockLabel.font = [UIFont monospacedDigitSystemFontOfSize:12 weight:UIFontWeightBold];
        [_clockLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _iphoneLabel = [BTTheme labelWithSize:11 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        _iphoneLabel.font = [UIFont monospacedDigitSystemFontOfSize:11 weight:UIFontWeightSemibold];

        _pcComputeLabel = [BTTheme labelWithSize:11 weight:UIFontWeightMedium color:[BTTheme textSecondary]];
        _pcComputeLabel.font = [UIFont monospacedDigitSystemFontOfSize:11 weight:UIFontWeightMedium];

        _pcPowerWifiLabel = [BTTheme labelWithSize:11 weight:UIFontWeightMedium color:[BTTheme textSecondary]];
        _pcPowerWifiLabel.font = [UIFont monospacedDigitSystemFontOfSize:11 weight:UIFontWeightMedium];

        _btLabel = [BTTheme labelWithSize:10 weight:UIFontWeightBold color:[BTTheme textSecondary]];
        [_btLabel.heightAnchor constraintEqualToConstant:18].active = YES;
        [_btLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _audioModeChip = [BTTheme labelWithSize:10 weight:UIFontWeightBold color:[BTTheme accentBlue]];
        [_audioModeChip.heightAnchor constraintEqualToConstant:18].active = YES;
        [_audioModeChip setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIView *spacer = [UIView new];
        [spacer setContentHuggingPriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *row = [[UIStackView alloc] initWithArrangedSubviews:@[
            _clockLabel,
            _iphoneLabel,
            _pcComputeLabel,
            _pcPowerWifiLabel,
            spacer,
            _btLabel,
            _audioModeChip
        ]];
        row.axis = UILayoutConstraintAxisHorizontal;
        row.alignment = UIStackViewAlignmentCenter;
        row.spacing = 8;
        row.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:row];

        [NSLayoutConstraint activateConstraints:@[
            [self.heightAnchor constraintGreaterThanOrEqualToConstant:26],
            [row.leadingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.leadingAnchor constant:10],
            [row.trailingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.trailingAnchor constant:-10],
            [row.topAnchor constraintEqualToAnchor:self.topAnchor constant:3],
            [row.bottomAnchor constraintEqualToAnchor:self.bottomAnchor constant:-3]
        ]];

        [self refreshClockAndPhoneTelemetry];
        [self renderTelemetry];
        [self startClockTimerIfNeeded];
    }
    return self;
}

- (void)didMoveToWindow {
    [super didMoveToWindow];
    if (self.window) {
        [self refreshClockAndPhoneTelemetry];
        [self startClockTimerIfNeeded];
    } else {
        [_clockTimer invalidate];
        _clockTimer = nil;
    }
}

- (void)startClockTimerIfNeeded {
    if (_clockTimer || !self.window) return;
    __weak typeof(self) weakSelf = self;
    _clockTimer = [NSTimer scheduledTimerWithTimeInterval:1.0 repeats:YES block:^(NSTimer *timer) {
        (void)timer;
        [weakSelf refreshClockAndPhoneTelemetry];
    }];
}

- (void)refreshClockAndPhoneTelemetry {
    _clockLabel.text = [_clockFormatter stringFromDate:[NSDate date]];

    NSDictionary *dev = [BTTheme deviceTelemetrySnapshot];
    NSInteger bat = [dev[@"batteryPercent"] integerValue];
    BOOL chg = [dev[@"charging"] boolValue];
    BOOL wifi = [dev[@"wifiConnected"] boolValue];
    NSString *batStr = bat >= 0 ? [NSString stringWithFormat:@"%ld%%%@", (long)bat, chg ? @"+" : @""] : @"--";
    _iphoneLabel.text = [NSString stringWithFormat:@"iPhone %@ · Wi-Fi %@", batStr, wifi ? @"On" : @"Off"];
}

- (void)updateWithHostTelemetry:(NSDictionary *)hostTelemetry
                   usbConnected:(BOOL)usbConnected
                audioOutputMode:(NSString *)audioOutputMode {
    _latestHostTelemetry = hostTelemetry;
    _usbConnected = usbConnected;
    if (audioOutputMode.length) {
        _audioOutputMode = [audioOutputMode copy];
    }
    [self renderTelemetry];
}

- (void)renderTelemetry {
    [self refreshClockAndPhoneTelemetry];

    NSString *modeText = @" Out: Both ";
    UIColor *modeColor = [BTTheme accentBlue];
    if ([_audioOutputMode isEqualToString:@"PcOnly"]) {
        modeText = @" Out: PC ";
        modeColor = [BTTheme accentAmber];
    } else if ([_audioOutputMode isEqualToString:@"PhoneOnly"]) {
        modeText = @" Out: Phone ";
        modeColor = [BTTheme accentTeal];
    }
    _audioModeChip.text = modeText;
    [BTTheme styleChipLabel:_audioModeChip accent:modeColor];

    NSDictionary *t = _latestHostTelemetry;
    BOOL avail = [t isKindOfClass:NSDictionary.class] && [t[@"available"] boolValue];
    if (!_usbConnected && !avail) {
        _pcComputeLabel.text = @"PC: Offline";
        _pcPowerWifiLabel.text = @"";
        _btLabel.text = @" BT: -- ";
        [BTTheme styleChipLabel:_btLabel accent:[BTTheme textMuted]];
        return;
    }

    id cpuVal = t[@"cpuUsagePercent"];
    NSString *cpuStr = [cpuVal isKindOfClass:NSNumber.class]
        ? [NSString stringWithFormat:@"%.0f%%", [cpuVal doubleValue]]
        : @"--";

    id memUsed = t[@"memoryUsedMb"];
    id memTotal = t[@"memoryTotalMb"];
    id memPct = t[@"memoryUsedPercent"];
    NSString *ramStr = @"--";
    if ([memUsed isKindOfClass:NSNumber.class] && [memTotal isKindOfClass:NSNumber.class] && [memTotal doubleValue] > 0) {
        double usedG = [memUsed doubleValue] / 1024.0;
        double totG = [memTotal doubleValue] / 1024.0;
        if ([memPct isKindOfClass:NSNumber.class]) {
            ramStr = [NSString stringWithFormat:@"%.1f/%.0fG (%.0f%%)", usedG, totG, [memPct doubleValue]];
        } else {
            ramStr = [NSString stringWithFormat:@"%.1f/%.0fG", usedG, totG];
        }
    } else if ([memPct isKindOfClass:NSNumber.class]) {
        ramStr = [NSString stringWithFormat:@"%.0f%%", [memPct doubleValue]];
    }
    _pcComputeLabel.text = [NSString stringWithFormat:@"PC CPU %@ · RAM %@", cpuStr, ramStr];

    id batPct = t[@"batteryPercent"];
    BOOL acOnline = [t[@"acOnline"] isKindOfClass:NSNumber.class] && [t[@"acOnline"] boolValue];
    NSString *batStatus = BTTelemStr(t[@"batteryStatus"], @"");
    NSString *pcBat = @"--";
    if ([batPct isKindOfClass:NSNumber.class]) {
        pcBat = [NSString stringWithFormat:@"%.0f%%%@", [batPct doubleValue], acOnline ? @" AC" : @""];
    } else if (acOnline || [batStatus isEqualToString:@"AC"]) {
        pcBat = @"AC";
    } else if (batStatus.length) {
        pcBat = batStatus;
    }

    NSString *wifiStr = @"--";
    if ([t[@"wifiConnected"] isKindOfClass:NSNumber.class]) {
        wifiStr = [t[@"wifiConnected"] boolValue] ? @"On" : @"Off";
    }
    _pcPowerWifiLabel.text = [NSString stringWithFormat:@"Bat %@ · Wi-Fi %@", pcBat, wifiStr];

    // Strict truthfulness rule: never report Bluetooth connected from radio-on alone.
    NSString *radioState = BTTelemStr(t[@"bluetoothRadioState"], @"Unavailable");
    id btConnVal = t[@"bluetoothConnected"];
    NSInteger btCount = [t[@"bluetoothConnectedCount"] isKindOfClass:NSNumber.class]
        ? [t[@"bluetoothConnectedCount"] integerValue]
        : 0;
    if ([btConnVal isKindOfClass:NSNumber.class] && [btConnVal boolValue] && btCount > 0) {
        _btLabel.text = [NSString stringWithFormat:@" BT: %ld conn ", (long)btCount];
        [BTTheme styleChipLabel:_btLabel accent:[BTTheme accentGreen]];
    } else if ([radioState isEqualToString:@"On"]) {
        _btLabel.text = @" BT: On (0) ";
        [BTTheme styleChipLabel:_btLabel accent:[BTTheme textSecondary]];
    } else if ([radioState isEqualToString:@"Off"] || [radioState isEqualToString:@"Disabled"]) {
        _btLabel.text = @" BT: Off ";
        [BTTheme styleChipLabel:_btLabel accent:[BTTheme textMuted]];
    } else {
        _btLabel.text = @" BT: -- ";
        [BTTheme styleChipLabel:_btLabel accent:[BTTheme textMuted]];
    }
}

- (void)dealloc {
    [_clockTimer invalidate];
}

@end
