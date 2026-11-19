#import "BTWifiClient.h"
#import "../../Shared/Protocol/bt_wire.h"
#import <Network/Network.h>
#import <Security/Security.h>
#import <CommonCrypto/CommonDigest.h>
#import <string.h>

static BOOL BTIsHex(NSString *s, NSUInteger expectedLen) {
    if (![s isKindOfClass:NSString.class] || s.length != expectedLen) return NO;
    NSCharacterSet *bad = [[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet];
    return [s rangeOfCharacterFromSet:bad].location == NSNotFound;
}

static NSData *BTDataFromHex(NSString *hex) {
    if (![hex isKindOfClass:NSString.class] || (hex.length % 2) != 0) return nil;
    NSUInteger count = hex.length / 2;
    NSMutableData *out = [NSMutableData dataWithLength:count];
    uint8_t *dst = (uint8_t *)out.mutableBytes;
    for (NSUInteger i = 0; i < count; i++) {
        unsigned int value = 0;
        if (![[NSScanner scannerWithString:[hex substringWithRange:NSMakeRange(i * 2, 2)]] scanHexInt:&value]) return nil;
        dst[i] = (uint8_t)value;
    }
    return out;
}

@implementation BTPairedPC

+ (NSString *)humanCheckCodeForFingerprint:(NSString *)fingerprint {
    NSString *fp = [fingerprint isKindOfClass:NSString.class] ? fingerprint.uppercaseString : @"";
    if (fp.length < 8) return fp;
    return [NSString stringWithFormat:@"%@ %@", [fp substringWithRange:NSMakeRange(0, 4)], [fp substringWithRange:NSMakeRange(4, 4)]];
}

+ (nullable instancetype)fromDictionary:(NSDictionary *)dict {
    if (![dict isKindOfClass:NSDictionary.class]) return nil;
    NSString *fp = [dict[@"fingerprint"] isKindOfClass:NSString.class] ? [dict[@"fingerprint"] lowercaseString] : nil;
    NSString *keyHex = [dict[@"sessionKeyHex"] isKindOfClass:NSString.class] ? [dict[@"sessionKeyHex"] lowercaseString] : nil;
    if (!BTIsHex(fp, 64) || !BTIsHex(keyHex, 64)) return nil;

    BTPairedPC *pc = [BTPairedPC new];
    NSString *pcId = [dict[@"pcId"] isKindOfClass:NSString.class] ? dict[@"pcId"] : [fp substringToIndex:16];
    pc.pcId = pcId.length ? pcId : [fp substringToIndex:16];
    pc.pcName = [dict[@"pcName"] isKindOfClass:NSString.class] && [dict[@"pcName"] length] ? dict[@"pcName"] : @"Windows PC";
    pc.fingerprint = fp;
    NSString *cc = [dict[@"checkCode"] isKindOfClass:NSString.class] ? dict[@"checkCode"] : nil;
    pc.checkCode = cc.length ? cc : [self humanCheckCodeForFingerprint:fp];
    int port = [dict[@"port"] intValue];
    pc.port = (port > 0 && port <= 65535) ? (uint16_t)port : 23453;
    NSMutableArray<NSString *> *hosts = [NSMutableArray new];
    if ([dict[@"hosts"] isKindOfClass:NSArray.class]) {
        for (id item in (NSArray *)dict[@"hosts"]) {
            if ([item isKindOfClass:NSString.class] && [item length] > 0 && [item length] <= 128) {
                [hosts addObject:item];
            }
        }
    }
    pc.hosts = [hosts copy];
    pc.sessionKeyHex = keyHex;
    return pc;
}

- (NSDictionary *)toDictionary {
    return @{
        @"pcId": self.pcId ?: @"",
        @"pcName": self.pcName ?: @"Windows PC",
        @"fingerprint": self.fingerprint ?: @"",
        @"checkCode": self.checkCode ?: @"",
        @"port": @(self.port),
        @"hosts": self.hosts ?: @[],
        @"sessionKeyHex": self.sessionKeyHex ?: @""
    };
}

@end

@implementation BTPairingProposal

+ (nullable instancetype)parseURI:(NSURL *)url {
    if (!url || ![[url.scheme lowercaseString] isEqualToString:@"backthread"]) return nil;
    if (![[url.host lowercaseString] isEqualToString:@"pair"]) return nil;
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
    if (!BTIsHex(fp, 64) || !code.length || code.length > 64 || port <= 0 || port > 65535) return nil;

    NSMutableArray<NSString *> *hosts = [NSMutableArray new];
    for (NSString *h in [(params[@"h"] ?: @"") componentsSeparatedByString:@","]) {
        NSString *trimmed = [h stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
        if (trimmed.length > 0 && trimmed.length <= 128) [hosts addObject:trimmed];
    }
    if (hosts.count == 0) return nil;

    BTPairingProposal *proposal = [BTPairingProposal new];
    proposal.pcName = params[@"pc"].length ? params[@"pc"] : @"Windows PC";
    proposal.fingerprint = fp;
    proposal.checkCode = [BTPairedPC humanCheckCodeForFingerprint:fp];
    proposal.port = (uint16_t)port;
    proposal.hosts = [hosts copy];
    proposal.oneTimeCode = code;
    return proposal;
}

@end

@implementation BTTlsStream {
    nw_connection_t _connection;
    dispatch_queue_t _nwQueue;
    NSLock *_lock;
    BOOL _closed;
    NSString *_remoteHost;
    dispatch_semaphore_t _pendingReadSem;
    dispatch_semaphore_t _pendingWriteSem;
}

- (BOOL)closed {
    [_lock lock];
    BOOL c = _closed;
    [_lock unlock];
    return c;
}

- (NSString *)remoteHost {
    return _remoteHost ?: @"";
}

+ (nullable instancetype)connectToEndpoint:(nw_endpoint_t)endpoint
                                labelHost:(NSString *)labelHost
                   expectedFingerprintHex:(NSString *)fingerprint
                                  timeout:(NSTimeInterval)timeout
                                    error:(NSError **)error {
    NSData *expectedBytes = BTDataFromHex(fingerprint);
    if (!endpoint || expectedBytes.length != CC_SHA256_DIGEST_LENGTH) {
        if (error) *error = [NSError errorWithDomain:@"BackThread.WiFi" code:1 userInfo:@{NSLocalizedDescriptionKey: @"Invalid TLS endpoint or SHA-256 fingerprint"}];
        return nil;
    }

    dispatch_queue_t nwQueue = dispatch_queue_create("app.backthread.wifi.tls", DISPATCH_QUEUE_SERIAL);
    dispatch_queue_t verifyQueue = dispatch_queue_create("app.backthread.wifi.verify", DISPATCH_QUEUE_SERIAL);

    nw_parameters_configure_protocol_block_t configureTLS = ^(nw_protocol_options_t tls_options) {
        sec_protocol_options_t sec = nw_tls_copy_sec_protocol_options(tls_options);
        if (@available(iOS 13.0, *)) {
            sec_protocol_options_set_min_tls_protocol_version(sec, tls_protocol_version_TLSv12);
        }
        sec_protocol_options_set_verify_block(sec, ^(sec_protocol_metadata_t metadata, sec_trust_t trust_ref, sec_protocol_verify_complete_t complete) {
            (void)metadata;
            BOOL matched = NO;
            SecTrustRef trust = sec_trust_copy_ref(trust_ref);
            if (trust) {
                SecCertificateRef leaf = NULL;
                if (@available(iOS 15.0, *)) {
                    CFArrayRef chain = SecTrustCopyCertificateChain(trust);
                    if (chain && CFArrayGetCount(chain) > 0) {
                        leaf = (SecCertificateRef)CFArrayGetValueAtIndex(chain, 0);
                        CFRetain(leaf);
                    }
                    if (chain) CFRelease(chain);
                } else {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
                    if (SecTrustGetCertificateCount(trust) > 0) {
                        leaf = SecTrustGetCertificateAtIndex(trust, 0);
                        if (leaf) CFRetain(leaf);
                    }
#pragma clang diagnostic pop
                }
                if (leaf) {
                    CFDataRef certData = SecCertificateCopyData(leaf);
                    if (certData) {
                        uint8_t digest[CC_SHA256_DIGEST_LENGTH];
                        CC_SHA256(CFDataGetBytePtr(certData), (CC_LONG)CFDataGetLength(certData), digest);
                        const uint8_t *exp = (const uint8_t *)expectedBytes.bytes;
                        uint8_t diff = 0;
                        for (size_t i = 0; i < CC_SHA256_DIGEST_LENGTH; i++) diff |= (uint8_t)(digest[i] ^ exp[i]);
                        matched = (diff == 0);
                        CFRelease(certData);
                    }
                    CFRelease(leaf);
                }
                CFRelease(trust);
            }
            complete(matched);
        }, verifyQueue);
    };

    nw_parameters_configure_protocol_block_t configureTCP = ^(nw_protocol_options_t tcp_options) {
        nw_tcp_options_set_no_delay(tcp_options, true);
    };

    nw_parameters_t params = nw_parameters_create_secure_tcp(configureTLS, configureTCP);
    nw_connection_t conn = nw_connection_create(endpoint, params);
    if (!conn) {
        if (error) *error = [NSError errorWithDomain:@"BackThread.WiFi" code:2 userInfo:@{NSLocalizedDescriptionKey: @"Failed to allocate TLS connection"}];
        return nil;
    }

    BTTlsStream *stream = [BTTlsStream new];
    stream->_connection = conn;
    stream->_nwQueue = nwQueue;
    stream->_lock = [NSLock new];
    stream->_remoteHost = [labelHost copy] ?: @"";

    dispatch_semaphore_t readySem = dispatch_semaphore_create(0);
    __block BOOL connected = NO;
    __block BOOL finished = NO;
    __block int lastErrCode = 0;
    __weak BTTlsStream *weakStream = stream;

    nw_connection_set_queue(conn, nwQueue);
    nw_connection_set_state_changed_handler(conn, ^(nw_connection_state_t state, nw_error_t err) {
        BTTlsStream *strong = weakStream;
        if (state == nw_connection_state_ready) {
            if (!finished) {
                finished = YES;
                connected = YES;
                dispatch_semaphore_signal(readySem);
            }
        } else if (state == nw_connection_state_failed || state == nw_connection_state_cancelled) {
            if (err) lastErrCode = nw_error_get_error_code(err);
            if (strong) {
                [strong markClosedAndWake];
            }
            if (!finished) {
                finished = YES;
                connected = NO;
                dispatch_semaphore_signal(readySem);
            }
        }
    });

    nw_connection_start(conn);
    dispatch_time_t deadline = dispatch_time(DISPATCH_TIME_NOW, (int64_t)(MAX(1.0, timeout) * NSEC_PER_SEC));
    long waited = dispatch_semaphore_wait(readySem, deadline);
    if (waited != 0 || !connected) {
        [stream close];
        if (error) {
            NSString *msg = waited != 0
                ? @"TLS connect timed out"
                : [NSString stringWithFormat:@"TLS connect or certificate pin failed (%d)", lastErrCode];
            *error = [NSError errorWithDomain:@"BackThread.WiFi" code:lastErrCode ?: 3 userInfo:@{NSLocalizedDescriptionKey: msg}];
        }
        return nil;
    }

    return stream;
}

- (void)markClosedAndWake {
    [_lock lock];
    _closed = YES;
    dispatch_semaphore_t r = _pendingReadSem;
    dispatch_semaphore_t w = _pendingWriteSem;
    [_lock unlock];
    if (r) dispatch_semaphore_signal(r);
    if (w) dispatch_semaphore_signal(w);
}

- (BOOL)writeBytes:(const void *)bytes length:(size_t)length timeout:(NSTimeInterval)timeout {
    if (length == 0) return YES;
    [_lock lock];
    if (_closed || !_connection) {
        [_lock unlock];
        return NO;
    }
    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    _pendingWriteSem = sem;
    nw_connection_t conn = _connection;
    [_lock unlock];

    __block BOOL ok = NO;
    dispatch_data_t payload = dispatch_data_create(bytes, length, _nwQueue, DISPATCH_DATA_DESTRUCTOR_DEFAULT);
    nw_connection_send(conn, payload, NW_CONNECTION_DEFAULT_MESSAGE_CONTEXT, true, ^(nw_error_t err) {
        ok = (err == nil);
        dispatch_semaphore_signal(sem);
    });

    dispatch_time_t deadline = dispatch_time(DISPATCH_TIME_NOW, (int64_t)(MAX(1.0, timeout) * NSEC_PER_SEC));
    long waited = dispatch_semaphore_wait(sem, deadline);

    [_lock lock];
    if (_pendingWriteSem == sem) _pendingWriteSem = nil;
    BOOL wasClosed = _closed;
    [_lock unlock];

    if (waited != 0 || !ok || wasClosed) {
        [self close];
        return NO;
    }
    return YES;
}

- (BOOL)readExact:(void *)bytes length:(size_t)length timeout:(NSTimeInterval)timeout {
    uint8_t *dst = (uint8_t *)bytes;
    size_t receivedTotal = 0;
    NSDate *endDate = [NSDate dateWithTimeIntervalSinceNow:MAX(1.0, timeout)];

    while (receivedTotal < length) {
        [_lock lock];
        if (_closed || !_connection) {
            [_lock unlock];
            return NO;
        }
        dispatch_semaphore_t sem = dispatch_semaphore_create(0);
        _pendingReadSem = sem;
        nw_connection_t conn = _connection;
        [_lock unlock];

        size_t remaining = length - receivedTotal;
        uint32_t req = remaining > UINT32_MAX ? UINT32_MAX : (uint32_t)remaining;
        __block size_t got = 0;
        __block BOOL failed = NO;
        uint8_t *chunkDst = dst + receivedTotal;

        nw_connection_receive(conn, 1, req, ^(dispatch_data_t content, nw_content_context_t context, bool is_complete, nw_error_t err) {
            (void)context;
            if (content) {
                dispatch_data_apply(content, ^bool(dispatch_data_t region, size_t offset, const void *buffer, size_t size) {
                    (void)region;
                    (void)offset;
                    size_t copyLen = size;
                    if (got + copyLen > remaining) copyLen = remaining - got;
                    if (copyLen > 0) {
                        memcpy(chunkDst + got, buffer, copyLen);
                        got += copyLen;
                    }
                    return got < remaining;
                });
            }
            if (err != nil || (got == 0 && is_complete)) {
                failed = YES;
            }
            dispatch_semaphore_signal(sem);
        });

        NSTimeInterval left = [endDate timeIntervalSinceNow];
        if (left <= 0) {
            [self close];
            return NO;
        }
        long waited = dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(left * NSEC_PER_SEC)));

        [_lock lock];
        if (_pendingReadSem == sem) _pendingReadSem = nil;
        BOOL wasClosed = _closed;
        [_lock unlock];

        if (waited != 0 || failed || wasClosed || got == 0) {
            [self close];
            return NO;
        }
        receivedTotal += got;
    }
    return YES;
}

