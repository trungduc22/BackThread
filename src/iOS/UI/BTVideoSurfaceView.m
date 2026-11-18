#import "BTVideoSurfaceView.h"
#import "BTTheme.h"

@implementation BTVideoSurfaceView {
    UIView *_videoContainer;
    __weak AVPlayerLayer *_playerLayer;
    UIView *_topHud;
    UIView *_bottomHud;
    UILabel *_modeBadge;
    UILabel *_titleLabel;
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
    UIView *_emptyStateView;
    UILabel *_emptyStateLabel;
    NSTimer *_hudHideTimer;
    BOOL _hudVisible;
    BOOL _isPlaying;
    BOOL _aspectFill;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [UIColor blackColor];
        self.clipsToBounds = YES;
        _hudVisible = YES;

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
        _topHud.backgroundColor = [UIColor colorWithWhite:0.06 alpha:0.84];
        _topHud.layer.cornerRadius = 12;
        _topHud.translatesAutoresizingMaskIntoConstraints = NO;

        _modeBadge = [BTTheme labelWithSize:11 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        _modeBadge.text = @" Mode A ";
        [BTTheme styleChipLabel:_modeBadge accent:[BTTheme accentTeal]];
        [_modeBadge.heightAnchor constraintEqualToConstant:24].active = YES;

        _titleLabel = [BTTheme labelWithSize:14 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        _titleLabel.lineBreakMode = NSLineBreakByTruncatingTail;

        _aspectButton = [BTTheme pillButtonWithTitle:@"Fill" target:self action:@selector(toggleAspect)];
        _returnButton = [BTTheme pillButtonWithTitle:@"To Windows" target:self action:@selector(returnToWindows)];
        _collapseButton = [BTTheme pillButtonWithTitle:@"Studio" target:self action:@selector(collapseFocus)];

        [_modeBadge setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [_aspectButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [_returnButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        [_collapseButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *topRow = [[UIStackView alloc] initWithArrangedSubviews:@[_modeBadge, _titleLabel, _aspectButton, _returnButton, _collapseButton]];
        topRow.axis = UILayoutConstraintAxisHorizontal;
        topRow.alignment = UIStackViewAlignmentCenter;
        topRow.spacing = 8;
        topRow.translatesAutoresizingMaskIntoConstraints = NO;
        [_topHud addSubview:topRow];
        [self addSubview:_topHud];

        _bottomHud = [UIView new];
        _bottomHud.backgroundColor = [UIColor colorWithWhite:0.06 alpha:0.84];
        _bottomHud.layer.cornerRadius = 12;
        _bottomHud.translatesAutoresizingMaskIntoConstraints = NO;

        _playPauseButton = [BTTheme primaryButtonWithTitle:@"Pause" target:self action:@selector(playPause)];
        [_playPauseButton.widthAnchor constraintGreaterThanOrEqualToConstant:78].active = YES;

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

        UIStackView *bottomRow = [[UIStackView alloc] initWithArrangedSubviews:@[_playPauseButton, _seekSlider, _timeLabel, _ambientButton]];
        bottomRow.axis = UILayoutConstraintAxisHorizontal;
        bottomRow.alignment = UIStackViewAlignmentCenter;
        bottomRow.spacing = 10;
        bottomRow.translatesAutoresizingMaskIntoConstraints = NO;
        [_bottomHud addSubview:bottomRow];
        [self addSubview:_bottomHud];

        _compactExpandButton = [BTTheme pillButtonWithTitle:@"Full PiP" target:self action:@selector(expandFocus)];
        _compactExpandButton.backgroundColor = [UIColor colorWithWhite:0.06 alpha:0.82];
        _compactExpandButton.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_compactExpandButton];

        _ambientChip = [BTTheme labelWithSize:11 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        _ambientChip.backgroundColor = [UIColor colorWithWhite:0.05 alpha:0.55];
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
            [bottomRow.leadingAnchor constraintEqualToAnchor:_bottomHud.leadingAnchor constant:10],
            [bottomRow.trailingAnchor constraintEqualToAnchor:_bottomHud.trailingAnchor constant:-10],
            [bottomRow.topAnchor constraintEqualToAnchor:_bottomHud.topAnchor constant:6],
            [bottomRow.bottomAnchor constraintEqualToAnchor:_bottomHud.bottomAnchor constant:-6],

            [_compactExpandButton.trailingAnchor constraintEqualToAnchor:self.trailingAnchor constant:-8],
            [_compactExpandButton.bottomAnchor constraintEqualToAnchor:self.bottomAnchor constant:-8],

            [_ambientChip.trailingAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.trailingAnchor constant:-12],
            [_ambientChip.bottomAnchor constraintEqualToAnchor:_ambientProgressTrack.topAnchor constant:-8],
            [_ambientChip.heightAnchor constraintEqualToConstant:24],
            [_ambientChip.widthAnchor constraintGreaterThanOrEqualToConstant:110],

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

        [self applyPresentationVisibility];
    }
    return self;
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
    _hudHideTimer = [NSTimer scheduledTimerWithTimeInterval:4.0 repeats:NO block:^(NSTimer *timer) {
        (void)timer;
        BTVideoSurfaceView *strong = weakSelf;
        if (!strong || strong->_presentationMode != BTVideoPresentationFocus || !strong->_isPlaying) return;
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

- (void)updateWithSnapshot:(NSDictionary *)snapshot usbConnected:(BOOL)usbConnected {
    BOOL active = [snapshot[@"independentActive"] boolValue];
    _isPlaying = [snapshot[@"isPlaying"] boolValue];
    BOOL ready = [snapshot[@"readyForDisplay"] boolValue] || [snapshot[@"videoWidth"] intValue] > 0;
    NSString *mode = [snapshot[@"mode"] isKindOfClass:NSString.class] ? snapshot[@"mode"] : @"DirectInternet";
    NSString *title = [snapshot[@"title"] isKindOfClass:NSString.class] ? snapshot[@"title"] : @"External PiP";
    double pos = [snapshot[@"positionSeconds"] doubleValue];
    double dur = [snapshot[@"durationSeconds"] doubleValue];

    _emptyStateView.hidden = active && ready;
    if (!active) {
        NSString *stage = [snapshot[@"stage"] isKindOfClass:NSString.class] ? snapshot[@"stage"] : @"idle";
        NSString *err = [snapshot[@"error"] isKindOfClass:NSString.class] ? snapshot[@"error"] : @"";
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

    _titleLabel.text = title.length ? title : @"External PiP Media";
    [_playPauseButton setTitle:_isPlaying ? @"Pause" : @"Play" forState:UIControlStateNormal];

    double maxDur = MAX(1.0, isfinite(dur) ? dur : 1.0);
    if (!_seekSlider.tracking) {
        _seekSlider.maximumValue = (float)maxDur;
        _seekSlider.value = (float)MIN(maxDur, MAX(0.0, isfinite(pos) ? pos : 0.0));
    }
    _seekSlider.enabled = active && dur > 0.5;
    _timeLabel.text = [NSString stringWithFormat:@"%@ / %@", [BTTheme formatDuration:pos], [BTTheme formatDuration:dur]];
    _ambientChip.text = [NSString stringWithFormat:@" %@ · %@ ", shortMode, [BTTheme formatDuration:pos]];

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
}

@end
