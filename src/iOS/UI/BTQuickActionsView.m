#import "BTQuickActionsView.h"
#import "BTTheme.h"

@implementation BTQuickActionsView {
    UIScrollView *_scroll;
    UIStackView *_rowsStack;
    UILabel *_headerLabel;
    UILabel *_feedbackLabel;
    NSArray<NSDictionary *> *_actions;
    BOOL _lastLandscape;
}

- (instancetype)initWithFrame:(CGRect)frame {
    if ((self = [super initWithFrame:frame])) {
        self.backgroundColor = [BTTheme canvasColor];
        _actions = @[];

        _scroll = [UIScrollView new];
        _scroll.translatesAutoresizingMaskIntoConstraints = NO;
        [self addSubview:_scroll];

        _headerLabel = [BTTheme labelWithSize:15 weight:UIFontWeightBold color:[BTTheme textPrimary]];
        _headerLabel.text = @"Windows Quick Actions (Allowlisted)";

        _feedbackLabel = [BTTheme labelWithSize:12 weight:UIFontWeightMedium color:[BTTheme accentTeal]];
        _feedbackLabel.text = @"Tap any action to trigger on Windows";

        _rowsStack = [UIStackView new];
        _rowsStack.axis = UILayoutConstraintAxisVertical;
        _rowsStack.spacing = 8;

        UIStackView *outer = [[UIStackView alloc] initWithArrangedSubviews:@[_headerLabel, _feedbackLabel, _rowsStack]];
        outer.axis = UILayoutConstraintAxisVertical;
        outer.spacing = 8;
        outer.translatesAutoresizingMaskIntoConstraints = NO;
        [_scroll addSubview:outer];

        [NSLayoutConstraint activateConstraints:@[
            [_scroll.leadingAnchor constraintEqualToAnchor:self.leadingAnchor],
            [_scroll.trailingAnchor constraintEqualToAnchor:self.trailingAnchor],
            [_scroll.topAnchor constraintEqualToAnchor:self.topAnchor],
            [_scroll.bottomAnchor constraintEqualToAnchor:self.bottomAnchor],

            [outer.leadingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.leadingAnchor constant:12],
            [outer.trailingAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.trailingAnchor constant:-12],
            [outer.topAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.topAnchor constant:10],
            [outer.bottomAnchor constraintEqualToAnchor:_scroll.contentLayoutGuide.bottomAnchor constant:-10],
            [outer.widthAnchor constraintEqualToAnchor:_scroll.frameLayoutGuide.widthAnchor constant:-24]
        ]];
        [self rebuildGrid];
    }
    return self;
}

- (void)layoutSubviews {
    [super layoutSubviews];
    BOOL landscape = self.bounds.size.width >= self.bounds.size.height;
    if (landscape != _lastLandscape) {
        _lastLandscape = landscape;
        [self rebuildGrid];
    }
}

- (void)updateActions:(NSArray<NSDictionary *> *)actions {
    _actions = [actions copy] ?: @[];
    [self rebuildGrid];
}

- (void)showFeedbackText:(NSString *)text {
    _feedbackLabel.text = text.length ? text : @"Tap any action to trigger on Windows";
}

- (void)actionTapped:(UIButton *)button {
    NSString *aid = button.accessibilityIdentifier;
    if (!aid.length) return;
    _feedbackLabel.text = [NSString stringWithFormat:@"Invoked: %@", [button titleForState:UIControlStateNormal] ?: aid];
    if (self.onActionInvoked) self.onActionInvoked(aid);
}

- (void)rebuildGrid {
    for (UIView *v in [_rowsStack.arrangedSubviews copy]) {
        [_rowsStack removeArrangedSubview:v];
        [v removeFromSuperview];
    }
    if (_actions.count == 0) {
        UIView *card = [BTTheme cardView];
        UILabel *empty = [BTTheme labelWithSize:13 weight:UIFontWeightMedium color:[BTTheme textSecondary]];
        empty.numberOfLines = 2;
        empty.text = @"No allowlisted Quick Actions registered yet.\nConnect BackThread Windows Host to sync actions.";
        empty.translatesAutoresizingMaskIntoConstraints = NO;
        [card addSubview:empty];
        [NSLayoutConstraint activateConstraints:@[
            [empty.leadingAnchor constraintEqualToAnchor:card.leadingAnchor constant:14],
            [empty.trailingAnchor constraintEqualToAnchor:card.trailingAnchor constant:-14],
            [empty.topAnchor constraintEqualToAnchor:card.topAnchor constant:14],
            [empty.bottomAnchor constraintEqualToAnchor:card.bottomAnchor constant:-14]
        ]];
        [_rowsStack addArrangedSubview:card];
        return;
    }

    NSUInteger cols = _lastLandscape ? 2 : 1;
    NSUInteger count = MIN((NSUInteger)32, _actions.count);
    for (NSUInteger i = 0; i < count; i += cols) {
        UIStackView *row = [UIStackView new];
        row.axis = UILayoutConstraintAxisHorizontal;
        row.spacing = 8;
        row.distribution = UIStackViewDistributionFillEqually;
        for (NSUInteger c = 0; c < cols; c++) {
            NSUInteger idx = i + c;
            if (idx < count) {
                NSDictionary *act = _actions[idx];
                NSString *title = [act[@"title"] isKindOfClass:NSString.class] ? act[@"title"] : @"Action";
                NSString *aid = [act[@"id"] isKindOfClass:NSString.class] ? act[@"id"] : @"";
                UIButton *btn = [BTTheme pillButtonWithTitle:title target:self action:@selector(actionTapped:)];
                btn.backgroundColor = [BTTheme surfaceColor];
                [btn.heightAnchor constraintGreaterThanOrEqualToConstant:52].active = YES;
                btn.accessibilityIdentifier = aid;
                [row addArrangedSubview:btn];
            } else {
                UIView *spacer = [UIView new];
                [row addArrangedSubview:spacer];
            }
        }
        [_rowsStack addArrangedSubview:row];
    }
}

@end
