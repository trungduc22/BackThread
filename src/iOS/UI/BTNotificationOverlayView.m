#import "BTNotificationOverlayView.h"
#import "BTTheme.h"
#import <ImageIO/ImageIO.h>

static NSString *BTNotifStr(id val, NSString *fallback) {
    return [val isKindOfClass:NSString.class] ? val : fallback;
}

@implementation BTNotificationOverlayView {
    UIView *_backdrop;
    UIView *_card;
    UIImageView *_iconView;
    UILabel *_metaLabel;
    UILabel *_priorityBadge;
    UILabel *_titleLabel;
    UITextView *_bodyTextView;
    UIButton *_dismissButton;
    NSLayoutConstraint *_cardTop;
    NSLayoutConstraint *_cardBottom;
    NSLayoutConstraint *_cardCenterY;
    NSLayoutConstraint *_cardMaxWidth;
    NSLayoutConstraint *_iconWidth;
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

        _backdrop = [UIView new];
        _backdrop.backgroundColor = [UIColor colorWithRed:0.02 green:0.03 blue:0.05 alpha:0.90];
        _backdrop.alpha = 0.0;
        _backdrop.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_backdrop];

        _card = [UIView new];
        _card.backgroundColor = [BTTheme surfaceColor];
        _card.layer.cornerRadius = 14;
        _card.layer.borderWidth = 1.0;
        _card.layer.borderColor = [BTTheme hairlineColor].CGColor;
        _card.clipsToBounds = YES;
        _card.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_card];

        _iconView = [UIImageView new];
        _iconView.contentMode = UIViewContentModeScaleAspectFit;
        _iconView.layer.cornerRadius = 8;
        _iconView.clipsToBounds = YES;
        _iconView.backgroundColor = [BTTheme elevatedColor];
        _iconView.translatesAutoresizingMaskIntoConstraints = NO;
        _iconWidth = [_iconView.widthAnchor constraintEqualToConstant:40];
        [_iconView.heightAnchor constraintEqualToAnchor:_iconView.widthAnchor].active = YES;

        _metaLabel = [BTTheme labelWithSize:12 weight:UIFontWeightSemibold color:[BTTheme textSecondary]];
        _priorityBadge = [BTTheme labelWithSize:10 weight:UIFontWeightBold color:[BTTheme accentRed]];
        [_priorityBadge.heightAnchor constraintEqualToConstant:20].active = YES;
        [_priorityBadge setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

        UIStackView *metaRow = [[UIStackView alloc] initWithArrangedSubviews:@[_iconView, _metaLabel, _priorityBadge]];
        metaRow.axis = UILayoutConstraintAxisHorizontal;
        metaRow.alignment = UIStackViewAlignmentCenter;
        metaRow.spacing = 8;

        _titleLabel = [BTTheme labelWithSize:20 weight:UIFontWeightBold color:[BTTheme textPrimary]];
        _titleLabel.numberOfLines = 2;

        _bodyTextView = [UITextView new];
        _bodyTextView.backgroundColor = [UIColor clearColor];
        _bodyTextView.textColor = [BTTheme textSecondary];
        _bodyTextView.font = [UIFont systemFontOfSize:15 weight:UIFontWeightRegular];
        _bodyTextView.editable = NO;
        _bodyTextView.selectable = NO;
        _bodyTextView.scrollEnabled = YES;
        _bodyTextView.textContainerInset = UIEdgeInsetsZero;
        _bodyTextView.textContainer.lineFragmentPadding = 0;

        _dismissButton = [BTTheme primaryButtonWithTitle:@"Dismiss" target:self action:@selector(dismissOverlay)];
        [_dismissButton.heightAnchor constraintGreaterThanOrEqualToConstant:48].active = YES;

        UIStackView *cardStack = [[UIStackView alloc] initWithArrangedSubviews:@[metaRow, _titleLabel, _bodyTextView, _dismissButton]];
        cardStack.axis = UILayoutConstraintAxisVertical;
        cardStack.spacing = 8;
        cardStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_card addSubview:cardStack];

        _cardTop = [_card.topAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.topAnchor constant:8];
        _cardBottom = [_card.bottomAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.bottomAnchor constant:-12];
        _cardCenterY = [_card.centerYAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.centerYAnchor];
        _cardMaxWidth = [_card.widthAnchor constraintLessThanOrEqualToConstant:520];
        NSLayoutConstraint *fillWidth = [_card.widthAnchor constraintEqualToAnchor:self.safeAreaLayoutGuide.widthAnchor constant:-24];
        fillWidth.priority = UILayoutPriorityDefaultHigh;

        NSLayoutConstraint *bodyHeight = [_bodyTextView.heightAnchor constraintLessThanOrEqualToConstant:140];
        bodyHeight.priority = UILayoutPriorityDefaultHigh;

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

            [cardStack.leadingAnchor constraintEqualToAnchor:_card.leadingAnchor constant:16],
            [cardStack.trailingAnchor constraintEqualToAnchor:_card.trailingAnchor constant:-16],
            [cardStack.topAnchor constraintEqualToAnchor:_card.topAnchor constant:12],
            [cardStack.bottomAnchor constraintEqualToAnchor:_card.bottomAnchor constant:-12]
        ]];

        UISwipeGestureRecognizer *swipe = [[UISwipeGestureRecognizer alloc] initWithTarget:self action:@selector(dismissOverlay)];
        swipe.direction = UISwipeGestureRecognizerDirectionUp;
        [_card addGestureRecognizer:swipe];
    }
    return self;
}

- (BOOL)fullscreenActive {
    return _fullscreenActive;
}

