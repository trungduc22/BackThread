#import "BTViewController.h"
#import "../Agent/BTAgent.h"
#import "../AudioReceiver/BTAudioReceiver.h"
#import <AVFoundation/AVFoundation.h>
#import <MediaPlayer/MediaPlayer.h>
#import <ImageIO/ImageIO.h>

@implementation BTViewController {
    UILabel *_connection,*_title,*_artist,*_source,*_workerStatus,*_timeline,*_noticeTitle,*_noticeBody;
    UIImageView *_art;
    UISlider *_seek;
    UIButton *_play,*_next,*_previous;
    UIStackView *_media,*_actions,*_worker,*_history;
    UISegmentedControl *_tabs;
    UIView *_notice;
    NSLayoutConstraint *_noticeCenter,*_noticeTop,*_noticeBottom;
    NSTimer *_noticeTimer;
    NSMutableArray *_events;
    NSDictionary *_session;
    NSString *_lastJob;
    NSString *_artworkKey;
    AVPlayer *_player;
}
- (UILabel *)label:(CGFloat)size {
    UILabel *l=[UILabel new];l.font=[UIFont systemFontOfSize:size];l.textColor=UIColor.whiteColor;l.numberOfLines=2;return l;
}
- (UIButton *)button:(NSString *)title selector:(SEL)selector {
    UIButton *b=[UIButton buttonWithType:UIButtonTypeSystem];[b setTitle:title forState:UIControlStateNormal];b.titleLabel.font=[UIFont systemFontOfSize:18 weight:UIFontWeightSemibold];[b setTitleColor:[UIColor colorWithRed:.35 green:.82 blue:.76 alpha:1] forState:UIControlStateNormal];[b.heightAnchor constraintGreaterThanOrEqualToConstant:44].active=YES;[b addTarget:self action:selector forControlEvents:UIControlEventTouchUpInside];return b;
}
- (void)viewDidLoad {
    [super viewDidLoad];self.view.backgroundColor=[UIColor colorWithWhite:.055 alpha:1];_events=[NSMutableArray new];
    _connection=[self label:12];_connection.text=@"BackThread · waiting for USB host";_connection.textColor=[UIColor colorWithWhite:.6 alpha:1];
    UIButton *settings=[self button:@"Pair" selector:@selector(pair)];
    UIStackView *top=[[UIStackView alloc] initWithArrangedSubviews:@[_connection,settings]];top.distribution=UIStackViewDistributionEqualSpacing;
    _art=[UIImageView new];_art.backgroundColor=[UIColor colorWithWhite:.12 alpha:1];_art.contentMode=UIViewContentModeScaleAspectFit;_art.layer.cornerRadius=8;_art.clipsToBounds=YES;[_art.widthAnchor constraintEqualToConstant:110].active=YES;[_art.heightAnchor constraintEqualToConstant:110].active=YES;
    _title=[self label:23];_title.font=[UIFont systemFontOfSize:23 weight:UIFontWeightSemibold];_title.text=@"Ready when you are";
    _artist=[self label:16];_artist.text=@"Play media on Windows to begin";_artist.textColor=[UIColor colorWithWhite:.7 alpha:1];_source=[self label:11];
    UIStackView *metadata=[[UIStackView alloc] initWithArrangedSubviews:@[_title,_artist,_source]];metadata.axis=UILayoutConstraintAxisVertical;metadata.spacing=6;
    UIStackView *track=[[UIStackView alloc] initWithArrangedSubviews:@[_art,metadata]];track.spacing=20;track.alignment=UIStackViewAlignmentCenter;
    _previous=[self button:@"Previous" selector:@selector(previous)];_play=[self button:@"Play / Pause" selector:@selector(toggle)];_next=[self button:@"Next" selector:@selector(next)];
    UIStackView *controls=[[UIStackView alloc] initWithArrangedSubviews:@[_previous,_play,_next]];controls.distribution=UIStackViewDistributionFillEqually;
    _seek=[UISlider new];_seek.enabled=NO;[_seek addTarget:self action:@selector(seek) forControlEvents:UIControlEventTouchUpInside|UIControlEventTouchUpOutside];_timeline=[self label:11];
    MPVolumeView *volume=[[MPVolumeView alloc] initWithFrame:CGRectZero];volume.showsRouteButton=YES;[volume.heightAnchor constraintEqualToConstant:30].active=YES;
    _media=[[UIStackView alloc] initWithArrangedSubviews:@[track,controls,_seek,_timeline,volume]];_media.axis=UILayoutConstraintAxisVertical;_media.spacing=4;
    _actions=[UIStackView new];_actions.axis=UILayoutConstraintAxisVertical;_actions.spacing=4;
    _workerStatus=[self label:18];_workerStatus.text=@"No background jobs";UIButton *cancel=[self button:@"Cancel active job" selector:@selector(cancel)];_worker=[[UIStackView alloc] initWithArrangedSubviews:@[_workerStatus,cancel]];_worker.axis=UILayoutConstraintAxisVertical;
    _history=[UIStackView new];_history.axis=UILayoutConstraintAxisVertical;_history.spacing=8;
    _tabs=[[UISegmentedControl alloc] initWithItems:@[@"Media",@"Actions",@"Worker",@"History"]];_tabs.selectedSegmentIndex=0;_tabs.tintColor=[UIColor colorWithRed:.35 green:.82 blue:.76 alpha:1];[_tabs addTarget:self action:@selector(tab) forControlEvents:UIControlEventValueChanged];
    UIStackView *content=[[UIStackView alloc] initWithArrangedSubviews:@[_media,_actions,_worker,_history]];content.axis=UILayoutConstraintAxisVertical;
    UIScrollView *scroll=[UIScrollView new];[scroll addSubview:content];content.translatesAutoresizingMaskIntoConstraints=NO;
    UIStackView *root=[[UIStackView alloc] initWithArrangedSubviews:@[top,scroll,_tabs]];root.axis=UILayoutConstraintAxisVertical;root.spacing=8;root.translatesAutoresizingMaskIntoConstraints=NO;[self.view addSubview:root];
    [NSLayoutConstraint activateConstraints:@[[root.leadingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.leadingAnchor constant:16],[root.trailingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.trailingAnchor constant:-16],[root.topAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.topAnchor constant:8],[root.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor constant:-8],[content.leadingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.leadingAnchor],[content.trailingAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.trailingAnchor],[content.topAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.topAnchor],[content.bottomAnchor constraintEqualToAnchor:scroll.contentLayoutGuide.bottomAnchor],[content.widthAnchor constraintEqualToAnchor:scroll.frameLayoutGuide.widthAnchor]]];
    _notice=[UIView new];_notice.backgroundColor=[UIColor colorWithWhite:.1 alpha:.98];_notice.layer.cornerRadius=12;_notice.hidden=YES;_notice.translatesAutoresizingMaskIntoConstraints=NO;[self.view addSubview:_notice];
    _noticeTitle=[self label:24];_noticeBody=[self label:17];_noticeBody.numberOfLines=5;UIButton *dismiss=[self button:@"Dismiss" selector:@selector(dismissNotice)];UIStackView *noticeStack=[[UIStackView alloc] initWithArrangedSubviews:@[_noticeTitle,_noticeBody,dismiss]];noticeStack.axis=UILayoutConstraintAxisVertical;noticeStack.spacing=12;noticeStack.translatesAutoresizingMaskIntoConstraints=NO;[_notice addSubview:noticeStack];
    _noticeCenter=[_notice.centerYAnchor constraintEqualToAnchor:self.view.centerYAnchor];_noticeTop=[_notice.topAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.topAnchor constant:8];_noticeBottom=[_notice.bottomAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.bottomAnchor constant:-8];
    [NSLayoutConstraint activateConstraints:@[[_notice.leadingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.leadingAnchor constant:12],[_notice.trailingAnchor constraintEqualToAnchor:self.view.safeAreaLayoutGuide.trailingAnchor constant:-12],_noticeCenter,[noticeStack.leadingAnchor constraintEqualToAnchor:_notice.leadingAnchor constant:20],[noticeStack.trailingAnchor constraintEqualToAnchor:_notice.trailingAnchor constant:-20],[noticeStack.topAnchor constraintEqualToAnchor:_notice.topAnchor constant:16],[noticeStack.bottomAnchor constraintEqualToAnchor:_notice.bottomAnchor constant:-16]]];
    UISwipeGestureRecognizer *swipe=[[UISwipeGestureRecognizer alloc] initWithTarget:self action:@selector(dismissNotice)];swipe.direction=UISwipeGestureRecognizerDirectionUp;[_notice addGestureRecognizer:swipe];[self tab];
}
- (NSDate *)expiry:(NSDictionary *)event { NSISO8601DateFormatter *f=[NSISO8601DateFormatter new];f.formatOptions=NSISO8601DateFormatWithInternetDateTime|NSISO8601DateFormatWithFractionalSeconds;NSDate *date=[f dateFromString:event[@"expiresUtc"]?:@""];if(!date){f.formatOptions=NSISO8601DateFormatWithInternetDateTime;date=[f dateFromString:event[@"expiresUtc"]?:@""];}return date; }
- (void)refreshHistory {
    NSIndexSet *expired=[_events indexesOfObjectsPassingTest:^BOOL(NSDictionary *event,NSUInteger i,BOOL *stop){NSDate *date=[self expiry:event];return !date||date.timeIntervalSinceNow<=0;}];[_events removeObjectsAtIndexes:expired];
    for(UIView *v in [_history.arrangedSubviews copy]){[_history removeArrangedSubview:v];[v removeFromSuperview];}
    for(NSDictionary *event in [_events reverseObjectEnumerator]){UILabel *l=[self label:14];l.text=[NSString stringWithFormat:@"%@ · %@\n%@",event[@"source"],event[@"title"],event[@"body"]];[_history addArrangedSubview:l];}
}
- (void)tab { _media.hidden=_tabs.selectedSegmentIndex!=0;_actions.hidden=_tabs.selectedSegmentIndex!=1;_worker.hidden=_tabs.selectedSegmentIndex!=2;_history.hidden=_tabs.selectedSegmentIndex!=3;if(!_history.hidden)[self refreshHistory]; }
- (void)mediaCommand:(NSString *)command { NSMutableDictionary *body=[@{@"command":command} mutableCopy];if([_session[@"source"] isKindOfClass:NSString.class])body[@"source"]=_session[@"source"];[self.agent command:@"media.command" body:body]; }
- (void)toggle { [self mediaCommand:@"toggle"]; }
- (void)previous { [self mediaCommand:@"previous"]; }
- (void)next { [self mediaCommand:@"next"]; }
- (void)seek { [self.agent command:@"media.command" body:@{@"command":@"seek",@"seconds":@(_seek.value),@"source":_session[@"source"]?:@""}]; }
- (void)cancel { if(_lastJob)[self.agent command:@"job.cancel" body:@{@"jobId":_lastJob}]; }
- (void)action:(UIButton *)button { [self.agent command:@"action.invoke" body:@{@"actionId":button.accessibilityIdentifier?:@""}]; }
- (void)dismissNotice { [_noticeTimer invalidate];_noticeTimer=nil;_notice.hidden=YES; }
- (void)receive:(NSString *)type body:(NSDictionary *)body {
    if(!self.isViewLoaded)[self loadViewIfNeeded];
    if([type isEqual:@"connection"])_connection.text=[@"BackThread · " stringByAppendingString:body[@"state"]?:@""];
    else if([type isEqual:@"media.state"]) {
        NSDictionary *s=body[@"session"];if(![s isKindOfClass:NSDictionary.class])s=@{};_session=s;
        _title.text=[s[@"title"] length]?s[@"title"]:@"Ready when you are";_artist.text=s[@"artist"]?:@"Play media on Windows to begin";_source.text=s[@"source"]?:@"";
        [_play setTitle:[s[@"state"] isEqual:@"Playing"]?@"Pause":@"Play" forState:UIControlStateNormal];_play.enabled=s.count>0;_next.enabled=[s[@"canNext"] boolValue];_previous.enabled=[s[@"canPrevious"] boolValue];
        if(!_seek.tracking){_seek.maximumValue=MAX(1,[s[@"durationSeconds"] floatValue]);_seek.value=[s[@"positionSeconds"] floatValue];}_seek.enabled=[s[@"canSeek"] boolValue];
        _timeline.text=[NSString stringWithFormat:@"%.0f / %.0f seconds",[s[@"positionSeconds"] doubleValue],[s[@"durationSeconds"] doubleValue]];
        NSString *art=s[@"artworkBase64"];
        if([art isKindOfClass:NSString.class]&&art.length<=32000) {
            if(![_artworkKey isEqual:art]) { _artworkKey=[art copy];NSData *data=[[NSData alloc] initWithBase64EncodedString:art options:0];CGImageSourceRef source=data?CGImageSourceCreateWithData((__bridge CFDataRef)data,NULL):NULL;_art.image=nil;
                if(source){NSDictionary *options=@{(id)kCGImageSourceCreateThumbnailFromImageAlways:@YES,(id)kCGImageSourceThumbnailMaxPixelSize:@128,(id)kCGImageSourceCreateThumbnailWithTransform:@YES};CGImageRef image=CGImageSourceCreateThumbnailAtIndex(source,0,(__bridge CFDictionaryRef)options);if(image){_art.image=[UIImage imageWithCGImage:image];CGImageRelease(image);}CFRelease(source);}
            }
        } else { _artworkKey=nil;_art.image=nil; }
    }
    else if([type isEqual:@"action.registry"]) {
        for(UIView *v in [_actions.arrangedSubviews copy]){[_actions removeArrangedSubview:v];[v removeFromSuperview];}
        NSArray *actions=body[@"actions"];if([actions isKindOfClass:NSArray.class])for(NSDictionary *a in actions){if(_actions.arrangedSubviews.count>=32)break;UIButton *button=[self button:a[@"title"] selector:@selector(action:)];button.accessibilityIdentifier=a[@"id"];[_actions addArrangedSubview:button];}
    }
    else if([type isEqual:@"job.progress"]) { _lastJob=body[@"jobId"];_workerStatus.text=[NSString stringWithFormat:@"%@ · %@ · %.0f%%\n%@",_lastJob,body[@"state"],[body[@"progress"] doubleValue]*100,body[@"error"]?:body[@"result"]?:@""]; }
    else if([type isEqual:@"event.publish"]) {
        [_events addObject:body];while(_events.count>64)[_events removeObjectAtIndex:0];
        [self refreshHistory];
        NSInteger priority=[body[@"priority"] integerValue];if(priority==0)return;
        _noticeTitle.text=body[@"title"];_noticeBody.text=body[@"body"];_noticeBody.hidden=priority==1;_notice.hidden=NO;[_noticeTimer invalidate];
        _noticeCenter.active=NO;_noticeTop.active=YES;_noticeBottom.active=priority>=2;_noticeTitle.font=[UIFont systemFontOfSize:priority>=2?24:16 weight:UIFontWeightSemibold];
        NSTimeInterval visible=MIN(priority>=2?8:3,MAX(.1,[self expiry:body].timeIntervalSinceNow));_noticeTimer=[NSTimer scheduledTimerWithTimeInterval:visible target:self selector:@selector(dismissNotice) userInfo:nil repeats:NO];
    }
    else if([type isEqual:@"media.handoff"]) {
        NSURL *url=[NSURL URLWithString:body[@"url"]?:@""];
        if(![url.scheme.lowercaseString isEqual:@"https"]||url.user||url.password){_connection.text=@"Handoff rejected: HTTPS media required";return;}
        [_player pause];_player=[AVPlayer playerWithURL:url];[_player play];_connection.text=@"Direct media handed to AVPlayer; playback is unverified";
    }
    else if([type isEqual:@"error"])_connection.text=body[@"error"];
}
- (void)pair {
    UIAlertController *alert=[UIAlertController alertControllerWithTitle:@"Provision host key" message:@"Paste the 64-character session key created by BackThread.Host. Save it, then close and reopen this app to use the key." preferredStyle:UIAlertControllerStyleAlert];
    [alert addTextFieldWithConfigurationHandler:^(UITextField *f){f.secureTextEntry=YES;f.autocorrectionType=UITextAutocorrectionTypeNo;f.autocapitalizationType=UITextAutocapitalizationTypeNone;}];
    [alert addAction:[UIAlertAction actionWithTitle:@"Cancel" style:UIAlertActionStyleCancel handler:nil]];
    [alert addAction:[UIAlertAction actionWithTitle:@"Save key" style:UIAlertActionStyleDefault handler:^(UIAlertAction *a){
        NSString *key=alert.textFields.firstObject.text;NSCharacterSet *invalid=[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet];if(key.length!=64||[key rangeOfCharacterFromSet:invalid].location!=NSNotFound){self->_connection.text=@"Key must contain exactly 64 hexadecimal characters";return;}
        NSString *dir=[NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread"];[[NSFileManager defaultManager] createDirectoryAtPath:dir withIntermediateDirectories:YES attributes:@{NSFileProtectionKey:NSFileProtectionCompleteUntilFirstUserAuthentication} error:nil];NSString *path=[dir stringByAppendingPathComponent:@"session.key"];NSError *error=nil;
        if(![key writeToFile:path atomically:YES encoding:NSASCIIStringEncoding error:&error]){self->_connection.text=error.localizedDescription;return;}
        [[NSFileManager defaultManager] setAttributes:@{NSFilePosixPermissions:@0600,NSFileProtectionKey:NSFileProtectionCompleteUntilFirstUserAuthentication} ofItemAtPath:path error:nil];
        self->_connection.text=@"Key saved. Close and reopen BackThread to start with the new key.";
    }]];[self presentViewController:alert animated:YES completion:nil];
}
- (void)didReceiveMemoryWarning { [super didReceiveMemoryWarning];_art.image=nil;_artworkKey=nil;[_events removeAllObjects];[self refreshHistory];self.agent.memoryPressure=YES;dispatch_after(dispatch_time(DISPATCH_TIME_NOW,10*NSEC_PER_SEC),dispatch_get_main_queue(),^{self.agent.memoryPressure=NO;}); }
- (void)dealloc { [_noticeTimer invalidate];[_player pause]; }
@end
