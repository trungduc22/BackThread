#import "BTYouTubeEmbedController.h"

@implementation BTYouTubeEmbedController {
    WKWebView *_webView;
    UILabel *_statusLabel;
    BOOL _notifiedConfirm;
}

- (instancetype)initWithEmbedURL:(NSURL *)url
                           title:(NSString *)title
                       sessionId:(NSString *)sessionId
                      generation:(uint64_t)generation {
    if ((self = [super initWithNibName:nil bundle:nil])) {
        _embedURL = [url copy];
        _videoTitle = [title.length ? title : @"YouTube Embed" copy];
        _sessionId = [sessionId copy];
        _generation = generation;
        self.modalPresentationStyle = UIModalPresentationFullScreen;
    }
    return self;
}

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [UIColor colorWithRed:0.04 green:0.05 blue:0.07 alpha:1.0];

    UIView *header = [UIView new];
    header.backgroundColor = [UIColor colorWithRed:0.08 green:0.10 blue:0.14 alpha:1.0];
    header.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:header];

    _statusLabel = [UILabel new];
    _statusLabel.textColor = [UIColor colorWithWhite:0.92 alpha:1.0];
    _statusLabel.font = [UIFont systemFontOfSize:13 weight:UIFontWeightSemibold];
    _statusLabel.text = [NSString stringWithFormat:@"Experimental Embed · %@", _videoTitle];
    _statusLabel.translatesAutoresizingMaskIntoConstraints = NO;
    [header addSubview:_statusLabel];

    UIButton *fallbackBtn = [UIButton buttonWithType:UIButtonTypeSystem];
    [fallbackBtn setTitle:@"Close · Use Mode D" forState:UIControlStateNormal];
    [fallbackBtn setTitleColor:[UIColor colorWithRed:0.20 green:0.84 blue:0.78 alpha:1.0] forState:UIControlStateNormal];
    fallbackBtn.titleLabel.font = [UIFont systemFontOfSize:13 weight:UIFontWeightSemibold];
    fallbackBtn.translatesAutoresizingMaskIntoConstraints = NO;
    [fallbackBtn addTarget:self action:@selector(closeAndFallback) forControlEvents:UIControlEventTouchUpInside];
    [header addSubview:fallbackBtn];

    WKWebViewConfiguration *cfg = [WKWebViewConfiguration new];
    cfg.allowsInlineMediaPlayback = YES;
    cfg.mediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypeNone;
    cfg.websiteDataStore = [WKWebsiteDataStore nonPersistentDataStore];

    _webView = [[WKWebView alloc] initWithFrame:CGRectZero configuration:cfg];
    _webView.navigationDelegate = self;
    _webView.backgroundColor = UIColor.blackColor;
    _webView.opaque = YES;
    _webView.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:_webView];

    UILayoutGuide *safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [header.topAnchor constraintEqualToAnchor:safe.topAnchor],
        [header.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor],
        [header.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor],
        [header.heightAnchor constraintEqualToConstant:44],

        [_statusLabel.leadingAnchor constraintEqualToAnchor:header.leadingAnchor constant:12],
        [_statusLabel.centerYAnchor constraintEqualToAnchor:header.centerYAnchor],
        [_statusLabel.trailingAnchor constraintLessThanOrEqualToAnchor:fallbackBtn.leadingAnchor constant:-10],

        [fallbackBtn.trailingAnchor constraintEqualToAnchor:header.trailingAnchor constant:-12],
        [fallbackBtn.centerYAnchor constraintEqualToAnchor:header.centerYAnchor],
        [fallbackBtn.heightAnchor constraintEqualToConstant:44],

        [_webView.topAnchor constraintEqualToAnchor:header.bottomAnchor],
        [_webView.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor],
        [_webView.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor],
        [_webView.bottomAnchor constraintEqualToAnchor:safe.bottomAnchor]
    ]];

    NSString *host = _embedURL.host.lowercaseString ?: @"";
    if (![_embedURL.scheme.lowercaseString isEqual:@"https"] || ![host isEqual:@"www.youtube-nocookie.com"]) {
        [self triggerFallback:@"Rejected non-nocookie YouTube embed URL"];
        return;
    }

    [_webView loadRequest:[NSURLRequest requestWithURL:_embedURL]];
}

- (void)webView:(WKWebView *)webView didFinishNavigation:(WKNavigation *)navigation {
    (void)webView;
    (void)navigation;
    _statusLabel.text = _videoTitle;
    if (!_notifiedConfirm && self.onConfirmed) {
        _notifiedConfirm = YES;
        self.onConfirmed(_sessionId, _generation);
    }
}

- (void)webView:(WKWebView *)webView didFailNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    (void)webView; (void)navigation;
    [self triggerFallback:error.localizedDescription ?: @"YouTube embed failed to load"];
}

- (void)webView:(WKWebView *)webView didFailProvisionalNavigation:(WKNavigation *)navigation withError:(NSError *)error {
    (void)webView; (void)navigation;
    [self triggerFallback:error.localizedDescription ?: @"YouTube embed disallowed or unavailable"];
}

- (void)closeAndFallback {
    [self triggerFallback:@"Switched back to Windows Mode D remote control"];
}

- (void)triggerFallback:(NSString *)reason {
    _webView.navigationDelegate = nil;
    [_webView stopLoading];
    void (^cb)(NSString *, uint64_t, NSString *) = self.onFallbackRequested;
    self.onFallbackRequested = nil;
    if (self.presentingViewController) {
        [self dismissViewControllerAnimated:YES completion:^{
            if (cb) cb(self->_sessionId, self->_generation, reason);
        }];
    } else if (cb) {
        cb(_sessionId, _generation, reason);
    }
}

- (void)dealloc {
    _webView.navigationDelegate = nil;
    [_webView stopLoading];
}

@end
