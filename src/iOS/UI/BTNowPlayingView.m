#import "BTNowPlayingView.h"
#import "BTTheme.h"
#import <MediaPlayer/MediaPlayer.h>
#import <ImageIO/ImageIO.h>
#import <QuartzCore/QuartzCore.h>

static NSString *BTNowStr(id val, NSString *fallback) {
    return [val isKindOfClass:NSString.class] ? val : fallback;
}

static double BTNowNum(id val) {
    return [val isKindOfClass:NSNumber.class] ? [val doubleValue] : 0.0;
}

@implementation BTNowPlayingView {
    UIScrollView *_scrollView;
    UIStackView *_columnsStack;
    UIView *_leftCard;
    UIView *_videoSlotView;
    UIImageView *_artworkView;
    UILabel *_modeBadge;
    BTEqualizerView *_equalizerView;
    UILabel *_ownerLabel;
    UILabel *_reasonLabel;
    UIButton *_videoFocusButton;
    UIButton *_returnButton;

    UILabel *_titleLabel;
    UILabel *_artistLabel;
    UISlider *_seekSlider;
    UILabel *_timelineLabel;
    UIButton *_prevButton;
    UIButton *_playButton;
    UIButton *_nextButton;

    UISegmentedControl *_outputModeSegment;
    UIButton *_volumeLayerButton;
    UISlider *_volumeSlider;
    UILabel *_audioWarningLabel;

    UIButton *_focusButton;
    UIButton *_qualityButton;
    UIButton *_awakeButton;

    NSLayoutConstraint *_leftWidthConstraint;
    dispatch_queue_t _artQueue;
    NSString *_artworkKey;
    NSString *_currentFocusPolicy;
    NSString *_currentQualityCap;
    BOOL _keepAwake;
    NSDictionary *_audioControlState;
    NSString *_selectedVolumeLayer;
    BOOL _userSelectedVolumeLayer;
    CFTimeInterval _lastVolumeSendTime;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [BTTheme canvasColor];
        _artQueue = dispatch_queue_create("app.backthread.artwork.decode", DISPATCH_QUEUE_SERIAL);
        _currentFocusPolicy = @"Auto";
        _currentQualityCap = @"auto";
        _keepAwake = YES;
        _selectedVolumeLayer = @"pc";
        _audioControlState = @{
            @"mode": @"Both",
            @"pcVolumeScalar": @0.8,
            @"phoneAppGain": @1.0,
            @"phonePlayerVolume": @1.0,
            @"activeVolumeLayer": @"pc"
        };

        _scrollView = [UIScrollView new];
        _scrollView.showsVerticalScrollIndicator = NO;
        _scrollView.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_scrollView];

        _leftCard = [BTTheme cardView];
        _leftCard.translatesAutoresizingMaskIntoConstraints = NO;

        _videoSlotView = [UIView new];
        _videoSlotView.backgroundColor = [UIColor blackColor];
        _videoSlotView.layer.cornerRadius = 8;
        _videoSlotView.clipsToBounds = YES;
        _videoSlotView.translatesAutoresizingMaskIntoConstraints = NO;

        _artworkView = [UIImageView new];
        _artworkView.backgroundColor = [BTTheme elevatedColor];
        _artworkView.contentMode = UIViewContentModeScaleAspectFill;
        _artworkView.layer.cornerRadius = 8;
        _artworkView.clipsToBounds = YES;
        _artworkView.translatesAutoresizingMaskIntoConstraints = NO;

        [_videoSlotView addSubview:_artworkView];

        _modeBadge = [BTTheme labelWithSize:11 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        _modeBadge.text = @" Mode C · Live PCM ";
        [BTTheme styleChipLabel:_modeBadge accent:[BTTheme accentTeal]];
        [_modeBadge.heightAnchor constraintEqualToConstant:22].active = YES;

        _equalizerView = [BTEqualizerView new];
        _equalizerView.barColor = [BTTheme accentTeal];
        [_equalizerView setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *badgeEqRow = [[UIStackView alloc] initWithArrangedSubviews:@[_modeBadge, _equalizerView]];
        badgeEqRow.axis = UILayoutConstraintAxisHorizontal;
        badgeEqRow.alignment = UIStackViewAlignmentCenter;
        badgeEqRow.spacing = 6;

        _ownerLabel = [BTTheme labelWithSize:12 weight:UIFontWeightSemibold color:[BTTheme textSecondary]];
        _ownerLabel.text = @"Windows Host Active";

        _reasonLabel = [BTTheme labelWithSize:11 weight:UIFontWeightRegular color:[BTTheme textMuted]];
        _reasonLabel.numberOfLines = 2;
        _reasonLabel.text = @"Ready for Windows playback or External PiP handoff";

        _videoFocusButton = [BTTheme primaryButtonWithTitle:@"Open Video PiP" target:self action:@selector(openVideoFocus)];
        _videoFocusButton.hidden = YES;

        _returnButton = [BTTheme pillButtonWithTitle:@"Return to Windows" target:self action:@selector(returnToWindows)];
        _returnButton.hidden = YES;

        UIStackView *leftStack = [[UIStackView alloc] initWithArrangedSubviews:@[
            _videoSlotView,
            badgeEqRow,
            _ownerLabel,
            _reasonLabel,
            _videoFocusButton,
            _returnButton
        ]];
        leftStack.axis = UILayoutConstraintAxisVertical;
        leftStack.spacing = 5;
        leftStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_leftCard addSubview:leftStack];

        UIView *rightCard = [BTTheme cardView];
        rightCard.translatesAutoresizingMaskIntoConstraints = NO;

        _titleLabel = [BTTheme labelWithSize:18 weight:UIFontWeightBold color:[BTTheme textPrimary]];
        _titleLabel.numberOfLines = 2;
        _titleLabel.text = @"Ready when you are";

        _artistLabel = [BTTheme labelWithSize:13 weight:UIFontWeightMedium color:[BTTheme textSecondary]];
        _artistLabel.text = @"Play media on Windows or hand off URL / file";

        _seekSlider = [UISlider new];
        _seekSlider.enabled = NO;
        _seekSlider.minimumTrackTintColor = [BTTheme accentTeal];
        _seekSlider.maximumTrackTintColor = [BTTheme pillColor];
        [_seekSlider addTarget:self action:@selector(commitSeek) forControlEvents:UIControlEventTouchUpInside | UIControlEventTouchUpOutside];

        _timelineLabel = [BTTheme labelWithSize:11 weight:UIFontWeightMedium color:[BTTheme textMuted]];
        _timelineLabel.font = [UIFont monospacedDigitSystemFontOfSize:11 weight:UIFontWeightMedium];
        _timelineLabel.text = @"0:00 / 0:00";
        [_timelineLabel setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *seekRow = [[UIStackView alloc] initWithArrangedSubviews:@[_seekSlider, _timelineLabel]];
        seekRow.axis = UILayoutConstraintAxisHorizontal;
        seekRow.alignment = UIStackViewAlignmentCenter;
        seekRow.spacing = 8;

        _prevButton = [BTTheme pillButtonWithTitle:@"Prev" target:self action:@selector(prevTapped)];
        _playButton = [BTTheme primaryButtonWithTitle:@"Play" target:self action:@selector(playTapped)];
        _nextButton = [BTTheme pillButtonWithTitle:@"Next" target:self action:@selector(nextTapped)];
        _prevButton.enabled = _playButton.enabled = _nextButton.enabled = NO;

        UIStackView *transportRow = [[UIStackView alloc] initWithArrangedSubviews:@[_prevButton, _playButton, _nextButton]];
        transportRow.axis = UILayoutConstraintAxisHorizontal;
        transportRow.spacing = 8;
        transportRow.distribution = UIStackViewDistributionFillEqually;

        _outputModeSegment = [[UISegmentedControl alloc] initWithItems:@[@"Only PC", @"Only Phone", @"Both"]];
        _outputModeSegment.selectedSegmentIndex = 2;
        _outputModeSegment.tintColor = [BTTheme accentTeal];
        [_outputModeSegment setTitleTextAttributes:@{NSFontAttributeName: [UIFont systemFontOfSize:11 weight:UIFontWeightSemibold]} forState:UIControlStateNormal];
        [_outputModeSegment.heightAnchor constraintEqualToConstant:30].active = YES;
        [_outputModeSegment addTarget:self action:@selector(outputModeChanged) forControlEvents:UIControlEventValueChanged];

        _volumeLayerButton = [BTTheme pillButtonWithTitle:@"PC Vol: 80%" target:self action:@selector(cycleVolumeLayer)];
        _volumeLayerButton.titleLabel.font = [UIFont monospacedDigitSystemFontOfSize:11 weight:UIFontWeightBold];
        _volumeLayerButton.contentEdgeInsets = UIEdgeInsetsMake(4, 8, 4, 8);
        [_volumeLayerButton.heightAnchor constraintEqualToConstant:34].active = YES;
        [_volumeLayerButton.widthAnchor constraintGreaterThanOrEqualToConstant:106].active = YES;
        [_volumeLayerButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _volumeSlider = [UISlider new];
        _volumeSlider.minimumValue = 0.0f;
        _volumeSlider.maximumValue = 1.0f;
        _volumeSlider.value = 0.8f;
        _volumeSlider.minimumTrackTintColor = [BTTheme accentTeal];
        _volumeSlider.maximumTrackTintColor = [BTTheme pillColor];
        [_volumeSlider addTarget:self action:@selector(volumeSliderLiveChanged) forControlEvents:UIControlEventValueChanged];
        [_volumeSlider addTarget:self action:@selector(volumeSliderCommitted) forControlEvents:UIControlEventTouchUpInside | UIControlEventTouchUpOutside | UIControlEventTouchCancel];

        MPVolumeView *routeButtonView = [[MPVolumeView alloc] initWithFrame:CGRectZero];
        routeButtonView.showsVolumeSlider = NO;
        routeButtonView.showsRouteButton = YES;
        routeButtonView.tintColor = [BTTheme textSecondary];
        [routeButtonView.widthAnchor constraintEqualToConstant:28].active = YES;
        [routeButtonView.heightAnchor constraintEqualToConstant:28].active = YES;

        UIStackView *volumeRow = [[UIStackView alloc] initWithArrangedSubviews:@[_volumeLayerButton, _volumeSlider, routeButtonView]];
        volumeRow.axis = UILayoutConstraintAxisHorizontal;
        volumeRow.alignment = UIStackViewAlignmentCenter;
        volumeRow.spacing = 8;

        _audioWarningLabel = [BTTheme labelWithSize:10 weight:UIFontWeightSemibold color:[BTTheme accentAmber]];
        _audioWarningLabel.numberOfLines = 2;
        _audioWarningLabel.hidden = YES;

        _focusButton = [BTTheme pillButtonWithTitle:@"Focus: Auto" target:self action:@selector(cycleFocusPolicy)];
        _qualityButton = [BTTheme pillButtonWithTitle:@"Cap: Auto" target:self action:@selector(cycleQualityCap)];
        _awakeButton = [BTTheme pillButtonWithTitle:@"Awake: On" target:self action:@selector(toggleAwake)];
        UIStackView *optionsRow = [[UIStackView alloc] initWithArrangedSubviews:@[_focusButton, _qualityButton, _awakeButton]];
        optionsRow.axis = UILayoutConstraintAxisHorizontal;
        optionsRow.spacing = 6;
        optionsRow.distribution = UIStackViewDistributionFillProportionally;

        UIStackView *rightStack = [[UIStackView alloc] initWithArrangedSubviews:@[
            _titleLabel,
            _artistLabel,
            seekRow,
            transportRow,
            _outputModeSegment,
            volumeRow,
            _audioWarningLabel,
            optionsRow
        ]];
        rightStack.axis = UILayoutConstraintAxisVertical;
        rightStack.spacing = 6;
        rightStack.translatesAutoresizingMaskIntoConstraints = NO;
        [rightCard addSubview:rightStack];

        _columnsStack = [[UIStackView alloc] initWithArrangedSubviews:@[_leftCard, rightCard]];
        _columnsStack.axis = UILayoutConstraintAxisHorizontal;
        _columnsStack.alignment = UIStackViewAlignmentTop;
        _columnsStack.spacing = 10;
        _columnsStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_scrollView addSubview:_columnsStack];

        _leftWidthConstraint = [_leftCard.widthAnchor constraintEqualToConstant:224];
        NSLayoutConstraint *aspect = [_videoSlotView.heightAnchor constraintEqualToAnchor:_videoSlotView.widthAnchor multiplier:9.0 / 16.0];
        aspect.priority = UILayoutPriorityDefaultHigh;

        [NSLayoutConstraint activateConstraints:@[
            [_scrollView.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_scrollView.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_scrollView.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_scrollView.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],

            [_columnsStack.leadingAnchor constraintEqualToAnchor:_scrollView.contentLayoutGuide.leadingAnchor constant:10],
            [_columnsStack.trailingAnchor constraintEqualToAnchor:_scrollView.contentLayoutGuide.trailingAnchor constant:-10],
            [_columnsStack.topAnchor constraintEqualToAnchor:_scrollView.contentLayoutGuide.topAnchor constant:6],
            [_columnsStack.bottomAnchor constraintEqualToAnchor:_scrollView.contentLayoutGuide.bottomAnchor constant:-6],
            [_columnsStack.widthAnchor constraintEqualToAnchor:_scrollView.frameLayoutGuide.widthAnchor constant:-20],

            _leftWidthConstraint,
            aspect,
            [_artworkView.leadingAnchor constraintEqualToAnchor:_videoSlotView.leadingAnchor],
            [_artworkView.trailingAnchor constraintEqualToAnchor:_videoSlotView.trailingAnchor],
            [_artworkView.topAnchor constraintEqualToAnchor:_videoSlotView.topAnchor],
            [_artworkView.bottomAnchor constraintEqualToAnchor:_videoSlotView.bottomAnchor],

            [leftStack.leadingAnchor constraintEqualToAnchor:_leftCard.leadingAnchor constant:10],
            [leftStack.trailingAnchor constraintEqualToAnchor:_leftCard.trailingAnchor constant:-10],
            [leftStack.topAnchor constraintEqualToAnchor:_leftCard.topAnchor constant:8],
            [leftStack.bottomAnchor constraintEqualToAnchor:_leftCard.bottomAnchor constant:-8],

            [rightStack.leadingAnchor constraintEqualToAnchor:rightCard.leadingAnchor constant:12],
            [rightStack.trailingAnchor constraintEqualToAnchor:rightCard.trailingAnchor constant:-12],
            [rightStack.topAnchor constraintEqualToAnchor:rightCard.topAnchor constant:8],
            [rightStack.bottomAnchor constraintEqualToAnchor:rightCard.bottomAnchor constant:-8]
        ]];

        [self renderAudioControlsFromAck];
    }
    return self;
}