- (BOOL)pointInside:(CGPoint)point withEvent:(UIEvent *)event {
    if (self.hidden) return NO;
    if (_fullscreenActive) return [super pointInside:point withEvent:event];
    CGPoint inCard = [self convertPoint:point toView:_card];
    return [_card pointInside:inCard withEvent:event];
}

- (void)presentNotification:(NSDictionary *)event expiresInterval:(NSTimeInterval)expiresInSeconds {
    NSInteger priority = [event[@"priority"] integerValue];
    if (priority <= 0) return;

    [_autoDismissTimer invalidate];
    _autoDismissTimer = nil;

    _currentPriority = priority;
    _fullscreenActive = (priority >= 3);

    NSString *appName = BTNotifStr(event[@"appName"], @"");
    NSString *source = BTNotifStr(event[@"source"], @"Windows");
    NSString *category = BTNotifStr(event[@"category"], @"");
    NSString *title = BTNotifStr(event[@"title"], @"Notification");
    NSString *body = BTNotifStr(event[@"body"], @"");
    NSString *iconB64 = BTNotifStr(event[@"iconBase64"], @"");

    NSMutableArray<NSString *> *metaParts = [NSMutableArray new];
    if (appName.length) [metaParts addObject:appName];
    else if (source.length) [metaParts addObject:source];
    if (category.length) [metaParts addObject:category.capitalizedString];
    _metaLabel.text = metaParts.count ? [metaParts componentsJoinedByString:@" · "] : @"Windows";

    if (priority == 1) {
        _backdrop.alpha = 0.0;
        _cardTop.active = YES;
        _cardBottom.active = NO;
        _cardCenterY.active = NO;
        _cardMaxWidth.constant = 420;
        _priorityBadge.text = @" NOTICE ";
        [BTTheme styleChipLabel:_priorityBadge accent:[BTTheme accentBlue]];
        _titleLabel.font = [UIFont systemFontOfSize:15 weight:UIFontWeightSemibold];
        _titleLabel.text = [NSString stringWithFormat:@"%@: %@", _metaLabel.text, title];
        _bodyTextView.hidden = YES;
        _dismissButton.hidden = YES;
        _iconView.hidden = YES;
        _iconWidth.constant = 0;
        _card.layer.borderColor = [BTTheme accentBlue].CGColor;
    } else if (priority == 2) {
        _backdrop.alpha = 0.0;
        _cardTop.active = YES;
        _cardBottom.active = NO;
        _cardCenterY.active = NO;
        _cardMaxWidth.constant = 480;
        _priorityBadge.text = @" PRIORITY ";
        [BTTheme styleChipLabel:_priorityBadge accent:[BTTheme accentAmber]];
        _titleLabel.font = [UIFont systemFontOfSize:17 weight:UIFontWeightBold];
        _titleLabel.text = title;
        _bodyTextView.hidden = body.length == 0;
        _bodyTextView.font = [UIFont systemFontOfSize:13 weight:UIFontWeightRegular];
        _bodyTextView.text = body;
        _dismissButton.hidden = NO;
        [_dismissButton setTitle:@"Dismiss" forState:UIControlStateNormal];
        BOOL hasIcon = iconB64.length > 0 && iconB64.length <= 24000;
        _iconView.hidden = !hasIcon;
        _iconWidth.constant = hasIcon ? 36 : 0;
        _card.layer.borderColor = [BTTheme accentAmber].CGColor;
    } else {
        _backdrop.alpha = 1.0;
        _cardTop.active = YES;
        _cardBottom.active = YES;
        _cardCenterY.active = YES;
        _cardMaxWidth.constant = 580;
        _priorityBadge.text = @" CRITICAL ALERT ";
        [BTTheme styleChipLabel:_priorityBadge accent:[BTTheme accentRed]];
        _titleLabel.font = [UIFont systemFontOfSize:22 weight:UIFontWeightBold];
        _titleLabel.text = title;
        _bodyTextView.hidden = NO;
        _bodyTextView.font = [UIFont systemFontOfSize:16 weight:UIFontWeightRegular];
        _bodyTextView.text = body.length ? body : @"Check Windows host notification";
        _dismissButton.hidden = NO;
        [_dismissButton setTitle:@"Dismiss & Return to Media" forState:UIControlStateNormal];
        BOOL hasIcon = iconB64.length > 0 && iconB64.length <= 24000;
        _iconView.hidden = !hasIcon;
        _iconWidth.constant = hasIcon ? 48 : 0;
        _card.layer.borderColor = [BTTheme accentRed].CGColor;
    }

    if (iconB64.length > 0 && iconB64.length <= 24000) {
        [self loadIconBase64:iconB64];
    } else {
        _activeIconKey = nil;
        _iconView.image = nil;
    }

    self.hidden = NO;
    [self.superview bringSubviewToFront:self];

    if (priority < 3) {
        NSTimeInterval timeout = MIN(priority >= 2 ? 8.0 : 3.0, MAX(0.5, expiresInSeconds));
        __weak typeof(self) weakSelf = self;
        _autoDismissTimer = [NSTimer scheduledTimerWithTimeInterval:timeout repeats:NO block:^(NSTimer *timer) {
            (void)timer;
            [weakSelf dismissOverlay];
        }];
    }
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
    if (idx >= 0 && (NSUInteger)idx < _events.count && self.onReplayFullscreenTapped) {
        self.onReplayFullscreenTapped(_events[(NSUInteger)idx]);
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

        UIButton *inspectBtn = [BTTheme pillButtonWithTitle:@"Inspect Fullscreen" target:self action:@selector(replayTapped:)];
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
