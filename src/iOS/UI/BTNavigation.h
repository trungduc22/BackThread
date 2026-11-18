#import <UIKit/UIKit.h>
@interface BTPageCoordinator : UIViewController <UIGestureRecognizerDelegate>
@property(nonatomic,copy) UIViewController *(^pageFactory)(NSInteger);
@property(nonatomic,copy) void (^pageChanged)(NSInteger);
@property(nonatomic,readonly) NSInteger selectedPage;
@property(nonatomic) BOOL gesturesBlocked;
- (void)selectPage:(NSInteger)page animated:(BOOL)animated;
@end
@interface BTCornerDrawer : UIView <UIGestureRecognizerDelegate>
@property(nonatomic,copy) void (^selected)(NSInteger);
@property(nonatomic,readonly) BOOL open;
- (void)setOpen:(BOOL)open animated:(BOOL)animated;
@end