- (void)layoutSubviews {
    [super layoutSubviews];
    BOOL landscape = self.bounds.size.width >= self.bounds.size.height;
    UILayoutConstraintAxis targetAxis = landscape ? UILayoutConstraintAxisHorizontal : UILayoutConstraintAxisVertical;
    if (_columnsStack.axis != targetAxis) {
        _columnsStack.axis = targetAxis;
        _columnsStack.alignment = landscape ? UIStackViewAlignmentTop : UIStackViewAlignmentFill;
        _leftWidthConstraint.active = landscape;
    }
}

- (void)playTapped {
    [BTTheme pulseView:_playButton accent:[BTTheme accentTeal]];
    if (self.onPlayPauseTapped) self.onPlayPauseTapped();
}
- (void)prevTapped { if (self.onPreviousTapped) self.onPreviousTapped(); }
- (void)nextTapped { if (self.onNextTapped) self.onNextTapped(); }
- (void)commitSeek { if (self.onSeekCommitted) self.onSeekCommitted(_seekSlider.value); }
- (void)returnToWindows { if (self.onReturnToWindowsTapped) self.onReturnToWindowsTapped(); }
- (void)openVideoFocus { if (self.onOpenVideoFocusTapped) self.onOpenVideoFocusTapped(); }

- (void)outputModeChanged {
    _userSelectedVolumeLayer = NO;
    NSString *mode = @"Both";
    if (_outputModeSegment.selectedSegmentIndex == 0) mode = @"PcOnly";
    else if (_outputModeSegment.selectedSegmentIndex == 1) mode = @"PhoneOnly";
    [BTTheme pulseView:_outputModeSegment accent:[BTTheme accentTeal]];
    if (self.onAudioOutputModeSelected) self.onAudioOutputModeSelected(mode);
}

