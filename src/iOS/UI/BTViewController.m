#import "BTViewController.h"
#import "BTTheme.h"
#import "BTVideoSurfaceView.h"
#import "BTNowPlayingView.h"
#import "BTIdleHubView.h"
#import "BTQuickActionsView.h"
#import "BTWorkerStatusView.h"
#import "BTSettingsView.h"
#import "BTNotificationOverlayView.h"
#import "../Agent/BTAgent.h"
#import "../AudioReceiver/BTAudioReceiver.h"
#import "../AudioReceiver/BTAudioSessionCoordinator.h"
#import "../Media/BTDirectMediaPlayer.h"
#import "../Media/BTBufferedMediaEngine.h"
#import "../Media/BTYouTubeEmbedController.h"
#import "../Transport/BTWifiClient.h"
#import <AVFoundation/AVFoundation.h>

@interface BTQrScannerViewController : UIViewController <AVCaptureMetadataOutputObjectsDelegate>
@property(nonatomic, copy) void (^onCodeScanned)(NSString *value);
@end

@implementation BTQrScannerViewController {
    AVCaptureSession *_session;
    AVCaptureVideoPreviewLayer *_previewLayer;
    BOOL _delivered;
}

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [BTTheme canvasColor];

    UILabel *hint = [BTTheme labelWithSize:14 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
    hint.text = @"Point camera at the BackThread PC pairing QR code";
    hint.textAlignment = NSTextAlignmentCenter;
    hint.numberOfLines = 2;
    hint.translatesAutoresizingMaskIntoConstraints = NO;

    UIButton *cancelBtn = [BTTheme pillButtonWithTitle:@"Cancel" target:self action:@selector(cancelTapped)];
    cancelBtn.translatesAutoresizingMaskIntoConstraints = NO;

    [self.view addSubview:hint];
    [self.view addSubview:cancelBtn];
    [NSLayoutConstraint activateConstraints:@[
        [hint.topAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.topAnchor constant:16],
        [hint.leadingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.leadingAnchor constant:16],
        [hint.trailingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.trailingAnchor constant:-16],
        [cancelBtn.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor constant:-20],
        [cancelBtn.centerXAnchor constraintEqualToAnchor:self.view.centerXAnchor]
    ]];

    AVCaptureDevice *device = [AVCaptureDevice defaultDeviceWithMediaType:AVMediaTypeVideo];
    if (!device) {
        hint.text = @"Camera unavailable on this device";
        return;
    }
    NSError *err = nil;
    AVCaptureDeviceInput *input = [AVCaptureDeviceInput deviceInputWithDevice:device error:&err];
    if (!input) {
        hint.text = err.localizedDescription ?: @"Cannot access camera";
        return;
    }

    _session = [AVCaptureSession new];
    if ([_session canAddInput:input]) [_session addInput:input];
    AVCaptureMetadataOutput *output = [AVCaptureMetadataOutput new];
    if ([_session canAddOutput:output]) {
        [_session addOutput:output];
        [output setMetadataObjectsDelegate:self queue:dispatch_get_main_queue()];
        if ([output.availableMetadataObjectTypes containsObject:AVMetadataObjectTypeQRCode]) {
            output.metadataObjectTypes = @[AVMetadataObjectTypeQRCode];
        }
    }

    _previewLayer = [AVCaptureVideoPreviewLayer layerWithSession:_session];
    _previewLayer.videoGravity = AVLayerVideoGravityResizeAspectFill;
    _previewLayer.frame = self.view.bounds;
    [self.view.layer insertSublayer:_previewLayer atIndex:0];
}

- (void)viewDidLayoutSubviews {
    [super viewDidLayoutSubviews];
    _previewLayer.frame = self.view.bounds;
}

- (void)viewDidAppear:(BOOL)animated {
    [super viewDidAppear:animated];
    if (_session && !_session.running) {
        AVCaptureSession *session = _session;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
            [session startRunning];
        });
    }
}

- (void)viewWillDisappear:(BOOL)animated {
    [super viewWillDisappear:animated];
    if (_session.running) {
        AVCaptureSession *session = _session;
        dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
            [session stopRunning];
        });
    }
}

- (void)cancelTapped {
    [self dismissViewControllerAnimated:YES completion:nil];
}

- (void)captureOutput:(AVCaptureOutput *)output didOutputMetadataObjects:(NSArray<__kindof AVMetadataObject *> *)metadataObjects fromConnection:(AVCaptureConnection *)connection {
    (void)output; (void)connection;
    if (_delivered) return;
    for (AVMetadataObject *obj in metadataObjects) {
        if ([obj isKindOfClass:AVMetadataMachineReadableCodeObject.class]) {
            NSString *val = ((AVMetadataMachineReadableCodeObject *)obj).stringValue;
            if (val.length && [[val lowercaseString] hasPrefix:@"backthread://pair"]) {
                _delivered = YES;
                [_session stopRunning];
                void (^cb)(NSString *) = self.onCodeScanned;
                [self dismissViewControllerAnimated:YES completion:^{
                    if (cb) cb(val);
                }];
                return;
            }
        }
    }
}

@end

typedef NS_ENUM(NSInteger, BTWorkspaceMode) {
    BTWorkspaceHub = 0,
    BTWorkspaceMedia = 1,
    BTWorkspaceVideoFocus = 2,
    BTWorkspaceActions = 3,
    BTWorkspaceWorker = 4,
    BTWorkspaceHistory = 5,
    BTWorkspaceSettings = 6,
    BTWorkspaceAmbient = 7
};

static NSString *BTSafeStr(id val, NSString *fallback) {
    return [val isKindOfClass:NSString.class] ? val : fallback;
}

@implementation BTViewController {
    UIView *_topBar;
    UIStackView *_topBarStack;
    UIView *_statusDot;
    UILabel *_connectionLabel;
    UISegmentedControl *_modeSegment;
    UIView *_workspaceContainer;
    UIView *_embedHostView;

    BTIdleHubView *_idleHubView;
    BTNowPlayingView *_nowPlayingView;
    BTVideoSurfaceView *_videoSurfaceView;
    BTQuickActionsView *_quickActionsView;
    BTWorkerStatusView *_workerStatusView;
    BTHistoryPanelView *_historyPanelView;
    BTSettingsView *_settingsView;
    BTNotificationOverlayView *_notificationOverlay;

    NSLayoutConstraint *_workspaceTopToBar;
    NSLayoutConstraint *_workspaceTopToView;
    NSArray<NSLayoutConstraint *> *_compactVideoConstraints;
    NSArray<NSLayoutConstraint *> *_fullscreenVideoConstraints;

    BTDirectMediaPlayer *_directPlayer;
    BTYouTubeEmbedController *_embedController;

    BTWorkspaceMode _workspaceMode;
    BOOL _usbConnected;
    NSString *_connectionText;
    NSString *_focusPolicy;
    NSDictionary *_latestHostMediaState;
    NSDictionary *_latestPlayerSnapshot;
    NSDictionary *_latestJob;
    NSArray<NSDictionary *> *_actions;
    NSMutableArray<NSDictionary *> *_events;
    NSString *_embedSessionId;
    uint64_t _embedGeneration;
    NSUInteger _memoryWarningGeneration;
    BOOL _autoEnteredVideoForSession;
    NSString *_autoEnteredSessionId;
    NSTimer *_telemetryPollTimer;
    NSUInteger _healthTickCounter;
}

