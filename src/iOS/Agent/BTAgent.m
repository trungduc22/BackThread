#import "BTAgent.h"
#import "../Transport/BTChannel.h"
#import <arpa/inet.h>
#import <float.h>
#import "../AudioReceiver/BTAudioReceiver.h"
#import "../../Shared/Protocol/bt_wire.h"
#import <CommonCrypto/CommonHMAC.h>
#import <CommonCrypto/CommonDigest.h>
#import <Security/Security.h>
#import <sys/socket.h>
#import <netinet/in.h>
#import <netinet/tcp.h>
#import <stdatomic.h>
#import <unistd.h>
#import <sys/stat.h>
#import <mach/mach.h>

static NSString *hex(const uint8_t *b,size_t n) { char *p=malloc(n*2+1);if(!p)return nil;for(size_t i=0;i<n;i++)snprintf(p+i*2,3,"%02x",b[i]);NSString *s=[NSString stringWithUTF8String:p];free(p);return s; }
static BOOL hexString(NSString *s,NSUInteger length) { return [s isKindOfClass:NSString.class]&&s.length==length&&[s rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet]].location==NSNotFound; }
static BOOL constantEqual(NSString *a,NSString *b) { if(![a isKindOfClass:NSString.class]||a.length!=b.length)return NO;NSData *x=[a dataUsingEncoding:NSASCIIStringEncoding],*y=[b dataUsingEncoding:NSASCIIStringEncoding];if(!x||!y)return NO;uint8_t diff=0;for(NSUInteger i=0;i<x.length;i++)diff|=((const uint8_t *)x.bytes)[i]^((const uint8_t *)y.bytes)[i];return diff==0; }
static NSString *proof(NSData *key,NSString *role,NSString *server,NSString *client) { NSData *text=[[NSString stringWithFormat:@"BTH1:%@:%@:%@",role,server,client] dataUsingEncoding:NSASCIIStringEncoding];uint8_t out[CC_SHA256_DIGEST_LENGTH];CCHmac(kCCHmacAlgSHA256,key.bytes,key.length,text.bytes,text.length,out);return hex(out,sizeof(out)); }
static BOOL jsonDepth(id value,unsigned depth) { BOOL dict=[value isKindOfClass:NSDictionary.class],array=[value isKindOfClass:NSArray.class];if((dict||array)&&depth>16)return NO;if(dict){for(id key in value)if(!jsonDepth(value[key],depth+1))return NO;}else if(array){for(id child in value)if(!jsonDepth(child,depth+1))return NO;}return YES; }