- (void)cycleVolumeLayer {
    _userSelectedVolumeLayer = YES;
    if ([_selectedVolumeLayer isEqualToString:@"pc"]) _selectedVolumeLayer = @"app";
    else if ([_selectedVolumeLayer isEqualToString:@"app"]) _selectedVolumeLayer = @"player";
    else _selectedVolumeLayer = @"pc";
    [self renderAudioControlsFromAck];
}

- (float)acknowledgedScalarForLayer:(NSString *)layer {
    if ([layer isEqualToString:@"app"]) {
        return [_audioControlState[@"phoneAppGain"] isKindOfClass:NSNumber.class]
            ? [_audioControlState[@"phoneAppGain"] floatValue] : 1.0f;
    }
    if ([layer isEqualToString:@"player"]) {
        return [_audioControlState[@"phonePlayerVolume"] isKindOfClass:NSNumber.class]
            ? [_audioControlState[@"phonePlayerVolume"] floatValue] : 1.0f;
    }
    return [_audioControlState[@"pcVolumeScalar"] isKindOfClass:NSNumber.class]
        ? [_audioControlState[@"pcVolumeScalar"] floatValue] : 1.0f;
}

- (NSString *)displayTitleForLayer:(NSString *)layer percent:(float)scalar {
    long pct = (long)llroundf(fmaxf(0.0f, fminf(1.0f, scalar)) * 100.0f);
    if ([layer isEqualToString:@"app"]) return [NSString stringWithFormat:@"App Gain: %ld%%", pct];
    if ([layer isEqualToString:@"player"]) return [NSString stringWithFormat:@"AVPlayer: %ld%%", pct];
    return [NSString stringWithFormat:@"PC Vol: %ld%%", pct];
}

