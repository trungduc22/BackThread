#import <UIKit/UIKit.h>
@class BTAgent;
@interface BTViewController : UIViewController
@property(nonatomic,strong) BTAgent *agent;
- (void)receive:(NSString *)type body:(NSDictionary *)body;
@end
