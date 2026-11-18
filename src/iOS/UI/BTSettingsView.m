#import "BTSettingsView.h"
#import "BTTheme.h"

@implementation BTSettingsView {
    UIScrollView *_scroll;
    UILabel *_diagLabel;
    UILabel *_audioLabel;
    UILabel *_cacheFeedbackLabel;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [BTTheme canvasColor];
        _scroll = [UIScrollView new];
        _scroll.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_scroll];

        UIView *pairCard = [BTTheme cardView];
        UILabel *pairTitle = [BTTheme labelWithSize:14 weight:UIFontWeightBold color:[BTTheme textPrimary]];
        pairTitle.text = @"Host Pairing & Security (USB)";
        UILabel *pairHint = [BTTheme labelWithSize:12 weight:UIFontWeightRegular color:[BTTheme textSecondary]];
        pairHint.numberOfLines = 2;
        pairHint.text = @"Key is stored in Library/BackThread/session.key with NSFileProtectionCompleteUntilFirstUserAuthentication (0600).";
        UIButton *pairBtn = [BTTheme primaryButtonWithTitle:@"Pair / Update 64-Hex Session Key" target:self action:@selector(pairTapped)];
        UIStackView *pairStack = [[UIStackView alloc] initWithArrangedSubviews:@[pairTitle, pairHint, pairBtn]];
        pairStack.axis = UILayoutConstraintAxisVertical;
        pairStack.spacing = 6;
        pairStack.translatesAutoresizingMaskIntoConstraints = NO;
        [pairCard addSubview:pairStack];

        UIView *diagCard = [BTTheme cardView];
        UILabel *diagTitle = [BTTheme labelWithSize:14 weight:UIFontWeightBold color:[BTTheme accentTeal]];
        diagTitle.text = @"Audio Session & Transport Diagnostics";
        _diagLabel = [BTTheme labelWithSize:12 weight:UIFontWeightMedium color:[BTTheme textPrimary]];
        _diagLabel.numberOfLines = 2;
        _diagLabel.text = @"Waiting for host telemetry";
        _audioLabel = [BTTheme labelWithSize:12 weight:UIFontWeightRegular color:[BTTheme textSecondary]];
        _audioLabel.numberOfLines = 3;
        _audioLabel.text = @"AVAudioSession coordinator idle";
        _cacheFeedbackLabel = [BTTheme labelWithSize:11 weight:UIFontWeightMedium color:[BTTheme accentAmber]];
        _cacheFeedbackLabel.text = @"Cache limit: 24 MiB bounded in Caches/BackThreadBuffered";

        UIButton *evictBtn = [BTTheme pillButtonWithTitle:@"Evict Buffered Media Cache" target:self action:@selector(evictTapped)];
        UIButton *clearBtn = [BTTheme pillButtonWithTitle:@"Clear Notification History" target:self action:@selector(clearTapped)];
        UIStackView *btnRow = [[UIStackView alloc] initWithArrangedSubviews:@[evictBtn, clearBtn]];
        btnRow.axis = UILayoutConstraintAxisHorizontal;
        btnRow.spacing = 8;
        btnRow.distribution = UIStackViewDistributionFillEqually;

        UIStackView *diagStack = [[UIStackView alloc] initWithArrangedSubviews:@[diagTitle, _diagLabel, _audioLabel, _cacheFeedbackLabel, btnRow]];
        diagStack.axis = UILayoutConstraintAxisVertical;
        diagStack.spacing = 6;
        diagStack.translatesAutoresizingMaskIntoConstraints = NO;
        [diagCard addSubview:diagStack];

        UIStackView *root = [[UIStackView alloc] initWithArrangedSubviews:@[pairCard, diagCard]];
        root.axis = UILayoutConstraintAxisVertical;
        root.spacing = 10;
        root.translatesAutoresizingMaskIntoConstraints = NO;
        [_scroll addSubview:root];

        [NSLayoutConstraint activateConstraints:@[
            [_scroll.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_scroll.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_scroll.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_scroll.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],

            [root.leadingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.leadingAnchor constant:12],
            [root.trailingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.trailingAnchor constant:-12],
            [root.topAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.topAnchor constant:8],
            [root.bottomAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.bottomAnchor constant:-8],
            [root.widthAnchor constraintEqualToAnchor:_scroll.frameLayoutGuide.widthAnchor constant:-24],

            [pairStack.leadingAnchor constraintEqualToAnchor:pairCard.leadingAnchor constant:12],
            [pairStack.trailingAnchor constraintEqualToAnchor:pairCard.trailingAnchor constant:-12],
            [pairStack.topAnchor constraintEqualToAnchor:pairCard.topAnchor constant:10],
            [pairStack.bottomAnchor constraintEqualToAnchor:pairCard.bottomAnchor constant:-10],

            [diagStack.leadingAnchor constraintEqualToAnchor:diagCard.leadingAnchor constant:12],
            [diagStack.trailingAnchor constraintEqualToAnchor:diagCard.trailingAnchor constant:-12],
            [diagStack.topAnchor constraintEqualToAnchor:diagCard.topAnchor constant:10],
            [diagStack.bottomAnchor constraintEqualToAnchor:diagCard.bottomAnchor constant:-10]
        ]];
    }
    return self;
}

- (void)pairTapped { if (self.onPairTapped) self.onPairTapped(); }
- (void)evictTapped {
    _cacheFeedbackLabel.text = @"Buffered media cache evicted (LRU reset)";
    if (self.onEvictCacheTapped) self.onEvictCacheTapped();
}
- (void)clearTapped {
    _cacheFeedbackLabel.text = @"Notification history cleared";
    if (self.onClearHistoryTapped) self.onClearHistoryTapped();
}

- (void)updateDiagnosticsWithConnection:(NSString *)connection
                               pcmStats:(NSDictionary *)pcmStats
                          sessionStatus:(NSDictionary *)sessionStatus {
    NSUInteger buffered = [pcmStats[@"bufferedBytes"] unsignedIntegerValue] / 4;
    NSUInteger drop = [pcmStats[@"droppedBytes"] unsignedIntegerValue];
    NSUInteger sil = [pcmStats[@"silenceBytes"] unsignedIntegerValue];
    _diagLabel.text = [NSString stringWithFormat:@"Host: %@ · PCM Buffer: %lu / 4800 fr · Drop %lu B · Gap %lu B",
                       connection.length ? connection : @"Disconnected",
                       (unsigned long)buffered, (unsigned long)drop, (unsigned long)sil];

    BOOL pcmActive = [sessionStatus[@"pcmReceiverActive"] boolValue];
    BOOL indActive = [sessionStatus[@"independentMediaActive"] boolValue];
    NSString *focus = [sessionStatus[@"focusPolicy"] isKindOfClass:NSString.class] ? sessionStatus[@"focusPolicy"] : @"Auto";
    float vol = [sessionStatus[@"independentVolume"] floatValue];
    _audioLabel.text = [NSString stringWithFormat:@"AVAudioSession: PCM %@ · Independent AVPlayer %@ · Focus %@ · Vol %.0f%%",
                        pcmActive ? @"Active" : @"Off",
                        indActive ? @"Active" : @"Idle",
                        focus,
                        vol * 100.0f];
}

@end
