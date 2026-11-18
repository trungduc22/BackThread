#import "BTIdleHubView.h"
#import "BTTheme.h"

@implementation BTIdleHubView {
    UIScrollView *_scroll;
    UIStackView *_rowStack;
    UILabel *_connSummary;
    UILabel *_pcmSummary;
    UILabel *_mediaSummary;
    UIStackView *_quickActionButtons;
    UILabel *_jobSummary;
    UILabel *_noticeSummary;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [BTTheme canvasColor];
        _scroll = [UIScrollView new];
        _scroll.showsVerticalScrollIndicator = NO;
        _scroll.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_scroll];

        UIView *leftCard = [BTTheme cardView];
        UIView *midCard = [BTTheme cardView];
        UIView *rightCard = [BTTheme cardView];

        UILabel *leftTitle = [BTTheme labelWithSize:12 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        leftTitle.text = @"COMPANION & AUDIO";
        _connSummary = [BTTheme labelWithSize:15 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        _connSummary.numberOfLines = 2;
        _connSummary.text = @"Waiting for Windows USB Host";
        _pcmSummary = [BTTheme labelWithSize:12 weight:UIFontWeightRegular color:[BTTheme textSecondary]];
        _pcmSummary.numberOfLines = 2;
        _pcmSummary.text = @"48 kHz Stereo PCM · Ready";
        _mediaSummary = [BTTheme labelWithSize:12 weight:UIFontWeightMedium color:[BTTheme textMuted]];
        _mediaSummary.numberOfLines = 2;
        _mediaSummary.text = @"Media: Idle";
        UIButton *openMedia = [BTTheme pillButtonWithTitle:@"Open Studio Media" target:self action:@selector(goMedia)];
        UIStackView *leftStack = [[UIStackView alloc] initWithArrangedSubviews:@[leftTitle, _connSummary, _pcmSummary, _mediaSummary, openMedia]];
        leftStack.axis = UILayoutConstraintAxisVertical;
        leftStack.spacing = 6;
        leftStack.translatesAutoresizingMaskIntoConstraints = NO;
        [leftCard addSubview:leftStack];

        UILabel *midTitle = [BTTheme labelWithSize:12 weight:UIFontWeightBold color:[BTTheme accentBlue]];
        midTitle.text = @"QUICK ACTIONS";
        _quickActionButtons = [UIStackView new];
        _quickActionButtons.axis = UILayoutConstraintAxisVertical;
        _quickActionButtons.spacing = 6;
        UIButton *moreActions = [BTTheme pillButtonWithTitle:@"All Actions" target:self action:@selector(goActions)];
        UIStackView *midStack = [[UIStackView alloc] initWithArrangedSubviews:@[midTitle, _quickActionButtons, moreActions]];
        midStack.axis = UILayoutConstraintAxisVertical;
        midStack.spacing = 6;
        midStack.translatesAutoresizingMaskIntoConstraints = NO;
        [midCard addSubview:midStack];

        UILabel *rightTitle = [BTTheme labelWithSize:12 weight:UIFontWeightBold color:[BTTheme accentAmber]];
        rightTitle.text = @"WORKER & NOTIFICATIONS";
        _jobSummary = [BTTheme labelWithSize:12 weight:UIFontWeightMedium color:[BTTheme textSecondary]];
        _jobSummary.numberOfLines = 2;
        _jobSummary.text = @"No active background job";
        _noticeSummary = [BTTheme labelWithSize:12 weight:UIFontWeightRegular color:[BTTheme textMuted]];
        _noticeSummary.numberOfLines = 4;
        _noticeSummary.text = @"No recent notifications";
        UIButton *openHistory = [BTTheme pillButtonWithTitle:@"Jobs & History" target:self action:@selector(goHistory)];
        UIStackView *rightStack = [[UIStackView alloc] initWithArrangedSubviews:@[rightTitle, _jobSummary, _noticeSummary, openHistory]];
        rightStack.axis = UILayoutConstraintAxisVertical;
        rightStack.spacing = 6;
        rightStack.translatesAutoresizingMaskIntoConstraints = NO;
        [rightCard addSubview:rightStack];

        _rowStack = [[UIStackView alloc] initWithArrangedSubviews:@[leftCard, midCard, rightCard]];
        _rowStack.axis = UILayoutConstraintAxisHorizontal;
        _rowStack.distribution = UIStackViewDistributionFillEqually;
        _rowStack.alignment = UIStackViewAlignmentTop;
        _rowStack.spacing = 10;
        _rowStack.translatesAutoresizingMaskIntoConstraints = NO;
        [_scroll addSubview:_rowStack];

        [NSLayoutConstraint activateConstraints:@[
            [_scroll.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_scroll.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_scroll.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_scroll.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],

            [_rowStack.leadingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.leadingAnchor constant:10],
            [_rowStack.trailingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.trailingAnchor constant:-10],
            [_rowStack.topAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.topAnchor constant:8],
            [_rowStack.bottomAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.bottomAnchor constant:-8],
            [_rowStack.widthAnchor constraintEqualToAnchor:_scroll.frameLayoutGuide.widthAnchor constant:-20],

            [leftStack.leadingAnchor constraintEqualToAnchor:leftCard.leadingAnchor constant:10],
            [leftStack.trailingAnchor constraintEqualToAnchor:leftCard.trailingAnchor constant:-10],
            [leftStack.topAnchor constraintEqualToAnchor:leftCard.topAnchor constant:10],
            [leftStack.bottomAnchor constraintEqualToAnchor:leftCard.bottomAnchor constant:-10],

            [midStack.leadingAnchor constraintEqualToAnchor:midCard.leadingAnchor constant:10],
            [midStack.trailingAnchor constraintEqualToAnchor:midCard.trailingAnchor constant:-10],
            [midStack.topAnchor constraintEqualToAnchor:midCard.topAnchor constant:10],
            [midStack.bottomAnchor constraintEqualToAnchor:midCard.bottomAnchor constant:-10],

            [rightStack.leadingAnchor constraintEqualToAnchor:rightCard.leadingAnchor constant:10],
            [rightStack.trailingAnchor constraintEqualToAnchor:rightCard.trailingAnchor constant:-10],
            [rightStack.topAnchor constraintEqualToAnchor:rightCard.topAnchor constant:10],
            [rightStack.bottomAnchor constraintEqualToAnchor:rightCard.bottomAnchor constant:-10]
        ]];
    }
    return self;
}

