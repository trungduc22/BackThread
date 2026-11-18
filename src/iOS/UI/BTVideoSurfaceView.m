#import "BTVideoSurfaceView.h"
#import "BTTheme.h"
#import <QuartzCore/QuartzCore.h>

static NSString *BTVidStr(id val, NSString *fallback) {
    return [val isKindOfClass:NSString.class] ? val : fallback;
}

@implementation BTVideoSurfaceView {
    UIView *_videoContainer;
    __weak AVPlayerLayer *_playerLayer;
    UIView *_topHud;
    UIView *_bottomHud;
    UILabel *_modeBadge;
    BTEqualizerView *_equalizerView;
    UILabel *_titleLabel;
    UILabel *_clockLabel;
    UILabel *_telemetryChip;
    UILabel *_ambientChip;
    UIView *_ambientProgressTrack;
    UIView *_ambientProgressFill;
    NSLayoutConstraint *_ambientProgressWidth;
    UIButton *_aspectButton;
    UIButton *_returnButton;
    UIButton *_collapseButton;
    UIButton *_compactExpandButton;
    UIButton *_playPauseButton;
    UIButton *_ambientButton;
    UISlider *_seekSlider;
    UILabel *_timeLabel;
    UISegmentedControl *_outputModeSegment;
    UIButton *_volumeLayerButton;
    UISlider *_volumeSlider;
    UIView *_emptyStateView;
    UILabel *_emptyStateLabel;
    NSTimer *_hudHideTimer;
    NSTimer *_clockTimer;
    NSDateFormatter *_clockFormatter;
    NSDictionary *_audioControlState;
    NSDictionary *_hostTelemetry;
    NSString *_selectedVolumeLayer;
    BOOL _userSelectedVolumeLayer;
    CFTimeInterval _lastVolumeSendTime;
    BOOL _hudVisible;
    BOOL _isPlaying;
    BOOL _aspectFill;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [UIColor blackColor];
        self.clipsToBounds = YES;
        _hudVisible = YES;
        _selectedVolumeLayer = @"player";
        _audioControlState = @{
            @"mode": @"Both",
            @"pcVolumeScalar": @0.8,
            @"phoneAppGain": @1.0,
            @"phonePlayerVolume": @1.0,
            @"activeVolumeLayer": @"player"
        };

        _clockFormatter = [NSDateFormatter new];
        _clockFormatter.locale = [NSLocale localeWithLocaleIdentifier:@"en_US_POSIX"];
        _clockFormatter.dateFormat = @"HH:mm:ss";

        _videoContainer = [UIView new];
        _videoContainer.backgroundColor = [UIColor blackColor];
        _videoContainer.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_videoContainer];

        _emptyStateView = [UIView new];
        _emptyStateView.backgroundColor = [BTTheme surfaceColor];
        _emptyStateView.translatesAutoresizingMaskIntoConstraints = NO;
        _emptyStateLabel = [BTTheme labelWithSize:13 weight:UIFontWeightMedium color:[BTTheme textSecondary]];
        _emptyStateLabel.numberOfLines = 2;
        _emptyStateLabel.textAlignment = NSTextAlignmentCenter;
        _emptyStateLabel.text = @"External PiP ready\nSend HTTPS or local video from Windows";
        _emptyStateLabel.translatesAutoresizingMaskIntoConstraints = NO;
        [_emptyStateView addSubview:_emptyStateLabel];
        [self addSubview:_emptyStateView];

        _topHud = [UIView new];
        _topHud.backgroundColor = [UIColor colorWithWhite:0.06 alpha:0.86];
        _topHud.layer.cornerRadius = 12;
        _topHud.translatesAutoresizingMaskIntoConstraints = NO;

        _modeBadge = [BTTheme labelWithSize:11 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        _modeBadge.text = @" Mode A ";
        [BTTheme styleChipLabel:_modeBadge accent:[BTTheme accentTeal]];
        [_modeBadge.heightAnchor constraintEqualToConstant:24].active = YES;

        _equalizerView = [BTEqualizerView new];
        _equalizerView.barColor = [BTTheme accentTeal];

        _titleLabel = [BTTheme labelWithSize:14 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        _titleLabel.lineBreakMode = NSLineBreakByTruncatingTail;

        _clockLabel = [BTTheme labelWithSize:12 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        _clockLabel.font = [UIFont monospacedDigitSystemFontOfSize:12 weight:UIFontWeightBold];
        [_clockLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _telemetryChip = [BTTheme labelWithSize:10 weight:UIFontWeightSemibold color:[BTTheme textSecondary]];
        _telemetryChip.font = [UIFont monospacedDigitSystemFontOfSize:10 weight:UIFontWeightSemibold];
        _telemetryChip.text = @"PC --";
        [_telemetryChip setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _aspectButton = [BTTheme pillButtonWithTitle:@"Fill" target:self action:@selector(toggleAspect)];
        _returnButton = [BTTheme pillButtonWithTitle:@"To Windows" target:self action:@selector(returnToWindows)];
        _collapseButton = [BTTheme pillButtonWithTitle:@"Studio" target:self action:@selector(collapseFocus)];

        [_modeBadge setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [_aspectButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [_returnButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [_collapseButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *topRow = [[UIStackView alloc] initWithArrangedSubviews:@[
            _modeBadge, _equalizerView, _titleLabel, _clockLabel, _telemetryChip, _aspectButton, _returnButton, _collapseButton
        ]];
        topRow.axis = UILayoutConstraintAxisHorizontal;
        topRow.alignment = UIStackViewAlignmentCenter;
        topRow.spacing = 6;
        topRow.translatesAutoresizingMaskIntoConstraints = NO;
        [_topHud addSubview:topRow];
        [self addSubview:_topHud];

        _bottomHud = [UIView new];
        _bottomHud.backgroundColor = [UIColor colorWithWhite:0.06 alpha:0.86];
        _bottomHud.layer.cornerRadius = 12;
        _bottomHud.translatesAutoresizingMaskIntoConstraints = NO;

        _playPauseButton = [BTTheme primaryButtonWithTitle:@"Pause" target:self action:@selector(playPause)];
        [_playPauseButton.widthAnchor constraintGreaterThanOrEqualToConstant:72].active = YES;

        _seekSlider = [UISlider new];
        _seekSlider.minimumTrackTintColor = [BTTheme accentTeal];
        _seekSlider.maximumTrackTintColor = [BTTheme pillColor];
        [_seekSlider addTarget:self action:@selector(seekDone) forControlEvents:UIControlEventTouchUpInside | UIControlEventTouchUpOutside];
        [_seekSlider addTarget:self action:@selector(resetHudTimer) forControlEvents:UIControlEventTouchDown];

        _timeLabel = [BTTheme labelWithSize:12 weight:UIFontWeightMedium color:[BTTheme textSecondary]];
        _timeLabel.font = [UIFont monospacedDigitSystemFontOfSize:12 weight:UIFontWeightMedium];
        _timeLabel.text = @"0:00 / 0:00";
        [_timeLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _ambientButton = [BTTheme pillButtonWithTitle:@"Ambient" target:self action:@selector(toggleAmbient)];
        [_ambientButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *seekRow = [[UIStackView alloc] initWithArrangedSubviews:@[_playPauseButton, _seekSlider, _timeLabel, _ambientButton]];
        seekRow.axis = UILayoutConstraintAxisHorizontal;
        seekRow.alignment = UIStackViewAlignmentCenter;
        seekRow.spacing = 8;

        _outputModeSegment = [[UISegmentedControl alloc] initWithItems:@[@"Only PC", @"Only Phone", @"Both"]];
        _outputModeSegment.selectedSegmentIndex = 2;
        _outputModeSegment.tintColor = [BTTheme accentTeal];
        [_outputModeSegment setTitleTextAttributes:@{NSFontAttributeName: [UIFont systemFontOfSize:10 weight:UIFontWeightSemibold]} forState:UIControlStateNormal];
        [_outputModeSegment.heightAnchor constraintEqualToConstant:28].active = YES;
        [_outputModeSegment.widthAnchor constraintGreaterThanOrEqualToConstant:195].active = YES;
        [_outputModeSegment addTarget:self action:@selector(outputModeChanged) forControlEvents:UIControlEventValueChanged];

        _volumeLayerButton = [BTTheme pillButtonWithTitle:@"AVPlayer: 100%" target:self action:@selector(cycleVolumeLayer)];
        _volumeLayerButton.titleLabel.font = [UIFont monospacedDigitSystemFontOfSize:11 weight:UIFontWeightBold];
        _volumeLayerButton.contentEdgeInsets = UIEdgeInsetsMake(3, 8, 3, 8);
        [_volumeLayerButton.heightAnchor constraintEqualToConstant:30].active = YES;
        [_volumeLayerButton.widthAnchor constraintGreaterThanOrEqualToConstant:106].active = YES;
        [_volumeLayerButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _volumeSlider = [UISlider new];
        _volumeSlider.minimumValue = 0.0f;
        _volumeSlider.maximumValue = 1.0f;
        _volumeSlider.value = 1.0f;
        _volumeSlider.minimumTrackTintColor = [BTTheme accentTeal];
        _volumeSlider.maximumTrackTintColor = [BTTheme pillColor];
        [_volumeSlider addTarget:self action:@selector(volumeLiveChanged) forControlEvents:UIControlEventValueChanged];
        [_volumeSlider addTarget:self action:@selector(volumeCommitted) forControlEvents:UIControlEventTouchUpInside | UIControlEventTouchUpOutside | UIControlEventTouchCancel];

        UIStackView *audioRow = [[UIStackView alloc] initWithArrangedSubviews:@[_outputModeSegment, _volumeLayerButton, _volumeSlider]];
        audioRow.axis = UILayoutConstraintAxisHorizontal;
        audioRow.alignment = UIStackViewAlignmentCenter;
        audioRow.spacing = 8;

        UIStackView *bottomStack = [[UIStackView alloc] initWithArrangedSubviews:@[seekRow, audioRow]];
        bottomStack.axis = UILayoutConstraintAxisVertical;
        bottomStack.spacing = 5;
        bottomStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_bottomHud addSubview:bottomStack];
        [self addSubview:_bottomHud];

        _compactExpandButton = [BTTheme pillButtonWithTitle:@"Full PiP" target:self action:@selector(expandFocus)];
        _compactExpandButton.backgroundColor = [UIColor colorWithWhite:0.06 alpha:0.82];
        _compactExpandButton.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_compactExpandButton];

        _ambientChip = [BTTheme labelWithSize:11 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        _ambientChip.backgroundColor = [UIColor colorWithWhite:0.05 alpha:0.60];
        _ambientChip.layer.cornerRadius = 8;
        _ambientChip.clipsToBounds = YES;
        _ambientChip.textAlignment = NSTextAlignmentCenter;
        _ambientChip.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_ambientChip];

        _ambientProgressTrack = [UIView new];
        _ambientProgressTrack.backgroundColor = [UIColor colorWithWhite:1.0 alpha:0.15];
        _ambientProgressTrack.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_ambientProgressTrack];

        _ambientProgressFill = [UIView new];
        _ambientProgressFill.backgroundColor = [BTTheme accentTeal];
        _ambientProgressFill.translatesAutoresizingMaskIntoConstraints = NO;
        [_ambientProgressTrack addSubview:_ambientProgressFill];

        _ambientProgressWidth = [_ambientProgressFill.widthAnchor constraintEqualToConstant:0];

        [NSLayoutConstraint activateConstraints:@[
            [_videoContainer.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_videoContainer.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_videoContainer.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_videoContainer.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],

            [_emptyStateView.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_emptyStateView.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_emptyStateView.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_emptyStateView.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],
            [_emptyStateLabel.centerXAnchor constraintEqualToAnchor:_emptyStateView.centerXAnchor],
            [_emptyStateLabel.centerYAnchor constraintEqualToAnchor:_emptyStateView.centerYAnchor],
            [_emptyStateLabel.leadingAnchor constraintGreaterThanOrEqualToAnchor:_emptyStateView.leadingAnchor constant:12],
            [_emptyStateLabel.trailingAnchor constraintLessThanOrEqualToAnchor:_emptyStateView.trailingAnchor constant:-12],

            [_topHud.leadingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.leadingAnchor constant:10],
            [_topHud.trailingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.trailingAnchor constant:-10],
            [_topHud.topAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.topAnchor constant:8],
            [topRow.leadingAnchor constraintEqualToAnchor:_topHud.leadingAnchor constant:10],
            [topRow.trailingAnchor constraintEqualToAnchor:_topHud.trailingAnchor constant:-10],
            [topRow.topAnchor constraintEqualToAnchor:_topHud.topAnchor constant:6],
            [topRow.bottomAnchor constraintEqualToAnchor:_topHud.bottomAnchor constant:-6],

            [_bottomHud.leadingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.leadingAnchor constant:10],
            [_bottomHud.trailingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.trailingAnchor constant:-10],
            [_bottomHud.bottomAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.bottomAnchor constant:-8],
            [bottomStack.leadingAnchor constraintEqualToAnchor:_bottomHud.leadingAnchor constant:10],
            [bottomStack.trailingAnchor constraintEqualToAnchor:_bottomHud.trailingAnchor constant:-10],
            [bottomStack.topAnchor constraintEqualToAnchor:_bottomHud.topAnchor constant:6],
            [bottomStack.bottomAnchor constraintEqualToAnchor:_bottomHud.bottomAnchor constant:-6],

            [_compactExpandButton.trailingAnchor constraintEqualToAnchor:self.trailingAnchor constant:-8],
            [_compactExpandButton.bottomAnchor constraintEqualToAnchor:self.bottomAnchor constant:-8],

            [_ambientChip.trailingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.trailingAnchor constant:-12],
            [_ambientChip.bottomAnchor constraintEqualToAnchor:_ambientProgressTrack.topAnchor constant:-8],
            [_ambientChip.heightAnchor constraintEqualToConstant:24],
            [_ambientChip.widthAnchor constraintGreaterThanOrEqualToConstant:150],

            [_ambientProgressTrack.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_ambientProgressTrack.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_ambientProgressTrack.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],
            [_ambientProgressTrack.heightAnchor constraintEqualToConstant:3],

            [_ambientProgressFill.leadingAnchor constraintEqualToAnchor:_ambientProgressTrack.leadingAnchor],
            [_ambientProgressFill.topAnchor constraintEqualToAnchor:_ambientProgressTrack.topAnchor],
            [_ambientProgressFill.bottomAnchor constraintEqualToAnchor:_ambientProgressTrack.bottomAnchor],
            _ambientProgressWidth
        ]];

        UITapGestureRecognizer *tap = [[UITapGestureRecognizer alloc] initWithTarget:self action:@selector(handleStageTap)];
        tap.cancelsTouchesInView = NO;
        [_videoContainer addGestureRecognizer:tap];

        [self refreshClock];
        [self renderAudioControls];
        [self applyPresentationVisibility];
    }
    return self;
}

- (void)didMoveToWindow {
    [super didMoveToWindow];
    if (self.window) {
        [self refreshClock];
        if (!_clockTimer) {
            __weak typeof(self) weakSelf = self;
            _clockTimer = [NSTimer scheduledTimerWithTimeInterval:1.0 repeats:YES block:^(NSTimer *timer) {
                (void)timer;
                [weakSelf refreshClock];
            }];
        }
    } else {
        [_clockTimer invalidate];
        _clockTimer = nil;
    }
}

- (void)refreshClock {
    _clockLabel.text = [_clockFormatter stringFromDate:[NSDate date]];
}

- (void)attachPlayerLayer:(AVPlayerLayer *)playerLayer {
    if (!playerLayer || _playerLayer == playerLayer) return;
    [_playerLayer removeFromSuperlayer];
    _playerLayer = playerLayer;
    _playerLayer.videoGravity = _aspectFill ? AVLayerVideoGravityResizeAspectFill : AVLayerVideoGravityResizeAspect;
    [_videoContainer.layer insertSublayer:playerLayer atIndex:0];
    [self setNeedsLayout];
}

- (void)layoutSubviews {
    [super layoutSubviews];
    if (_playerLayer) {
        [CATransaction begin];
        [CATransaction setDisableActions:YES];
        _playerLayer.frame = _videoContainer.bounds;
        [CATransaction commit];
    }
    if (_ambientProgressTrack.bounds.size.width > 0 && _seekSlider.maximumValue > 0) {
        double ratio = MAX(0.0, MIN(1.0, _seekSlider.value / _seekSlider.maximumValue));
        _ambientProgressWidth.constant = _ambientProgressTrack.bounds.size.width * ratio;
    }
}

- (void)setPresentationMode:(BTVideoPresentationMode)presentationMode {
    _presentationMode = presentationMode;
    _hudVisible = (presentationMode == BTVideoPresentationFocus);
    [self applyPresentationVisibility];
    if (presentationMode == BTVideoPresentationFocus) {
        [self resetHudTimer];
    } else {
        [_hudHideTimer invalidate];
        _hudHideTimer = nil;
    }
}

- (void)applyPresentationVisibility {
    BOOL compact = (_presentationMode == BTVideoPresentationCompact);
    BOOL focus = (_presentationMode == BTVideoPresentationFocus);
    BOOL ambient = (_presentationMode == BTVideoPresentationAmbient);

    _compactExpandButton.hidden = !compact;
    _topHud.hidden = !focus || !_hudVisible;
    _bottomHud.hidden = !focus || !_hudVisible;
    _ambientChip.hidden = !ambient;
    _ambientProgressTrack.hidden = !ambient;
}

- (void)handleStageTap {
    if (_presentationMode == BTVideoPresentationCompact) {
        if (self.onExpandFocusTapped) self.onExpandFocusTapped();
        return;
    }
    if (_presentationMode == BTVideoPresentationAmbient) {
        if (self.onAmbientToggleTapped) self.onAmbientToggleTapped();
        return;
    }
    _hudVisible = !_hudVisible;
    [self applyPresentationVisibility];
    if (_hudVisible) {
        [self resetHudTimer];
    } else {
        [_hudHideTimer invalidate];
        _hudHideTimer = nil;
    }
}

- (void)showOverlayTemporarily {
    if (_presentationMode != BTVideoPresentationFocus) return;
    _hudVisible = YES;
    [self applyPresentationVisibility];
    [self resetHudTimer];
}

- (void)resetHudTimer {
    [_hudHideTimer invalidate];
    _hudHideTimer = nil;
    if (_presentationMode != BTVideoPresentationFocus || !_isPlaying) return;
    __weak typeof(self) weakSelf = self;
    _hudHideTimer = [NSTimer scheduledTimerWithTimeInterval:4.5 repeats:NO block:^(NSTimer *timer) {
        (void)timer;
        BTVideoSurfaceView *strong = weakSelf;
        if (!strong || strong->_presentationMode != BTVideoPresentationFocus || !strong->_isPlaying) return;
        if (strong->_seekSlider.tracking || strong->_volumeSlider.tracking) {
            [strong resetHudTimer];
            return;
        }
        strong->_hudVisible = NO;
        [strong applyPresentationVisibility];
    }];
}

- (void)toggleAspect {
    _aspectFill = !_aspectFill;
    _playerLayer.videoGravity = _aspectFill ? AVLayerVideoGravityResizeAspectFill : AVLayerVideoGravityResizeAspect;
    [_aspectButton setTitle:_aspectFill ? @"Fit" : @"Fill" forState:UIControlStateNormal];
    [self resetHudTimer];
}

- (void)playPause {
    if (self.onPlayPauseTapped) self.onPlayPauseTapped();
    [self resetHudTimer];
}

- (void)seekDone {
    if (self.onSeekCommitted) self.onSeekCommitted(_seekSlider.value);
    [self resetHudTimer];
}

- (void)returnToWindows {
    if (self.onReturnToWindowsTapped) self.onReturnToWindowsTapped();
}

- (void)expandFocus {
    if (self.onExpandFocusTapped) self.onExpandFocusTapped();
}

- (void)collapseFocus {
    if (self.onCloseFocusTapped) self.onCloseFocusTapped();
}

- (void)toggleAmbient {
    if (self.onAmbientToggleTapped) self.onAmbientToggleTapped();
}

- (void)outputModeChanged {
    _userSelectedVolumeLayer = NO;
    NSString *mode = @"Both";
    if (_outputModeSegment.selectedSegmentIndex == 0) mode = @"PcOnly";
    else if (_outputModeSegment.selectedSegmentIndex == 1) mode = @"PhoneOnly";
    [BTTheme pulseView:_outputModeSegment accent:[BTTheme accentTeal]];
    if (self.onAudioOutputModeSelected) self.onAudioOutputModeSelected(mode);
    [self resetHudTimer];
}

- (void)cycleVolumeLayer {
    _userSelectedVolumeLayer = YES;
    if ([_selectedVolumeLayer isEqualToString:@"player"]) _selectedVolumeLayer = @"pc";
    else if ([_selectedVolumeLayer isEqualToString:@"pc"]) _selectedVolumeLayer = @"app";
    else _selectedVolumeLayer = @"player";
    [self renderAudioControls];
    [self resetHudTimer];
}

- (float)acknowledgedScalarForLayer:(NSString *)layer {
    if ([layer isEqualToString:@"app"]) {
        return [_audioControlState[@"phoneAppGain"] isKindOfClass:NSNumber.class]
            ? [_audioControlState[@"phoneAppGain"] floatValue] : 1.0f;
    }
    if ([layer isEqualToString:@"pc"]) {
        return [_audioControlState[@"pcVolumeScalar"] isKindOfClass:NSNumber.class]
            ? [_audioControlState[@"pcVolumeScalar"] floatValue] : 1.0f;
    }
    return [_audioControlState[@"phonePlayerVolume"] isKindOfClass:NSNumber.class]
        ? [_audioControlState[@"phonePlayerVolume"] floatValue] : 1.0f;
}

- (NSString *)labelForLayer:(NSString *)layer scalar:(float)scalar {
    long pct = (long)llroundf(fmaxf(0.0f, fminf(1.0f, scalar)) * 100.0f);
    if ([layer isEqualToString:@"app"]) return [NSString stringWithFormat:@"App Gain: %ld%%", pct];
    if ([layer isEqualToString:@"pc"]) return [NSString stringWithFormat:@"PC Vol: %ld%%", pct];
    return [NSString stringWithFormat:@"AVPlayer: %ld%%", pct];
}

- (void)volumeLiveChanged {
    [_volumeLayerButton setTitle:[self labelForLayer:_selectedVolumeLayer scalar:_volumeSlider.value]
                        forState:UIControlStateNormal];
    CFTimeInterval now = CACurrentMediaTime();
    if (now - _lastVolumeSendTime >= 0.12) {
        _lastVolumeSendTime = now;
        if (self.onVolumeChanged) self.onVolumeChanged(_volumeSlider.value, _selectedVolumeLayer);
    }
    [self resetHudTimer];
}

- (void)volumeCommitted {
    _lastVolumeSendTime = CACurrentMediaTime();
    [_volumeLayerButton setTitle:[self labelForLayer:_selectedVolumeLayer scalar:_volumeSlider.value]
                        forState:UIControlStateNormal];
    if (self.onVolumeChanged) self.onVolumeChanged(_volumeSlider.value, _selectedVolumeLayer);
    [self resetHudTimer];
}

- (void)updateWithAudioControlState:(NSDictionary *)audioControlState {
    if (![audioControlState isKindOfClass:NSDictionary.class]) return;
    _audioControlState = [audioControlState copy];
    if (!_userSelectedVolumeLayer) {
        NSString *ackedLayer = BTVidStr(audioControlState[@"activeVolumeLayer"], @"player");
        if ([ackedLayer isEqualToString:@"pc"] || [ackedLayer isEqualToString:@"app"] || [ackedLayer isEqualToString:@"player"]) {
            _selectedVolumeLayer = ackedLayer;
        }
    }
    [self renderAudioControls];
}

- (void)renderAudioControls {
    NSString *mode = BTVidStr(_audioControlState[@"mode"], @"Both");
    NSInteger seg = 2;
    if ([mode isEqualToString:@"PcOnly"]) seg = 0;
    else if ([mode isEqualToString:@"PhoneOnly"]) seg = 1;
    if (_outputModeSegment.selectedSegmentIndex != seg) {
        _outputModeSegment.selectedSegmentIndex = seg;
    }

    float acked = [self acknowledgedScalarForLayer:_selectedVolumeLayer];
    if (!_volumeSlider.tracking) {
        _volumeSlider.value = fmaxf(0.0f, fminf(1.0f, acked));
    }
    [_volumeLayerButton setTitle:[self labelForLayer:_selectedVolumeLayer scalar:_volumeSlider.tracking ? _volumeSlider.value : acked]
                        forState:UIControlStateNormal];
}

- (void)updateWithHostTelemetry:(NSDictionary *)hostTelemetry {
    _hostTelemetry = [hostTelemetry copy];
    if (![hostTelemetry isKindOfClass:NSDictionary.class] || ![hostTelemetry[@"available"] boolValue]) {
        _telemetryChip.text = @"PC --";
        return;
    }
    id cpu = hostTelemetry[@"cpuUsagePercent"];
    id ram = hostTelemetry[@"memoryUsedPercent"];
    NSString *cpuStr = [cpu isKindOfClass:NSNumber.class] ? [NSString stringWithFormat:@"%.0f%%", [cpu doubleValue]] : @"--";
    NSString *ramStr = [ram isKindOfClass:NSNumber.class] ? [NSString stringWithFormat:@"%.0f%%", [ram doubleValue]] : @"--";
    BOOL btConn = [hostTelemetry[@"bluetoothConnected"] isKindOfClass:NSNumber.class] && [hostTelemetry[@"bluetoothConnected"] boolValue];
    NSInteger btCount = [hostTelemetry[@"bluetoothConnectedCount"] isKindOfClass:NSNumber.class]
        ? [hostTelemetry[@"bluetoothConnectedCount"] integerValue] : 0;
    NSString *btStr = (btConn && btCount > 0) ? [NSString stringWithFormat:@"BT:%ld", (long)btCount] : @"BT:0";
    _telemetryChip.text = [NSString stringWithFormat:@"CPU %@ · RAM %@ · %@", cpuStr, ramStr, btStr];
}

- (void)updateWithSnapshot:(NSDictionary *)snapshot usbConnected:(BOOL)usbConnected {
    BOOL active = [snapshot[@"independentActive"] boolValue];
    _isPlaying = [snapshot[@"isPlaying"] boolValue];
    BOOL ready = [snapshot[@"readyForDisplay"] boolValue] || [snapshot[@"videoWidth"] intValue] > 0;
    NSString *mode = BTVidStr(snapshot[@"mode"], @"DirectInternet");
    NSString *title = BTVidStr(snapshot[@"title"], @"External PiP");
    double pos = [snapshot[@"positionSeconds"] doubleValue];
    double dur = [snapshot[@"durationSeconds"] doubleValue];

    _emptyStateView.hidden = active && ready;
    if (!active) {
        NSString *stage = BTVidStr(snapshot[@"stage"], @"idle");
        NSString *err = BTVidStr(snapshot[@"error"], @"");
        if (err.length > 0) {
            _emptyStateLabel.text = [NSString stringWithFormat:@"Handoff Error: %@", err];
        } else if ([stage isEqual:@"connecting"] || [stage isEqual:@"buffering"] || [stage isEqual:@"validating"]) {
            _emptyStateLabel.text = [NSString stringWithFormat:@"Starting External PiP (%@)...", stage];
        } else {
            _emptyStateLabel.text = @"External PiP ready\nSend HTTPS or local video from Windows";
        }
    } else if (!ready) {
        _emptyStateLabel.text = [NSString stringWithFormat:@"Audio playing on iPhone\n%@", title];
    }

    NSString *shortMode = [mode isEqual:@"BufferedUsb"] ? @"Mode B" : @"Mode A";
    UIColor *accent = [mode isEqual:@"BufferedUsb"] ? [BTTheme accentAmber] : [BTTheme accentTeal];
    _modeBadge.text = usbConnected ? [NSString stringWithFormat:@" %@ ", shortMode] : [NSString stringWithFormat:@" %@ · Wi-Fi ", shortMode];
    [BTTheme styleChipLabel:_modeBadge accent:accent];
    _equalizerView.barColor = accent;
    _equalizerView.playing = _isPlaying && active;

    _titleLabel.text = title.length ? title : @"External PiP Media";
    [_playPauseButton setTitle:_isPlaying ? @"Pause" : @"Play" forState:UIControlStateNormal];

    double maxDur = MAX(1.0, isfinite(dur) ? dur : 1.0);
    if (!_seekSlider.tracking) {
        _seekSlider.maximumValue = (float)maxDur;
        _seekSlider.value = (float)MIN(maxDur, MAX(0.0, isfinite(pos) ? pos : 0.0));
    }
    _seekSlider.enabled = active && dur > 0.5;
    _timeLabel.text = [NSString stringWithFormat:@"%@ / %@", [BTTheme formatDuration:pos], [BTTheme formatDuration:dur]];
    _ambientChip.text = [NSString stringWithFormat:@" %@ · %@ · %@ ", [_clockFormatter stringFromDate:[NSDate date]], shortMode, [BTTheme formatDuration:pos]];

    if (_ambientProgressTrack.bounds.size.width > 0) {
        double ratio = dur > 0.5 ? MAX(0.0, MIN(1.0, pos / maxDur)) : 0.0;
        _ambientProgressWidth.constant = _ambientProgressTrack.bounds.size.width * ratio;
    }

    if (_presentationMode == BTVideoPresentationFocus && _hudVisible && !_isPlaying) {
        [_hudHideTimer invalidate];
        _hudHideTimer = nil;
    }
}

- (void)dealloc {
    [_hudHideTimer invalidate];
    [_clockTimer invalidate];
}

@end
