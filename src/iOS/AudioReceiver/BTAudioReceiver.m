#import "BTAudioReceiver.h"
#import <AVFoundation/AVFoundation.h>

static void BTOutput(void *ctx,AudioQueueRef q,AudioQueueBufferRef b) {
    BTAudioReceiver *receiver=(__bridge BTAudioReceiver *)ctx;
    pthread_mutex_lock(&receiver->_mutex);
    BOOL active=receiver->_running;
    if(active&&!receiver->_primed&&receiver->_ring.count>=5760)receiver->_primed=YES;
    if(active&&receiver->_primed)bt_pcm_read(&receiver->_ring,b->mAudioData,1920);
    else memset(b->mAudioData,0,1920);
    receiver->_callbacks++;
    pthread_mutex_unlock(&receiver->_mutex);
    b->mAudioDataByteSize=1920;
    if(active) {
        OSStatus status=AudioQueueEnqueueBuffer(q,b,0,NULL);
        if(status!=noErr){pthread_mutex_lock(&receiver->_mutex);receiver->_lastStatus=status;receiver->_running=NO;pthread_mutex_unlock(&receiver->_mutex);}
    }
}
@implementation BTAudioReceiver
- (instancetype)init {
    if((self=[super init])) {
        pthread_mutex_init(&_mutex,NULL);_volume=1;_lifecycle=[NSRecursiveLock new];
        [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(interruption:) name:AVAudioSessionInterruptionNotification object:nil];
        [[NSNotificationCenter defaultCenter] addObserver:self selector:@selector(reset:) name:AVAudioSessionMediaServicesWereResetNotification object:nil];
    } return self;
}
- (BOOL)running { pthread_mutex_lock(&_mutex);BOOL v=_running;pthread_mutex_unlock(&_mutex);return v; }
- (BOOL)start:(NSError **)error {
    [_lifecycle lock];@try {
    [self stop]; AVAudioSession *session=[AVAudioSession sharedInstance];
    if(![session setCategory:AVAudioSessionCategoryPlayback error:error])return NO;
    [session setPreferredSampleRate:48000 error:NULL];[session setPreferredIOBufferDuration:0.01 error:NULL];
    if(![session setActive:YES error:error])return NO;_sessionActive=YES;
    AudioStreamBasicDescription f={0};f.mSampleRate=48000;f.mFormatID=kAudioFormatLinearPCM;f.mFormatFlags=kLinearPCMFormatFlagIsSignedInteger|kAudioFormatFlagIsPacked;f.mBytesPerPacket=4;f.mFramesPerPacket=1;f.mBytesPerFrame=4;f.mChannelsPerFrame=2;f.mBitsPerChannel=16;
    OSStatus status=AudioQueueNewOutput(&f,BTOutput,(__bridge void *)self,NULL,NULL,0,&_queue);
    if(status!=noErr) { [self stop];if(error)*error=[NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];return NO; }
    pthread_mutex_lock(&_mutex);memset(&_ring,0,sizeof(_ring));_primed=NO;_interrupted=NO;_running=YES;_lastStatus=noErr;_callbacks=0;pthread_mutex_unlock(&_mutex);
    for(int i=0;i<3;i++) {
        status=AudioQueueAllocateBuffer(_queue,1920,&_buffers[i]);
        if(status!=noErr) { [self stop];if(error)*error=[NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];return NO; }
        memset(_buffers[i]->mAudioData,0,1920);_buffers[i]->mAudioDataByteSize=1920;status=AudioQueueEnqueueBuffer(_queue,_buffers[i],0,NULL);
        if(status!=noErr){[self stop];if(error)*error=[NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];return NO;}
    }
    AudioQueueSetParameter(_queue,kAudioQueueParam_Volume,_volume);
    status=AudioQueueStart(_queue,NULL);
    if(status!=noErr) { [self stop];if(error)*error=[NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];return NO; }
    return YES;
    } @finally { [_lifecycle unlock]; }
}
- (void)receive:(NSData *)data { pthread_mutex_lock(&_mutex);if(_running)bt_pcm_write(&_ring,data.bytes,data.length);pthread_mutex_unlock(&_mutex); }
- (void)stop {
    [_lifecycle lock];
    pthread_mutex_lock(&_mutex);_running=NO;bt_pcm_clear(&_ring);pthread_mutex_unlock(&_mutex);
    if(_queue) { AudioQueueStop(_queue,true);AudioQueueDispose(_queue,true);_queue=NULL; }
    if(_sessionActive){[[AVAudioSession sharedInstance] setActive:NO withOptions:AVAudioSessionSetActiveOptionNotifyOthersOnDeactivation error:NULL];_sessionActive=NO;}
    [_lifecycle unlock];
}
- (void)setVolume:(float)value { [_lifecycle lock];_volume=fmaxf(0,fminf(1,value));if(_queue)AudioQueueSetParameter(_queue,kAudioQueueParam_Volume,_volume);[_lifecycle unlock]; }
- (NSDictionary *)statistics {
    pthread_mutex_lock(&_mutex);size_t buffered=_ring.count;uint64_t dropped=_ring.dropped,silence=_ring.silence,callbacks=_callbacks;OSStatus status=_lastStatus;BOOL running=_running,primed=_primed,interrupted=_interrupted;pthread_mutex_unlock(&_mutex);
    NSMutableArray *routes=[NSMutableArray new];AVAudioSession *session=[AVAudioSession sharedInstance];for(AVAudioSessionPortDescription *port in session.currentRoute.outputs)if(port.portType)[routes addObject:port.portType];
    return @{@"bufferedBytes":@(buffered),@"droppedBytes":@(dropped),@"silenceBytes":@(silence),@"callbacks":@(callbacks),@"lastOSStatus":@(status),@"running":@(running),@"primed":@(primed),@"interrupted":@(interrupted),@"routeTypes":routes,@"outputVolume":@(session.outputVolume)};
}
- (void)interruption:(NSNotification *)n {
    [_lifecycle lock];
    if([n.userInfo[AVAudioSessionInterruptionTypeKey] unsignedIntegerValue]==AVAudioSessionInterruptionTypeBegan) { if(_queue)AudioQueuePause(_queue);pthread_mutex_lock(&_mutex);bt_pcm_clear(&_ring);_primed=NO;_interrupted=YES;pthread_mutex_unlock(&_mutex); }
    else if([n.userInfo[AVAudioSessionInterruptionOptionKey] unsignedIntegerValue]&AVAudioSessionInterruptionOptionShouldResume) {
        NSError *error=nil;BOOL activated=[[AVAudioSession sharedInstance] setActive:YES error:&error];OSStatus status=activated&&_queue?AudioQueueStart(_queue,NULL):-1;
        pthread_mutex_lock(&_mutex);_interrupted=NO;_lastStatus=status;if(status!=noErr)_running=NO;pthread_mutex_unlock(&_mutex);
    }
    [_lifecycle unlock];
}
- (void)reset:(NSNotification *)n { if(self.running) { NSError *error=nil;[self start:&error]; } }
- (void)dealloc { [[NSNotificationCenter defaultCenter] removeObserver:self];[self stop];pthread_mutex_destroy(&_mutex); }
@end
