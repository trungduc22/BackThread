#import <Foundation/Foundation.h>

@class BTAudioReceiver;

@interface BTAgent : NSObject
@property(atomic, copy) void (^stateChanged)(NSString *type, NSDictionary *body);
@property(atomic, copy) void (^bufferedChunkReceived)(NSString *streamId, uint64_t offset, NSData *bytes);
@property(atomic, copy) NSDictionary * (^mediaSyncProvider)(NSDictionary *hostSync);
@property(nonatomic, readonly) BTAudioReceiver *audio;
@property(atomic) BOOL memoryPressure;

- (instancetype)initWithKey:(NSData *)key storage:(NSString *)storage;
- (void)start;
- (void)stop;
- (void)command:(NSString *)type body:(NSDictionary *)body;
- (void)sendMediaMessage:(NSString *)type body:(NSDictionary *)body;
- (void)registerBufferedStreamId:(NSString *)streamId;
- (void)unregisterBufferedStreamId:(NSString *)streamId;
@end