- (void)dealloc {
    [_telemetryPollTimer invalidate];
}

- (void)setAgent:(BTAgent *)agent {
    _agent = agent;
    if (!agent) return;
    if (self.isViewLoaded) {
        [self bindAgent:agent];
    }
}

- (void)bindAgent:(BTAgent *)agent {
    if (!agent) return;
    if (!_directPlayer) {
        _directPlayer = [[BTDirectMediaPlayer alloc] initWithAgent:agent];
        __weak typeof(self) weakSelf = self;
        _directPlayer.stateUpdated = ^(NSDictionary *snapshot) {
            dispatch_async(dispatch_get_main_queue(), ^{
                [weakSelf handleDirectPlayerSnapshot:snapshot];
            });
        };
    }

    BTDirectMediaPlayer *playerRef = _directPlayer;
    agent.mediaSyncProvider = ^NSDictionary *(NSDictionary *hostSync) {
        (void)hostSync;
        return [playerRef syncSnapshot];
    };

    BTBufferedMediaEngine *bufferedRef = _directPlayer.bufferedEngine;
    agent.bufferedChunkReceived = ^(NSString *streamId, uint64_t offset, NSData *bytes) {
        [bufferedRef ingestChunkForStreamId:streamId offset:offset bytes:bytes];
    };

    if (_videoSurfaceView) {
        [_videoSurfaceView attachPlayerLayer:_directPlayer.playerLayer];
    }
}

- (void)viewDidLoad {
    [super viewDidLoad];
    self.view.backgroundColor = [BTTheme canvasColor];
    [UIDevice currentDevice].batteryMonitoringEnabled = YES;
    _events = [NSMutableArray new];
    _actions = @[];
    _latestHostMediaState = @{};
    _latestPlayerSnapshot = @{@"independentActive": @NO};
    _connectionText = @"Waiting for USB or Wi-Fi host";
    _focusPolicy = @"Auto";
    _workspaceMode = BTWorkspaceMedia;

    [self setupTopBar];
    [self setupWorkspaceViews];
    [self setupVideoSurfaceAndOverlay];

    if (_agent) {
        [self bindAgent:_agent];
    }

    [self switchWorkspaceMode:BTWorkspaceMedia];

    __weak typeof(self) weakSelf = self;
    _telemetryPollTimer = [NSTimer scheduledTimerWithTimeInterval:2.0 repeats:YES block:^(NSTimer *timer) {
        (void)timer;
        [weakSelf refreshAuxiliaryDiagnostics];
        BTViewController *strong = weakSelf;
        if (strong && (++strong->_healthTickCounter % 15 == 0)) {
            [strong reportPhoneHealth];
        }
    }];
}

- (void)setupTopBar {
    _topBar = [UIView new];
    _topBar.backgroundColor = [BTTheme surfaceColor];
    _topBar.layer.borderWidth = 1.0 / UIScreen.mainScreen.scale;
    _topBar.layer.borderColor = [BTTheme hairlineColor].CGColor;
    _topBar.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:_topBar];

    _statusDot = [UIView new];
    _statusDot.backgroundColor = [BTTheme accentAmber];
    _statusDot.layer.cornerRadius = 4;
    _statusDot.translatesAutoresizingMaskIntoConstraints = NO;
    [_statusDot.widthAnchor constraintEqualToConstant:8].active = YES;
    [_statusDot.heightAnchor constraintEqualToConstant:8].active = YES;

    _connectionLabel = [BTTheme labelWithSize:12 weight:UIFontWeightSemibold color:[BTTheme textPrimary]];
    _connectionLabel.text = _connectionText;
    [_connectionLabel setContentCompressionResistancePriority:UILayoutPriorityDefaultLow forAxis:UILayoutConstraintAxisHorizontal];

    UIStackView *statusRow = [[UIStackView alloc] initWithArrangedSubviews:@[_statusDot, _connectionLabel]];
    statusRow.axis = UILayoutConstraintAxisHorizontal;
    statusRow.alignment = UIStackViewAlignmentCenter;
    statusRow.spacing = 6;

    _modeSegment = [[UISegmentedControl alloc] initWithItems:@[@"Hub", @"Media", @"PiP", @"Actions", @"Jobs", @"History", @"Setup"]];
    _modeSegment.selectedSegmentIndex = 1;
    if (@available(iOS 13.0, *)) {
        _modeSegment.selectedSegmentTintColor = [BTTheme accentTeal];
        [_modeSegment setTitleTextAttributes:@{
            NSFontAttributeName: [UIFont systemFontOfSize:11 weight:UIFontWeightSemibold],
            NSForegroundColorAttributeName: [BTTheme textPrimary]
        } forState:UIControlStateNormal];
        [_modeSegment setTitleTextAttributes:@{
            NSFontAttributeName: [UIFont systemFontOfSize:11 weight:UIFontWeightBold],
            NSForegroundColorAttributeName: [BTTheme canvasColor]
        } forState:UIControlStateSelected];
    } else {
        _modeSegment.tintColor = [BTTheme accentTeal];
        [_modeSegment setTitleTextAttributes:@{NSFontAttributeName: [UIFont systemFontOfSize:11 weight:UIFontWeightSemibold]} forState:UIControlStateNormal];
    }
    [_modeSegment addTarget:self action:@selector(segmentChanged) forControlEvents:UIControlEventValueChanged];
    [_modeSegment setContentCompressionResistancePriority:UILayoutPriorityRequired forAxis:UILayoutConstraintAxisHorizontal];

    _topBarStack = [[UIStackView alloc] initWithArrangedSubviews:@[statusRow, _modeSegment]];
    _topBarStack.axis = UILayoutConstraintAxisHorizontal;
    _topBarStack.alignment = UIStackViewAlignmentCenter;
    _topBarStack.distribution = UIStackViewDistributionFill;
    _topBarStack.spacing = 10;
    _topBarStack.translatesAutoresizingMaskIntoConstraints = NO;
    [_topBar addSubview:_topBarStack];

    UILayoutGuide *safe = self.view.safeAreaLayoutGuide;
    [NSLayoutConstraint activateConstraints:@[
        [_topBar.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_topBar.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_topBar.topAnchor constraintEqualToAnchor:self.view.topAnchor],

        [_topBarStack.leadingAnchor constraintEqualToAnchor:safe.leadingAnchor constant:10],
        [_topBarStack.trailingAnchor constraintEqualToAnchor:safe.trailingAnchor constant:-10],
        [_topBarStack.topAnchor constraintEqualToAnchor:safe.topAnchor constant:4],
        [_topBarStack.bottomAnchor constraintEqualToAnchor:_topBar.bottomAnchor constant:-6]
    ]];
}

