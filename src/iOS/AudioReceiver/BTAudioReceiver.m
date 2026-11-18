#import "BTAudioReceiver.h"
#import "BTAudioSessionCoordinator.h"
#import <AVFoundation/AVFoundation.h>
#include <errno.h>

static void BTOutput(void *ctx,AudioQueueRef q,AudioQueueBufferRef b) {
    BTAudioReceiver *receiver=(__bridge BTAudioReceiver *)ctx;
    pthread_mutex_lock(&receiver->_mutex);
    BOOL active=receiver->_running;
    if(active&&!receiver->_primed&&receiver->_ring.count>=5760){receiver->_primed=YES;receiver->_underrunStreak=0;}
    if(active&&receiver->_primed) {
        size_t readBytes=bt_pcm_read(&receiver->_ring,b->mAudioData,1920);
        if(readBytes==0) {
            if(++receiver->_underrunStreak>=2){receiver->_primed=NO;receiver->_underrunStreak=0;}
        } else {
            receiver->_underrunStreak=0;
        }
    }
    else memset(b->mAudioData,0,1920);
    const int16_t *samples=b->mAudioData;
    for(unsigned i=0;i<960;i++){int32_t value=samples[i];if(value){receiver->_renderNonzero++;if(value<0)value=-value;if(value>receiver->_renderPeak)receiver->_renderPeak=value;}}
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
- (BOOL)failStart:(OSStatus)status error:(NSError **)error {
    [self stop];pthread_mutex_lock(&_mutex);_lastStatus=status;pthread_mutex_unlock(&_mutex);
    if(error)*error=[NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];return NO;
}
- (BOOL)start:(NSError **)error {
    [_lifecycle lock];@try {
    [self stop];
    if(![[BTAudioSessionCoordinator sharedCoordinator] activateForPcmReceiver:error])return NO;
    _sessionActive=YES;
    AudioStreamBasicDescription f={0};f.mSampleRate=48000;f.mFormatID=kAudioFormatLinearPCM;f.mFormatFlags=kLinearPCMFormatFlagIsSignedInteger|kAudioFormatFlagIsPacked;f.mBytesPerPacket=4;f.mFramesPerPacket=1;f.mBytesPerFrame=4;f.mChannelsPerFrame=2;f.mBitsPerChannel=16;
    OSStatus status=AudioQueueNewOutput(&f,BTOutput,(__bridge void *)self,NULL,NULL,0,&_queue);
    if(status!=noErr)return [self failStart:status error:error];
    pthread_mutex_lock(&_mutex);memset(&_ring,0,sizeof(_ring));_primed=NO;_underrunStreak=0;_interrupted=NO;_running=YES;_lastStatus=noErr;_callbacks=0;_inputNonzero=_renderNonzero=0;_inputPeak=_renderPeak=0;pthread_mutex_unlock(&_mutex);
    for(int i=0;i<3;i++) {
        status=AudioQueueAllocateBuffer(_queue,1920,&_buffers[i]);
        if(status!=noErr)return [self failStart:status error:error];
        memset(_buffers[i]->mAudioData,0,1920);_buffers[i]->mAudioDataByteSize=1920;status=AudioQueueEnqueueBuffer(_queue,_buffers[i],0,NULL);
        if(status!=noErr)return [self failStart:status error:error];
    }
    status=AudioQueueSetParameter(_queue,kAudioQueueParam_Volume,_volume);
    if(status!=noErr)return [self failStart:status error:error];
    status=AudioQueueStart(_queue,NULL);
    if(status!=noErr)return [self failStart:status error:error];
    _requested=YES;
    return YES;
    } @finally { [_lifecycle unlock]; }
}
- (void)receive:(NSData *)data { pthread_mutex_lock(&_mutex);if(_running&&!_interrupted){const uint8_t *bytes=data.bytes;for(NSUInteger i=0;i+1<data.length;i+=2){int32_t value=(int16_t)((uint16_t)bytes[i]|(uint16_t)bytes[i+1]<<8);if(value){_inputNonzero++;if(value<0)value=-value;if(value>_inputPeak)_inputPeak=value;}}bt_pcm_write(&_ring,data.bytes,data.length);}pthread_mutex_unlock(&_mutex); }
- (void)stop {
    [_lifecycle lock];
    _requested=NO;
    pthread_mutex_lock(&_mutex);_running=NO;_primed=NO;_underrunStreak=0;_interrupted=NO;bt_pcm_clear(&_ring);pthread_mutex_unlock(&_mutex);
    if(_queue) { AudioQueueStop(_queue,true);AudioQueueDispose(_queue,true);_queue=NULL; }
    if(_sessionActive){[[BTAudioSessionCoordinator sharedCoordinator] deactivatePcmReceiver];_sessionActive=NO;}
    [_lifecycle unlock];
}
- (BOOL)setVolume:(float)value error:(NSError **)error {
    [_lifecycle lock];@try {
        if(!isfinite(value)||value<0||value>1){if(error)*error=[NSError errorWithDomain:NSPOSIXErrorDomain code:EINVAL userInfo:nil];return NO;}
        OSStatus status=_queue?AudioQueueSetParameter(_queue,kAudioQueueParam_Volume,value):noErr;
        if(status!=noErr){pthread_mutex_lock(&_mutex);_lastStatus=status;pthread_mutex_unlock(&_mutex);if(error)*error=[NSError errorWithDomain:NSOSStatusErrorDomain code:status userInfo:nil];return NO;}
        _volume=value;return YES;
    } @finally{[_lifecycle unlock];}
}
- (NSDictionary *)statistics {
    [_lifecycle lock];@try {
    pthread_mutex_lock(&_mutex);size_t buffered=_ring.count;uint64_t dropped=_ring.dropped,silence=_ring.silence,callbacks=_callbacks;OSStatus status=_lastStatus;BOOL running=_running,primed=_primed,interrupted=_interrupted;uint64_t inputNonzero=_inputNonzero,renderNonzero=_renderNonzero;int32_t inputPeak=_inputPeak,renderPeak=_renderPeak;pthread_mutex_unlock(&_mutex);
    UInt32 queueRunning=0,size=sizeof(queueRunning);AudioQueueParameterValue gain=_volume;OSStatus runningStatus=noErr,gainStatus=noErr;if(_queue){runningStatus=AudioQueueGetProperty(_queue,kAudioQueueProperty_IsRunning,&queueRunning,&size);gainStatus=AudioQueueGetParameter(_queue,kAudioQueueParam_Volume,&gain);}
    NSMutableArray *routes=[NSMutableArray new];AVAudioSession *session=[AVAudioSession sharedInstance];for(AVAudioSessionPortDescription *port in session.currentRoute.outputs)if(port.portType)[routes addObject:port.portType];
    return @{@"bufferedBytes":@(buffered),@"droppedBytes":@(dropped),@"silenceBytes":@(silence),@"callbacks":@(callbacks),@"lastOSStatus":@(status),@"running":@(running),@"primed":@(primed),@"interrupted":@(interrupted),@"routeTypes":routes,@"outputVolume":@(session.outputVolume),@"queueRunning":@(queueRunning!=0),@"queueGain":@(gain),@"queueRunningStatus":@(runningStatus),@"queueGainStatus":@(gainStatus),@"queuePresent":@(_queue!=NULL),@"hardwareSampleRate":@(session.sampleRate),@"sessionCategory":session.category?:@"",@"sessionMode":session.mode?:@"",@"otherAudioPlaying":@(session.otherAudioPlaying),@"secondaryAudioSilencedHint":@(session.secondaryAudioShouldBeSilencedHint),@"inputPeak":@(inputPeak),@"renderPeak":@(renderPeak),@"inputNonzeroSamples":@(inputNonzero),@"renderNonzeroSamples":@(renderNonzero)};
    } @finally{[_lifecycle unlock];}
}
- (void)interruption:(NSNotification *)n {
    [_lifecycle lock];@try {
    // Delayed resume notifications must never reactivate a session after stop/disconnect.
    if(!_requested||!_queue||!self.running)return;
    if([n.userInfo[AVAudioSessionInterruptionTypeKey] unsignedIntegerValue]==AVAudioSessionInterruptionTypeBegan) { OSStatus status=AudioQueuePause(_queue);pthread_mutex_lock(&_mutex);bt_pcm_clear(&_ring);_primed=NO;_underrunStreak=0;_interrupted=YES;_lastStatus=status;if(status!=noErr)_running=NO;pthread_mutex_unlock(&_mutex); }
    else if([n.userInfo[AVAudioSessionInterruptionOptionKey] unsignedIntegerValue]&AVAudioSessionInterruptionOptionShouldResume) {
        NSError *error=nil;BOOL activated=[[BTAudioSessionCoordinator sharedCoordinator] reactivateIfNeeded:&error];OSStatus status=activated&&_queue?AudioQueueStart(_queue,NULL):-1;
        pthread_mutex_lock(&_mutex);bt_pcm_clear(&_ring);_primed=NO;_underrunStreak=0;_interrupted=NO;_lastStatus=status;if(status!=noErr)_running=NO;pthread_mutex_unlock(&_mutex);
    }
    } @finally { [_lifecycle unlock]; }
}
- (void)reset:(NSNotification *)n {
    (void)n;[_lifecycle lock];@try {
        // Check intent under the same lock as Stop. A failed queue still needs recovery,
        // while an old reset delivered after Stop must never restart playback.
        if(_requested){NSError *error=nil;[self start:&error];}
    } @finally{[_lifecycle unlock];}
}
- (void)dealloc { [[NSNotificationCenter defaultCenter] removeObserver:self];[self stop];pthread_mutex_destroy(&_mutex); }
@end
