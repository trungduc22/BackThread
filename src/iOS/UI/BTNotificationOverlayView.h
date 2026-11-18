#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTNotificationOverlayView : UIView
@property(nonatomic, readonly) BOOL fullscreenActive;
@property(nonatomic, copy, nullable) void (^onDismissed)(NSInteger priority);

- (void)presentNotification:(NSDictionary *)event expiresInterval:(NSTimeInterval)expiresInSeconds;
- (void)dismissOverlay;
- (void)clearIconCache;
@end

@interface BTHistoryPanelView : UIView
@property(nonatomic, copy, nullable) void (^onReplayFullscreenTapped)(NSDictionary *event);
@property(nonatomic, copy, nullable) void (^onClearTapped)(void);
- (void)updateWithEvents:(NSArray<NSDictionary *> *)events;
@end

NS_ASSUME_NONNULL_END