- (void)setupWorkspaceViews {
    _workspaceContainer = [UIView new];
    _workspaceContainer.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:_workspaceContainer];

    _workspaceTopToBar = [_workspaceContainer.topAnchor constraintEqualToAnchor:_topBar.bottomAnchor];
    _workspaceTopToView = [_workspaceContainer.topAnchor constraintEqualToAnchor:self.view.topAnchor];
    [NSLayoutConstraint activateConstraints:@[
        _workspaceTopToBar,
        [_workspaceContainer.leadingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.leadingAnchor],
        [_workspaceContainer.trailingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.trailingAnchor],
        [_workspaceContainer.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor]
    ]];

    __weak typeof(self) weakSelf = self;

    _idleHubView = [BTIdleHubView new];
    _idleHubView.onNavigateMode = ^(NSString *modeKey) {
        if ([modeKey isEqualToString:@"media"]) [weakSelf switchWorkspaceMode:BTWorkspaceMedia];
        else if ([modeKey isEqualToString:@"actions"]) [weakSelf switchWorkspaceMode:BTWorkspaceActions];
        else if ([modeKey isEqualToString:@"history"]) [weakSelf switchWorkspaceMode:BTWorkspaceHistory];
    };
    _idleHubView.onActionInvoked = ^(NSString *actionId) {
        [weakSelf invokeQuickAction:actionId];
    };

    _nowPlayingView = [BTNowPlayingView new];
    _nowPlayingView.onPlayPauseTapped = ^{ [weakSelf handlePlayPause]; };
    _nowPlayingView.onPreviousTapped = ^{ [weakSelf sendWindowsMediaCommand:@"previous" seconds:0]; };
    _nowPlayingView.onNextTapped = ^{ [weakSelf sendWindowsMediaCommand:@"next" seconds:0]; };
    _nowPlayingView.onSeekCommitted = ^(double seconds) { [weakSelf handleSeek:seconds]; };
    _nowPlayingView.onReturnToWindowsTapped = ^{ [weakSelf handleReturnToWindows]; };
    _nowPlayingView.onOpenVideoFocusTapped = ^{ [weakSelf switchWorkspaceMode:BTWorkspaceVideoFocus]; };
    _nowPlayingView.onFocusPolicySelected = ^(NSString *policy) { [weakSelf applyLocalFocusPolicy:policy]; };
    _nowPlayingView.onQualityCapSelected = ^(NSString *cap) { [weakSelf applyQualityCap:cap]; };
    _nowPlayingView.onKeepAwakeToggled = ^(BOOL awake) { [weakSelf applyKeepAwake:awake]; };

    _quickActionsView = [BTQuickActionsView new];
    _quickActionsView.onActionInvoked = ^(NSString *actionId) {
        [weakSelf invokeQuickAction:actionId];
    };

    _workerStatusView = [BTWorkerStatusView new];
    _workerStatusView.onCancelJobTapped = ^(NSString *jobId) {
        [weakSelf.agent command:@"job.cancel" body:@{@"jobId": jobId}];
    };

    _historyPanelView = [BTHistoryPanelView new];
    _historyPanelView.onClearTapped = ^{
        [weakSelf clearNotificationHistory];
    };
    _historyPanelView.onReplayFullscreenTapped = ^(NSDictionary *event) {
        BTViewController *strong = weakSelf;
        if (!strong) return;
        NSMutableDictionary *replay = [event mutableCopy];
        replay[@"priority"] = @3;
        [strong->_notificationOverlay presentNotification:replay expiresInterval:30.0];
        [strong setNeedsStatusBarAppearanceUpdate];
    };

    _settingsView = [BTSettingsView new];
    _settingsView.onPairTapped = ^{ [weakSelf pair]; };
    _settingsView.onScanQrTapped = ^{ [weakSelf scanQrCode]; };
    _settingsView.onEvictCacheTapped = ^{
        BTViewController *strong = weakSelf;
        BTDirectMediaPlayer *p = strong ? strong->_directPlayer : nil;
        [p handleMemoryPressure];
    };
    _settingsView.onClearHistoryTapped = ^{
        [weakSelf clearNotificationHistory];
    };

    for (UIView *panel in @[_idleHubView, _nowPlayingView, _quickActionsView, _workerStatusView, _historyPanelView, _settingsView]) {
        panel.translatesAutoresizingMaskIntoConstraints = NO;
        [_workspaceContainer addSubview:panel];
        [NSLayoutConstraint activateConstraints:@[
            [panel.leadingAnchor constraintEqualToAnchor:_workspaceContainer.leadingAnchor],
            [panel.trailingAnchor constraintEqualToAnchor:_workspaceContainer.trailingAnchor],
            [panel.topAnchor constraintEqualToAnchor:_workspaceContainer.topAnchor],
            [panel.bottomAnchor constraintEqualToAnchor:_workspaceContainer.bottomAnchor]
        ]];
    }

    _embedHostView = [UIView new];
    _embedHostView.hidden = YES;
    _embedHostView.translatesAutoresizingMaskIntoConstraints = NO;
    [self.view addSubview:_embedHostView];
    [NSLayoutConstraint activateConstraints:@[
        [_embedHostView.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_embedHostView.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_embedHostView.topAnchor constraintEqualToAnchor:_topBar.bottomAnchor],
        [_embedHostView.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor]
    ]];
}

