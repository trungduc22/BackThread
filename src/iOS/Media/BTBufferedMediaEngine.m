#import "BTBufferedMediaEngine.h"
#import "../Agent/BTAgent.h"

static uint64_t const BTBufferedMaxCacheBytes = 24ULL * 1024ULL * 1024ULL;
static uint32_t const BTBufferedChunkWindow = 32768U;

@interface BTRangeExtent : NSObject
@property(nonatomic) uint64_t start;
@property(nonatomic) uint64_t end;
@end

@implementation BTRangeExtent
@end

@implementation BTBufferedMediaEngine {
    dispatch_queue_t _queue;
    NSString *_cacheDir;
    NSString *_streamId;
    uint64_t _generation;
    uint64_t _totalBytes;
    uint64_t _receivedBytes;
    NSString *_uti;
    NSString *_filePath;
    NSFileHandle *_writeHandle;
    NSFileHandle *_readHandle;
    NSMutableArray<BTRangeExtent *> *_ranges;
    NSMutableArray<AVAssetResourceLoadingRequest *> *_pendingRequests;
    NSMutableSet<NSNumber *> *_inflightOffsets;
    BOOL _eofReached;
    BOOL _closed;
}

- (instancetype)init {
    if ((self = [super init])) {
        _queue = dispatch_queue_create("app.backthread.buffered.loader", DISPATCH_QUEUE_SERIAL);
        NSString *base = NSSearchPathForDirectoriesInDomains(NSCachesDirectory, NSUserDomainMask, YES).firstObject
            ?: NSTemporaryDirectory();
        _cacheDir = [base stringByAppendingPathComponent:@"BackThreadBuffered"];
        [[NSFileManager defaultManager] createDirectoryAtPath:_cacheDir
                                  withIntermediateDirectories:YES
                                                   attributes:nil
                                                        error:nil];
        _ranges = [NSMutableArray new];
        _pendingRequests = [NSMutableArray new];
        _inflightOffsets = [NSMutableSet new];
    }
    return self;
}

+ (NSString *)utiForMimeType:(NSString *)mimeType extensionOut:(NSString **)extOut {
    NSString *lower = mimeType.lowercaseString;
    if ([lower containsString:@"quicktime"]) {
        if (extOut) *extOut = @"mov";
        return @"com.apple.quicktime-movie";
    }
    if ([lower containsString:@"m4v"]) {
        if (extOut) *extOut = @"m4v";
        return @"com.apple.m4v-video";
    }
    if ([lower containsString:@"mpegurl"] || [lower containsString:@"m3u8"]) {
        if (extOut) *extOut = @"m3u8";
        return @"com.apple.mpegurl";
    }
    if ([lower containsString:@"audio/mp4"] || [lower containsString:@"m4a"]) {
        if (extOut) *extOut = @"m4a";
        return @"com.apple.m4a-audio";
    }
    if ([lower containsString:@"mpeg"] || [lower containsString:@"mp3"]) {
        if (extOut) *extOut = @"mp3";
        return @"public.mp3";
    }
    if ([lower containsString:@"aac"]) {
        if (extOut) *extOut = @"aac";
        return @"public.aac-audio";
    }
    if ([lower containsString:@"wav"]) {
        if (extOut) *extOut = @"wav";
        return @"com.microsoft.waveform-audio";
    }
    if (extOut) *extOut = @"mp4";
    return @"public.mpeg-4";
}