- (void)close {
    nw_connection_t conn = nil;
    [_lock lock];
    if (!_closed) {
        _closed = YES;
        conn = _connection;
        _connection = nil;
    }
    dispatch_semaphore_t r = _pendingReadSem;
    dispatch_semaphore_t w = _pendingWriteSem;
    [_lock unlock];
    if (conn) nw_connection_cancel(conn);
    if (r) dispatch_semaphore_signal(r);
    if (w) dispatch_semaphore_signal(w);
}

- (void)dealloc {
    [self close];
}

@end

@implementation BTWifiClient {
    NSString *_storageFile;
    NSLock *_lock;
    NSMutableDictionary<NSString *, BTPairedPC *> *_paired;
    NSMutableDictionary<NSString *, id> *_discoveredEndpoints;
    dispatch_queue_t _queue;
    id _browser;
}

- (instancetype)initWithStorageDirectory:(NSString *)directory {
    if ((self = [super init])) {
        _lock = [NSLock new];
        _paired = [NSMutableDictionary new];
        _discoveredEndpoints = [NSMutableDictionary new];
        _queue = dispatch_queue_create("app.backthread.wifi.client", DISPATCH_QUEUE_SERIAL);
        [[NSFileManager defaultManager] createDirectoryAtPath:directory
                                  withIntermediateDirectories:YES
                                                   attributes:@{NSFileProtectionKey: NSFileProtectionCompleteUntilFirstUserAuthentication}
                                                        error:nil];
        _storageFile = [directory stringByAppendingPathComponent:@"paired-pcs.json"];
        [self loadPaired];
    }
    return self;
}

