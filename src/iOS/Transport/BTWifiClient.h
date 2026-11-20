#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTPairedPC : NSObject
@property(nonatomic, copy) NSString *pcId;
@property(nonatomic, copy) NSString *pcName;
@property(nonatomic, copy) NSString *fingerprint;
@property(nonatomic, copy) NSString *checkCode;
@property(nonatomic, assign) uint16_t port;
@property(nonatomic, copy) NSArray<NSString *> *hosts;
@property(nonatomic, copy) NSString *sessionKeyHex;

+ (NSString *)humanCheckCodeForFingerprint:(NSString *)fingerprint;
+ (nullable instancetype)fromDictionary:(NSDictionary *)dict;
- (NSDictionary *)toDictionary;
@end

@interface BTPairingProposal : NSObject
@property(nonatomic, copy) NSString *pcName;
@property(nonatomic, copy) NSString *fingerprint;
@property(nonatomic, copy) NSString *checkCode;
@property(nonatomic, assign) uint16_t port;
@property(nonatomic, copy) NSArray<NSString *> *hosts;
@property(nonatomic, copy) NSString *oneTimeCode;

+ (nullable instancetype)parseURI:(NSURL *)url;
+ (nullable instancetype)proposalWithHost:(NSString *)host
                                     port:(uint16_t)port
                              fingerprint:(NSString *)fingerprint
                              oneTimeCode:(NSString *)oneTimeCode
                                   pcName:(nullable NSString *)pcName;
@end

@interface BTTlsStream : NSObject
@property(nonatomic, readonly) BOOL closed;
@property(nonatomic, copy, readonly) NSString *remoteHost;

- (BOOL)writeBytes:(const void *)bytes length:(size_t)length timeout:(NSTimeInterval)timeout;
- (BOOL)readExact:(void *)bytes length:(size_t)length timeout:(NSTimeInterval)timeout;
- (void)close;
@end

@interface BTWifiClient : NSObject
@property(atomic, copy, nullable) void (^onNetworkChanged)(BOOL satisfied);

- (instancetype)initWithStorageDirectory:(NSString *)directory;
- (void)startDiscovery;
- (void)stopDiscovery;

- (NSArray<BTPairedPC *> *)pairedPCs;
- (void)savePairedPC:(BTPairedPC *)pc;
- (void)removePairedPCWithId:(NSString *)pcId;

- (nullable BTTlsStream *)connectToPairedPC:(BTPairedPC *)pc
                                    timeout:(NSTimeInterval)timeout
                                      error:(NSError * _Nullable * _Nullable)error;

- (void)claimProposal:(BTPairingProposal *)proposal
             deviceId:(NSString *)deviceId
           deviceName:(NSString *)deviceName
           completion:(void (^)(BTPairedPC * _Nullable pc, NSError * _Nullable error))completion;
@end

NS_ASSUME_NONNULL_END