- (void)setupVideoSurfaceAndOverlay {
    __weak typeof(self) weakSelf = self;

    _videoSurfaceView = [BTVideoSurfaceView new];
    _videoSurfaceView.translatesAutoresizingMaskIntoConstraints = NO;
    _videoSurfaceView.onPlayPauseTapped = ^{ [weakSelf handlePlayPause]; };
    _videoSurfaceView.onSeekCommitted = ^(double seconds) { [weakSelf handleSeek:seconds]; };
    _videoSurfaceView.onReturnToWindowsTapped = ^{ [weakSelf handleReturnToWindows]; };
    _videoSurfaceView.onExpandFocusTapped = ^{ [weakSelf switchWorkspaceMode:BTWorkspaceVideoFocus]; };
    _videoSurfaceView.onCloseFocusTapped = ^{ [weakSelf switchWorkspaceMode:BTWorkspaceMedia]; };
    _videoSurfaceView.onAmbientToggleTapped = ^{
        BTViewController *strong = weakSelf;
        if (!strong) return;
        [strong switchWorkspaceMode:strong->_workspaceMode == BTWorkspaceAmbient ? BTWorkspaceVideoFocus : BTWorkspaceAmbient];
    };
    [self.view addSubview:_videoSurfaceView];

    UIView *slot = _nowPlayingView.videoSlotView;
    _compactVideoConstraints = @[
        [_videoSurfaceView.leadingAnchor constraintEqualToAnchor:slot.leadingAnchor],
        [_videoSurfaceView.trailingAnchor constraintEqualToAnchor:slot.trailingAnchor],
        [_videoSurfaceView.topAnchor constraintEqualToAnchor:slot.topAnchor],
        [_videoSurfaceView.bottomAnchor constraintEqualToAnchor:slot.bottomAnchor]
    ];
    _fullscreenVideoConstraints = @[
        [_videoSurfaceView.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_videoSurfaceView.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_videoSurfaceView.topAnchor constraintEqualToAnchor:self.view.topAnchor],
        [_videoSurfaceView.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor]
    ];

    _notificationOverlay = [BTNotificationOverlayView new];
    _notificationOverlay.translatesAutoresizingMaskIntoConstraints = NO;
    _notificationOverlay.onDismissed = ^(NSInteger priority) {
        (void)priority;
        BTViewController *strong = weakSelf;
        if (!strong) return;
        [strong setNeedsStatusBarAppearanceUpdate];
        if (@available(iOS 11.0, *)) {
            [strong setNeedsUpdateOfHomeIndicatorAutoHidden];
        }
    };
    [self.view addSubview:_notificationOverlay];
    [NSLayoutConstraint activateConstraints:@[
        [_notificationOverlay.leadingAnchor constraintEqualToAnchor:self.view.leadingAnchor],
        [_notificationOverlay.trailingAnchor constraintEqualToAnchor:self.view.trailingAnchor],
        [_notificationOverlay.topAnchor constraintEqualToAnchor:self.view.topAnchor],
        [_notificationOverlay.bottomAnchor constraintEqualToAnchor:self.view.bottomAnchor]
    ]];
}

- (void)viewDidLayoutSubviews {
    [super viewDidLayoutSubviews];
    BOOL wide = self.view.bounds.size.width >= 520.0;
    UILayoutConstraintAxis target = wide ? UILayoutConstraintAxisHorizontal : UILayoutConstraintAxisVertical;
    if (_topBarStack.axis != target) {
        _topBarStack.axis = target;
        _topBarStack.alignment = wide ? UIStackViewAlignmentCenter : UIStackViewAlignmentFill;
        _topBarStack.spacing = wide ? 10 : 4;
    }
}

- (BOOL)prefersStatusBarHidden {
    if (_notificationOverlay.fullscreenActive) return NO;
    return _workspaceMode == BTWorkspaceVideoFocus || _workspaceMode == BTWorkspaceAmbient;
}

- (BOOL)prefersHomeIndicatorAutoHidden {
    if (_notificationOverlay.fullscreenActive) return NO;
    return _workspaceMode == BTWorkspaceVideoFocus || _workspaceMode == BTWorkspaceAmbient;
}

- (UIRectEdge)preferredScreenEdgesDeferringSystemGestures {
    if (_notificationOverlay.fullscreenActive) return UIRectEdgeNone;
    return (_workspaceMode == BTWorkspaceVideoFocus || _workspaceMode == BTWorkspaceAmbient) ? UIRectEdgeBottom : UIRectEdgeNone;
}

- (void)segmentChanged {
    switch (_modeSegment.selectedSegmentIndex) {
        case 0: [self switchWorkspaceMode:BTWorkspaceHub]; break;
        case 1: [self switchWorkspaceMode:BTWorkspaceMedia]; break;
        case 2: [self switchWorkspaceMode:BTWorkspaceVideoFocus]; break;
        case 3: [self switchWorkspaceMode:BTWorkspaceActions]; break;
        case 4: [self switchWorkspaceMode:BTWorkspaceWorker]; break;
        case 5: [self switchWorkspaceMode:BTWorkspaceHistory]; break;
        case 6: [self switchWorkspaceMode:BTWorkspaceSettings]; break;
        default: break;
    }
}

- (void)switchWorkspaceMode:(BTWorkspaceMode)mode {
    _workspaceMode = mode;

    BOOL fullscreen = (mode == BTWorkspaceVideoFocus || mode == BTWorkspaceAmbient);
    _topBar.hidden = fullscreen;
    _workspaceTopToBar.active = !fullscreen;
    _workspaceTopToView.active = fullscreen;

    _idleHubView.hidden = (mode != BTWorkspaceHub);
    _nowPlayingView.hidden = (mode != BTWorkspaceMedia);
    _quickActionsView.hidden = (mode != BTWorkspaceActions);
    _workerStatusView.hidden = (mode != BTWorkspaceWorker);
    _historyPanelView.hidden = (mode != BTWorkspaceHistory);
    _settingsView.hidden = (mode != BTWorkspaceSettings);

    NSInteger seg = UISegmentedControlNoSegment;
    NSString *tabName = @"now";
    switch (mode) {
        case BTWorkspaceHub: seg = 0; tabName = @"hub"; break;
        case BTWorkspaceMedia: seg = 1; tabName = @"now"; break;
        case BTWorkspaceVideoFocus: seg = 2; tabName = @"video"; break;
        case BTWorkspaceAmbient: seg = 2; tabName = @"ambient"; break;
        case BTWorkspaceActions: seg = 3; tabName = @"actions"; break;
        case BTWorkspaceWorker: seg = 4; tabName = @"worker"; break;
        case BTWorkspaceHistory: seg = 5; tabName = @"history"; break;
        case BTWorkspaceSettings: seg = 6; tabName = @"settings"; break;
    }
    _modeSegment.selectedSegmentIndex = seg;

    BOOL independentVideo = _directPlayer.independentActive && _directPlayer.hasVideoTrack;
    BOOL showVideoSurface = fullscreen || (mode == BTWorkspaceMedia && independentVideo && !_embedController);

    [NSLayoutConstraint deactivateConstraints:_compactVideoConstraints];
    [NSLayoutConstraint deactivateConstraints:_fullscreenVideoConstraints];

    if (fullscreen) {
        [NSLayoutConstraint activateConstraints:_fullscreenVideoConstraints];
        _videoSurfaceView.presentationMode = (mode == BTWorkspaceAmbient) ? BTVideoPresentationAmbient : BTVideoPresentationFocus;
        _videoSurfaceView.hidden = NO;
    } else if (showVideoSurface) {
        [NSLayoutConstraint activateConstraints:_compactVideoConstraints];
        _videoSurfaceView.presentationMode = BTVideoPresentationCompact;
        _videoSurfaceView.hidden = NO;
    } else {
        _videoSurfaceView.hidden = YES;
    }

    if (!_embedHostView.hidden) {
        [self.view bringSubviewToFront:_embedHostView];
    }
    if (!fullscreen) {
        [self.view bringSubviewToFront:_topBar];
    }
    [self.view bringSubviewToFront:_notificationOverlay];

    [self setNeedsStatusBarAppearanceUpdate];
    if (@available(iOS 11.0, *)) {
        [self setNeedsUpdateOfHomeIndicatorAutoHidden];
        [self setNeedsUpdateOfScreenEdgesDeferringSystemGestures];
    }

    [self.agent sendViewportTab:tabName awake:YES dimmed:(mode == BTWorkspaceAmbient)];
}

