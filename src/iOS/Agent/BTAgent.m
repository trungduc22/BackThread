#import "BTAgent.h"
#import "../AudioReceiver/BTAudioReceiver.h"
#import "../../Shared/Protocol/bt_wire.h"
#import <CommonCrypto/CommonHMAC.h>
#import <CommonCrypto/CommonDigest.h>
#import <Security/Security.h>
#import <sys/socket.h>
#import <netinet/in.h>
#import <unistd.h>
#import <sys/stat.h>
#import <mach/mach.h>

static BOOL exact(int fd,void *data,size_t n,BOOL writing) {
    uint8_t *p=data;
    while(n) { ssize_t r=writing?send(fd,p,n,0):recv(fd,p,n,0);if(r<0&&errno==EINTR)continue;if(r<=0)return NO;p+=r;n-=(size_t)r; }return YES;
}
static NSString *hex(const uint8_t *b,size_t n) { char *p=malloc(n*2+1);if(!p)return nil;for(size_t i=0;i<n;i++)snprintf(p+i*2,3,"%02x",b[i]);NSString *s=[NSString stringWithUTF8String:p];free(p);return s; }
static BOOL hexString(NSString *s,NSUInteger length) { return [s isKindOfClass:NSString.class]&&s.length==length&&[s rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet]].location==NSNotFound; }
static BOOL constantEqual(NSString *a,NSString *b) { if(![a isKindOfClass:NSString.class]||a.length!=b.length)return NO;NSData *x=[a dataUsingEncoding:NSASCIIStringEncoding],*y=[b dataUsingEncoding:NSASCIIStringEncoding];if(!x||!y)return NO;uint8_t diff=0;for(NSUInteger i=0;i<x.length;i++)diff|=((const uint8_t *)x.bytes)[i]^((const uint8_t *)y.bytes)[i];return diff==0; }
static NSString *proof(NSData *key,NSString *role,NSString *server,NSString *client) { NSData *text=[[NSString stringWithFormat:@"BTH1:%@:%@:%@",role,server,client] dataUsingEncoding:NSASCIIStringEncoding];uint8_t out[CC_SHA256_DIGEST_LENGTH];CCHmac(kCCHmacAlgSHA256,key.bytes,key.length,text.bytes,text.length,out);return hex(out,sizeof(out)); }
static BOOL jsonDepth(id value,unsigned depth) { BOOL dict=[value isKindOfClass:NSDictionary.class],array=[value isKindOfClass:NSArray.class];if((dict||array)&&depth>16)return NO;if(dict){for(id key in value)if(!jsonDepth(value[key],depth+1))return NO;}else if(array){for(id child in value)if(!jsonDepth(child,depth+1))return NO;}return YES; }