- (void)loadPaired {
    NSData *raw = [NSData dataWithContentsOfFile:_storageFile];
    if (!raw.length) return;
    id parsed = [NSJSONSerialization JSONObjectWithData:raw options:0 error:nil];
    if (![parsed isKindOfClass:NSArray.class]) return;
    [_lock lock];
    for (id item in (NSArray *)parsed) {
        BTPairedPC *pc = [BTPairedPC fromDictionary:item];
        if (pc) _paired[pc.pcId] = pc;
    }
    [_lock unlock];
}

- (void)persistPairedLocked {
    NSMutableArray *list = [NSMutableArray new];
    for (BTPairedPC *pc in _paired.allValues) {
        [list addObject:[pc toDictionary]];
    }
    NSData *json = [NSJSONSerialization dataWithJSONObject:list options:NSJSONWritingPrettyPrinted error:nil];
    if (json) {
        [json writeToFile:_storageFile options:NSDataWritingAtomic error:nil];
        [[NSFileManager defaultManager] setAttributes:@{
            NSFilePosixPermissions: @0600,
            NSFileProtectionKey: NSFileProtectionCompleteUntilFirstUserAuthentication
        } ofItemAtPath:_storageFile error:nil];
    }
}

- (NSArray<BTPairedPC *> *)pairedPCs {
    [_lock lock];
    NSArray<BTPairedPC *> *list = [_paired.allValues copy];
    [_lock unlock];
    return list;
}

