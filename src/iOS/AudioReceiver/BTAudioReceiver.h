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
    float _volume;
    NSRecursiveLock *_lifecycle;
}
@property(nonatomic,readonly) BOOL running;
- (BOOL)start:(NSError **)error;
- (void)receive:(NSData *)data;
- (void)stop;
- (void)setVolume:(float)value;
- (NSDictionary *)statistics;
@end