@implementation BTAgent {
    NSData *_key;
    NSString *_root;
    BTAudioReceiver *_audio;
    int _listener,_client;
    BOOL _stopping,_audioOn,_authenticated;
    NSLock *_tx;
    uint32_t _sequences[8],_previousAudio;
    uint64_t _audioPackets,_audioBytes,_audioGaps;
    dispatch_queue_t _network,_worker,_commands;
    dispatch_semaphore_t _commandSlots;
    NSMutableDictionary *_jobs,*_jobSpecs;
    NSMutableSet *_cancelled;
    NSMutableArray *_events;
    NSUInteger _queuedJobs;
    NSDate *_eventWindow;
    NSUInteger _eventsInWindow;
}
- (instancetype)initWithKey:(NSData *)key storage:(NSString *)storage {
    if(key.length!=32)return nil;
    if((self=[super init])) {
        _key=[key copy];_root=[storage copy];_listener=-1;_client=-1;_tx=[NSLock new];_audio=[BTAudioReceiver new];
        _network=dispatch_queue_create("app.backthread.network",DISPATCH_QUEUE_SERIAL);
        _worker=dispatch_queue_create("app.backthread.worker",dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL,QOS_CLASS_UTILITY,0));
        _commands=dispatch_queue_create("app.backthread.commands",DISPATCH_QUEUE_SERIAL);_commandSlots=dispatch_semaphore_create(16);
        [[NSFileManager defaultManager] createDirectoryAtPath:_root withIntermediateDirectories:YES attributes:@{NSFileProtectionKey:NSFileProtectionCompleteUntilFirstUserAuthentication} error:nil];
        NSDictionary *saved=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfFile:[_root stringByAppendingPathComponent:@".jobs.json"]]?:[NSData data] options:NSJSONReadingMutableContainers error:nil];
        _jobs=[saved isKindOfClass:NSDictionary.class]?[saved mutableCopy]:[NSMutableDictionary new];_jobSpecs=[NSMutableDictionary new];_cancelled=[NSMutableSet new];_events=[NSMutableArray new];
        for(NSString *jid in [_jobs.allKeys copy]) { NSDictionary *s=_jobs[jid];if([@[@"queued",@"running"] containsObject:s[@"state"]])_jobs[jid]=@{@"jobId":jid,@"state":@"interrupted",@"progress":@0,@"error":@"Agent restarted; submit a new job ID to retry"}; }
    } return self;
}
- (BTAudioReceiver *)audio { return _audio; }
- (void)notify:(NSString *)type body:(NSDictionary *)body { void(^callback)(NSString *,NSDictionary *)=self.stateChanged;if(callback)dispatch_async(dispatch_get_main_queue(),^{callback(type,body);}); }
- (BOOL)send:(NSString *)type body:(NSDictionary *)body reply:(id)reply channel:(uint16_t)channel {
    NSMutableDictionary *m=[@{@"type":type,@"body":body?:@{}} mutableCopy];if([reply isKindOfClass:NSString.class])m[@"replyTo"]=reply;
    if([type isEqual:@"media.command"]||[type isEqual:@"action.invoke"]||[type isEqual:@"job.cancel"])m[@"id"]=[[[NSUUID UUID] UUIDString] lowercaseString];
    NSData *data=[NSJSONSerialization dataWithJSONObject:m options:0 error:nil];if(!data||data.length>65536)return NO;
    [_tx lock];int fd=_client;if(!_authenticated&&![type hasPrefix:@"auth."]){[_tx unlock];return NO;}uint8_t header[BT_HEADER_SIZE];BTHeader h={(uint32_t)data.length,channel,++_sequences[channel],0};
    BOOL ok=fd>=0&&bt_header_encode(header,&h)==0&&exact(fd,header,sizeof(header),YES)&&exact(fd,(void *)data.bytes,data.length,YES);if(!ok&&fd>=0)shutdown(fd,SHUT_RDWR);[_tx unlock];return ok;
}
- (NSDictionary *)read:(int)fd header:(BTHeader *)h payload:(NSData **)payload {
    uint8_t b[BT_HEADER_SIZE];if(!exact(fd,b,sizeof(b),NO)||bt_header_decode(b,h))return nil;
    NSMutableData *p=[NSMutableData dataWithLength:h->length];if(!exact(fd,p.mutableBytes,h->length,NO))return nil;if(payload)*payload=p;
    if(h->channel==2||h->channel==6)return @{};
    id m=[NSJSONSerialization JSONObjectWithData:p options:0 error:nil];
    if(![m isKindOfClass:NSDictionary.class]||![m[@"type"] isKindOfClass:NSString.class]||[m[@"type"] length]==0||[m[@"type"] length]>80||![m[@"body"] isKindOfClass:NSDictionary.class]||!jsonDepth(m,1))return nil;
    for(NSString *field in @[@"id",@"replyTo"])if(m[field]&&m[field]!=NSNull.null&&(![m[field] isKindOfClass:NSString.class]||[m[field] length]>64))return nil;
    return m;
}
- (BOOL)authenticate:(int)fd {
    uint8_t random[32];if(SecRandomCopyBytes(kSecRandomDefault,sizeof(random),random)!=errSecSuccess)return NO;
    NSString *server=hex(random,sizeof(random));[self send:@"auth.challenge" body:@{@"serverNonce":server} reply:nil channel:1];
    BTHeader h;NSDictionary *m=[self read:fd header:&h payload:nil];if(!m)return NO;NSDictionary *b=m[@"body"];
    if(h.channel!=1||![m[@"type"] isEqual:@"auth.response"]||!hexString(b[@"clientNonce"],64)||!constantEqual(b[@"proof"],proof(_key,@"host",server,b[@"clientNonce"])))return NO;
    BOOL ok=[self send:@"auth.ok" body:@{@"proof":proof(_key,@"agent",server,b[@"clientNonce"]),@"protocol":@1} reply:nil channel:1];[_tx lock];_authenticated=ok;[_tx unlock];return ok;
}
- (void)start {
    dispatch_async(_network,^{@autoreleasepool {
        self->_stopping=NO;int fd=socket(AF_INET,SOCK_STREAM,0);if(fd<0){[self notify:@"connection" body:@{@"state":@"socket failed"}];return;}
        self->_listener=fd;int yes=1;setsockopt(fd,SOL_SOCKET,SO_REUSEADDR,&yes,sizeof(yes));
        struct sockaddr_in address={0};address.sin_len=sizeof(address);address.sin_family=AF_INET;address.sin_port=htons(23451);address.sin_addr.s_addr=htonl(INADDR_LOOPBACK);
        if(bind(fd,(struct sockaddr *)&address,sizeof(address))||listen(fd,2)){close(fd);self->_listener=-1;[self notify:@"connection" body:@{@"state":@"Cannot bind localhost:23451"}];return;}
        [self notify:@"connection" body:@{@"state":@"Waiting for USB host"}];
        while(!self->_stopping) {
            int client=accept(fd,NULL,NULL);if(client<0){if(errno==EINTR)continue;break;}
            struct timeval timeout={5,0},sendTimeout={3,0};setsockopt(client,SOL_SOCKET,SO_RCVTIMEO,&timeout,sizeof(timeout));setsockopt(client,SOL_SOCKET,SO_SNDTIMEO,&sendTimeout,sizeof(sendTimeout));setsockopt(client,SOL_SOCKET,SO_NOSIGPIPE,&yes,sizeof(yes));
            [self->_tx lock];self->_client=client;self->_authenticated=NO;memset(self->_sequences,0,sizeof(self->_sequences));[self->_tx unlock];
            @try { if([self authenticate:client]) { timeout.tv_sec=12;setsockopt(client,SOL_SOCKET,SO_RCVTIMEO,&timeout,sizeof(timeout));[self notify:@"connection" body:@{@"state":@"Connected by USB"}];[self session:client]; } }
            @catch(NSException *exception) { [self notify:@"error" body:@{@"error":exception.reason?:@"Protocol error"}]; }
            [self->_tx lock];self->_client=-1;self->_authenticated=NO;shutdown(client,SHUT_RDWR);close(client);[self->_tx unlock];
            self->_audioOn=NO;[self->_audio stop];[self notify:@"connection" body:@{@"state":@"Disconnected — waiting for host"}];
        }
        if(self->_listener==fd){close(fd);self->_listener=-1;}
    }});
}
- (void)stop { _stopping=YES;if(_listener>=0)shutdown(_listener,SHUT_RDWR);[_tx lock];if(_client>=0)shutdown(_client,SHUT_RDWR);[_tx unlock];[_audio stop]; }
- (void)command:(NSString *)type body:(NSDictionary *)body {
    if([type isEqual:@"job.cancel"]){NSString *jid=body[@"jobId"];@synchronized(self){if(jid&&[@[@"queued",@"running"] containsObject:_jobs[jid][@"state"]])[_cancelled addObject:jid];}return;}
    if(dispatch_semaphore_wait(_commandSlots,DISPATCH_TIME_NOW)!=0)return;
    dispatch_async(_commands,^{@autoreleasepool { [self send:type body:body reply:nil channel:3];dispatch_semaphore_signal(self->_commandSlots); }});
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
            if(!_audioOn||payload.length%4)break;
            if(_previousAudio&&h.sequence!=_previousAudio+1)_audioGaps++;_previousAudio=h.sequence;_audioPackets++;_audioBytes+=payload.length;[_audio receive:payload];continue;
        }
        if(h.channel==6) { if(![self append:payload])break;continue; }
        NSString *type=m[@"type"];NSDictionary *b=m[@"body"];id rid=m[@"id"];
        if([type isEqual:@"ping"]) { [self send:@"pong" body:@{} reply:rid channel:1]; }
        else if([type isEqual:@"hello"]) { [self send:@"ok" body:@{@"agent":@"BackThread.Agent",@"simulated":@NO,@"capabilities":@[@"pcm",@"jobs",@"files",@"events",@"UIKit",@"direct-handoff"]} reply:rid channel:h.channel]; }
        else if([type isEqual:@"audio.start"]) {
            NSError *error=nil;if([b[@"sampleRate"] intValue]!=48000||[b[@"channels"] intValue]!=2||[b[@"bits"] intValue]!=16) { [self fail:@"Only 48 kHz stereo PCM16 supported" message:m channel:h.channel];continue; }
            if([_audio start:&error]) { _audioOn=YES;_audioPackets=_audioBytes=_audioGaps=0;_previousAudio=0;[self send:@"ok" body:@{@"ready":@YES} reply:rid channel:h.channel]; }
            else [self fail:error.localizedDescription message:m channel:h.channel];
        }
        else if([type isEqual:@"audio.stop"]) { _audioOn=NO;[_audio stop];[self send:@"ok" body:@{@"stopped":@YES} reply:rid channel:h.channel]; }
        else if([type isEqual:@"audio.stats"]) { NSMutableDictionary *s=[[_audio statistics] mutableCopy];s[@"packets"]=@(_audioPackets);s[@"bytes"]=@(_audioBytes);s[@"gaps"]=@(_audioGaps);[self send:@"ok" body:s reply:rid channel:h.channel]; }
        else if([type isEqual:@"audio.volume"]) { [_audio setVolume:[b[@"value"] floatValue]];[self send:@"ok" body:@{} reply:rid channel:h.channel]; }
        else if([type isEqual:@"media.state"]||[type isEqual:@"action.registry"]||[type isEqual:@"media.handoff"]) { [self notify:type body:b];[self send:@"ok" body:@{@"received":@YES} reply:rid channel:h.channel]; }
        else if([type isEqual:@"event.publish"]) { BOOL accepted=[self event:b];[self send:@"ok" body:@{@"accepted":@(accepted)} reply:rid channel:h.channel]; }
        else if([type isEqual:@"event.history"]) { [self send:@"ok" body:@{@"events":[_events copy]} reply:rid channel:h.channel]; }
        else if([type isEqual:@"job.submit"]) { [self submit:b message:m channel:h.channel]; }
        else if([type isEqual:@"job.query"]) { NSDictionary *s;@synchronized(self){s=_jobs[b[@"jobId"]];}if(s)[self send:@"ok" body:s reply:rid channel:h.channel];else [self fail:@"Unknown job" message:m channel:h.channel]; }
        else if([type isEqual:@"job.cancel"]) { BOOL found;@synchronized(self){found=[@[@"queued",@"running"] containsObject:_jobs[b[@"jobId"]][@"state"]];if(found)[_cancelled addObject:b[@"jobId"]];}[self send:@"ok" body:@{@"cancelled":@(found)} reply:rid channel:h.channel]; }
        else if([type isEqual:@"file.begin"]) { [self begin:b message:m channel:h.channel]; }
        else if([type isEqual:@"file.commit"]) { [self commit:b message:m channel:h.channel client:fd]; }
        else if([type isEqual:@"file.read"]) { [self download:b message:m channel:h.channel]; }
        else if([type isEqual:@"file.list"]) {
            NSMutableArray *files=[NSMutableArray new];for(NSString *name in [[NSFileManager defaultManager] contentsOfDirectoryAtPath:_root error:nil])if(![name hasPrefix:@"."]&&files.count<256){NSDictionary *a=[[NSFileManager defaultManager] attributesOfItemAtPath:[self path:name] error:nil];if(a)[files addObject:@{@"name":name,@"size":a[NSFileSize]}];}[self send:@"ok" body:@{@"files":files} reply:rid channel:h.channel];
        }
        else if(![m[@"replyTo"] isKindOfClass:NSString.class]) [self fail:@"Unsupported operation" message:m channel:h.channel];
    }}
}
- (BOOL)event:(NSDictionary *)b {
    NSString *eid=b[@"id"],*title=b[@"title"],*text=b[@"body"],*source=b[@"source"];
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
- (BOOL)append:(NSData *)data {
    if(data.length<40||data.length>32808)return NO;NSString *tid=[[NSString alloc] initWithBytes:data.bytes length:32 encoding:NSASCIIStringEncoding];NSString *part=[self part:tid];if(!part)return NO;
    NSDictionary *spec=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfFile:[part stringByAppendingString:@".json"]]?:[NSData data] options:0 error:nil];if(!spec)return NO;
    uint64_t offset=bt_u64((const uint8_t *)data.bytes+32);NSFileHandle *file=[NSFileHandle fileHandleForWritingAtPath:part];if(!file)return NO;unsigned long long length=[file seekToEndOfFile];
    if(offset>length||offset+data.length-40>[spec[@"size"] unsignedLongLongValue]||(offset<length&&offset+data.length-40>length)){[file closeFile];return NO;}
    if(offset==length){[file writeData:[data subdataWithRange:NSMakeRange(40,data.length-40)]];[file synchronizeFile];length=[file offsetInFile];}[file closeFile];
    return [self send:@"file.ack" body:@{@"transferId":tid,@"offset":@(length)} reply:nil channel:5];
}
- (void)commit:(NSDictionary *)b message:(NSDictionary *)m channel:(uint16_t)c client:(int)client {
    NSString *part=[self part:b[@"transferId"]];if(!part){[self fail:@"Invalid transfer" message:m channel:c];return;}
    @synchronized(self){if(_queuedJobs>=8){[self fail:@"Worker queue full" message:m channel:c];return;}_queuedJobs++;}
    dispatch_async(_worker,^{@autoreleasepool { @try {
        NSString *meta=[part stringByAppendingString:@".json"];NSDictionary *spec=[NSJSONSerialization JSONObjectWithData:[NSData dataWithContentsOfFile:meta]?:[NSData data] options:0 error:nil];
        NSString *destination=[self path:spec[@"name"]];NSError *error=nil;NSString *sha=[self hash:part job:nil error:&error];
        unsigned long long length=[[[NSFileManager defaultManager] attributesOfItemAtPath:part error:nil][NSFileSize] unsignedLongLongValue];
        if(!destination||!sha||length!=[spec[@"size"] unsignedLongLongValue]||![sha isEqual:[spec[@"sha256"] lowercaseString]]){if(self->_client==client)[self fail:error.localizedDescription?:@"Incomplete transfer or checksum mismatch" message:m channel:c];}
        else if(rename(part.fileSystemRepresentation,destination.fileSystemRepresentation)){if(self->_client==client)[self fail:@"Atomic archive rename failed" message:m channel:c];}
        else {[[NSFileManager defaultManager] removeItemAtPath:meta error:nil];if(self->_client==client)[self send:@"ok" body:@{@"name":spec[@"name"],@"size":@(length),@"sha256":sha} reply:m[@"id"] channel:c];}
    } @catch(NSException *e){if(self->_client==client)[self fail:e.reason message:m channel:c];} @finally{@synchronized(self){self->_queuedJobs--;}} }});
}
- (void)download:(NSDictionary *)b message:(NSDictionary *)m channel:(uint16_t)c {
    NSString *path=[self path:b[@"name"]],*tid=b[@"transferId"];long long offset=[b[@"offset"] longLongValue];int count=[b[@"count"] intValue];
    if(!path||!hexString(tid,32)||offset<0||count<1||count>32768){[self fail:@"Invalid artifact range" message:m channel:c];return;}
    NSFileHandle *file=[NSFileHandle fileHandleForReadingAtPath:path];if(!file){[self fail:@"Artifact not found" message:m channel:c];return;}unsigned long long size=[file seekToEndOfFile];if((unsigned long long)offset>size){[file closeFile];[self fail:@"Read beyond file" message:m channel:c];return;}
    [file seekToFileOffset:(unsigned long long)offset];NSData *chunk=[file readDataOfLength:(NSUInteger)count];[file closeFile];NSMutableData *payload=[NSMutableData dataWithLength:40];memcpy(payload.mutableBytes,[tid dataUsingEncoding:NSASCIIStringEncoding].bytes,32);bt_put64((uint8_t *)payload.mutableBytes+32,(uint64_t)offset);[payload appendData:chunk];
    uint8_t header[BT_HEADER_SIZE];[_tx lock];BTHeader h={(uint32_t)payload.length,6,++_sequences[6],0};bt_header_encode(header,&h);BOOL ok=_client>=0&&exact(_client,header,sizeof(header),YES)&&exact(_client,payload.mutableBytes,payload.length,YES);[_tx unlock];
    if(ok)[self send:@"ok" body:@{@"length":@(chunk.length)} reply:m[@"id"] channel:c];
}
- (void)persist { NSData *data=[NSJSONSerialization dataWithJSONObject:_jobs options:0 error:nil];[data writeToFile:[_root stringByAppendingPathComponent:@".jobs.json"] atomically:YES]; }
- (void)update:(NSDictionary *)state { @synchronized(self){NSString *previous=_jobs[state[@"jobId"]][@"state"];_jobs[state[@"jobId"]]=state;if(![previous isEqual:state[@"state"]])[self persist];}[self notify:@"job.progress" body:state];[self send:@"job.progress" body:state reply:nil channel:5]; }
- (BOOL)cancelled:(NSString *)jid { @synchronized(self){return _stopping||(jid&&[_cancelled containsObject:jid]);} }
- (NSString *)hash:(NSString *)path job:(NSString *)jid error:(NSError **)error {
    FILE *file=fopen(path.fileSystemRepresentation,"rb");if(!file){if(error)*error=[NSError errorWithDomain:NSPOSIXErrorDomain code:errno userInfo:nil];return nil;}
    CC_SHA256_CTX hash;CC_SHA256_Init(&hash);uint8_t buffer[32768];size_t n;BOOL failed=NO;uint64_t done=0;struct stat st;fstat(fileno(file),&st);double last=0;
    while((n=fread(buffer,1,sizeof(buffer),file))>0) {
        while(self.memoryPressure&&![self cancelled:jid])usleep(100000);
        if([self cancelled:jid]){failed=YES;break;}CC_SHA256_Update(&hash,buffer,(CC_LONG)n);done+=n;
        double progress=st.st_size?((double)done/(double)st.st_size):1;if(jid&&progress-last>=.1){[self update:@{@"jobId":jid,@"state":@"running",@"progress":@(progress)}];last=progress;}
    }
    if(ferror(file))failed=YES;fclose(file);if(failed)return nil;uint8_t digest[CC_SHA256_DIGEST_LENGTH];CC_SHA256_Final(digest,&hash);return hex(digest,sizeof(digest));
}
- (void)submit:(NSDictionary *)b message:(NSDictionary *)m channel:(uint16_t)c {
    NSString *jid=b[@"jobId"],*kind=b[@"kind"],*name=b[@"name"];
    if(![jid isKindOfClass:NSString.class]||jid.length==0||jid.length>64||![@[@"sha256",@"verify",@"metadata",@"text",@"diagnostic"] containsObject:kind]||![name isKindOfClass:NSString.class]){[self fail:@"Invalid job" message:m channel:c];return;}
    @synchronized(self) {
        if(_jobs[jid]){if(_jobSpecs[jid]&&![_jobSpecs[jid] isEqual:b])[self fail:@"Job ID conflict" message:m channel:c];else [self send:@"ok" body:_jobs[jid] reply:m[@"id"] channel:c];return;}
        if(_queuedJobs>=8||self.memoryPressure){[self fail:@"Worker busy or paused for memory pressure" message:m channel:c];return;}
        if(_jobs.count>=64){NSString *old=nil;for(NSString *key in _jobs)if(![@[@"queued",@"running"] containsObject:_jobs[key][@"state"]]){old=key;break;}if(!old){[self fail:@"Job history full" message:m channel:c];return;}[_jobs removeObjectForKey:old];[_jobSpecs removeObjectForKey:old];}
        _queuedJobs++;_jobSpecs[jid]=[b copy];_jobs[jid]=@{@"jobId":jid,@"state":@"queued",@"progress":@0};[self persist];[self send:@"ok" body:_jobs[jid] reply:m[@"id"] channel:c];
    }
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
        @synchronized(self){self->_queuedJobs--;[self->_cancelled removeObject:jid];}
    }});
}
@end
