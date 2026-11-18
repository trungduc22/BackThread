#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>

@class BTAgent;

NS_ASSUME_NONNULL_BEGIN

@interface BTBufferedMediaEngine : NSObject <AVAssetResourceLoaderDelegate>
@property(nonatomic, weak, nullable) BTAgent *agent;
@property(nonatomic, copy, readonly, nullable) NSString *activeStreamId;
@property(nonatomic, readonly) uint64_t totalBytes;
@property(nonatomic, readonly) uint64_t receivedBytes;
@property(nonatomic, readonly) BOOL eofReached;

- (AVURLAsset * _Nullable)prepareStreamWithId:(NSString *)streamId
                                   generation:(uint64_t)generation
                                   totalBytes:(uint64_t)totalBytes
                                     mimeType:(NSString *)mimeType;

- (void)ingestChunkForStreamId:(NSString *)streamId
                        offset:(uint64_t)offset
                         bytes:(NSData *)bytes;

- (void)markEndOfStreamForStreamId:(NSString *)streamId;
- (void)handleMemoryPressure;
- (void)closeStream;
@end

NS_ASSUME_NONNULL_END