@interface BTAgent ()
@property(atomic,strong) BTChannel *activeChannel;
@end
@implementation BTAgent {
    NSData *_key;
    NSString *_root;
    BTAudioReceiver *_audio;
    int _listener,_client;
    atomic_bool _stopping;
    BOOL _audioOn,_authenticated,_started;
    uint64_t _generation;
    NSLock *_tx;
    uint32_t _sequences[8],_previousAudio;
    uint64_t _audioPackets,_audioBytes,_audioGaps;
    dispatch_queue_t _network,_worker,_commands,_fileIO,_jobCommands;
    dispatch_semaphore_t _commandSlots,_jobCommandSlots;
    dispatch_semaphore_t _fileSlots;
    NSMutableSet *_committing;
    NSMutableDictionary *_jobs,*_jobSpecs;
    NSMutableSet *_cancelled;
    NSMutableArray *_events;
    NSMutableDictionary *_pendingCommands;
    dispatch_semaphore_t _uiSlots;
    BOOL _nativePlaybackActive;
    NSCondition *_owner;dispatch_queue_t _wifiQueue;NSDictionary *_wifiConfig;NSTimeInterval _wifiDeadline;BOOL _wifiStarted;
    NSUInteger _queuedJobs;
    NSDate *_eventWindow;
    NSUInteger _eventsInWindow;
}
- (instancetype)initWithKey:(NSData *)key storage:(NSString *)storage {
    if(key.length!=32)return nil;
    if((self=[super init])) {
        _key=[key copy];_root=[storage copy];_listener=-1;_client=-1;atomic_init(&_stopping,NO);_tx=[NSLock new];_audio=[BTAudioReceiver new];
        _network=dispatch_queue_create("app.backthread.network",DISPATCH_QUEUE_SERIAL);
        _worker=dispatch_queue_create("app.backthread.worker",dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL,QOS_CLASS_UTILITY,0));
        _jobCommands=dispatch_queue_create("app.backthread.jobcommands",dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL,QOS_CLASS_UTILITY,0));
        _jobCommandSlots=dispatch_semaphore_create(8);
        _commands=dispatch_queue_create("app.backthread.commands",DISPATCH_QUEUE_SERIAL);_commandSlots=dispatch_semaphore_create(16);
        _fileIO=dispatch_queue_create("app.backthread.fileio",dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL,QOS_CLASS_UTILITY,0));_fileSlots=dispatch_semaphore_create(2);_committing=[NSMutableSet new];
        [[NSFileManager defaultManager] createDirectoryAtPath:_root withIntermediateDirectories:YES attributes:@{NSFileProtectionKey:NSFileProtectionCompleteUntilFirstUserAuthentication} error:nil];
        NSDictionary *saved=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfFile:[_root stringByAppendingPathComponent:@".jobs.json"]]?:[NSData data] options:NSJSONReadingMutableContainers error:nil];
        _jobs=[saved isKindOfClass:NSDictionary.class]?[saved mutableCopy]:[NSMutableDictionary new];_jobSpecs=[NSMutableDictionary new];_cancelled=[NSMutableSet new];_events=[NSMutableArray new];_pendingCommands=[NSMutableDictionary new];_uiSlots=dispatch_semaphore_create(2);_owner=[NSCondition new];_wifiQueue=dispatch_queue_create("app.backthread.wifi",DISPATCH_QUEUE_SERIAL);
        for(NSString *jid in [_jobs.allKeys copy]) { NSDictionary *s=_jobs[jid];if([@[@"queued",@"running"] containsObject:s[@"state"]])_jobs[jid]=@{@"jobId":jid,@"state":@"interrupted",@"progress":@0,@"error":@"Agent restarted; submit a new job ID to retry"}; }
    } return self;
}
- (BTAudioReceiver *)audio { return _audio; }
- (BOOL)nativePlaybackActive { @synchronized(self){return _nativePlaybackActive;} }
- (void)setNativePlaybackActive:(BOOL)active { @synchronized(self){_nativePlaybackActive=active;if(active){_audioOn=NO;[_audio stop];}} }
- (void)networkPhase:(NSString *)phase error:(int)code {
    // Own diagnostic only: no keys, peer identity or system configuration.
    NSDictionary *record=@{@"phase":phase,@"errno":@(code),@"port":@23451,@"pid":@(getpid())};
    NSString *path=[_root stringByAppendingPathComponent:@".network-diagnostics.json"];
    [[NSJSONSerialization dataWithJSONObject:record options:0 error:NULL] writeToFile:path atomically:YES];
    [[NSFileManager defaultManager] setAttributes:@{NSFilePosixPermissions:@0600} ofItemAtPath:path error:NULL];
    NSLog(@"BackThread.Agent network %@ errno=%d",phase,code);
}
- (void)notify:(NSString *)type body:(NSDictionary *)body { void(^callback)(NSString *,NSDictionary *)=self.stateChanged;if(callback)dispatch_async(dispatch_get_main_queue(),^{callback(type,body);}); }
- (BOOL)send:(NSString *)type body:(NSDictionary *)body reply:(id)reply channel:(uint16_t)channel {
    return [self send:type body:body reply:reply channel:channel generation:0];
}
- (BOOL)send:(NSString *)type body:(NSDictionary *)body reply:(id)reply channel:(uint16_t)channel generation:(uint64_t)generation {
    NSMutableDictionary *m=[@{@"type":type,@"body":body?:@{}} mutableCopy];if([reply isKindOfClass:NSString.class])m[@"replyTo"]=reply;
    BOOL request=[@[@"media.command",@"action.invoke",@"job.cancel",@"apps.launch",@"apps.order",@"apps.refresh",@"apps.unpin",@"wifi.enable",@"wifi.disable",@"media.owner",@"media.playback"] containsObject:type];
    if(request)m[@"id"]=[[[NSUUID UUID] UUIDString] lowercaseString];
    NSData *data=[NSJSONSerialization dataWithJSONObject:m options:0 error:nil];if(!data||data.length>65536)return NO;
    [_tx lock];int fd=_client;if((generation&&generation!=_generation)||(!_authenticated&&![type hasPrefix:@"auth."])){[_tx unlock];return NO;}uint8_t header[BT_HEADER_SIZE];BTHeader h={(uint32_t)data.length,channel,++_sequences[channel],0};
    if(request){@synchronized(_pendingCommands){if(_pendingCommands.count>=32){[_tx unlock];[self notify:@"error" body:@{@"error":@"Too many pending commands"}];return NO;}_pendingCommands[m[@"id"]]=@{@"requestType":type,@"appId":body[@"appId"]?:@"",@"actionId":body[@"actionId"]?:@""};}}
    BOOL ok=fd>=0&&bt_header_encode(header,&h)==0&&[self transfer:fd data:header count:sizeof(header) writing:YES]&&[self transfer:fd data:(void *)data.bytes count:data.length writing:YES];if(!ok&&fd>=0)shutdown(fd,SHUT_RDWR);[_tx unlock];
    if(request){NSString *identifier=m[@"id"];if(!ok){@synchronized(_pendingCommands){[_pendingCommands removeObjectForKey:identifier];}}else{__weak typeof(self) weak=self;dispatch_after(dispatch_time(DISPATCH_TIME_NOW,10*NSEC_PER_SEC),dispatch_get_main_queue(),^{BTAgent *owner=weak;if(!owner)return;NSDictionary *pending;@synchronized(owner->_pendingCommands){pending=owner->_pendingCommands[identifier];[owner->_pendingCommands removeObjectForKey:identifier];}if(pending){NSMutableDictionary *failed=[pending mutableCopy];failed[@"ok"]=@NO;failed[@"error"]=@"Windows did not confirm before the deadline; the result is unknown";[owner notify:@"command.result" body:failed];}});}}
    return ok;
}
- (BOOL)transfer:(int)fd data:(void *)bytes count:(size_t)count writing:(BOOL)writing { BTChannel *channel=self.activeChannel;if(!channel||channel.descriptor!=fd)return NO;return writing?[channel writeExact:bytes count:count]:[channel readExact:bytes count:count]; }
- (NSDictionary *)readChannel:(BTChannel *)channel header:(BTHeader *)h payload:(NSData **)payload {
    uint8_t bytes[BT_HEADER_SIZE];if(![channel readExact:bytes count:sizeof(bytes)]||bt_header_decode(bytes,h))return nil;
    NSMutableData *data=[NSMutableData dataWithLength:h->length];if(![channel readExact:data.mutableBytes count:h->length])return nil;if(payload)*payload=data;
    if(h->channel==2||h->channel==6)return @{};
    id message=[NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if(![message isKindOfClass:NSDictionary.class]||![message[@"type"] isKindOfClass:NSString.class]||[message[@"type"] length]==0||[message[@"type"] length]>80||![message[@"body"] isKindOfClass:NSDictionary.class]||!jsonDepth(message,1))return nil;
    for(NSString *field in @[@"id",@"replyTo"])if(message[field]&&message[field]!=NSNull.null&&(![message[field] isKindOfClass:NSString.class]||[message[field] length]>64))return nil;
    return message;
}
- (NSDictionary *)read:(int)fd header:(BTHeader *)h payload:(NSData **)payload { BTChannel *channel=self.activeChannel;if(!channel||channel.descriptor!=fd)return nil;return [self readChannel:channel header:h payload:payload]; }
- (BOOL)sendChannel:(BTChannel *)channel type:(NSString *)type body:(NSDictionary *)body sequence:(uint32_t)sequence {
    NSData *data=[NSJSONSerialization dataWithJSONObject:@{@"type":type,@"body":body} options:0 error:nil];uint8_t header[BT_HEADER_SIZE];BTHeader h={(uint32_t)data.length,1,sequence,0};return data&&bt_header_encode(header,&h)==0&&[channel writeExact:header count:sizeof(header)]&&[channel writeExact:data.bytes count:data.length];
}
- (BOOL)authenticateChannel:(BTChannel *)channel {
    uint8_t random[32];if(SecRandomCopyBytes(kSecRandomDefault,sizeof(random),random)!=errSecSuccess)return NO;
    NSString *server=hex(random,sizeof(random));if(![self sendChannel:channel type:@"auth.challenge" body:@{@"serverNonce":server} sequence:1])return NO;
    channel.readTimeout=5;BTHeader h={0};NSDictionary *message=[self readChannel:channel header:&h payload:nil],*body=message[@"body"];
    if(h.channel!=1||![message[@"type"] isEqual:@"auth.response"]||!hexString(body[@"clientNonce"],64)||!constantEqual(body[@"proof"],proof(_key,@"host",server,body[@"clientNonce"])))return NO;
    BOOL ok=[self sendChannel:channel type:@"auth.ok" body:@{@"proof":proof(_key,@"agent",server,body[@"clientNonce"]),@"protocol":@1} sequence:2];channel.readTimeout=12;if(!channel.secure){struct timeval timeout={12,0};setsockopt(channel.descriptor,SOL_SOCKET,SO_RCVTIMEO,&timeout,sizeof(timeout));}return ok;
}
- (void)serveChannel:(BTChannel *)channel allowReplacement:(BOOL)allowReplacement {
    [_owner lock];if(channel.secure&&BTSessionClock()>=_wifiDeadline){[_owner unlock];[channel close];return;}BTChannel *old=self.activeChannel;
    if(old){if(!allowReplacement||old.secure==channel.secure){[_owner unlock];[channel close];return;}[old close];NSDate *end=[NSDate dateWithTimeIntervalSinceNow:3];while(self.activeChannel&&[_owner waitUntilDate:end]){}if(self.activeChannel){[_owner unlock];[channel close];return;}}
    if(_stopping){[_owner unlock];[channel close];return;}
    [_tx lock];_client=channel.descriptor;self.activeChannel=channel;_authenticated=YES;_generation++;memset(_sequences,0,sizeof(_sequences));_sequences[1]=2;[_tx unlock];[_owner unlock];
    [self notify:@"connection" body:@{@"state":channel.secure?@"Connected by Wi-Fi":@"Connected by USB",@"transport":channel.secure?@"wifi":@"usb"}];
    @try{[self session:channel.descriptor];}@catch(NSException *exception){[self notify:@"error" body:@{@"error":exception.reason?:@"Protocol error"}];}
    [_owner lock];[_tx lock];_client=-1;_authenticated=NO;self.activeChannel=nil;[_tx unlock];[channel close];_audioOn=NO;[_audio stop];
    @synchronized(_pendingCommands){for(NSDictionary *pending in _pendingCommands.allValues){NSMutableDictionary *failed=[pending mutableCopy];failed[@"ok"]=@NO;failed[@"error"]=@"Connection closed before Windows confirmed";[self notify:@"command.result" body:failed];}[_pendingCommands removeAllObjects];}
    [self notify:@"connection" body:@{@"state":@"Disconnected — waiting for host"}];[_owner broadcast];[_owner unlock];
}
- (void)clearWifi {
    @synchronized(self){_wifiConfig=nil;_wifiDeadline=0;}BTChannel *channel=self.activeChannel;if(channel.secure)[channel close];[self notify:@"wifi.state" body:@{@"state":@"Temporary Wi-Fi off",@"remainingSeconds":@0,@"enabled":@NO}];
}
- (BOOL)configureWifi:(NSDictionary *)body {
    NSString *address=body[@"address"],*pin=body[@"certificateSha256"],*sid=body[@"sessionId"];int port=[body[@"port"] intValue],ttl=[body[@"ttlSeconds"] intValue];struct in_addr parsed;
    if(self.activeChannel.secure||![address isKindOfClass:NSString.class]||inet_pton(AF_INET,address.UTF8String,&parsed)!=1||port<1024||port>65535||ttl<0||ttl>1800||!hexString(pin,64)||!hexString(sid,32))return NO;
    uint32_t host=ntohl(parsed.s_addr);BOOL privateAddress=(host>>24)==10||(host>>20)==0xac1||(host>>16)==0xc0a8||(host>>16)==0xa9fe;if(!privateAddress)return NO;
    BOOL start=NO;@synchronized(self){_wifiConfig=[body copy];_wifiDeadline=ttl?BTSessionClock()+ttl:DBL_MAX;if(!_wifiStarted){_wifiStarted=YES;start=YES;}}
    if(start)dispatch_async(_wifiQueue,^{[self wifiLoop];});return YES;
}
- (void)wifiLoop {
    while(!_stopping){@autoreleasepool {
        NSDictionary *config;NSTimeInterval deadline;@synchronized(self){config=_wifiConfig;deadline=_wifiDeadline;}if(!config)break;
        NSTimeInterval remaining=deadline==DBL_MAX?0:deadline-BTSessionClock();if(deadline!=DBL_MAX&&remaining<=0){[self clearWifi];[self notify:@"wifi.state" body:@{@"state":@"Session expired",@"remainingSeconds":@0,@"enabled":@NO}];break;}
        BOOL preferUsb=[config[@"preferUsb"] boolValue];BTChannel *active=self.activeChannel;
        [self notify:@"wifi.state" body:@{@"state":active&&!active.secure&&preferUsb?@"Authorized · USB preferred":@"Connecting via pinned TLS",@"remainingSeconds":@(MAX(0,remaining)),@"enabled":@YES}];
        if(active&&!active.secure&&preferUsb){usleep(1000000);continue;}
        NSError *error=nil;BTChannel *channel=[BTChannel connectTLS:config[@"address"] port:[config[@"port"] intValue] pin:config[@"certificateSha256"] deadline:deadline error:&error];
        if(!channel){[self notify:@"wifi.state" body:@{@"state":@"Peer unreachable or TLS identity rejected",@"remainingSeconds":@(MAX(0,remaining)),@"enabled":@YES}];usleep(1000000);continue;}
        BOOL current;@synchronized(self){current=[_wifiConfig[@"sessionId"] isEqual:config[@"sessionId"]];}
        if(current&&[self sendChannel:channel type:@"wifi.join" body:@{@"sessionId":config[@"sessionId"]} sequence:0]&&[self authenticateChannel:channel]){
            @synchronized(self){current=[_wifiConfig[@"sessionId"] isEqual:config[@"sessionId"]];}
            if(current){[self notify:@"wifi.state" body:@{@"state":@"Wi-Fi TLS authenticated",@"remainingSeconds":@(MAX(0,remaining)),@"enabled":@YES}];[self serveChannel:channel allowReplacement:!preferUsb];}
        }
        [channel close];usleep(300000);
    }}
    @synchronized(self){_wifiStarted=NO;if(_wifiConfig&&!_stopping){_wifiStarted=YES;dispatch_async(_wifiQueue,^{[self wifiLoop];});}}
}
- (void)start {
    [_tx lock];if(_started){[_tx unlock];return;}_started=YES;_stopping=NO;[_tx unlock];
    dispatch_async(_network,^{@autoreleasepool {
        if(self->_stopping){[self->_tx lock];self->_started=NO;[self->_tx unlock];return;}
        int fd=socket(AF_INET,SOCK_STREAM,0);if(fd<0){int code=errno;[self networkPhase:@"socket-failed" error:code];[self->_tx lock];self->_started=NO;[self->_tx unlock];[self notify:@"connection" body:@{@"state":[NSString stringWithFormat:@"USB socket failed (%d)",code]}];return;}
        [self->_tx lock];self->_listener=fd;[self->_tx unlock];int yes=1;setsockopt(fd,SOL_SOCKET,SO_REUSEADDR,&yes,sizeof(yes));
        struct sockaddr_in address={0};address.sin_len=sizeof(address);address.sin_family=AF_INET;address.sin_port=htons(23451);address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);
        if(bind(fd,(struct sockaddr *)&address,sizeof(address))||listen(fd,2)){int code=errno;[self networkPhase:@"listen-failed" error:code];[self->_tx lock];self->_listener=-1;self->_started=NO;close(fd);[self->_tx unlock];[self notify:@"connection" body:@{@"state":[NSString stringWithFormat:@"Cannot listen on USB port 23451 (%d)",code]}];return;}
        [self networkPhase:@"listening" error:0];
        [self notify:@"connection" body:@{@"state":@"Waiting for USB host"}];
        while(!self->_stopping) {
            int client=accept(fd,NULL,NULL);if(client<0){int code=errno;if(code==EINTR)continue;if(!self->_stopping){[self networkPhase:@"accept-failed" error:code];[self notify:@"connection" body:@{@"state":[NSString stringWithFormat:@"USB listener stopped (%d); reopen BackThread",code]}];}break;}
            struct timeval timeout={5,0},sendTimeout={3,0};setsockopt(client,SOL_SOCKET,SO_RCVTIMEO,&timeout,sizeof(timeout));setsockopt(client,SOL_SOCKET,SO_SNDTIMEO,&sendTimeout,sizeof(sendTimeout));setsockopt(client,SOL_SOCKET,SO_NOSIGPIPE,&yes,sizeof(yes));
            setsockopt(client,IPPROTO_TCP,TCP_NODELAY,&yes,sizeof(yes));
            @autoreleasepool{BTChannel *channel=[[BTChannel alloc] initWithDescriptor:client];if([self authenticateChannel:channel])[self serveChannel:channel allowReplacement:YES];[channel close];}
        }
        [self->_tx lock];if(self->_listener==fd){close(fd);self->_listener=-1;}self->_started=NO;[self->_tx unlock];
    }});
}
- (void)stop { _stopping=YES;[self clearWifi];[_tx lock];if(_listener>=0)shutdown(_listener,SHUT_RDWR);if(_client>=0)shutdown(_client,SHUT_RDWR);[_tx unlock];[_audio stop]; }
- (void)rejectCommand:(NSString *)type body:(NSDictionary *)body reason:(NSString *)reason{[self notify:@"command.result" body:@{@"requestType":type,@"appId":body[@"appId"]?:@"",@"actionId":body[@"actionId"]?:@"",@"ok":@NO,@"error":reason}];}
- (void)command:(NSString *)type body:(NSDictionary *)body {
    if([type isEqual:@"wifi.disable"]&&!_authenticated){[self clearWifi];return;}
    if([type isEqual:@"job.cancel"]){
        if(dispatch_semaphore_wait(_jobCommandSlots,DISPATCH_TIME_NOW)!=0){[self rejectCommand:type body:body reason:@"Job command queue is busy"];return;}
        dispatch_async(_jobCommands,^{
            @try { [self cancelJob:body[@"jobId"]]; }
            @finally { dispatch_semaphore_signal(self->_jobCommandSlots); }
        });
        return;
    }
    if(dispatch_semaphore_wait(_commandSlots,DISPATCH_TIME_NOW)!=0){[self rejectCommand:type body:body reason:@"Command queue is busy"];return;}
    [_tx lock];uint64_t generation=_generation;BOOL connected=_authenticated;[_tx unlock];if(!connected){dispatch_semaphore_signal(_commandSlots);[self rejectCommand:type body:body reason:@"Companion is disconnected"];return;}
    dispatch_async(_commands,^{@autoreleasepool { BOOL delivered=[self send:type body:body reply:nil channel:3 generation:generation];if(!delivered)[self rejectCommand:type body:body reason:@"Command was not delivered"];if([type isEqual:@"wifi.disable"])[self clearWifi];dispatch_semaphore_signal(self->_commandSlots); }});
}
- (NSString *)path:(NSString *)name {
    if(![name isKindOfClass:NSString.class]||name.length==0||name.length>120||[name hasPrefix:@"."]||[name hasSuffix:@"."]||[name rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789._-"] invertedSet]].location!=NSNotFound)return nil;
    NSString *stem=[[name componentsSeparatedByString:@"."][0] uppercaseString];if([@[@"CON",@"PRN",@"AUX",@"NUL"] containsObject:stem]||([stem length]==4&&([stem hasPrefix:@"COM"]||[stem hasPrefix:@"LPT"])&&[stem characterAtIndex:3]>='0'&&[stem characterAtIndex:3]<='9'))return nil;
    return [_root stringByAppendingPathComponent:name];
}
- (NSString *)part:(NSString *)tid { return hexString(tid,32)?[_root stringByAppendingPathComponent:[NSString stringWithFormat:@".%@.part",tid.lowercaseString]]:nil; }
- (void)fail:(NSString *)error message:(NSDictionary *)m channel:(uint16_t)channel { [self send:@"error" body:@{@"error":error?:@"Operation failed"} reply:m[@"id"] channel:channel]; }
- (void)session:(int)fd {
    for(;;) {@autoreleasepool {
        BTHeader h;NSData *payload;NSDictionary *m=[self read:fd header:&h payload:&payload];if(!m)break;
        if(h.channel==2) {
            if(payload.length%4)break;
            // Audio failure is local to this channel. Keep heartbeats, jobs and files alive.
            // Also discard already queued PCM after audio.stop or a failed audio.start.
            if(!_audioOn||self.nativePlaybackActive)continue;
            if(!_audio.running){_audioOn=NO;NSDictionary *stats=[_audio statistics];[_audio stop];[self send:@"audio.error" body:@{@"error":@"AudioQueue stopped; request audio.start to retry",@"lastOSStatus":stats[@"lastOSStatus"]?:@0} reply:nil channel:7];[self notify:@"error" body:@{@"error":@"Audio playback stopped; USB control remains connected"}];continue;}
            if(_previousAudio&&h.sequence!=_previousAudio+1)_audioGaps++;_previousAudio=h.sequence;_audioPackets++;_audioBytes+=payload.length;[_audio receive:payload];continue;
        }
        if(h.channel==6) {
            if(dispatch_semaphore_wait(_fileSlots,DISPATCH_TIME_NOW)!=0)break;
            [_tx lock];uint64_t generation=_generation;[_tx unlock];
            dispatch_async(_fileIO,^{@autoreleasepool {
                @try { if(![self append:payload generation:generation]){[self->_tx lock];if(self->_generation==generation&&self->_client>=0)shutdown(self->_client,SHUT_RDWR);[self->_tx unlock];} }
                @catch(NSException *e){[self->_tx lock];if(self->_generation==generation&&self->_client>=0)shutdown(self->_client,SHUT_RDWR);[self->_tx unlock];}
                @finally{dispatch_semaphore_signal(self->_fileSlots);}
            }});continue;
        }
        NSString *type=m[@"type"];NSDictionary *b=m[@"body"];id rid=m[@"id"];
        if([m[@"replyTo"] isKindOfClass:NSString.class]){NSDictionary *pending;@synchronized(_pendingCommands){pending=_pendingCommands[m[@"replyTo"]];[_pendingCommands removeObjectForKey:m[@"replyTo"]];}if(pending){NSMutableDictionary *result=[pending mutableCopy];result[@"ok"]=@(![type isEqual:@"error"]&&![b[@"success"] isEqual:@NO]&&![b[@"launched"] isEqual:@NO]);result[@"error"]=b[@"error"]?:@"";result[@"result"]=b;[self notify:@"command.result" body:result];continue;}}
        if([type isEqual:@"wifi.enroll"]){BOOL accepted=[self configureWifi:b];[self send:accepted?@"ok":@"error" body:accepted?@{@"authorized":@YES}:@{@"error":@"Temporary Wi-Fi enrollment requires authenticated USB and valid bounded settings"} reply:rid channel:h.channel];}
        else if([type isEqual:@"wifi.revoke"]){[self send:@"ok" body:@{@"revoked":@YES} reply:rid channel:h.channel];[self clearWifi];}
        else if([type isEqual:@"ping"]) { [self send:@"pong" body:@{} reply:rid channel:1]; }
        else if([type isEqual:@"hello"]) { [self send:@"ok" body:@{@"agent":@"BackThread.Agent",@"simulated":@NO,@"capabilities":@[@"pcm",@"jobs",@"files",@"events",@"UIKit",@"direct-handoff",@"apps",@"ui-inspect",@"temporary-wifi-tls"],@"playbackOwner":self.nativePlaybackActive?@"phone":@"pc"} reply:rid channel:h.channel]; }
        else if([type isEqual:@"audio.start"]) { @synchronized(self){
            if(self.nativePlaybackActive){[self fail:@"Independent phone playback owns audio" message:m channel:h.channel];continue;}
            NSError *error=nil;if([b[@"sampleRate"] intValue]!=48000||[b[@"channels"] intValue]!=2||[b[@"bits"] intValue]!=16) { [self fail:@"Only 48 kHz stereo PCM16 supported" message:m channel:h.channel];continue; }
            _audioOn=NO;
            if([_audio start:&error]) { _audioOn=YES;_audioPackets=_audioBytes=_audioGaps=0;_previousAudio=0;[self send:@"ok" body:@{@"ready":@YES} reply:rid channel:h.channel]; }
            else [self fail:error.localizedDescription message:m channel:h.channel];
        }
        } else if([type isEqual:@"audio.stop"]) { _audioOn=NO;[_audio stop];[self send:@"ok" body:@{@"stopped":@YES} reply:rid channel:h.channel]; }
        else if([type isEqual:@"audio.stats"]) { NSMutableDictionary *s=[[_audio statistics] mutableCopy];s[@"packets"]=@(_audioPackets);s[@"bytes"]=@(_audioBytes);s[@"gaps"]=@(_audioGaps);s[@"acceptingPCM"]=@(_audioOn);[self send:@"ok" body:s reply:rid channel:h.channel]; }
        else if([type isEqual:@"audio.volume"]) { NSError *error=nil;if(![b[@"value"] isKindOfClass:NSNumber.class]||![_audio setVolume:[b[@"value"] floatValue] error:&error])[self fail:error.localizedDescription?:@"Invalid audio volume" message:m channel:h.channel];else [self send:@"ok" body:@{} reply:rid channel:h.channel]; }
        else if([type isEqual:@"media.state"]||[type isEqual:@"action.registry"]||[type isEqual:@"apps.catalog"]||[type isEqual:@"wifi.state"]) { [self notify:type body:b];if([rid isKindOfClass:NSString.class])[self send:@"ok" body:@{@"received":@YES} reply:rid channel:h.channel]; }
        else if([type isEqual:@"ui.inspect"]||[type isEqual:@"media.handoff"]){
            void(^handler)(NSString *,NSDictionary *,void(^)(NSDictionary *,BOOL))=self.uiRequest;
            if(!handler||dispatch_semaphore_wait(_uiSlots,DISPATCH_TIME_NOW)!=0){[self fail:@"UI unavailable or busy" message:m channel:h.channel];continue;}
            [_tx lock];uint64_t generation=_generation;[_tx unlock];uint16_t channel=h.channel;NSObject *ticket=[NSObject new];__block BOOL replied=NO;
            void(^complete)(NSDictionary *,BOOL)=^(NSDictionary *result,BOOL success){@synchronized(ticket){if(replied)return;replied=YES;}dispatch_async(self->_commands,^{[self send:success?@"ok":@"error" body:result reply:rid channel:channel generation:generation];dispatch_semaphore_signal(self->_uiSlots);});};
            dispatch_async(dispatch_get_main_queue(),^{handler(type,b,complete);});dispatch_after(dispatch_time(DISPATCH_TIME_NOW,15*NSEC_PER_SEC),_commands,^{complete(@{@"error":@"UI request deadline exceeded"},NO);});
        }
        else if([type isEqual:@"error"]) { [self notify:@"error" body:b]; }
        else if([type isEqual:@"ok"]&&[m[@"replyTo"] isKindOfClass:NSString.class]&&[b[@"success"] isEqual:@NO]) { [self notify:@"error" body:@{@"error":@"Windows could not perform the requested action"}]; }
        else if([type isEqual:@"event.publish"]) { BOOL accepted=[self event:b];[self send:@"ok" body:@{@"accepted":@(accepted)} reply:rid channel:h.channel]; }
        else if([type isEqual:@"event.history"]) { [self send:@"ok" body:@{@"events":[_events copy]} reply:rid channel:h.channel]; }
        else if([type isEqual:@"job.submit"]||[type isEqual:@"job.query"]||[type isEqual:@"job.cancel"]) {
            [self queueJobCommand:type body:b message:m channel:h.channel];
        }
        else if([type isEqual:@"file.begin"]) { [self begin:b message:m channel:h.channel]; }
        else if([type isEqual:@"file.commit"]) { [self commit:b message:m channel:h.channel client:fd]; }
        else if([type isEqual:@"file.read"]) {
            if(dispatch_semaphore_wait(_fileSlots,DISPATCH_TIME_NOW)!=0){[self fail:@"File I/O queue is busy" message:m channel:h.channel];continue;}
            [_tx lock];uint64_t generation=_generation;[_tx unlock];uint16_t channel=h.channel;
            dispatch_async(_fileIO,^{@autoreleasepool {
                @try{[self download:b message:m channel:channel generation:generation];}
                @catch(NSException *e){[self send:@"error" body:@{@"error":e.reason?:@"Artifact read failed"} reply:m[@"id"] channel:channel generation:generation];}
                @finally{dispatch_semaphore_signal(self->_fileSlots);}
            }});
        }
        else if([type isEqual:@"file.list"]) {
            NSMutableArray *files=[NSMutableArray new];for(NSString *name in [[NSFileManager defaultManager] contentsOfDirectoryAtPath:_root error:nil])if(![name hasPrefix:@"."]&&files.count<256){NSDictionary *a=[[NSFileManager defaultManager] attributesOfItemAtPath:[self path:name] error:nil];if(a)[files addObject:@{@"name":name,@"size":a[NSFileSize]}];}[self send:@"ok" body:@{@"files":files} reply:rid channel:h.channel];
        }
        else if(![m[@"replyTo"] isKindOfClass:NSString.class]) [self fail:@"Unsupported operation" message:m channel:h.channel];
    }}
}
- (BOOL)event:(NSDictionary *)b {
    NSString *eid=b[@"id"],*title=b[@"title"],*text=b[@"body"],*source=b[@"source"];
    if((b[@"category"]&&b[@"category"]!=NSNull.null&&(![b[@"category"] isKindOfClass:NSString.class]||[b[@"category"] length]>64))||(b[@"iconBase64"]&&b[@"iconBase64"]!=NSNull.null&&(![b[@"iconBase64"] isKindOfClass:NSString.class]||[b[@"iconBase64"] length]>12000)))return NO;
    if(![eid isKindOfClass:NSString.class]||eid.length==0||eid.length>64||![title isKindOfClass:NSString.class]||title.length>256||![text isKindOfClass:NSString.class]||text.length>2048||![source isKindOfClass:NSString.class]||source.length>128)return NO;
    NSISO8601DateFormatter *formatter=[NSISO8601DateFormatter new];formatter.formatOptions=NSISO8601DateFormatWithInternetDateTime|NSISO8601DateFormatWithFractionalSeconds;NSDate *expiry=[formatter dateFromString:b[@"expiresUtc"]?:@""];if(!expiry){formatter.formatOptions=NSISO8601DateFormatWithInternetDateTime;expiry=[formatter dateFromString:b[@"expiresUtc"]?:@""];}
    if(!expiry||[expiry timeIntervalSinceNow]<=0||[expiry timeIntervalSinceNow]>86400||[b[@"priority"] intValue]<0||[b[@"priority"] intValue]>3)return NO;
    if(!_eventWindow||-[_eventWindow timeIntervalSinceNow]>=1){_eventWindow=[NSDate date];_eventsInWindow=0;}if(++_eventsInWindow>10)return NO;
    for(NSDictionary *e in _events)if([e[@"id"] isEqual:eid])return NO;
    [_events addObject:[b copy]];while(_events.count>64)[_events removeObjectAtIndex:0];[self notify:@"event.publish" body:b];return YES;
}
- (void)begin:(NSDictionary *)b message:(NSDictionary *)m channel:(uint16_t)c {
    NSString *part=[self part:b[@"transferId"]],*name=[self path:b[@"name"]];long long size=[b[@"size"] longLongValue];
    if(!part||!name||size<0||size>2147483648LL||!hexString(b[@"sha256"],64)){[self fail:@"Invalid transfer" message:m channel:c];return;}
    @synchronized(self){if([_committing containsObject:[b[@"transferId"] lowercaseString]]){[self fail:@"Transfer commit is still active" message:m channel:c];return;}}
    NSString *meta=[part stringByAppendingString:@".json"];NSData *old=[NSData dataWithContentsOfFile:meta];
    if(old){NSDictionary *previous=[NSJSONSerialization JSONObjectWithData:old options:0 error:nil];if(![previous isEqual:b]){[self fail:@"Transfer identifier conflict" message:m channel:c];return;}}
    else {
        NSUInteger count=0,completed=0;unsigned long long reserved=0;
        for(NSString *file in [[NSFileManager defaultManager] contentsOfDirectoryAtPath:_root error:nil]) {
            NSString *path=[_root stringByAppendingPathComponent:file];
            if([file hasSuffix:@".part.json"]){count++;NSDictionary *spec=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfFile:path]?:[NSData data] options:0 error:nil];if(!spec){[self fail:@"Invalid transfer journal" message:m channel:c];return;}reserved+=[spec[@"size"] unsignedLongLongValue];}
            else if(![file hasPrefix:@"."]){completed++;reserved+=[[[NSFileManager defaultManager] attributesOfItemAtPath:path error:nil][NSFileSize] unsignedLongLongValue];}
        }
        if(count>=16){[self fail:@"Too many unfinished transfers" message:m channel:c];return;}
        if(reserved>2147483648ULL-(unsigned long long)size){[self fail:@"Application storage quota exceeded (2 GiB), including reserved partial transfers" message:m channel:c];return;}
        if(completed>=256&&![[NSFileManager defaultManager] fileExistsAtPath:name]){[self fail:@"Stored file count limit reached" message:m channel:c];return;}
        unsigned long long free=[[[NSFileManager defaultManager] attributesOfFileSystemForPath:_root error:nil][NSFileSystemFreeSize] unsignedLongLongValue];if(free<(unsigned long long)size+64ULL*1024*1024){[self fail:@"Insufficient storage: preserve at least 64 MiB free" message:m channel:c];return;}
        NSError *error=nil;NSData *data=[NSJSONSerialization dataWithJSONObject:b options:0 error:&error];if(![data writeToFile:meta options:NSDataWritingAtomic error:&error]){[self fail:error.localizedDescription message:m channel:c];return;}
    }
    if(![[NSFileManager defaultManager] fileExistsAtPath:part])[[NSFileManager defaultManager] createFileAtPath:part contents:nil attributes:nil];
    unsigned long long offset=[[[NSFileManager defaultManager] attributesOfItemAtPath:part error:nil][NSFileSize] unsignedLongLongValue];
    if(offset>(unsigned long long)size){[self fail:@"Partial file exceeds declared size" message:m channel:c];return;}[self send:@"ok" body:@{@"offset":@(offset)} reply:m[@"id"] channel:c];
}
- (BOOL)append:(NSData *)data generation:(uint64_t)generation {
    [_tx lock];BOOL current=_authenticated&&_generation==generation&&!_stopping;[_tx unlock];if(!current)return NO;
    if(data.length<40||data.length>32808)return NO;NSString *tid=[[NSString alloc] initWithBytes:data.bytes length:32 encoding:NSASCIIStringEncoding];NSString *part=[self part:tid];if(!part)return NO;
    @synchronized(self){if([_committing containsObject:tid.lowercaseString])return NO;}
    NSDictionary *spec=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfFile:[part stringByAppendingString:@".json"]]?:[NSData data] options:0 error:nil];if(!spec)return NO;
    uint64_t offset=bt_u64((const uint8_t *)data.bytes+32);NSFileHandle *file=[NSFileHandle fileHandleForWritingAtPath:part];if(!file)return NO;unsigned long long length=[file seekToEndOfFile];
    if(offset>length||offset+data.length-40>[spec[@"size"] unsignedLongLongValue]||(offset<length&&offset+data.length-40>length)){[file closeFile];return NO;}
    if(offset==length){[file writeData:[data subdataWithRange:NSMakeRange(40,data.length-40)]];[file synchronizeFile];length=[file offsetInFile];}[file closeFile];
    return [self send:@"file.ack" body:@{@"transferId":tid,@"offset":@(length)} reply:nil channel:5 generation:generation];
}
- (void)commit:(NSDictionary *)b message:(NSDictionary *)m channel:(uint16_t)c client:(int)client {
    (void)client;if(!hexString(b[@"transferId"],32)){[self fail:@"Invalid transfer" message:m channel:c];return;}
    [_tx lock];uint64_t generation=_generation;[_tx unlock];NSString *tid=[b[@"transferId"] lowercaseString];
    NSString *part=[self part:tid];if(!part){[self fail:@"Invalid transfer" message:m channel:c];return;}
    @synchronized(self){if(_queuedJobs>=8||[_committing containsObject:tid]){[self fail:@"Worker queue full or transfer commit already active" message:m channel:c];return;}_queuedJobs++;[_committing addObject:tid];}
    dispatch_async(_worker,^{@autoreleasepool { @try {
        NSString *meta=[part stringByAppendingString:@".json"];NSDictionary *spec=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfFile:meta]?:[NSData data] options:0 error:nil];
        NSString *destination=[self path:spec[@"name"]];NSError *error=nil;NSString *sha=[self hash:part job:nil error:&error];
        unsigned long long length=[[[NSFileManager defaultManager] attributesOfItemAtPath:part error:nil][NSFileSize] unsignedLongLongValue];
        if(!destination||!sha||length!=[spec[@"size"] unsignedLongLongValue]||![sha isEqual:[spec[@"sha256"] lowercaseString]])[self send:@"error" body:@{@"error":error.localizedDescription?:@"Incomplete transfer or checksum mismatch"} reply:m[@"id"] channel:c generation:generation];
        else if(rename(part.fileSystemRepresentation,destination.fileSystemRepresentation))[self send:@"error" body:@{@"error":@"Atomic archive rename failed"} reply:m[@"id"] channel:c generation:generation];
        else {[[NSFileManager defaultManager] removeItemAtPath:meta error:nil];[self send:@"ok" body:@{@"name":spec[@"name"],@"size":@(length),@"sha256":sha} reply:m[@"id"] channel:c generation:generation];}
    } @catch(NSException *e){[self send:@"error" body:@{@"error":e.reason?:@"Commit failed"} reply:m[@"id"] channel:c generation:generation];} @finally{@synchronized(self){self->_queuedJobs--;[self->_committing removeObject:tid];}} }});
}
- (void)download:(NSDictionary *)b message:(NSDictionary *)m channel:(uint16_t)c generation:(uint64_t)generation {
    [_tx lock];BOOL current=_authenticated&&_generation==generation&&!_stopping;[_tx unlock];if(!current)return;
    NSString *path=[self path:b[@"name"]],*tid=b[@"transferId"];long long offset=[b[@"offset"] longLongValue];int count=[b[@"count"] intValue];
    if(!path||!hexString(tid,32)||offset<0||count<1||count>32768){[self send:@"error" body:@{@"error":@"Invalid artifact range"} reply:m[@"id"] channel:c generation:generation];return;}
    NSFileHandle *file=[NSFileHandle fileHandleForReadingAtPath:path];if(!file){[self send:@"error" body:@{@"error":@"Artifact not found"} reply:m[@"id"] channel:c generation:generation];return;}
    @try {
        unsigned long long size=[file seekToEndOfFile];if((unsigned long long)offset>size){[self send:@"error" body:@{@"error":@"Read beyond file"} reply:m[@"id"] channel:c generation:generation];return;}
        [file seekToFileOffset:(unsigned long long)offset];NSData *chunk=[file readDataOfLength:(NSUInteger)count];NSMutableData *payload=[NSMutableData dataWithLength:40];memcpy(payload.mutableBytes,[tid dataUsingEncoding:NSASCIIStringEncoding].bytes,32);bt_put64((uint8_t *)payload.mutableBytes+32,(uint64_t)offset);[payload appendData:chunk];
        uint8_t header[BT_HEADER_SIZE];[_tx lock];BTHeader h={(uint32_t)payload.length,6,++_sequences[6],0};bt_header_encode(header,&h);BOOL ok=_authenticated&&_generation==generation&&_client>=0&&[self transfer:_client data:header count:sizeof(header) writing:YES]&&[self transfer:_client data:payload.mutableBytes count:payload.length writing:YES];if(!ok&&_generation==generation&&_client>=0)shutdown(_client,SHUT_RDWR);[_tx unlock];
        if(ok)[self send:@"ok" body:@{@"length":@(chunk.length)} reply:m[@"id"] channel:c generation:generation];
    } @finally{[file closeFile];}
}
- (BOOL)cancelJob:(id)jobID {
    if(![jobID isKindOfClass:NSString.class])return NO;
    @synchronized(_jobs) {
        BOOL found=[@[@"queued",@"running"] containsObject:_jobs[jobID][@"state"]];
        if(found)[_cancelled addObject:jobID];
        return found;
    }
}
- (void)queueJobCommand:(NSString *)type body:(NSDictionary *)body message:(NSDictionary *)message channel:(uint16_t)channel {
    if(dispatch_semaphore_wait(_jobCommandSlots,DISPATCH_TIME_NOW)!=0){
        [self fail:@"Job command queue is busy" message:message channel:channel];
        return;
    }
    [_tx lock];uint64_t generation=_generation;[_tx unlock];
    dispatch_async(_jobCommands,^{@autoreleasepool {
        @try {
            [self->_tx lock];
            BOOL current=self->_authenticated&&self->_generation==generation&&!self->_stopping;
            [self->_tx unlock];
            if(!current)return;
            id reply=message[@"id"];
            if([type isEqual:@"job.submit"]){
                [self submit:body message:message channel:channel generation:generation];
            } else if([type isEqual:@"job.cancel"]){
                [self send:@"ok" body:@{@"cancelled":@([self cancelJob:body[@"jobId"]])} reply:reply channel:channel generation:generation];
            } else {
                NSDictionary *state=nil;
                id jobID=body[@"jobId"];
                if([jobID isKindOfClass:NSString.class]){
                    @synchronized(self->_jobs){state=self->_jobs[jobID];}
                }
                [self send:state?@"ok":@"error" body:state?:@{@"error":@"Unknown job"} reply:reply channel:channel generation:generation];
            }
        } @catch(NSException *exception) {
            (void)exception;
            [self send:@"error" body:@{@"error":@"Job command failed"} reply:message[@"id"] channel:channel generation:generation];
        } @finally { dispatch_semaphore_signal(self->_jobCommandSlots); }
    }});
}
- (void)persist { NSData *data=[NSJSONSerialization dataWithJSONObject:_jobs options:0 error:nil];[data writeToFile:[_root stringByAppendingPathComponent:@".jobs.json"] atomically:YES]; }
- (void)update:(NSDictionary *)state { @synchronized(_jobs){NSString *previous=_jobs[state[@"jobId"]][@"state"];_jobs[state[@"jobId"]]=state;if(![previous isEqual:state[@"state"]])[self persist];}[self notify:@"job.progress" body:state];[self send:@"job.progress" body:state reply:nil channel:5]; }
- (BOOL)cancelled:(NSString *)jid { @synchronized(_jobs){return _stopping||(jid&&[_cancelled containsObject:jid]);} }
- (NSString *)hash:(NSString *)path job:(NSString *)jid error:(NSError **)error {
    FILE *file=fopen(path.fileSystemRepresentation,"rb");if(!file){if(error)*error=[NSError errorWithDomain:NSPOSIXErrorDomain code:errno userInfo:nil];return nil;}
    struct stat st;if(fstat(fileno(file),&st)){if(error)*error=[NSError errorWithDomain:NSPOSIXErrorDomain code:errno userInfo:nil];fclose(file);return nil;}
    CC_SHA256_CTX hash;CC_SHA256_Init(&hash);uint8_t buffer[32768];size_t n;BOOL failed=NO;uint64_t done=0;double last=0;
    while((n=fread(buffer,1,sizeof(buffer),file))>0) {
        while(self.memoryPressure&&![self cancelled:jid])usleep(100000);
        if([self cancelled:jid]){failed=YES;break;}CC_SHA256_Update(&hash,buffer,(CC_LONG)n);done+=n;
        double progress=st.st_size?((double)done/(double)st.st_size):1;if(jid&&progress-last>=.1){[self update:@{@"jobId":jid,@"state":@"running",@"progress":@(progress)}];last=progress;}
    }
    if(ferror(file))failed=YES;fclose(file);if(failed)return nil;uint8_t digest[CC_SHA256_DIGEST_LENGTH];CC_SHA256_Final(digest,&hash);return hex(digest,sizeof(digest));
}
- (void)submit:(NSDictionary *)b message:(NSDictionary *)m channel:(uint16_t)c generation:(uint64_t)generation {
    NSString *jid=b[@"jobId"],*kind=b[@"kind"],*name=b[@"name"];
    if(![jid isKindOfClass:NSString.class]||jid.length==0||jid.length>64||![@[@"sha256",@"verify",@"metadata",@"text",@"diagnostic"] containsObject:kind]||![name isKindOfClass:NSString.class]){
        [self send:@"error" body:@{@"error":@"Invalid job"} reply:m[@"id"] channel:c generation:generation];
        return;
    }
    NSDictionary *response=nil;
    NSString *rejection=nil;
    BOOL admitted=NO;
    @synchronized(_jobs) {
        response=_jobs[jid];
        if(response){
            if(_jobSpecs[jid]&&![_jobSpecs[jid] isEqual:b])rejection=@"Job ID conflict";
        } else {
            NSString *old=nil;
            if(_jobs.count>=64){
                for(NSString *key in _jobs){
                    if(![@[@"queued",@"running"] containsObject:_jobs[key][@"state"]]){old=key;break;}
                }
                if(!old)rejection=@"Job history full";
            }
            if(!rejection){
                // The shared admission count uses the short agent lock only.
                @synchronized(self) {
                    if(_queuedJobs>=8||self.memoryPressure)rejection=@"Worker busy or paused for memory pressure";
                    else {_queuedJobs++;admitted=YES;}
                }
            }
            if(admitted){
                if(old){[_jobs removeObjectForKey:old];[_jobSpecs removeObjectForKey:old];}
                _jobSpecs[jid]=[b copy];
                response=@{@"jobId":jid,@"state":@"queued",@"progress":@0};
                _jobs[jid]=response;
                // Serialize journal replacement with other job updates, never with PCM.
                [self persist];
            }
        }
    }
    [self send:rejection?@"error":@"ok" body:rejection?@{@"error":rejection}:response reply:m[@"id"] channel:c generation:generation];
    if(!admitted)return;
    dispatch_async(_worker,^{@autoreleasepool {
        NSDictionary *result=nil;NSString *error=nil;
        @try {
            if(![self cancelled:jid]) {
                [self update:@{@"jobId":jid,@"state":@"running",@"progress":@0}];NSString *path=[self path:name];
                if([kind isEqual:@"diagnostic"]) { if([name isEqual:@"version"])result=@{@"agent":@"BackThread.Agent",@"protocol":@1};else if([name isEqual:@"memory"]){mach_task_basic_info_data_t info;mach_msg_type_number_t count=MACH_TASK_BASIC_INFO_COUNT;kern_return_t status=task_info(mach_task_self(),MACH_TASK_BASIC_INFO,(task_info_t)&info,&count);if(status==KERN_SUCCESS)result=@{@"residentBytes":@(info.resident_size),@"virtualBytes":@(info.virtual_size),@"physicalMemory":@([NSProcessInfo processInfo].physicalMemory)};else error=@"task_info failed";}else error=@"Diagnostic is not authorized"; }
                else if(!path)error=@"Invalid storage name";
                else if([kind isEqual:@"metadata"]) { NSDictionary *a=[[NSFileManager defaultManager] attributesOfItemAtPath:path error:nil];if(a)result=@{@"name":name,@"size":a[NSFileSize]};else error=@"File not found"; }
                else if([kind isEqual:@"text"]) {
                    FILE *file=fopen(path.fileSystemRepresentation,"rb");if(!file)error=@"File not found";else { uint8_t buffer[32768];size_t n;uint64_t bytes=0,lines=0,words=0;BOOL inWord=NO;int last=-1;
                        while((n=fread(buffer,1,sizeof(buffer),file))>0){while(self.memoryPressure&&![self cancelled:jid])usleep(100000);if([self cancelled:jid])break;for(size_t i=0;i<n;i++){int ch=buffer[i];BOOL space=ch==' '||ch=='\t'||ch=='\n'||ch=='\r'||ch=='\f'||ch=='\v';if(ch=='\n')lines++;if(!space&&!inWord)words++;inWord=!space;last=ch;}bytes+=n;}if(ferror(file))error=@"File read failed";fclose(file);if(!error)result=@{@"bytes":@(bytes),@"lines":@(lines+(last>=0&&last!=10)),@"words":@(words),@"wordDefinition":@"ASCII whitespace"}; }
                }
                else { NSError *e=nil;NSString *sha=[self hash:path job:jid error:&e];if(!sha)error=e.localizedDescription?:@"Hash failed";else if([kind isEqual:@"verify"]&&![sha isEqual:[b[@"expectedSha256"] lowercaseString]])error=@"Integrity verification failed";else result=@{@"sha256":sha,@"verified":@([kind isEqual:@"verify"])}; }
            }
        } @catch(NSException *e){error=e.reason;}
        BOOL cancelled=[self cancelled:jid];NSString *state=cancelled?@"cancelled":error?@"failed":@"completed";NSMutableDictionary *s=[@{@"jobId":jid,@"state":state,@"progress":@([state isEqual:@"completed"]?1:0)} mutableCopy];if(result)s[@"result"]=result;if(error)s[@"error"]=error;[self update:s];
        @synchronized(self->_jobs){[self->_cancelled removeObject:jid];}
        @synchronized(self){self->_queuedJobs--;}
    }});
}
@end
