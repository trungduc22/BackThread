#import <UIKit/UIKit.h>

@class BTAgent;

NS_ASSUME_NONNULL_BEGIN

@interface BTViewController : UIViewController
@property(nonatomic, strong, nullable) BTAgent *agent;
@property(nonatomic, copy, nullable) void (^onKeyUpdated)(NSData *key);
- (void)receive:(NSString *)type body:(NSDictionary *)body;
- (void)handlePairingURL:(NSURL *)url;
@end

NS_ASSUME_NONNULL_END