- (void)handlePlayPause {
    if (_directPlayer.independentActive) {
        [_directPlayer togglePlayPause];
    } else {
        [self sendWindowsMediaCommand:@"toggle" seconds:0];
    }
}

- (void)handleSeek:(double)seconds {
    if (_directPlayer.independentActive) {
        [_directPlayer seekToSeconds:seconds];
    } else {
        [self sendWindowsMediaCommand:@"seek" seconds:seconds];
    }
}

- (void)sendWindowsMediaCommand:(NSString *)command seconds:(double)seconds {
    NSDictionary *session = [_latestHostMediaState[@"session"] isKindOfClass:NSDictionary.class] ? _latestHostMediaState[@"session"] : @{};
    NSMutableDictionary *body = [@{@"command": command} mutableCopy];
    if ([command isEqualToString:@"seek"]) body[@"seconds"] = @(seconds);
    if ([session[@"source"] isKindOfClass:NSString.class]) body[@"source"] = session[@"source"];
    [self.agent command:@"media.command" body:body];
}

- (void)handleReturnToWindows {
    double pos = 0;
    if (_embedController) {
        [self teardownYouTubeEmbed];
    } else {
        pos = [_directPlayer stopAndReturnToWindows];
    }
    [self.agent sendMediaMessage:@"media.return_to_windows" body:@{
        @"resumeWindows": @YES,
        @"positionSeconds": @(pos)
    }];
    if (_workspaceMode == BTWorkspaceVideoFocus || _workspaceMode == BTWorkspaceAmbient) {
        [self switchWorkspaceMode:BTWorkspaceMedia];
    }
}

- (void)applyLocalFocusPolicy:(NSString *)policy {
    _focusPolicy = [policy copy] ?: @"Auto";
    BOOL mute = [_focusPolicy isEqualToString:@"KeepVideoMuteAudio"] || [_focusPolicy isEqualToString:@"LivePcmOnly"];
    BOOL duck = [_focusPolicy isEqualToString:@"DuckVideo"] || [_focusPolicy isEqualToString:@"LivePcmPrioritized"];
    float vol = mute ? 0.0f : (duck ? 0.35f : 1.0f);
    [[BTAudioSessionCoordinator sharedCoordinator] updateFocusPolicy:_focusPolicy muteIndependent:mute independentVolume:vol duck:duck];
    [_directPlayer applyAudioFocusMute:mute volume:vol];
    [self.agent sendMediaMessage:@"media.focus.set" body:@{@"policy": _focusPolicy}];
    [self refreshMediaUI];
}

- (void)applyQualityCap:(NSString *)cap {
    [_directPlayer applyQualityCap:cap];
    [self refreshMediaUI];
}

- (void)applyKeepAwake:(BOOL)awake {
    _directPlayer.keepScreenAwake = awake;
    [self refreshMediaUI];
}

- (void)invokeQuickAction:(NSString *)actionId {
    if (!actionId.length) return;
    [self.agent command:@"action.invoke" body:@{@"actionId": actionId}];
}

- (void)handleDirectPlayerSnapshot:(NSDictionary *)snapshot {
    _latestPlayerSnapshot = snapshot ?: @{@"independentActive": @NO};
    [_videoSurfaceView updateWithSnapshot:_latestPlayerSnapshot usbConnected:_usbConnected];

    NSString *sid = BTSafeStr(_latestPlayerSnapshot[@"sessionId"], @"");
    BOOL active = [_latestPlayerSnapshot[@"independentActive"] boolValue];
    BOOL hasVideo = [_latestPlayerSnapshot[@"readyForDisplay"] boolValue] || [_latestPlayerSnapshot[@"videoWidth"] intValue] > 0;
    if (active && hasVideo && sid.length && ![sid isEqualToString:_autoEnteredSessionId]) {
        _autoEnteredSessionId = [sid copy];
        _autoEnteredVideoForSession = YES;
        [self switchWorkspaceMode:BTWorkspaceVideoFocus];
    } else if (_workspaceMode == BTWorkspaceMedia) {
        [self switchWorkspaceMode:BTWorkspaceMedia];
    }

    [self refreshMediaUI];
}

- (void)presentYouTubeEmbedFromPayload:(NSDictionary *)body {
    [self teardownYouTubeEmbed];
    NSURL *url = [NSURL URLWithString:BTSafeStr(body[@"url"], @"")];
    if (!url) return;

    _embedSessionId = [BTSafeStr(body[@"sessionId"], @"") copy];
    _embedGeneration = [body[@"generation"] unsignedLongLongValue];
    NSString *title = BTSafeStr(body[@"title"], @"YouTube Embed");

    [[BTAudioSessionCoordinator sharedCoordinator] activateForIndependentMedia:nil];

    BTYouTubeEmbedController *embed = [[BTYouTubeEmbedController alloc] initWithEmbedURL:url
                                                                                   title:title
                                                                               sessionId:_embedSessionId
                                                                              generation:_embedGeneration];
    __weak typeof(self) weakSelf = self;
    embed.onConfirmed = ^(NSString *sid, uint64_t gen) {
        [weakSelf.agent sendMediaMessage:@"media.handoff.status" body:@{
            @"sessionId": sid,
            @"generation": @(gen),
            @"stage": @"confirmed",
            @"isPlaying": @YES,
            @"readyForDisplay": @YES
        }];
    };
    embed.onFallbackRequested = ^(NSString *sid, uint64_t gen, NSString *reason) {
        BTViewController *strong = weakSelf;
        if (!strong) return;
        [strong teardownYouTubeEmbed];
        [strong.agent sendMediaMessage:@"media.handoff.status" body:@{
            @"sessionId": sid,
            @"generation": @(gen),
            @"stage": @"returned",
            @"reason": reason ?: @"Embed closed",
            @"fallbackSuggested": @YES
        }];
        [strong.agent sendMediaMessage:@"media.return_to_windows" body:@{@"resumeWindows": @YES}];
        [strong refreshMediaUI];
    };

    _embedController = embed;
    [self addChildViewController:embed];
    embed.view.translatesAutoresizingMaskIntoConstraints = NO;
    [_embedHostView addSubview:embed.view];
    [NSLayoutConstraint activateConstraints:@[
        [embed.view.leadingAnchor constraintEqualToAnchor:_embedHostView.leadingAnchor],
        [embed.view.trailingAnchor constraintEqualToAnchor:_embedHostView.trailingAnchor],
        [embed.view.topAnchor constraintEqualToAnchor:_embedHostView.topAnchor],
        [embed.view.bottomAnchor constraintEqualToAnchor:_embedHostView.bottomAnchor]
    ]];
    [embed didMoveToParentViewController:self];
    _embedHostView.hidden = NO;
    [self.view bringSubviewToFront:_embedHostView];
    [self.view bringSubviewToFront:_topBar];
    [self.view bringSubviewToFront:_notificationOverlay];
    [self refreshMediaUI];
}