- (void)volumeSliderLiveChanged {
    [_volumeLayerButton setTitle:[self displayTitleForLayer:_selectedVolumeLayer percent:_volumeSlider.value]
                        forState:UIControlStateNormal];
    CFTimeInterval now = CACurrentMediaTime();
    if (now - _lastVolumeSendTime >= 0.12) {
        _lastVolumeSendTime = now;
        if (self.onVolumeChanged) self.onVolumeChanged(_volumeSlider.value, _selectedVolumeLayer);
    }
}

- (void)volumeSliderCommitted {
    _lastVolumeSendTime = CACurrentMediaTime();
    [_volumeLayerButton setTitle:[self displayTitleForLayer:_selectedVolumeLayer percent:_volumeSlider.value]
                        forState:UIControlStateNormal];
    if (self.onVolumeChanged) self.onVolumeChanged(_volumeSlider.value, _selectedVolumeLayer);
}

- (void)updateWithAudioControlState:(NSDictionary *)audioControlState {
    if (![audioControlState isKindOfClass:NSDictionary.class]) return;
    _audioControlState = [audioControlState copy];
    if (!_userSelectedVolumeLayer) {
        NSString *ackedLayer = BTNowStr(audioControlState[@"activeVolumeLayer"], @"pc");
        if ([ackedLayer isEqualToString:@"pc"] || [ackedLayer isEqualToString:@"app"] || [ackedLayer isEqualToString:@"player"]) {
            _selectedVolumeLayer = ackedLayer;
        }
    }
    [self renderAudioControlsFromAck];
}