- (void)evictOldCacheLockedKeeping:(NSString * _Nullable)keepPath {
    NSFileManager *fm = [NSFileManager defaultManager];
    NSArray<NSString *> *files = [fm contentsOfDirectoryAtPath:_cacheDir error:nil];
    if (!files.count) return;

    NSMutableArray<NSDictionary *> *entries = [NSMutableArray new];
    uint64_t totalSize = 0;
    for (NSString *name in files) {
        NSString *full = [_cacheDir stringByAppendingPathComponent:name];
        if (keepPath && [full isEqualToString:keepPath]) continue;
        NSDictionary *attrs = [fm attributesOfItemAtPath:full error:nil];
        if (!attrs) continue;
        uint64_t sz = [attrs[NSFileSize] unsignedLongLongValue];
        NSDate *mod = attrs[NSFileModificationDate] ?: [NSDate distantPast];
        totalSize += sz;
        [entries addObject:@{@"path": full, @"size": @(sz), @"date": mod}];
    }

    if (totalSize <= BTBufferedMaxCacheBytes) return;
    [entries sortUsingComparator:^NSComparisonResult(NSDictionary *a, NSDictionary *b) {
        return [a[@"date"] compare:b[@"date"]];
    }];
    for (NSDictionary *entry in entries) {
        [fm removeItemAtPath:entry[@"path"] error:nil];
        totalSize -= [entry[@"size"] unsignedLongLongValue];
        if (totalSize <= BTBufferedMaxCacheBytes / 2) break;
    }
}

- (void)resetLocked {
    [_writeHandle closeFile];
    [_readHandle closeFile];
    _writeHandle = nil;
    _readHandle = nil;
    for (AVAssetResourceLoadingRequest *req in _pendingRequests) {
        if (!req.isFinished && !req.isCancelled) {
            [req finishLoadingWithError:[NSError errorWithDomain:NSURLErrorDomain
                                                            code:NSURLErrorCancelled
                                                        userInfo:nil]];
        }
    }
    [_pendingRequests removeAllObjects];
    [_ranges removeAllObjects];
    [_inflightOffsets removeAllObjects];
    if (_streamId) {
        [self.agent unregisterBufferedStreamId:_streamId];
    }
    _streamId = nil;
    _generation = 0;
    _totalBytes = 0;
    _receivedBytes = 0;
    _eofReached = NO;
}

- (AVURLAsset * _Nullable)prepareStreamWithId:(NSString *)streamId
                                   generation:(uint64_t)generation
                                   totalBytes:(uint64_t)totalBytes
                                     mimeType:(NSString *)mimeType {
    if (!streamId.length || totalBytes == 0) return nil;
    NSString *ext = @"mp4";
    NSString *uti = [BTBufferedMediaEngine utiForMimeType:mimeType ?: @"video/mp4" extensionOut:&ext];
    __block BOOL prepared = NO;

    dispatch_sync(_queue, ^{
        [self resetLocked];
        self->_closed = NO;
        self->_streamId = [streamId.lowercaseString copy];
        self->_generation = generation;
        self->_totalBytes = totalBytes;
        self->_uti = uti;
        self->_filePath = [self->_cacheDir stringByAppendingPathComponent:
                           [NSString stringWithFormat:@"%@.%@", self->_streamId, ext]];
        [self evictOldCacheLockedKeeping:nil];

        [[NSFileManager defaultManager] createFileAtPath:self->_filePath contents:nil attributes:nil];
        self->_writeHandle = [NSFileHandle fileHandleForWritingAtPath:self->_filePath];
        self->_readHandle = [NSFileHandle fileHandleForReadingAtPath:self->_filePath];
        [self.agent registerBufferedStreamId:self->_streamId];
        prepared = (self->_filePath.length > 0 && self->_writeHandle != nil && self->_readHandle != nil);
    });

    if (!prepared) return nil;
    NSURL *customURL = [NSURL URLWithString:[NSString stringWithFormat:@"backthread-buf://stream/%@.%@",
                                             streamId.lowercaseString, ext]];
    if (!customURL) return nil;
    AVURLAsset *asset = [[AVURLAsset alloc] initWithURL:customURL options:nil];
    [asset.resourceLoader setDelegate:self queue:_queue];
    return asset;
}