- (void)teardownYouTubeEmbed {
    if (!_embedController) return;
    [_embedController willMoveToParentViewController:nil];
    [_embedController.view removeFromSuperview];
    [_embedController removeFromParentViewController];
    _embedController = nil;
    _embedHostView.hidden = YES;
    _embedSessionId = nil;
    _embedGeneration = 0;
    [[BTAudioSessionCoordinator sharedCoordinator] deactivateIndependentMedia];
}

- (void)receive:(NSString *)type body:(NSDictionary *)body {
    if (!self.isViewLoaded) [self loadViewIfNeeded];
    if (!_directPlayer && _agent) [self bindAgent:_agent];

    if ([type isEqualToString:@"connection"]) {
        NSString *state = BTSafeStr(body[@"state"], @"");
        NSString *route = BTSafeStr(body[@"route"], @"");
        BOOL connected = [state hasPrefix:@"Connected"];
        _usbConnected = connected;
        [_directPlayer setUsbConnected:connected];

        if (connected) {
            BOOL isWifi = [route isEqualToString:@"Wi-Fi"] || [state containsString:@"Wi-Fi"];
            _statusDot.backgroundColor = isWifi ? [BTTheme accentTeal] : [BTTheme accentGreen];
            _connectionText = isWifi ? (state.length ? state : @"Wi-Fi TLS Connected") : @"USB Connected";
            [self reportPhoneHealth];
        } else if (_directPlayer.independentActive || _embedController != nil) {
            _statusDot.backgroundColor = [BTTheme accentTeal];
            _connectionText = @"Wi-Fi Independent · Host Away";
        } else {
            _statusDot.backgroundColor = [BTTheme accentAmber];
            _connectionText = state.length ? state : @"Waiting for USB or Wi-Fi host";
        }
        _connectionLabel.text = _connectionText;
        [self refreshAuxiliaryDiagnostics];
    } else if ([type isEqualToString:@"transport.wifi.provision"]) {
        NSString *pcName = BTSafeStr(body[@"pcName"], @"Windows PC");
        NSString *code = BTSafeStr(body[@"checkCode"], @"");
        [_quickActionsView showFeedbackText:[NSString stringWithFormat:@"Wi-Fi failover ready for %@ (%@)", pcName, code]];
    } else if ([type isEqualToString:@"rule.action"]) {
        NSString *action = BTSafeStr(body[@"action"], @"");
        NSString *target = BTSafeStr(body[@"target"], @"");
        if ([action isEqualToString:@"goto"]) {
            if ([target isEqualToString:@"hub"]) [self switchWorkspaceMode:BTWorkspaceHub];
            else if ([target isEqualToString:@"media"] || [target isEqualToString:@"now"]) [self switchWorkspaceMode:BTWorkspaceMedia];
            else if ([target isEqualToString:@"pip"] || [target isEqualToString:@"video"]) [self switchWorkspaceMode:BTWorkspaceVideoFocus];
            else if ([target isEqualToString:@"ambient"]) [self switchWorkspaceMode:BTWorkspaceAmbient];
            else if ([target isEqualToString:@"actions"]) [self switchWorkspaceMode:BTWorkspaceActions];
            else if ([target isEqualToString:@"worker"] || [target isEqualToString:@"jobs"]) [self switchWorkspaceMode:BTWorkspaceWorker];
            else if ([target isEqualToString:@"history"]) [self switchWorkspaceMode:BTWorkspaceHistory];
            else if ([target isEqualToString:@"settings"]) [self switchWorkspaceMode:BTWorkspaceSettings];
        } else if ([action isEqualToString:@"ambient"]) {
            [self switchWorkspaceMode:BTWorkspaceAmbient];
        }
    } else if ([type isEqualToString:@"offload.pip.suggest"]) {
        NSString *reason = BTSafeStr(body[@"reason"], @"High PC load detected — External PiP on iPhone can save PC CPU/GPU.");
        NSDictionary *hintEvent = @{
            @"id": [[[NSUUID UUID] UUIDString] lowercaseString],
            @"title": @"Smart Offload Suggestion",
            @"body": reason,
            @"source": @"BackThread Offload",
            @"priority": @1
        };
        [_notificationOverlay presentNotification:hintEvent expiresInterval:8.0];
    } else if ([type isEqualToString:@"media.state"]) {
        _latestHostMediaState = body ?: @{};
        [self refreshMediaUI];
    } else if ([type isEqualToString:@"media.buffered.start"]) {
        [_directPlayer prepareBufferedStream:body ?: @{}];
    } else if ([type isEqualToString:@"media.buffered.eof"]) {
        [_directPlayer.bufferedEngine markEndOfStreamForStreamId:BTSafeStr(body[@"streamId"], @"")];
    } else if ([type isEqualToString:@"media.handoff"]) {
        if ([body[@"experimentalEmbed"] boolValue]) {
            [self presentYouTubeEmbedFromPayload:body];
        } else {
            [self teardownYouTubeEmbed];
            [_directPlayer startHandoffWithPayload:body ?: @{}];
        }
    } else if ([type isEqualToString:@"media.handoff.cancel"]) {
        [self teardownYouTubeEmbed];
        [_directPlayer cancelHandoffWithReason:BTSafeStr(body[@"reason"], @"Cancelled by Windows")];
        if (_workspaceMode == BTWorkspaceVideoFocus || _workspaceMode == BTWorkspaceAmbient) {
            [self switchWorkspaceMode:BTWorkspaceMedia];
        }
    } else if ([type isEqualToString:@"media.return_to_windows"]) {
        [self teardownYouTubeEmbed];
        [_directPlayer stopAndReturnToWindows];
        if (_workspaceMode == BTWorkspaceVideoFocus || _workspaceMode == BTWorkspaceAmbient) {
            [self switchWorkspaceMode:BTWorkspaceMedia];
        }
    } else if ([type isEqualToString:@"media.focus.set"]) {
        NSString *policy = BTSafeStr(body[@"policy"], @"Auto");
        _focusPolicy = [policy copy];
        BOOL mute = [body[@"muteVideoAudio"] boolValue];
        BOOL duck = [body[@"duckVideo"] boolValue];
        float vol = body[@"volume"] ? [body[@"volume"] floatValue] : (duck ? 0.35f : 1.0f);
        [[BTAudioSessionCoordinator sharedCoordinator] updateFocusPolicy:policy muteIndependent:mute independentVolume:vol duck:duck];
        [_directPlayer applyAudioFocusMute:mute volume:vol];
        [self refreshMediaUI];
    } else if ([type isEqualToString:@"media.mode.select"]) {
        NSMutableDictionary *merged = [_latestHostMediaState mutableCopy] ?: [NSMutableDictionary new];
        if (body[@"mode"]) merged[@"mode"] = body[@"mode"];
        if (body[@"provider"]) merged[@"provider"] = body[@"provider"];
        _latestHostMediaState = merged;
        [self refreshMediaUI];
    } else if ([type isEqualToString:@"media.command"]) {
        [_directPlayer applyRemoteCommand:body ?: @{}];
    } else if ([type isEqualToString:@"action.registry"]) {
        NSArray *raw = [body[@"actions"] isKindOfClass:NSArray.class] ? body[@"actions"] : @[];
        NSMutableArray *filtered = [NSMutableArray new];
        for (id item in raw) {
            if (filtered.count >= 32) break;
            if ([item isKindOfClass:NSDictionary.class] && [item[@"id"] isKindOfClass:NSString.class] && [item[@"title"] isKindOfClass:NSString.class]) {
                [filtered addObject:item];
            }
        }
        _actions = filtered;
        [_quickActionsView updateActions:_actions];
        [self refreshAuxiliaryDiagnostics];
    } else if ([type isEqualToString:@"job.progress"]) {
        _latestJob = body ?: @{};
        [self refreshAuxiliaryDiagnostics];
    } else if ([type isEqualToString:@"event.publish"]) {
        [self presentNotificationEvent:body ?: @{}];
    } else if ([type isEqualToString:@"error"]) {
        NSString *err = BTSafeStr(body[@"error"], @"The host rejected the request");
        _connectionLabel.text = err;
        [_quickActionsView showFeedbackText:err];
    }
}