- (void)renderAudioControlsFromAck {
    NSString *mode = BTNowStr(_audioControlState[@"mode"], @"Both");
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
    [_volumeLayerButton setTitle:[self displayTitleForLayer:_selectedVolumeLayer percent:_volumeSlider.tracking ? _volumeSlider.value : acked]
                        forState:UIControlStateNormal];

    NSString *warning = BTNowStr(_audioControlState[@"warning"], @"");
    NSString *capStatus = BTNowStr(_audioControlState[@"captureSignalStatus"], @"");
    if (warning.length == 0 && [capStatus isEqualToString:@"silent-driver-coupled"]) {
        warning = @"Driver silences loopback capture while PC speaker is muted; use Both if needed.";
    }
    _audioWarningLabel.text = warning;
    _audioWarningLabel.hidden = (warning.length == 0);
}

- (void)cycleFocusPolicy {
    NSArray<NSString *> *policies = @[@"Auto", @"DuckVideo", @"KeepVideoMuteAudio", @"LivePcmOnly", @"IndependentOnly"];
    NSUInteger idx = [policies indexOfObject:_currentFocusPolicy];
    NSString *next = (idx == NSNotFound) ? @"Auto" : policies[(idx + 1) % policies.count];
    _currentFocusPolicy = next;
    [self updateOptionTitles];
    if (self.onFocusPolicySelected) self.onFocusPolicySelected(next);
}

