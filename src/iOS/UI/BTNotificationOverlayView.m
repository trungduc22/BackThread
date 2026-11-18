#import "BTNotificationOverlayView.h"
#import "BTTheme.h"
#import <ImageIO/ImageIO.h>

static NSString *BTNotifStr(id val, NSString *fallback) {
    return [val isKindOfClass:NSString.class] ? val : fallback;
}

@implementation BTNotificationOverlayView {
    UIView *_backdrop;
    UIView *_card;
    UIView *_beaconRail;
    UIImageView *_iconView;
    UILabel *_metaLabel;
    UILabel *_priorityBadge;
    UILabel *_styleBadge;
    UILabel *_titleLabel;
    UITextView *_bodyTextView;
    UIButton *_dismissButton;
    UIView *_progressTrack;
    UIView *_progressFill;
    NSLayoutConstraint *_cardTop;
    NSLayoutConstraint *_cardMaxWidth;
    NSLayoutConstraint *_iconWidth;
    NSLayoutConstraint *_beaconRailWidth;
    NSLayoutConstraint *_progressFractionWidth;
    NSTimer *_autoDismissTimer;
    dispatch_queue_t _iconQueue;
    NSString *_activeIconKey;
    NSInteger _currentPriority;
    BOOL _fullscreenActive;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.hidden = YES;
        self.backgroundColor = [UIColor clearColor];
        _iconQueue = dispatch_queue_create("app.backthread.notif.icon", DISPATCH_QUEUE_SERIAL);

        // Backdrop kept disabled: all notification priorities render strictly as a top bar.
        _backdrop = [UIView new];
        _backdrop.backgroundColor = [UIColor clearColor];
        _backdrop.alpha = 0.0;
        _backdrop.hidden = YES;
        _backdrop.userInteractionEnabled = NO;
        _backdrop.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_backdrop];

        _card = [UIView new];
        _card.backgroundColor = [BTTheme surfaceColor];
        _card.layer.cornerRadius = 14;
        _card.layer.borderWidth = 1.25;
        _card.layer.borderColor = [BTTheme hairlineColor].CGColor;
        _card.clipsToBounds = YES;
        _card.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_card];

        _beaconRail = [UIView new];
        _beaconRail.backgroundColor = [BTTheme accentTeal];
        _beaconRail.translatesAutoresizingMaskIntoConstraints = NO;
        [_card addSubview:_beaconRail];
        _beaconRailWidth = [_beaconRail.widthAnchor constraintEqualToConstant:0];

        _iconView = [UIImageView new];
        _iconView.contentMode = UIViewContentModeScaleAspectFit;
        _iconView.layer.cornerRadius = 7;
        _iconView.clipsToBounds = YES;
        _iconView.backgroundColor = [BTTheme elevatedColor];
        _iconView.translatesAutoresizingMaskIntoConstraints = NO;
        _iconWidth = [_iconView.widthAnchor constraintEqualToConstant:32];
        [_iconView.heightAnchor constraintEqualToAnchor:_iconView.widthAnchor].active = YES;

        _metaLabel = [BTTheme labelWithSize:11 weight:UIFontWeightSemibold color:[BTTheme textSecondary]];
        _priorityBadge = [BTTheme labelWithSize:10 weight:UIFontWeightBold color:[BTTheme accentRed]];
        [_priorityBadge.heightAnchor constraintEqualToConstant:19].active = YES;
        [_priorityBadge setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        _styleBadge = [BTTheme labelWithSize:9 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        [_styleBadge.heightAnchor constraintEqualToConstant:18].active = YES;
        [_styleBadge setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];
        _styleBadge.hidden = YES;

        _dismissButton = [BTTheme pillButtonWithTitle:@"Dismiss" target:self action:@selector(dismissOverlay)];
        _dismissButton.titleLabel.font = [UIFont systemFontOfSize:11 weight:UIFontWeightBold];
        _dismissButton.contentEdgeInsets = UIEdgeInsetsMake(4, 10, 4, 10);
        [_dismissButton.heightAnchor constraintEqualToConstant:30].active = YES;
        [_dismissButton setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIView *spacer = [UIView new];
        [spacer setContentHuggingPriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *metaRow = [[UIStackView alloc] initWithArrangedSubviews:@[
            _iconView, _metaLabel, _priorityBadge, _styleBadge, spacer, _dismissButton
        ]];
        metaRow.axis = UILayoutConstraintAxisHorizontal;
        metaRow.alignment = UIStackViewAlignmentCenter;
        metaRow.spacing = 6;

        _titleLabel = [BTTheme labelWithSize:15 weight:UIFontWeightBold color:[BTTheme textPrimary]];
        _titleLabel.numberOfLines = 1;
        _titleLabel.lineBreakMode = NSLineBreakByTruncatingTail;

        _bodyTextView = [UITextView new];
        _bodyTextView.backgroundColor = [UIColor clearColor];
        _bodyTextView.textColor = [BTTheme textSecondary];
        _bodyTextView.font = [UIFont systemFontOfSize:13 weight:UIFontWeightRegular];
        _bodyTextView.editable = NO;
        _bodyTextView.selectable = NO;
        _bodyTextView.scrollEnabled = YES;
        _bodyTextView.textContainerInset = UIEdgeInsetsZero;
        _bodyTextView.textContainer.lineFragmentPadding = 0;

        UIStackView *cardStack = [[UIStackView alloc] initWithArrangedSubviews:@[metaRow, _titleLabel, _bodyTextView]];
        cardStack.axis = UILayoutConstraintAxisVertical;
        cardStack.spacing = 4;
        cardStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_card addSubview:cardStack];

        _progressTrack = [UIView new];
        _progressTrack.backgroundColor = [UIColor colorWithWhite:1.0 alpha:0.09];
        _progressTrack.translatesAutoresizingMaskIntoConstraints = NO;
        [_card addSubview:_progressTrack];

        _progressFill = [UIView new];
        _progressFill.backgroundColor = [BTTheme accentTeal];
        _progressFill.translatesAutoresizingMaskIntoConstraints = NO;
        [_progressTrack addSubview:_progressFill];

        _cardTop = [_card.topAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.topAnchor constant:6];
        _cardMaxWidth = [_card.widthAnchor constraintLessThanOrEqualToConstant:580];
        NSLayoutConstraint *fillWidth = [_card.widthAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.widthAnchor constant:-20];
        fillWidth.priority = UILayoutPriorityDefaultHigh;

        NSLayoutConstraint *bodyHeight = [_bodyTextView.heightAnchor constraintLessThanOrEqualToConstant:44];
        bodyHeight.priority = UILayoutPriorityDefaultHigh;

        _progressFractionWidth = [_progressFill.widthAnchor constraintEqualToAnchor:_progressTrack.widthAnchor multiplier:1.0];

        [NSLayoutConstraint activateConstraints:@[
            [_backdrop.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_backdrop.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_backdrop.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_backdrop.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],

            [_card.centerXAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.centerXAnchor],
            _cardMaxWidth,
            fillWidth,
            _cardTop,
            _iconWidth,
            bodyHeight,

            [_beaconRail.leadingAnchor constraintEqualToAnchor:_card.leadingAnchor],
            [_beaconRail.topAnchor constraintEqualToAnchor:_card.topAnchor],
            [_beaconRail.bottomAnchor constraintEqualToAnchor:_card.bottomAnchor],
            _beaconRailWidth,

            [cardStack.leadingAnchor constraintEqualToAnchor:_beaconRail.trailingAnchor constant:12],
            [cardStack.trailingAnchor constraintEqualToAnchor:_card.trailingAnchor constant:-12],
            [cardStack.topAnchor constraintEqualToAnchor:_card.topAnchor constant:8],
            [cardStack.bottomAnchor constraintEqualToAnchor:_progressTrack.topAnchor constant:-7],

            [_progressTrack.leadingAnchor constraintEqualToAnchor:_beaconRail.trailingAnchor],
            [_progressTrack.trailingAnchor constraintEqualToAnchor:_card.trailingAnchor],
            [_progressTrack.bottomAnchor constraintEqualToAnchor:_card.bottomAnchor],
            [_progressTrack.heightAnchor constraintEqualToConstant:3],

            [_progressFill.leadingAnchor constraintEqualToAnchor:_progressTrack.leadingAnchor],
            [_progressFill.topAnchor constraintEqualToAnchor:_progressTrack.topAnchor],
            [_progressFill.bottomAnchor constraintEqualToAnchor:_progressTrack.bottomAnchor],
            _progressFractionWidth
        ]];

        UISwipeGestureRecognizer *swipe = [[UISwipeGestureRecognizer alloc] initWithTarget:self action:@selector(dismissOverlay)];
        swipe.direction = UISwipeGestureRecognizerDirectionUp;
        [_card addGestureRecognizer:swipe];
    }
    return self;
}

- (BOOL)fullscreenActive {
    // Fullscreen notifications are permanently disabled by requirement: always return NO.
    return NO;
}

- (BOOL)pointInside:(CGPoint)point withEvent:(UIEvent *)event {
    if (self.hidden) return NO;
    CGPoint inCard = [self convertPoint:point toView:_card];
    return [_card pointInside:inCard withEvent:event];
}

- (void)presentNotification:(NSDictionary *)event expiresInterval:(NSTimeInterval)expiresInSeconds {
    NSInteger priority = [event[@"priority"] integerValue];
    if (priority <= 0) return;

    [_autoDismissTimer invalidate];
    _autoDismissTimer = nil;

    _currentPriority = priority;
    _fullscreenActive = NO;
    _backdrop.alpha = 0.0;
    _backdrop.hidden = YES;

    NSString *appName = BTNotifStr(event[@"appName"], @"");
    NSString *source = BTNotifStr(event[@"source"], @"Windows");
    NSString *category = BTNotifStr(event[@"category"], @"");
    NSString *title = BTNotifStr(event[@"title"], @"Notification");
    NSString *body = BTNotifStr(event[@"body"], @"");
    NSString *iconB64 = BTNotifStr(event[@"iconBase64"], @"");
    NSString *rawStyle = BTNotifStr(event[@"style"], @"");
    NSString *style = rawStyle.length ? rawStyle.lowercaseString : (priority >= 3 ? @"beacon" : @"standard");

    NSMutableArray<NSString *> *metaParts = [NSMutableArray new];
    if (appName.length) [metaParts addObject:appName];
    else if (source.length) [metaParts addObject:source];
    if (category.length) [metaParts addObject:category.capitalizedString];
    _metaLabel.text = metaParts.count ? [metaParts componentsJoinedByString:@" · "] : @"Windows";

    BOOL hasIcon = iconB64.length > 0 && iconB64.length <= 24000;
    UIColor *accent;
    BOOL isBeacon = [style isEqualToString:@"beacon"] || priority >= 3;
    BOOL isSpotlight = [style isEqualToString:@"spotlight"];

    if (priority == 1) {
        _cardMaxWidth.constant = 480;
        accent = isSpotlight ? [BTTheme accentTeal] : [BTTheme accentBlue];
        _priorityBadge.text = @" NOTICE ";
        [BTTheme styleChipLabel:_priorityBadge accent:accent];
        _titleLabel.font = [UIFont systemFontOfSize:14 weight:UIFontWeightSemibold];
        _titleLabel.text = title;
        _bodyTextView.hidden = body.length == 0;
        _bodyTextView.font = [UIFont systemFontOfSize:12 weight:UIFontWeightRegular];
        _bodyTextView.text = body;
        _dismissButton.hidden = YES;
        _iconView.hidden = !hasIcon;
        _iconWidth.constant = hasIcon ? 24 : 0;
        _card.backgroundColor = [BTTheme surfaceColor];
    } else if (priority == 2) {
        _cardMaxWidth.constant = 540;
        accent = isSpotlight ? [BTTheme accentTeal] : [BTTheme accentAmber];
        _priorityBadge.text = @" PRIORITY ";
        [BTTheme styleChipLabel:_priorityBadge accent:accent];
        _titleLabel.font = [UIFont systemFontOfSize:15 weight:UIFontWeightBold];
        _titleLabel.text = title;
        _bodyTextView.hidden = body.length == 0;
        _bodyTextView.font = [UIFont systemFontOfSize:13 weight:UIFontWeightRegular];
        _bodyTextView.text = body;
        _dismissButton.hidden = NO;
        _iconView.hidden = !hasIcon;
        _iconWidth.constant = hasIcon ? 30 : 0;
        _card.backgroundColor = isSpotlight
            ? [UIColor colorWithRed:0.065 green:0.130 blue:0.160 alpha:0.98]
            : [BTTheme surfaceColor];
    } else {
        // Priority 3 renders as a prominent Beacon Top Bar rather than taking over the screen.
        _cardMaxWidth.constant = 600;
        accent = [BTTheme accentRed];
        _priorityBadge.text = @" CRITICAL BAR ";
        [BTTheme styleChipLabel:_priorityBadge accent:accent];
        _titleLabel.font = [UIFont systemFontOfSize:16 weight:UIFontWeightBold];
        _titleLabel.text = title;
        _bodyTextView.hidden = NO;
        _bodyTextView.font = [UIFont systemFontOfSize:13 weight:UIFontWeightMedium];
        _bodyTextView.text = body.length ? body : @"Check Windows host notification";
        _dismissButton.hidden = NO;
        _iconView.hidden = !hasIcon;
        _iconWidth.constant = hasIcon ? 34 : 0;
        _card.backgroundColor = [UIColor colorWithRed:0.145 green:0.065 blue:0.085 alpha:0.98];
    }

    if (isBeacon) {
        _beaconRailWidth.constant = 5.0;
        _beaconRail.backgroundColor = accent;
        _styleBadge.hidden = NO;
        _styleBadge.text = @" BEACON ";
        [BTTheme styleChipLabel:_styleBadge accent:accent];
    } else if (isSpotlight) {
        _beaconRailWidth.constant = 4.0;
        _beaconRail.backgroundColor = [BTTheme accentTeal];
        _styleBadge.hidden = NO;
        _styleBadge.text = @" SPOTLIGHT ";
        [BTTheme styleChipLabel:_styleBadge accent:[BTTheme accentTeal]];
    } else {
        _beaconRailWidth.constant = 2.0;
        _beaconRail.backgroundColor = accent;
        _styleBadge.hidden = YES;
    }

    _card.layer.borderColor = accent.CGColor;
    _progressFill.backgroundColor = accent;

    if (hasIcon) {
        [self loadIconBase64:iconB64];
    } else {
        _activeIconKey = nil;
        _iconView.image = nil;
    }

    NSTimeInterval defaultDur = priority >= 3 ? 12.0 : (priority == 2 ? 7.0 : 3.5);
    NSTimeInterval timeout = MIN(defaultDur, MAX(0.8, expiresInSeconds));

    [_progressFill.layer removeAllAnimations];
    _progressFractionWidth.active = NO;
    _progressFractionWidth = [_progressFill.widthAnchor constraintEqualToAnchor:_progressTrack.widthAnchor multiplier:1.0];
    _progressFractionWidth.active = YES;

    self.hidden = NO;
    [self.superview bringSubviewToFront:self];
    [self layoutIfNeeded];

    BOOL reduceMotion = [BTTheme isReduceMotionEnabled];
    if (reduceMotion) {
        _card.transform = CGAffineTransformIdentity;
        _card.alpha = 1.0;
    } else {
        _card.transform = CGAffineTransformMakeTranslation(0, -18.0);
        _card.alpha = 0.0;
        [UIView animateWithDuration:0.24 delay:0 usingSpringWithDamping:0.86 initialSpringVelocity:0.4 options:UIViewAnimationOptionAllowUserInteraction animations:^{
            self->_card.transform = CGAffineTransformIdentity;
            self->_card.alpha = 1.0;
        } completion:^(BOOL finished) {
            (void)finished;
            if (isBeacon || isSpotlight) {
                [BTTheme pulseView:self->_card accent:accent];
            }
        }];
    }

    _progressFractionWidth.active = NO;
    _progressFractionWidth = [_progressFill.widthAnchor constraintEqualToAnchor:_progressTrack.widthAnchor multiplier:0.001];
    _progressFractionWidth.active = YES;
    if (!reduceMotion) {
        [UIView animateWithDuration:timeout delay:0 options:UIViewAnimationOptionCurveLinear | UIViewAnimationOptionAllowUserInteraction animations:^{
            [self->_progressTrack layoutIfNeeded];
        } completion:nil];
    }

    __weak typeof(self) weakSelf = self;
    _autoDismissTimer = [NSTimer scheduledTimerWithTimeInterval:timeout repeats:NO block:^(NSTimer *timer) {
        (void)timer;
        [weakSelf dismissOverlay];
    }];
}

- (void)loadIconBase64:(NSString *)iconB64 {
    if ([_activeIconKey isEqualToString:iconB64]) return;
    _activeIconKey = [iconB64 copy];
    NSString *keyCopy = _activeIconKey;
    __weak typeof(self) weakSelf = self;
    dispatch_async(_iconQueue, ^{
        NSData *data = [[NSData alloc] initWithBase64EncodedString:keyCopy options:0];
        UIImage *img = nil;
        if (data) {
            CGImageSourceRef src = CGImageSourceCreateWithData((__bridge CFDataRef)data, NULL);
            if (src) {
                NSDictionary *opts = @{
                    (id)kCGImageSourceCreateThumbnailFromImageAlways: @YES,
                    (id)kCGImageSourceThumbnailMaxPixelSize: @96,
                    (id)kCGImageSourceCreateThumbnailWithTransform: @YES
                };
                CGImageRef cg = CGImageSourceCreateThumbnailAtIndex(src, 0, (__bridge CFDictionaryRef)opts);
                if (cg) {
                    img = [UIImage imageWithCGImage:cg];
                    CGImageRelease(cg);
                }
                CFRelease(src);
            }
        }
        dispatch_async(dispatch_get_main_queue(), ^{
            BTNotificationOverlayView *strong = weakSelf;
            if (!strong || ![strong->_activeIconKey isEqualToString:keyCopy]) return;
            strong->_iconView.image = img;
        });
    });
}

- (void)dismissOverlay {
    [_autoDismissTimer invalidate];
    _autoDismissTimer = nil;
    [_progressFill.layer removeAllAnimations];
    NSInteger dismissedPriority = _currentPriority;
    _fullscreenActive = NO;
    self.hidden = YES;
    if (self.onDismissed) self.onDismissed(dismissedPriority);
}

- (void)clearIconCache {
    _activeIconKey = nil;
    _iconView.image = nil;
}

- (void)dealloc {
    [_autoDismissTimer invalidate];
}

@end

@implementation BTHistoryPanelView {
    UIScrollView *_scroll;
    UIStackView *_listStack;
    NSArray<NSDictionary *> *_events;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [BTTheme canvasColor];
        _events = @[];

        UILabel *header = [BTTheme labelWithSize:15 weight:UIFontWeightBold color:[BTTheme textPrimary]];
        header.text = @"Notification History";
        UIButton *clearBtn = [BTTheme pillButtonWithTitle:@"Clear All" target:self action:@selector(clearTapped)];

        UIStackView *top = [[UIStackView alloc] initWithArrangedSubviews:@[header, clearBtn]];
        top.axis = UILayoutConstraintAxisHorizontal;
        top.distribution = UIStackViewDistributionEqualSpacing;
        top.alignment = UIStackViewAlignmentCenter;
        top.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:top];

        _scroll = [UIScrollView new];
        _scroll.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_scroll];

        _listStack = [UIStackView new];
        _listStack.axis = UILayoutConstraintAxisVertical;
        _listStack.spacing = 8;
        _listStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_scroll addSubview:_listStack];

        [NSLayoutConstraint activateConstraints:@[
            [top.leadingAnchor constraintEqualToAnchor:self.leadingAnchor constant:12],
            [top.trailingAnchor constraintEqualToAnchor:self.trailingAnchor constant:-12],
            [top.topAnchor constraintEqualToAnchor:self.topAnchor constant:8],

            [_scroll.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_scroll.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_scroll.topAnchor constraintEqualToAnchor:top.bottomAnchor constant:6],
            [_scroll.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],

            [_listStack.leadingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.leadingAnchor constant:12],
            [_listStack.trailingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.trailingAnchor constant:-12],
            [_listStack.topAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.topAnchor constant:4],
            [_listStack.bottomAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.bottomAnchor constant:-10],
            [_listStack.widthAnchor constraintEqualToAnchor:_scroll.frameLayoutGuide.widthAnchor constant:-24]
        ]];
        [self updateWithEvents:@[]];
    }
    return self;
}

