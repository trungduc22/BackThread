#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTQuickActionsView : UIView
@property(nonatomic, copy, nullable) void (^onActionInvoked)(NSString *actionId);
- (void)updateActions:(NSArray<NSDictionary *> *)actions;
- (void)showFeedbackText:(NSString *)text;
@end

NS_ASSUME_NONNULL_END