- (void)cycleQualityCap {
    NSArray<NSString *> *caps = @[@"auto", @"720p", @"480p", @"audio-only"];
    NSUInteger idx = [caps indexOfObject:_currentQualityCap];
    NSString *next = (idx == NSNotFound) ? @"auto" : caps[(idx + 1) % caps.count];
    _currentQualityCap = next;
    [self updateOptionTitles];
    if (self.onQualityCapSelected) self.onQualityCapSelected(next);
}

- (void)toggleAwake {
    _keepAwake = !_keepAwake;
    [self updateOptionTitles];
    if (self.onKeepAwakeToggled) self.onKeepAwakeToggled(_keepAwake);
}

- (void)updateOptionTitles {
    NSString *focusShort = _currentFocusPolicy;
    if ([_currentFocusPolicy isEqualToString:@"DuckVideo"]) focusShort = @"Duck";
    else if ([_currentFocusPolicy isEqualToString:@"KeepVideoMuteAudio"]) focusShort = @"Mute";
    else if ([_currentFocusPolicy isEqualToString:@"LivePcmOnly"]) focusShort = @"PCM";
    else if ([_currentFocusPolicy isEqualToString:@"IndependentOnly"]) focusShort = @"iPhone";
    [_focusButton setTitle:[NSString stringWithFormat:@"Focus: %@", focusShort] forState:UIControlStateNormal];

    NSString *qShort = [_currentQualityCap isEqualToString:@"audio-only"] ? @"Audio" : _currentQualityCap.capitalizedString;
    [_qualityButton setTitle:[NSString stringWithFormat:@"Cap: %@", qShort] forState:UIControlStateNormal];
    [_awakeButton setTitle:_keepAwake ? @"Awake: On" : @"Awake: Off" forState:UIControlStateNormal];
}

