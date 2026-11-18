#import <Foundation/Foundation.h>
#import <AudioToolbox/AudioToolbox.h>
#import "BTPcmRing.h"
#import <pthread.h>
@interface BTAudioReceiver : NSObject {
@public
    BTPcmRing _ring;
    pthread_mutex_t _mutex;
    AudioQueueRef _queue;
    AudioQueueBufferRef _buffers[3];
    BOOL _running;
    BOOL _primed;
    BOOL _interrupted;
    BOOL _sessionActive;
    BOOL _requested;
    OSStatus _lastStatus;
    uint64_t _callbacks;
    uint64_t _inputNonzero,_renderNonzero;
    int32_t _inputPeak,_renderPeak;
    NSUInteger _underrunStreak;
    float _volume;
    NSRecursiveLock *_lifecycle;
}
@property(nonatomic,readonly) BOOL running;
- (BOOL)start:(NSError **)error;
- (void)receive:(NSData *)data;
- (void)stop;
- (BOOL)setVolume:(float)value error:(NSError **)error;
- (NSDictionary *)statistics;
@end
