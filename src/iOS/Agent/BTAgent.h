#import <Foundation/Foundation.h>
@class BTAudioReceiver;
@interface BTAgent : NSObject
@property(atomic,copy) void (^stateChanged)(NSString *type,NSDictionary *body);
@property(nonatomic,readonly) BTAudioReceiver *audio;
@property(atomic) BOOL memoryPressure;
@property(atomic) BOOL nativePlaybackActive;
@property(atomic,copy) void (^uiRequest)(NSString *type,NSDictionary *body,void (^reply)(NSDictionary *body,BOOL success));
- (instancetype)initWithKey:(NSData *)key storage:(NSString *)storage;
- (void)start;
- (void)stop;
- (void)command:(NSString *)type body:(NSDictionary *)body;
@end