- (void)layoutSubviews {
    [super layoutSubviews];
    BOOL landscape = self.bounds.size.width >= self.bounds.size.height;
    UILayoutConstraintAxis target = landscape ? UILayoutConstraintAxisHorizontal : UILayoutConstraintAxisVertical;
    if (_rowStack.axis != target) {
        _rowStack.axis = target;
        _rowStack.distribution = landscape ? UIStackViewDistributionFillEqually : UIStackViewDistributionFill;
        _rowStack.alignment = landscape ? UIStackViewAlignmentTop : UIStackViewAlignmentFill;
    }
}

- (void)goMedia { if (self.onNavigateMode) self.onNavigateMode(@"media"); }
- (void)goActions { if (self.onNavigateMode) self.onNavigateMode(@"actions"); }
- (void)goHistory { if (self.onNavigateMode) self.onNavigateMode(@"history"); }
- (void)actionTapped:(UIButton *)sender {
    NSString *aid = sender.accessibilityIdentifier;
    if (aid.length && self.onActionInvoked) self.onActionInvoked(aid);
}

- (void)updateConnectionText:(NSString *)connectionText
                usbConnected:(BOOL)usbConnected
                    pcmStats:(NSDictionary *)pcmStats
                 mediaTitle:(NSString *)mediaTitle
                 mediaMode:(NSString *)mediaMode
                    actions:(NSArray<NSDictionary *> *)actions
                    lastJob:(NSDictionary * _Nullable)lastJob
               recentEvents:(NSArray<NSDictionary *> *)recentEvents {
    _connSummary.text = connectionText.length ? connectionText : (usbConnected ? @"USB Connected" : @"Waiting for USB Host");
    NSUInteger buffered = [pcmStats[@"bufferedBytes"] unsignedIntegerValue] / 4;
    NSUInteger overflow = [pcmStats[@"droppedBytes"] unsignedIntegerValue];
    NSUInteger silence = [pcmStats[@"silenceBytes"] unsignedIntegerValue];
    _pcmSummary.text = [NSString stringWithFormat:@"PCM Ring: %lu frames · Drop %lu B · Gap %lu B",
                        (unsigned long)buffered, (unsigned long)overflow, (unsigned long)silence];
    _mediaSummary.text = mediaTitle.length
        ? [NSString stringWithFormat:@"%@ · %@", [BTTheme displayModeName:mediaMode], mediaTitle]
        : [NSString stringWithFormat:@"%@ · Ready", [BTTheme displayModeName:mediaMode]];

    for (UIView *v in [_quickActionButtons.arrangedSubviews copy]) {
        [_quickActionButtons removeArrangedSubview:v];
        [v removeFromSuperview];
    }
    if (actions.count == 0) {
        UILabel *empty = [BTTheme labelWithSize:12 weight:UIFontWeightRegular color:[BTTheme textMuted]];
        empty.text = @"Actions appear when Windows connects";
        empty.numberOfLines = 2;
        [_quickActionButtons addArrangedSubview:empty];
    } else {
        NSUInteger limit = MIN((NSUInteger)3, actions.count);
        for (NSUInteger i = 0; i < limit; i++) {
            NSDictionary *act = actions[i];
            NSString *title = [act[@"title"] isKindOfClass:NSString.class] ? act[@"title"] : @"Action";
            NSString *aid = [act[@"id"] isKindOfClass:NSString.class] ? act[@"id"] : @"";
            UIButton *b = [BTTheme pillButtonWithTitle:title target:self action:@selector(actionTapped:)];
            b.accessibilityIdentifier = aid;
            [_quickActionButtons addArrangedSubview:b];
        }
    }

    if (lastJob) {
        _jobSummary.text = [NSString stringWithFormat:@"%@ · %@ (%.0f%%)",
                            lastJob[@"jobId"] ?: @"job",
                            lastJob[@"state"] ?: @"running",
                            [lastJob[@"progress"] doubleValue] * 100.0];
    } else {
        _jobSummary.text = @"No active background job";
    }

    if (recentEvents.count > 0) {
        NSDictionary *latest = recentEvents.lastObject;
        NSString *t = [latest[@"title"] isKindOfClass:NSString.class] ? latest[@"title"] : @"Notification";
        NSString *b = [latest[@"body"] isKindOfClass:NSString.class] ? latest[@"body"] : @"";
        _noticeSummary.text = [NSString stringWithFormat:@"Latest: %@\n%@", t, b];
    } else {
        _noticeSummary.text = @"No recent notifications";
    }
}

@end
