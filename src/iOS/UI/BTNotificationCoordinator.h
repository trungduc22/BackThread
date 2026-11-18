#import <UIKit/UIKit.h>
@interface BTNotificationCoordinator : NSObject
@property(nonatomic,copy) void (^presentationChanged)(BOOL fullscreen,BOOL visible);
@property(nonatomic,copy) void (^historyChanged)(NSArray *events);
@property(nonatomic,readonly) NSArray *history;
@property(nonatomic,readonly) UIView *view;
- (instancetype)initWithHost:(UIView *)host;
- (void)accept:(NSDictionary *)event;
- (void)dismiss;
- (NSDictionary *)snapshot;
@end
