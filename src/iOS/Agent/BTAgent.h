#import <Foundation/Foundation.h>
@class BTAudioReceiver;
@interface BTAgent : NSObject
@property(nonatomic,copy) void (^stateChanged)(NSString *type,NSDictionary *body);
@property(nonatomic,readonly) BTAudioReceiver *audio;
@property(atomic) BOOL memoryPressure;
- (instancetype)initWithKey:(NSData *)key storage:(NSString *)storage;
- (void)start;
- (void)stop;
- (void)command:(NSString *)type body:(NSDictionary *)body;
@end