- (void)addExtentLockedFrom:(uint64_t)start end:(uint64_t)end {
    if (end <= start) return;
    BTRangeExtent *ext = [BTRangeExtent new];
    ext.start = start;
    ext.end = end;
    [_ranges addObject:ext];
    [_ranges sortUsingComparator:^NSComparisonResult(BTRangeExtent *a, BTRangeExtent *b) {
        if (a.start < b.start) return NSOrderedAscending;
        if (a.start > b.start) return NSOrderedDescending;
        return NSOrderedSame;
    }];

    NSMutableArray<BTRangeExtent *> *merged = [NSMutableArray new];
    for (BTRangeExtent *cur in _ranges) {
        BTRangeExtent *last = merged.lastObject;
        if (!last || cur.start > last.end) {
            [merged addObject:cur];
        } else if (cur.end > last.end) {
            last.end = cur.end;
        }
    }
    _ranges = merged;
}

- (uint64_t)contiguousAvailableFromLocked:(uint64_t)offset {
    for (BTRangeExtent *ext in _ranges) {
        if (offset >= ext.start && offset < ext.end) {
            return ext.end - offset;
        }
        if (ext.start > offset) break;
    }
    return 0;
}

- (void)requestChunksLockedAroundOffset:(uint64_t)offset {
    if (_closed || !_streamId || offset >= _totalBytes) return;
    BTAgent *agent = self.agent;
    if (!agent) return;

    // Request current aligned block plus 1 lookahead block (bounded <= 64 KiB in flight per miss).
    uint64_t aligned = (offset / BTBufferedChunkWindow) * BTBufferedChunkWindow;
    for (int i = 0; i < 2; i++) {
        uint64_t reqOff = aligned + (uint64_t)i * BTBufferedChunkWindow;
        if (reqOff >= _totalBytes) break;
        if ([self contiguousAvailableFromLocked:reqOff] >= MIN((uint64_t)BTBufferedChunkWindow, _totalBytes - reqOff)) {
            continue;
        }
        NSNumber *key = @(reqOff);
        if ([_inflightOffsets containsObject:key]) continue;
        if (_inflightOffsets.count >= 6) break;

        uint32_t reqLen = (uint32_t)MIN((uint64_t)BTBufferedChunkWindow, _totalBytes - reqOff);
        [_inflightOffsets addObject:key];
        [agent sendMediaMessage:@"media.buffered.request" body:@{
            @"streamId": _streamId,
            @"generation": @(_generation),
            @"offset": @(reqOff),
            @"length": @(reqLen)
        }];
    }
}

- (void)processPendingRequestsLocked {
    NSMutableArray<AVAssetResourceLoadingRequest *> *completed = [NSMutableArray new];
    for (AVAssetResourceLoadingRequest *loadingRequest in _pendingRequests) {
        if (loadingRequest.isCancelled || loadingRequest.isFinished) {
            [completed addObject:loadingRequest];
            continue;
        }

        if (loadingRequest.contentInformationRequest) {
            loadingRequest.contentInformationRequest.contentType = _uti;
            loadingRequest.contentInformationRequest.contentLength = (long long)_totalBytes;
            loadingRequest.contentInformationRequest.byteRangeAccessSupported = YES;
        }

        AVAssetResourceLoadingDataRequest *dataReq = loadingRequest.dataRequest;
        if (!dataReq) {
            [loadingRequest finishLoading];
            [completed addObject:loadingRequest];
            continue;
        }

        uint64_t currentOffset = (uint64_t)dataReq.currentOffset;
        NSInteger requestedLength = dataReq.requestedLength;
        BOOL toEnd = dataReq.requestsAllDataToEndOfResource;
        uint64_t endTarget = toEnd ? _totalBytes : MIN(_totalBytes, (uint64_t)dataReq.requestedOffset + (uint64_t)requestedLength);

        while (currentOffset < endTarget) {
            uint64_t avail = [self contiguousAvailableFromLocked:currentOffset];
            if (avail == 0) {
                [self requestChunksLockedAroundOffset:currentOffset];
                break;
            }
            uint64_t step = MIN(avail, MIN(endTarget - currentOffset, (uint64_t)65536));
            if (step == 0 || !_readHandle) break;
            [_readHandle seekToFileOffset:currentOffset];
            NSData *chunk = [_readHandle readDataOfLength:(NSUInteger)step];
            if (chunk.length == 0) break;
            [dataReq respondWithData:chunk];
            currentOffset += chunk.length;
        }

        if (currentOffset >= endTarget) {
            [loadingRequest finishLoading];
            [completed addObject:loadingRequest];
        }
    }
    [_pendingRequests removeObjectsInArray:completed];
}

