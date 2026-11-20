#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

/// Optional PhoneBox interoperability adapter (separate from BackThread BTH1).
/// Connects to a compatible PhoneBox.Agent over pinned TLS using its documented LAN allowlist.
@interface BTPhoneBoxProposal : NSObject
@property(nonatomic, copy) NSString *pcName;
@property(nonatomic, copy) NSString *fingerprintHex;
@property(nonatomic, copy) NSString *checkCode;
@property(nonatomic, assign) uint16_t port;
@property(nonatomic, copy) NSArray<NSString *> *hosts;
@property(nonatomic, copy) NSString *oneTimeCode;

+ (nullable instancetype)parseURI:(NSURL *)url;
@end

@interface BTPhoneBoxClient : NSObject
@property(nonatomic, copy, readonly, nullable) NSString *bearerToken;
@property(nonatomic, readonly) BOOL supportsBackThreadPcm;
@property(nonatomic, readonly) BOOL supportsBackThreadWorkers;

- (instancetype)initWithHost:(NSString *)host
                        port:(uint16_t)port
              fingerprintHex:(NSString *)fingerprintHex
                 bearerToken:(nullable NSString *)bearerToken;

+ (BOOL)isAllowedLanPath:(NSString *)path;

- (void)pairWithCode:(NSString *)oneTimeCode
          completion:(void (^)(NSString * _Nullable token, NSError * _Nullable error))completion;

- (void)getReadOnlyPath:(NSString *)path
             completion:(void (^)(NSDictionary * _Nullable json, NSError * _Nullable error))completion;
@end

NS_ASSUME_NONNULL_END