- (NSDate *)expiryForEvent:(NSDictionary *)event {
    NSISO8601DateFormatter *f = [NSISO8601DateFormatter new];
    f.formatOptions = NSISO8601DateFormatWithInternetDateTime | NSISO8601DateFormatWithFractionalSeconds;
    NSDate *date = [f dateFromString:BTSafeStr(event[@"expiresUtc"], @"")];
    if (!date) {
        f.formatOptions = NSISO8601DateFormatWithInternetDateTime;
        date = [f dateFromString:BTSafeStr(event[@"expiresUtc"], @"")];
    }
    return date;
}

- (void)pruneExpiredEvents {
    NSIndexSet *expired = [_events indexesOfObjectsPassingTest:^BOOL(NSDictionary *ev, NSUInteger idx, BOOL *stop) {
        (void)idx; (void)stop;
        NSDate *exp = [self expiryForEvent:ev];
        return !exp || exp.timeIntervalSinceNow <= 0;
    }];
    if (expired.count > 0) {
        [_events removeObjectsAtIndexes:expired];
    }
}

- (void)presentNotificationEvent:(NSDictionary *)event {
    [_events addObject:event];
    while (_events.count > 64) [_events removeObjectAtIndex:0];
    [self pruneExpiredEvents];
    [_historyPanelView updateWithEvents:_events];
    [self refreshAuxiliaryDiagnostics];

    NSInteger priority = [event[@"priority"] integerValue];
    if (priority <= 0) return;

    NSDate *exp = [self expiryForEvent:event];
    NSTimeInterval remaining = exp ? exp.timeIntervalSinceNow : 8.0;
    [_notificationOverlay presentNotification:event expiresInterval:remaining];
    [self setNeedsStatusBarAppearanceUpdate];
    if (@available(iOS 11.0, *)) {
        [self setNeedsUpdateOfHomeIndicatorAutoHidden];
    }
}

- (void)clearNotificationHistory {
    [_events removeAllObjects];
    [_historyPanelView updateWithEvents:@[]];
    [self refreshAuxiliaryDiagnostics];
}

- (void)refreshMediaUI {
    [_nowPlayingView updateWithHostMediaState:_latestHostMediaState
                               playerSnapshot:_latestPlayerSnapshot
                                 embedActive:(_embedController != nil)
                                 focusPolicy:_focusPolicy];
    [self refreshAuxiliaryDiagnostics];
}

- (void)refreshAuxiliaryDiagnostics {
    [self pruneExpiredEvents];
    NSDictionary *pcmStats = [_agent.audio statistics] ?: @{};
    NSDictionary *sessionStatus = [[BTAudioSessionCoordinator sharedCoordinator] statusSnapshot];
    NSDictionary *hostSession = [_latestHostMediaState[@"session"] isKindOfClass:NSDictionary.class]
        ? _latestHostMediaState[@"session"]
        : @{};
    NSString *mediaTitle = [_latestPlayerSnapshot[@"independentActive"] boolValue]
        ? BTSafeStr(_latestPlayerSnapshot[@"title"], @"External PiP")
        : BTSafeStr(hostSession[@"title"], @"");
    NSString *mediaMode = [_latestPlayerSnapshot[@"independentActive"] boolValue]
        ? BTSafeStr(_latestPlayerSnapshot[@"mode"], @"DirectInternet")
        : BTSafeStr(_latestHostMediaState[@"mode"], @"LivePcm");

    [_idleHubView updateConnectionText:_connectionText
                          usbConnected:_usbConnected
                              pcmStats:pcmStats
                           mediaTitle:mediaTitle
                           mediaMode:mediaMode
                              actions:_actions
                              lastJob:_latestJob
                         recentEvents:_events];

    NSDictionary *bufInfo = @{
        @"streamId": _directPlayer.bufferedEngine.activeStreamId ?: @"",
        @"receivedBytes": @(_directPlayer.bufferedEngine.receivedBytes),
        @"totalBytes": @(_directPlayer.bufferedEngine.totalBytes),
        @"eofReached": @(_directPlayer.bufferedEngine.eofReached)
    };
    [_workerStatusView updateWithJob:_latestJob
                      memoryPressure:_agent.memoryPressure
                      bufferedStatus:bufInfo];

    [_settingsView updateDiagnosticsWithConnection:_connectionText
                                          pcmStats:pcmStats
                                     sessionStatus:sessionStatus];
}

- (void)reportPhoneHealth {
    float level = [UIDevice currentDevice].batteryLevel;
    if (level < 0) return;
    int pct = (int)llroundf(level * 100.0f);
    UIDeviceBatteryState st = [UIDevice currentDevice].batteryState;
    BOOL charging = (st == UIDeviceBatteryStateCharging || st == UIDeviceBatteryStateFull);
    [self.agent sendPhoneHealthWithBatteryLevel:pct charging:charging];
}