- (void)savePairedPC:(BTPairedPC *)pc {
    if (!pc || !pc.pcId.length) return;
    [_lock lock];
    _paired[pc.pcId] = pc;
    [self persistPairedLocked];
    [_lock unlock];
}

- (void)removePairedPCWithId:(NSString *)pcId {
    if (!pcId.length) return;
    [_lock lock];
    [_paired removeObjectForKey:pcId];
    [self persistPairedLocked];
    [_lock unlock];
}

- (void)startDiscovery {
    if (@available(iOS 13.0, *)) {
        [_lock lock];
        if (_browser) {
            [_lock unlock];
            return;
        }
        nw_browse_descriptor_t desc = nw_browse_descriptor_create_bonjour_service("_backthread._tcp", "local.");
        nw_browse_descriptor_set_include_txt_record(desc, true);
        nw_parameters_t params = nw_parameters_create();
        nw_browser_t browser = nw_browser_create(desc, params);
        _browser = browser;
        [_lock unlock];

        __weak typeof(self) weakSelf = self;
        nw_browser_set_queue(browser, _queue);
        nw_browser_set_browse_results_changed_handler(browser, ^(nw_browse_result_t old_result, nw_browse_result_t new_result, bool batch_complete) {
            (void)old_result;
            (void)batch_complete;
            BTWifiClient *strong = weakSelf;
            if (!strong || !new_result) return;
            nw_txt_record_t txt = nw_browse_result_copy_txt_record_object(new_result);
            __block NSString *pcId = nil;
            if (txt) {
                nw_txt_record_apply(txt, ^bool(const char *key, nw_txt_record_find_key_t found, const uint8_t *value, size_t value_len) {
                    if (found == nw_txt_record_find_key_non_empty_value && key && strcmp(key, "pc") == 0 && value && value_len > 0 && value_len <= 64) {
                        pcId = [[NSString alloc] initWithBytes:value length:value_len encoding:NSUTF8StringEncoding];
                        return false;
                    }
                    return true;
                });
            }
            if (pcId.length) {
                nw_endpoint_t ep = nw_browse_result_copy_endpoint(new_result);
                if (ep) {
                    [strong->_lock lock];
                    strong->_discoveredEndpoints[pcId] = ep;
                    [strong->_lock unlock];
                }
            }
        });
        nw_browser_start(browser);
    }
}