- (void)updateWithHostMediaState:(NSDictionary *)hostState
                   playerSnapshot:(NSDictionary *)playerSnapshot
                     embedActive:(BOOL)embedActive
                     focusPolicy:(NSString *)focusPolicy {
    if (focusPolicy.length) _currentFocusPolicy = focusPolicy;
    BOOL independent = [playerSnapshot[@"independentActive"] boolValue];
    BOOL transferring = [BTNowStr(playerSnapshot[@"stage"], @"") isEqualToString:@"connecting"]
        || [BTNowStr(playerSnapshot[@"stage"], @"") isEqualToString:@"buffering"];

    if (independent || transferring || embedActive) {
        NSString *mode = embedActive ? @"DirectInternet" : BTNowStr(playerSnapshot[@"mode"], @"DirectInternet");
        NSString *stage = embedActive ? @"confirmed" : BTNowStr(playerSnapshot[@"stage"], @"idle");
        NSString *owner = (independent || embedActive) ? @"iPhoneOwned" : @"TransferPending";
        NSString *title = BTNowStr(playerSnapshot[@"title"], @"External PiP Media");
        NSString *artist = BTNowStr(playerSnapshot[@"artist"], @"Playing independently on iPhone");
        BOOL isPlaying = [playerSnapshot[@"isPlaying"] boolValue];
        double pos = BTNowNum(playerSnapshot[@"positionSeconds"]);
        double dur = BTNowNum(playerSnapshot[@"durationSeconds"]);
        double buf = BTNowNum(playerSnapshot[@"bufferedSeconds"]);
        BOOL hasVideo = [playerSnapshot[@"readyForDisplay"] boolValue] || [playerSnapshot[@"videoWidth"] intValue] > 0;
        NSString *qCap = BTNowStr(playerSnapshot[@"qualityCap"], @"auto");
        _currentQualityCap = qCap;
        if (playerSnapshot[@"keepScreenAwake"]) {
            _keepAwake = [playerSnapshot[@"keepScreenAwake"] boolValue];
        }

        _modeBadge.text = [NSString stringWithFormat:@" %@ ", [BTTheme displayModeName:mode]];
        UIColor *accent = [mode isEqualToString:@"BufferedUsb"] ? [BTTheme accentAmber] : [BTTheme accentTeal];
        [BTTheme styleChipLabel:_modeBadge accent:accent];
        _equalizerView.barColor = accent;
        _equalizerView.playing = isPlaying || embedActive;

        _ownerLabel.text = embedActive ? @"YouTube nocookie Embed Active" : [BTTheme displayOwnerName:owner stage:stage];
        _reasonLabel.text = embedActive
            ? @"Official youtube-nocookie iframe; tap Return to resume Windows"
            : [NSString stringWithFormat:@"%@ · Buffered %.0fs", mode, buf];

        _titleLabel.text = title.length ? title : @"External PiP Media";
        _artistLabel.text = artist.length ? artist : @"Playing on iPhone";

        [_playButton setTitle:isPlaying ? @"Pause" : @"Play" forState:UIControlStateNormal];
        _playButton.enabled = !embedActive;
        _prevButton.enabled = NO;
        _nextButton.enabled = NO;

        double maxDur = MAX(1.0, dur);
        if (!_seekSlider.tracking) {
            _seekSlider.maximumValue = (float)maxDur;
            _seekSlider.value = (float)MIN(maxDur, MAX(0.0, pos));
        }
        _seekSlider.enabled = !embedActive && dur > 0.5;
        _timelineLabel.text = [NSString stringWithFormat:@"%@ / %@", [BTTheme formatDuration:pos], [BTTheme formatDuration:dur]];
        _videoFocusButton.hidden = !(independent && hasVideo);
        _returnButton.hidden = NO;
        _artworkView.hidden = independent && hasVideo;
    } else {
        NSDictionary *session = [hostState[@"session"] isKindOfClass:NSDictionary.class] ? hostState[@"session"] : @{};
        NSString *mode = BTNowStr(hostState[@"mode"], @"LivePcm");
        NSString *owner = BTNowStr(hostState[@"owner"], @"WindowsOwned");
        NSString *provider = BTNowStr(hostState[@"provider"], BTNowStr(session[@"source"], @"windows-smtc"));
        NSString *stage = BTNowStr(playerSnapshot[@"stage"], @"idle");
        NSString *err = BTNowStr(playerSnapshot[@"error"], @"");

        UIColor *accent = [BTTheme accentBlue];
        if ([mode isEqualToString:@"LivePcm"]) accent = [BTTheme accentGreen];
        else if ([mode isEqualToString:@"RemoteControl"]) accent = [BTTheme accentAmber];
        _modeBadge.text = [NSString stringWithFormat:@" %@ ", [BTTheme displayModeName:mode]];
        [BTTheme styleChipLabel:_modeBadge accent:accent];

        _ownerLabel.text = [BTTheme displayOwnerName:owner stage:stage];
        if (err.length > 0 && [stage isEqualToString:@"failed"]) {
            _reasonLabel.text = err;
        } else if ([provider isEqualToString:@"spotify"] || [BTNowStr(session[@"source"], @"") isEqualToString:@"spotify.webapi"]) {
            _reasonLabel.text = @"Spotify DRM streams remain on Windows (Mode D + Live PCM)";
        } else {
            _reasonLabel.text = [NSString stringWithFormat:@"Provider: %@", provider.length ? provider : @"Windows SMTC"];
        }

        NSString *title = BTNowStr(session[@"title"], @"");
        NSString *artist = BTNowStr(session[@"artist"], @"Play media on Windows or start PiP handoff");
        NSString *source = BTNowStr(session[@"source"], @"");
        _titleLabel.text = title.length ? title : @"Ready when you are";
        _artistLabel.text = source.length && artist.length ? [NSString stringWithFormat:@"%@ · %@", artist, source] : artist;

        BOOL playing = [BTNowStr(session[@"state"], @"") isEqualToString:@"Playing"];
        _equalizerView.barColor = accent;
        _equalizerView.playing = playing;

        [_playButton setTitle:playing ? @"Pause" : @"Play" forState:UIControlStateNormal];
        _playButton.enabled = session.count > 0;
        _nextButton.enabled = BTNowNum(session[@"canNext"]) != 0;
        _prevButton.enabled = BTNowNum(session[@"canPrevious"]) != 0;

        double pos = BTNowNum(session[@"positionSeconds"]);
        double dur = BTNowNum(session[@"durationSeconds"]);
        if (!_seekSlider.tracking) {
            _seekSlider.maximumValue = (float)MAX(1.0, dur);
            _seekSlider.value = (float)MIN(MAX(1.0, dur), MAX(0.0, pos));
        }
        _seekSlider.enabled = BTNowNum(session[@"canSeek"]) != 0;
        _timelineLabel.text = [NSString stringWithFormat:@"%@ / %@", [BTTheme formatDuration:pos], [BTTheme formatDuration:dur]];
        _videoFocusButton.hidden = YES;
        _returnButton.hidden = YES;
        _artworkView.hidden = NO;

        NSString *artB64 = session[@"artworkBase64"];
        [self updateArtworkWithBase64:artB64];
    }

    [self updateOptionTitles];
}

