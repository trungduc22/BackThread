#import <UIKit/UIKit.h>
@interface BTMediaView : UIView
@property(nonatomic,copy) void (^command)(NSString *,NSDictionary *);
@property(nonatomic,copy) void (^ownershipChanged)(BOOL);
@property(nonatomic,copy) void (^playbackChanged)(NSDictionary *);
@property(nonatomic,copy) void (^sourceWillChange)(void);
@property(nonatomic,readonly) BOOL nativeActive;
@property(nonatomic) BOOL fillVideo;
- (void)updateWindows:(NSDictionary *)session;
- (void)setConnected:(BOOL)connected transport:(NSString *)transport;
- (BOOL)openURL:(NSString *)url;
- (void)returnToPC;
- (void)purgeArtwork;
- (NSDictionary *)snapshot;
- (NSDictionary *)pauseForNotification;
- (void)restoreAfterNotification:(NSDictionary *)token;
- (void)showPreview:(NSString *)kind;
- (void)promptForURL;
- (void)showControls;
@end