- (void)stopDiscovery {
    if (@available(iOS 13.0, *)) {
        [_lock lock];
        nw_browser_t browser = (nw_browser_t)_browser;
        _browser = nil;
        [_discoveredEndpoints removeAllObjects];
        [_lock unlock];
        if (browser) nw_browser_cancel(browser);
    }
}

- (nullable BTTlsStream *)connectToPairedPC:(BTPairedPC *)pc
                                    timeout:(NSTimeInterval)timeout
                                      error:(NSError **)error {
    if (!pc || !BTIsHex(pc.fingerprint, 64)) {
        if (error) *error = [NSError errorWithDomain:@"BackThread.WiFi" code:10 userInfo:@{NSLocalizedDescriptionKey: @"Invalid paired PC profile"}];
        return nil;
    }

    NSError *lastError = nil;
    // Prefer Bonjour-resolved endpoint if currently visible on LAN (handles DHCP IP changes automatically).
    nw_endpoint_t bonjourEndpoint = nil;
    [_lock lock];
    bonjourEndpoint = (nw_endpoint_t)_discoveredEndpoints[pc.pcId];
    [_lock unlock];
    if (bonjourEndpoint) {
        BTTlsStream *stream = [BTTlsStream connectToEndpoint:bonjourEndpoint
                                                   labelHost:[NSString stringWithFormat:@"%@.local", pc.pcName]
                                      expectedFingerprintHex:pc.fingerprint
                                                     timeout:timeout
                                                       error:&lastError];
        if (stream) return stream;
    }

    NSString *portStr = [NSString stringWithFormat:@"%u", (unsigned)pc.port];
    for (NSString *host in pc.hosts) {
        if (!host.length) continue;
        nw_endpoint_t ep = nw_endpoint_create_host(host.UTF8String, portStr.UTF8String);
        if (!ep) continue;
        BTTlsStream *stream = [BTTlsStream connectToEndpoint:ep
                                                   labelHost:host
                                      expectedFingerprintHex:pc.fingerprint
                                                     timeout:timeout
                                                       error:&lastError];
        if (stream) return stream;
    }

    if (error) *error = lastError ?: [NSError errorWithDomain:@"BackThread.WiFi" code:11 userInfo:@{NSLocalizedDescriptionKey: @"Could not reach paired PC over Wi-Fi"}];
    return nil;
}