- (void)clearTapped {
    if (self.onClearTapped) self.onClearTapped();
}

- (void)replayTapped:(UIButton *)btn {
    NSInteger idx = btn.tag;
    if (idx >= 0 && (NSUInteger)idx < _events.count && self.onReplayBannerTapped) {
        self.onReplayBannerTapped(_events[(NSUInteger)idx]);
    }
}

- (void)updateWithEvents:(NSArray<NSDictionary *> *)events {
    _events = [events copy] ?: @[];
    for (UIView *v in [_listStack.arrangedSubviews copy]) {
        [_listStack removeArrangedSubview:v];
        [v removeFromSuperview];
    }
    if (_events.count == 0) {
        UIView *card = [BTTheme cardView];
        UILabel *empty = [BTTheme labelWithSize:13 weight:UIFontWeightRegular color:[BTTheme textSecondary]];
        empty.text = @"No active Windows notifications in history.";
        empty.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:empty];
        [NSLayoutConstraint activateConstraints:@[
            [empty.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:12],
            [empty.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-12],
            [empty.topAnchor constraintEqualToAnchor:card.topAnchor constant:12],
            [empty.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-12]
        ]];
        [_listStack addArrangedSubview:card];
        return;
    }

    for (NSInteger i = (NSInteger)_events.count - 1; i >= 0; i--) {
        NSDictionary *ev = _events[(NSUInteger)i];
        UIView *card = [BTTheme cardView];
        NSInteger p = [ev[@"priority"] integerValue];
        NSString *meta = BTNotifStr(ev[@"appName"], BTNotifStr(ev[@"source"], @"Windows"));
        UILabel *metaLbl = [BTTheme labelWithSize:11 weight:UIFontWeightBold color:p >= 3 ? [BTTheme accentRed] : (p == 2 ? [BTTheme accentAmber] : [BTTheme accentBlue])];
        metaLbl.text = [NSString stringWithFormat:@"%@ · P%ld", meta, (long)p];

        UILabel *titleLbl = [BTTheme labelWithSize:14 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        titleLbl.numberOfLines = 2;
        titleLbl.text = BTNotifStr(ev[@"title"], @"Notification");

        UILabel *bodyLbl = [BTTheme labelWithSize:12 weight:UIFontWeightRegular color:[BTTheme textSecondary]];
        bodyLbl.numberOfLines = 3;
        bodyLbl.text = BTNotifStr(ev[@"body"], @"");

        UIButton *inspectBtn = [BTTheme pillButtonWithTitle:@"Preview Top Bar" target:self action:@selector(replayTapped:)];
        inspectBtn.tag = i;

        UIStackView *topRow = [[UIStackView alloc] initWithArrangedSubviews:@[metaLbl, inspectBtn]];
        topRow.axis = UILayoutConstraintAxisHorizontal;
        topRow.distribution = UIStackViewDistributionEqualSpacing;
        topRow.alignment = UIStackViewAlignmentCenter;

        UIStackView *stack = [[UIStackView alloc] initWithArrangedSubviews:@[topRow, titleLbl, bodyLbl]];
        stack.axis = UILayoutConstraintAxisVertical;
        stack.spacing = 4;
        stack.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:stack];

        [NSLayoutConstraint activateConstraints:@[
            [stack.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:12],
            [stack.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-12],
            [stack.topAnchor constraintEqualToAnchor:card.topAnchor constant:8],
            [stack.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-8]
        ]];
        [_listStack addArrangedSubview:card];
    }
}

@end

