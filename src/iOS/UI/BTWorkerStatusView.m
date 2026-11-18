#import "BTWorkerStatusView.h"
#import "BTTheme.h"

@implementation BTWorkerStatusView {
    UILabel *_titleLabel;
    UILabel *_detailLabel;
    UIProgressView *_progressView;
    UILabel *_pressureBadge;
    UILabel *_bufferedLabel;
    UIButton *_cancelButton;
    NSString *_lastJobId;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [BTTheme canvasColor];

        UIView *jobCard = [BTTheme cardView];
        jobCard.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:jobCard];

        UILabel *section = [BTTheme labelWithSize:12 weight:UIFontWeightBold color:[BTTheme accentBlue]];
        section.text = @"COMPANION WORKER & USB BUFFER";

        _pressureBadge = [BTTheme labelWithSize:11 weight:UIFontWeightBold color:[BTTheme accentGreen]];
        _pressureBadge.text = @" Memory Normal ";
        [BTTheme styleChipLabel:_pressureBadge accent:[BTTheme accentGreen]];
        [_pressureBadge.heightAnchor constraintEqualToConstant:22].active = YES;

        UIStackView *topRow = [[UIStackView alloc] initWithArrangedSubviews:@[section, _pressureBadge]];
        topRow.axis = UILayoutConstraintAxisHorizontal;
        topRow.distribution = UIStackViewDistributionEqualSpacing;
        topRow.alignment = UIStackViewAlignmentCenter;

        _titleLabel = [BTTheme labelWithSize:18 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
        _titleLabel.text = @"No background jobs";

        _progressView = [[UIProgressView alloc] initWithProgressViewStyle:UIProgressViewStyleDefault];
        _progressView.progressTintColor = [BTTheme accentTeal];
        _progressView.trackTintColor = [BTTheme pillColor];
        _progressView.progress = 0;

        _detailLabel = [BTTheme labelWithSize:13 weight:UIFontWeightRegular color:[BTTheme textSecondary]];
        _detailLabel.numberOfLines = 3;
        _detailLabel.text = @"File hashing, verification, metadata inspection, and text analysis jobs run bounded on A8.";

        _bufferedLabel = [BTTheme labelWithSize:12 weight:UIFontWeightMedium color:[BTTheme textMuted]];
        _bufferedLabel.numberOfLines = 2;
        _bufferedLabel.text = @"Mode B Buffered Stream: Inactive";

        _cancelButton = [BTTheme pillButtonWithTitle:@"Cancel active job" target:self action:@selector(cancelTapped)];
        _cancelButton.enabled = NO;

        UIStackView *stack = [[UIStackView alloc] initWithArrangedSubviews:@[
            topRow,
            _titleLabel,
            _progressView,
            _detailLabel,
            _bufferedLabel,
            _cancelButton
        ]];
        stack.axis = UILayoutConstraintAxisVertical;
        stack.spacing = 10;
        stack.translatesAutoresizingMaskIntoConstraints = NO;
        [jobCard addSubview:stack];

        [NSLayoutConstraint activateConstraints:@[
            [jobCard.leadingAnchor constraintEqualToAnchor:self.leadingAnchor constant:12],
            [jobCard.trailingAnchor constraintEqualToAnchor:self.trailingAnchor constant:-12],
            [jobCard.topAnchor constraintEqualToAnchor:self.topAnchor constant:10],
            [jobCard.bottomAnchor constraintLessThanOrEqualToAnchor:self.bottomAnchor constant:-10],

            [stack.leadingAnchor constraintEqualToAnchor:jobCard.leadingAnchor constant:14],
            [stack.trailingAnchor constraintEqualToAnchor:jobCard.trailingAnchor constant:-14],
            [stack.topAnchor constraintEqualToAnchor:jobCard.topAnchor constant:12],
            [stack.bottomAnchor constraintEqualToAnchor:jobCard.bottomAnchor constant:-12]
        ]];
    }
    return self;
}

- (void)cancelTapped {
    if (_lastJobId.length && self.onCancelJobTapped) {
        self.onCancelJobTapped(_lastJobId);
    }
}

- (void)updateWithJob:(NSDictionary *)job
       memoryPressure:(BOOL)memoryPressure
       bufferedStatus:(NSDictionary *)bufferedStatus {
    if (memoryPressure) {
        _pressureBadge.text = @" Memory Pressure Paused ";
        [BTTheme styleChipLabel:_pressureBadge accent:[BTTheme accentRed]];
    } else {
        _pressureBadge.text = @" Memory Normal ";
        [BTTheme styleChipLabel:_pressureBadge accent:[BTTheme accentGreen]];
    }

    if (job.count > 0) {
        _lastJobId = [job[@"jobId"] isKindOfClass:NSString.class] ? [job[@"jobId"] copy] : nil;
        NSString *state = [job[@"state"] isKindOfClass:NSString.class] ? job[@"state"] : @"running";
        double progress = [job[@"progress"] doubleValue];
        _titleLabel.text = [NSString stringWithFormat:@"Job %@ · %@", _lastJobId ?: @"unknown", state.capitalizedString];
        _progressView.progress = (float)MAX(0.0, MIN(1.0, progress));
        NSString *extra = [job[@"error"] isKindOfClass:NSString.class]
            ? job[@"error"]
            : ([job[@"result"] description] ?: @"Running bounded worker queue");
        _detailLabel.text = [NSString stringWithFormat:@"%.0f%% · %@", progress * 100.0, extra];
        _cancelButton.enabled = ([state isEqualToString:@"queued"] || [state isEqualToString:@"running"]) && _lastJobId.length > 0;
    } else {
        _lastJobId = nil;
        _titleLabel.text = @"No background jobs";
        _progressView.progress = 0;
        _detailLabel.text = @"File hashing, verification, metadata inspection, and text analysis jobs run bounded on A8.";
        _cancelButton.enabled = NO;
    }

    NSString *streamId = [bufferedStatus[@"streamId"] isKindOfClass:NSString.class] ? bufferedStatus[@"streamId"] : @"";
    if (streamId.length > 0) {
        uint64_t received = [bufferedStatus[@"receivedBytes"] unsignedLongLongValue];
        uint64_t total = [bufferedStatus[@"totalBytes"] unsignedLongLongValue];
        BOOL eof = [bufferedStatus[@"eofReached"] boolValue];
        _bufferedLabel.text = [NSString stringWithFormat:@"Mode B USB Stream %@ · %.2f / %.2f MiB%@",
                               [streamId substringToIndex:MIN((NSUInteger)8, streamId.length)],
                               (double)received / (1024.0 * 1024.0),
                               (double)total / (1024.0 * 1024.0),
                               eof ? @" (EOF)" : @""];
    } else {
        _bufferedLabel.text = @"Mode B Buffered Stream: Inactive";
    }
}

@end
