#import <Foundation/Foundation.h>

// A foreground-only command inbox inside this app's private container. SSH is
// responsible for authentication; this does not open a network listener.
@interface BTPhoneControl : NSObject
@property(nonatomic,copy) NSDictionary *(^perform)(NSDictionary *request);
- (void)start;
@end