- (void)claimProposal:(BTPairingProposal *)proposal
             deviceId:(NSString *)deviceId
           deviceName:(NSString *)deviceName
           completion:(void (^)(BTPairedPC * _Nullable pc, NSError * _Nullable error))completion {
    dispatch_async(_queue, ^{
        @autoreleasepool {
            NSError *lastError = nil;
            BTTlsStream *stream = nil;
            NSString *portStr = [NSString stringWithFormat:@"%u", (unsigned)proposal.port];
            for (NSString *host in proposal.hosts) {
                nw_endpoint_t ep = nw_endpoint_create_host(host.UTF8String, portStr.UTF8String);
                if (!ep) continue;
                stream = [BTTlsStream connectToEndpoint:ep
                                              labelHost:host
                                 expectedFingerprintHex:proposal.fingerprint
                                                timeout:4.0
                                                  error:&lastError];
                if (stream) break;
            }
            if (!stream) {
                dispatch_async(dispatch_get_main_queue(), ^{
                    completion(nil, lastError ?: [NSError errorWithDomain:@"BackThread.WiFi" code:12 userInfo:@{NSLocalizedDescriptionKey: @"Cannot reach PC TLS endpoint or fingerprint mismatch"}]);
                });
                return;
            }

            NSDictionary *req = @{
                @"id": [[[NSUUID UUID] UUIDString] lowercaseString],
                @"type": @"pair.claim",
                @"body": @{
                    @"code": proposal.oneTimeCode ?: @"",
                    @"deviceId": deviceId ?: @"iphone",
                    @"deviceName": deviceName ?: @"iPhone"
                }
            };
            NSData *json = [NSJSONSerialization dataWithJSONObject:req options:0 error:nil];
            uint8_t headerBuf[BT_HEADER_SIZE];
            BTHeader outHeader = {(uint32_t)json.length, 1, 1, 0};
            BOOL sent = json && bt_header_encode(headerBuf, &outHeader) == 0
                && [stream writeBytes:headerBuf length:sizeof(headerBuf) timeout:5.0]
                && [stream writeBytes:json.bytes length:json.length timeout:5.0];

            BTPairedPC *paired = nil;
            NSError *claimErr = nil;
            if (sent) {
                uint8_t respHeaderBuf[BT_HEADER_SIZE];
                BTHeader inHeader;
                if ([stream readExact:respHeaderBuf length:sizeof(respHeaderBuf) timeout:8.0] && bt_header_decode(respHeaderBuf, &inHeader) == 0 && inHeader.length <= 65536) {
                    NSMutableData *payload = [NSMutableData dataWithLength:inHeader.length];
                    if ([stream readExact:payload.mutableBytes length:inHeader.length timeout:8.0]) {
                        id decoded = [NSJSONSerialization JSONObjectWithData:payload options:0 error:nil];
                        if ([decoded isKindOfClass:NSDictionary.class] && [decoded[@"type"] isEqualToString:@"pair.ok"] && [decoded[@"body"] isKindOfClass:NSDictionary.class]) {
                            NSDictionary *b = decoded[@"body"];
                            NSString *keyHex = [b[@"sessionKey"] isKindOfClass:NSString.class] ? [b[@"sessionKey"] lowercaseString] : nil;
                            if (BTIsHex(keyHex, 64)) {
                                paired = [BTPairedPC new];
                                paired.pcId = [b[@"pcId"] isKindOfClass:NSString.class] ? b[@"pcId"] : [proposal.fingerprint substringToIndex:16];
                                paired.pcName = [b[@"pcName"] isKindOfClass:NSString.class] ? b[@"pcName"] : proposal.pcName;
                                paired.fingerprint = proposal.fingerprint;
                                paired.checkCode = proposal.checkCode;
                                int p = [b[@"port"] intValue];
                                paired.port = (p > 0 && p <= 65535) ? (uint16_t)p : proposal.port;
                                paired.hosts = [b[@"hosts"] isKindOfClass:NSArray.class] && [b[@"hosts"] count] ? b[@"hosts"] : proposal.hosts;
                                paired.sessionKeyHex = keyHex;
                                [self savePairedPC:paired];
                            } else {
                                claimErr = [NSError errorWithDomain:@"BackThread.WiFi" code:15 userInfo:@{NSLocalizedDescriptionKey: @"Invalid session key in pair.ok"}];
                            }
                        } else if ([decoded isKindOfClass:NSDictionary.class] && [decoded[@"body"] isKindOfClass:NSDictionary.class]) {
                            NSString *msg = [decoded[@"body"][@"error"] isKindOfClass:NSString.class] ? decoded[@"body"][@"error"] : @"Pairing rejected by PC";
                            claimErr = [NSError errorWithDomain:@"BackThread.WiFi" code:14 userInfo:@{NSLocalizedDescriptionKey: msg}];
                        }
                    }
                }
            }
            [stream close];
            if (!paired && !claimErr) {
                claimErr = [NSError errorWithDomain:@"BackThread.WiFi" code:13 userInfo:@{NSLocalizedDescriptionKey: @"Pairing handshake failed"}];
            }
            dispatch_async(dispatch_get_main_queue(), ^{
                completion(paired, claimErr);
            });
        }
    });
}

@end
