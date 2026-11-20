#import "BTPhoneBoxClient.h"
#import <Security/Security.h>
#import <CommonCrypto/CommonDigest.h>

static BOOL BTPBIsHex(NSString *s, NSUInteger len) {
    if (![s isKindOfClass:NSString.class] || s.length != len) return NO;
    NSCharacterSet *bad = [[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet];
    return [s rangeOfCharacterFromSet:bad].location == NSNotFound;
}

@implementation BTPhoneBoxProposal

+ (nullable instancetype)parseURI:(NSURL *)url {
    if (!url || ![[url.scheme lowercaseString] isEqualToString:@"https"]) return nil;
    NSString *fragment = url.fragment;
    if (!fragment.length) return nil;

    NSMutableDictionary<NSString *, NSString *> *params = [NSMutableDictionary new];
    for (NSString *part in [fragment componentsSeparatedByString:@"&"]) {
        NSRange eq = [part rangeOfString:@"="];
        if (eq.location == NSNotFound || eq.location == 0) continue;
        NSString *k = [part substringToIndex:eq.location];
        NSString *v = [[part substringFromIndex:eq.location + 1] stringByRemovingPercentEncoding] ?: @"";
        params[k] = v;
    }

    if (![params[@"v"] isEqualToString:@"1"]) return nil;
    NSString *fp = [params[@"fp"] lowercaseString];
    NSString *code = params[@"c"];
    int port = [params[@"port"] intValue];
    if (!BTPBIsHex(fp, 64) || !code.length || code.length > 64 || port <= 0 || port > 65535) return nil;

    NSMutableArray<NSString *> *hosts = [NSMutableArray new];
    for (NSString *h in [(params[@"h"] ?: @"") componentsSeparatedByString:@","]) {
        NSString *t = [h stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
        if (t.length > 0 && t.length <= 128) [hosts addObject:t];
    }
    if (!hosts.count) return nil;

    BTPhoneBoxProposal *p = [BTPhoneBoxProposal new];
    p.pcName = params[@"pc"].length ? params[@"pc"] : hosts.firstObject;
    p.fingerprintHex = fp;
    p.checkCode = [NSString stringWithFormat:@"%@ %@",
                   [[fp substringWithRange:NSMakeRange(0, 4)] uppercaseString],
                   [[fp substringWithRange:NSMakeRange(4, 4)] uppercaseString]];
    p.port = (uint16_t)port;
    p.hosts = [hosts copy];
    p.oneTimeCode = code;
    return p;
}

@end

@interface BTPhoneBoxClient () <NSURLSessionDelegate>
@end

@implementation BTPhoneBoxClient {
    NSString *_host;
    uint16_t _port;
    NSString *_expectedFingerprint;
    NSString *_bearerToken;
    NSURLSession *_session;
}

- (instancetype)initWithHost:(NSString *)host
                        port:(uint16_t)port
              fingerprintHex:(NSString *)fingerprintHex
                 bearerToken:(nullable NSString *)bearerToken {
    if ((self = [super init])) {
        _host = [host copy];
        _port = port;
        _expectedFingerprint = [fingerprintHex lowercaseString];
        _bearerToken = [bearerToken copy];
        NSURLSessionConfiguration *cfg = [NSURLSessionConfiguration ephemeralSessionConfiguration];
        cfg.timeoutIntervalForRequest = 6.0;
        _session = [NSURLSession sessionWithConfiguration:cfg delegate:self delegateQueue:nil];
    }
    return self;
}

- (NSString *)bearerToken { return _bearerToken; }
- (BOOL)supportsBackThreadPcm { return NO; }
- (BOOL)supportsBackThreadWorkers { return NO; }

+ (BOOL)isAllowedLanPath:(NSString *)path {
    if (!path.length) return NO;
    NSArray<NSString *> *allowed = @[
        @"/api/pair/claim", @"/api/status", @"/api/media", @"/api/trackpad",
        @"/api/work", @"/api/procs", @"/api/batteries", @"/api/rules/pause", @"/api/phone/battery"
    ];
    for (NSString *prefix in allowed) {
        if ([path isEqualToString:prefix] || [path hasPrefix:[prefix stringByAppendingString:@"/"]]) return YES;
    }
    return NO;
}

- (void)URLSession:(NSURLSession *)session
didReceiveChallenge:(NSURLAuthenticationChallenge *)challenge
 completionHandler:(void (^)(NSURLSessionAuthChallengeDisposition, NSURLCredential * _Nullable))completionHandler {
    (void)session;
    if (![challenge.protectionSpace.authenticationMethod isEqualToString:NSURLAuthenticationMethodServerTrust]) {
        completionHandler(NSURLSessionAuthChallengePerformDefaultHandling, nil);
        return;
    }
    SecTrustRef trust = challenge.protectionSpace.serverTrust;
    if (!trust || SecTrustGetCertificateCount(trust) < 1) {
        completionHandler(NSURLSessionAuthChallengeCancelAuthenticationChallenge, nil);
        return;
    }
    SecCertificateRef cert = NULL;
    CFArrayRef chain = NULL;
    if (@available(iOS 15.0, *)) {
        chain = SecTrustCopyCertificateChain(trust);
        if (chain && CFArrayGetCount(chain) > 0) {
            cert = (SecCertificateRef)CFArrayGetValueAtIndex(chain, 0);
        }
    }
#if !defined(BT_PROFILE_IOS15)
    else {
        cert = SecTrustGetCertificateAtIndex(trust, 0);
    }
#endif
    if (!cert) {
        if (chain) CFRelease(chain);
        completionHandler(NSURLSessionAuthChallengeCancelAuthenticationChallenge, nil);
        return;
    }
    NSData *der = (__bridge_transfer NSData *)SecCertificateCopyData(cert);
    if (chain) CFRelease(chain);
    if (!der.length) {
        completionHandler(NSURLSessionAuthChallengeCancelAuthenticationChallenge, nil);
        return;
    }
    uint8_t digest[CC_SHA256_DIGEST_LENGTH];
    CC_SHA256(der.bytes, (CC_LONG)der.length, digest);
    NSMutableString *hex = [NSMutableString stringWithCapacity:64];
    for (int i = 0; i < CC_SHA256_DIGEST_LENGTH; i++) [hex appendFormat:@"%02x", digest[i]];
    if ([hex isEqualToString:_expectedFingerprint]) {
        completionHandler(NSURLSessionAuthChallengeUseCredential, [NSURLCredential credentialForTrust:trust]);
    } else {
        completionHandler(NSURLSessionAuthChallengeCancelAuthenticationChallenge, nil);
    }
}

- (void)pairWithCode:(NSString *)oneTimeCode
          completion:(void (^)(NSString * _Nullable, NSError * _Nullable))completion {
    NSURL *url = [NSURL URLWithString:[NSString stringWithFormat:@"https://%@:%u/api/pair/claim", _host, (unsigned)_port]];
    NSMutableURLRequest *req = [NSMutableURLRequest requestWithURL:url];
    req.HTTPMethod = @"POST";
    [req setValue:@"application/json" forHTTPHeaderField:@"Content-Type"];
    req.HTTPBody = [NSJSONSerialization dataWithJSONObject:@{@"code": oneTimeCode ?: @""} options:0 error:nil];

    [[_session dataTaskWithRequest:req completionHandler:^(NSData *data, NSURLResponse *response, NSError *error) {
        if (error) { completion(nil, error); return; }
        NSHTTPURLResponse *http = (NSHTTPURLResponse *)response;
        if (http.statusCode < 200 || http.statusCode >= 300 || !data.length) {
            completion(nil, [NSError errorWithDomain:@"BackThread.PhoneBox" code:http.statusCode userInfo:@{NSLocalizedDescriptionKey: @"PhoneBox pairing rejected"}]);
            return;
        }
        NSDictionary *json = [NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
        NSString *tok = [json[@"token"] isKindOfClass:NSString.class] ? json[@"token"]
                      : ([json[@"key"] isKindOfClass:NSString.class] ? json[@"key"] : nil);
        if (!tok.length) {
            completion(nil, [NSError errorWithDomain:@"BackThread.PhoneBox" code:422 userInfo:@{NSLocalizedDescriptionKey: @"Missing PhoneBox token"}]);
            return;
        }
        self->_bearerToken = [tok copy];
        completion(tok, nil);
    }] resume];
}

- (void)getReadOnlyPath:(NSString *)path
             completion:(void (^)(NSDictionary * _Nullable, NSError * _Nullable))completion {
    if (![BTPhoneBoxClient isAllowedLanPath:path] || !_bearerToken.length) {
        completion(nil, [NSError errorWithDomain:@"BackThread.PhoneBox" code:403 userInfo:@{NSLocalizedDescriptionKey: @"Disallowed PhoneBox path or unauthenticated"}]);
        return;
    }
    NSURL *url = [NSURL URLWithString:[NSString stringWithFormat:@"https://%@:%u%@", _host, (unsigned)_port, path]];
    NSMutableURLRequest *req = [NSMutableURLRequest requestWithURL:url];
    req.HTTPMethod = @"GET";
    [req setValue:[@"Bearer " stringByAppendingString:_bearerToken] forHTTPHeaderField:@"Authorization"];
    [[_session dataTaskWithRequest:req completionHandler:^(NSData *data, NSURLResponse *response, NSError *error) {
        if (error) { completion(nil, error); return; }
        NSHTTPURLResponse *http = (NSHTTPURLResponse *)response;
        if (http.statusCode < 200 || http.statusCode >= 300) {
            completion(nil, [NSError errorWithDomain:@"BackThread.PhoneBox" code:http.statusCode userInfo:@{NSLocalizedDescriptionKey: @"PhoneBox request failed"}]);
            return;
        }
        id parsed = data.length ? [NSJSONSerialization JSONObjectWithData:data options:0 error:nil] : @{};
        completion([parsed isKindOfClass:NSDictionary.class] ? parsed : @{}, nil);
    }] resume];
}

- (void)dealloc {
    [_session invalidateAndCancel];
}

@end
