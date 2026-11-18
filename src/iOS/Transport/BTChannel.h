#import <Foundation/Foundation.h>
FOUNDATION_EXPORT NSTimeInterval BTSessionClock(void);
/// One owned socket. USB remains plain loopback; Wi-Fi is TLS 1.2 with a USB-enrolled certificate pin.
@interface BTChannel : NSObject
@property(nonatomic,readonly) int descriptor;
@property(nonatomic,readonly) BOOL secure;
@property(nonatomic) NSTimeInterval readTimeout;
- (instancetype)initWithDescriptor:(int)descriptor;
+ (instancetype)connectTLS:(NSString *)address port:(int)port pin:(NSString *)pin deadline:(NSTimeInterval)deadline error:(NSError **)error;
- (BOOL)readExact:(void *)bytes count:(size_t)count;
- (BOOL)writeExact:(const void *)bytes count:(size_t)count;
- (void)close;
@end
