#import <UIKit/UIKit.h>

@class BTAgent;

NS_ASSUME_NONNULL_BEGIN

@interface BTViewController : UIViewController
@property(nonatomic, strong, nullable) BTAgent *agent;
- (void)receive:(NSString *)type body:(NSDictionary *)body;
@end

NS_ASSUME_NONNULL_END
