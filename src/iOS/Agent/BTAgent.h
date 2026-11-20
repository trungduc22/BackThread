#import <Foundation/Foundation.h>

@class BTAudioReceiver;
@class BTWifiClient;

@interface BTAgent : NSObject
@property(atomic, copy) void (^stateChanged)(NSString *type, NSDictionary *body);
@property(atomic, copy) void (^bufferedChunkReceived)(NSString *streamId, uint64_t offset, NSData *bytes);
@property(atomic, copy) NSDictionary * (^mediaSyncProvider)(NSDictionary *hostSync);
@property(nonatomic, readonly) BTAudioReceiver *audio;
@property(nonatomic, readonly) BTWifiClient *wifiClient;
@property(nonatomic, copy, readonly) NSString *activeRoute;
@property(atomic, copy) NSString *deviceName;
@property(atomic) BOOL memoryPressure;

- (instancetype)initWithKey:(NSData *)key storage:(NSString *)storage;
- (void)updateKey:(NSData *)key;
- (void)start;
- (void)stop;
- (void)triggerWifiReconnect;
- (void)disconnectActiveSession;
- (void)setNativePlaybackActive:(BOOL)active;
- (void)command:(NSString *)type body:(NSDictionary *)body;
- (void)sendMediaMessage:(NSString *)type body:(NSDictionary *)body;
- (void)sendControlMessage:(NSString *)type body:(NSDictionary *)body channel:(uint16_t)channel;
- (void)sendViewportTab:(NSString *)tab awake:(BOOL)awake dimmed:(BOOL)dimmed;
- (void)sendPhoneHealthWithBatteryLevel:(int)batteryLevel charging:(BOOL)charging;
- (void)claimPairingURI:(NSURL *)url completion:(void (^)(BOOL ok, NSString *message))completion;
- (void)registerBufferedStreamId:(NSString *)streamId;
- (void)unregisterBufferedStreamId:(NSString *)streamId;
@end