- (BOOL)resourceLoader:(AVAssetResourceLoader *)resourceLoader
shouldWaitForLoadingOfRequestedResource:(AVAssetResourceLoadingRequest *)loadingRequest {
    (void)resourceLoader;
    if (_closed) return NO;
    [_pendingRequests addObject:loadingRequest];
    [self processPendingRequestsLocked];
    return YES;
}

- (void)resourceLoader:(AVAssetResourceLoader *)resourceLoader
didCancelLoadingRequest:(AVAssetResourceLoadingRequest *)loadingRequest {
    (void)resourceLoader;
    [_pendingRequests removeObject:loadingRequest];
}

- (void)ingestChunkForStreamId:(NSString *)streamId
                        offset:(uint64_t)offset
                         bytes:(NSData *)bytes {
    if (!streamId.length || bytes.length == 0) return;
    NSString *lower = [streamId.lowercaseString copy];
    NSData *dataCopy = [bytes copy];
    __weak typeof(self) weakSelf = self;
    dispatch_async(_queue, ^{
        BTBufferedMediaEngine *strong = weakSelf;
        if (!strong || strong->_closed || !strong->_streamId || ![strong->_streamId isEqualToString:lower]) return;
        if (offset >= strong->_totalBytes || !strong->_writeHandle) return;

        uint64_t maxWritable = strong->_totalBytes - offset;
        NSUInteger writeLen = (NSUInteger)MIN((uint64_t)dataCopy.length, maxWritable);
        NSData *slice = writeLen == dataCopy.length ? dataCopy : [dataCopy subdataWithRange:NSMakeRange(0, writeLen)];

        @try {
            [strong->_writeHandle seekToFileOffset:offset];
            [strong->_writeHandle writeData:slice];
        } @catch (NSException *ignored) {
            (void)ignored;
            return;
        }

        strong->_receivedBytes += writeLen;
        uint64_t aligned = (offset / BTBufferedChunkWindow) * BTBufferedChunkWindow;
        [strong->_inflightOffsets removeObject:@(aligned)];
        [strong addExtentLockedFrom:offset end:offset + writeLen];
        [strong processPendingRequestsLocked];
    });
}

- (void)markEndOfStreamForStreamId:(NSString *)streamId {
    if (!streamId.length) return;
    NSString *lower = [streamId.lowercaseString copy];
    __weak typeof(self) weakSelf = self;
    dispatch_async(_queue, ^{
        BTBufferedMediaEngine *strong = weakSelf;
        if (!strong || ![strong->_streamId isEqualToString:lower]) return;
        strong->_eofReached = YES;
        [strong processPendingRequestsLocked];
    });
}

- (void)handleMemoryPressure {
    __weak typeof(self) weakSelf = self;
    dispatch_async(_queue, ^{
        BTBufferedMediaEngine *strong = weakSelf;
        if (!strong) return;
        [strong evictOldCacheLockedKeeping:strong->_filePath];
    });
}

- (void)closeStream {
    dispatch_sync(_queue, ^{
        self->_closed = YES;
        [self resetLocked];
    });
}

- (NSString * _Nullable)activeStreamId {
    __block NSString *v = nil;
    dispatch_sync(_queue, ^{ v = self->_streamId; });
    return v;
}

- (uint64_t)totalBytes {
    __block uint64_t v = 0;
    dispatch_sync(_queue, ^{ v = self->_totalBytes; });
    return v;
}

- (uint64_t)receivedBytes {
    __block uint64_t v = 0;
    dispatch_sync(_queue, ^{ v = self->_receivedBytes; });
    return v;
}

- (BOOL)eofReached {
    __block BOOL v = NO;
    dispatch_sync(_queue, ^{ v = self->_eofReached; });
    return v;
}

- (void)dealloc {
    [_writeHandle closeFile];
    [_readHandle closeFile];
}

@end