- (void)updateArtworkWithBase64:(id)artValue {
    if (![artValue isKindOfClass:NSString.class] || [artValue length] == 0 || [artValue length] > 32000) {
        _artworkKey = nil;
        _artworkView.image = nil;
        return;
    }
    NSString *art = (NSString *)artValue;
    if ([_artworkKey isEqualToString:art]) return;
    _artworkKey = [art copy];
    NSString *keySnapshot = _artworkKey;

    __weak typeof(self) weakSelf = self;
    dispatch_async(_artQueue, ^{
        NSData *data = [[NSData alloc] initWithBase64EncodedString:keySnapshot options:0];
        UIImage *decoded = nil;
        if (data) {
            CGImageSourceRef source = CGImageSourceCreateWithData((__bridge CFDataRef)data, NULL);
            if (source) {
                NSDictionary *options = @{
                    (id)kCGImageSourceCreateThumbnailFromImageAlways: @YES,
                    (id)kCGImageSourceThumbnailMaxPixelSize: @128,
                    (id)kCGImageSourceCreateThumbnailWithTransform: @YES
                };
                CGImageRef cgImg = CGImageSourceCreateThumbnailAtIndex(source, 0, (__bridge CFDictionaryRef)options);
                if (cgImg) {
                    decoded = [UIImage imageWithCGImage:cgImg];
                    CGImageRelease(cgImg);
                }
                CFRelease(source);
            }
        }
        dispatch_async(dispatch_get_main_queue(), ^{
            BTNowPlayingView *strong = weakSelf;
            if (!strong || ![strong->_artworkKey isEqualToString:keySnapshot]) return;
            strong->_artworkView.image = decoded;
        });
    });
}

- (void)clearArtworkCache {
    _artworkKey = nil;
    _artworkView.image = nil;
}

@end