- (void)scanQrCode {
    AVAuthorizationStatus status = [AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo];
    if (status == AVAuthorizationStatusNotDetermined) {
        __weak typeof(self) weakSelf = self;
        [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo completionHandler:^(BOOL granted) {
            dispatch_async(dispatch_get_main_queue(), ^{
                BTViewController *strong = weakSelf;
                if (!strong) return;
                if (granted) [strong presentQrScanner];
                else strong->_connectionLabel.text = @"Camera permission denied for QR scan";
            });
        }];
        return;
    }
    if (status != AVAuthorizationStatusAuthorized) {
        _connectionLabel.text = @"Enable Camera access in Settings to scan QR";
        return;
    }
    [self presentQrScanner];
}

- (void)presentQrScanner {
    BTQrScannerViewController *scanner = [BTQrScannerViewController new];
    scanner.modalPresentationStyle = UIModalPresentationFullScreen;
    __weak typeof(self) weakSelf = self;
    scanner.onCodeScanned = ^(NSString *value) {
        NSURL *url = [NSURL URLWithString:value];
        if (url) [weakSelf handlePairingURL:url];
    };
    [self presentViewController:scanner animated:YES completion:nil];
}

- (void)handlePairingURL:(NSURL *)url {
    if (!self.isViewLoaded) [self loadViewIfNeeded];
    BTPairingProposal *proposal = [BTPairingProposal parseURI:url];
    if (!proposal) {
        _connectionLabel.text = @"Invalid BackThread QR code";
        return;
    }
    NSString *msg = [NSString stringWithFormat:@"Pair with %@ over pinned TLS Wi-Fi?\n\nVerify check code matches your PC:\n%@", proposal.pcName, proposal.checkCode];
    UIAlertController *confirm = [UIAlertController alertControllerWithTitle:@"Confirm Wi-Fi Pairing"
                                                                     message:msg
                                                              preferredStyle:UIAlertControllerStyleAlert];
    [confirm addAction:[UIAlertAction actionWithTitle:@"Cancel" style:UIAlertActionStyleCancel handler:nil]];
    __weak typeof(self) weakSelf = self;
    [confirm addAction:[UIAlertAction actionWithTitle:@"Confirm & Pair" style:UIAlertActionStyleDefault handler:^(UIAlertAction *a) {
        (void)a;
        BTViewController *strong = weakSelf;
        if (!strong) return;
        strong->_connectionLabel.text = [NSString stringWithFormat:@"Pairing with %@...", proposal.pcName];
        [strong.agent claimPairingURI:url completion:^(BOOL ok, NSString *message) {
            BTViewController *inner = weakSelf;
            if (!inner) return;
            inner->_connectionLabel.text = message ?: (ok ? @"Paired" : @"Pairing failed");
            if (ok) {
                inner->_statusDot.backgroundColor = [BTTheme accentTeal];
            }
            [inner refreshAuxiliaryDiagnostics];
        }];
    }]];
    [self presentViewController:confirm animated:YES completion:nil];
}

- (void)pair {
    UIAlertController *alert = [UIAlertController alertControllerWithTitle:@"Provision host key"
                                                                   message:@"Paste the 64-character session key created by BackThread.Host. It activates immediately without restarting."
                                                            preferredStyle:UIAlertControllerStyleAlert];
    [alert addTextFieldWithConfigurationHandler:^(UITextField *f) {
        f.secureTextEntry = YES;
        f.autocorrectionType = UITextAutocorrectionTypeNo;
        f.autocapitalizationType = UITextAutocapitalizationTypeNone;
    }];
    [alert addAction:[UIAlertAction actionWithTitle:@"Cancel" style:UIAlertActionStyleCancel handler:nil]];
    __weak typeof(self) weakSelf = self;
    __weak UIAlertController *weakAlert = alert;
    [alert addAction:[UIAlertAction actionWithTitle:@"Save key" style:UIAlertActionStyleDefault handler:^(UIAlertAction *a) {
        (void)a;
        BTViewController *strong = weakSelf;
        UIAlertController *strongAlert = weakAlert;
        if (!strong || !strongAlert) return;
        NSString *key = [strongAlert.textFields.firstObject.text stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
        NSCharacterSet *invalid = [[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet];
        if (key.length != 64 || [key rangeOfCharacterFromSet:invalid].location != NSNotFound) {
            strong->_connectionLabel.text = @"Key must contain 64 hex chars";
            return;
        }
        NSString *dir = [NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread"];
        [[NSFileManager defaultManager] createDirectoryAtPath:dir
                                  withIntermediateDirectories:YES
                                                   attributes:@{NSFileProtectionKey: NSFileProtectionCompleteUntilFirstUserAuthentication}
                                                        error:nil];
        NSString *path = [dir stringByAppendingPathComponent:@"session.key"];
        NSError *error = nil;
        if (![key writeToFile:path atomically:YES encoding:NSASCIIStringEncoding error:&error]) {
            strong->_connectionLabel.text = error.localizedDescription ?: @"Save failed";
            return;
        }
        [[NSFileManager defaultManager] setAttributes:@{
            NSFilePosixPermissions: @0600,
            NSFileProtectionKey: NSFileProtectionCompleteUntilFirstUserAuthentication
        } ofItemAtPath:path error:nil];

        NSMutableData *keyBytes = [NSMutableData dataWithLength:32];
        uint8_t *dst = (uint8_t *)keyBytes.mutableBytes;
        for (NSUInteger i = 0; i < 32; i++) {
            unsigned int byte = 0;
            [[NSScanner scannerWithString:[key substringWithRange:NSMakeRange(i * 2, 2)]] scanHexInt:&byte];
            dst[i] = (uint8_t)byte;
        }
        if (strong.onKeyUpdated) {
            strong.onKeyUpdated(keyBytes);
        } else {
            [strong.agent updateKey:keyBytes];
            [strong.agent start];
        }
        strong->_connectionLabel.text = @"Key saved and activated — waiting for host";
    }]];
    [self presentViewController:alert animated:YES completion:nil];
}

- (void)didReceiveMemoryWarning {
    [super didReceiveMemoryWarning];
    [_nowPlayingView clearArtworkCache];
    [_notificationOverlay clearIconCache];
    if (_events.count > 16) {
        [_events removeObjectsInRange:NSMakeRange(0, _events.count - 16)];
    }
    [_historyPanelView updateWithEvents:_events];
    [_directPlayer handleMemoryPressure];
    self.agent.memoryPressure = YES;
    NSUInteger generation = ++_memoryWarningGeneration;
    __weak typeof(self) weakSelf = self;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 10 * NSEC_PER_SEC), dispatch_get_main_queue(), ^{
        BTViewController *strong = weakSelf;
        if (strong && generation == strong->_memoryWarningGeneration) {
            strong.agent.memoryPressure = NO;
        }
    });
}

@end
