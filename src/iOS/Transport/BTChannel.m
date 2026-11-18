#import "BTChannel.h"
#import <Security/SecureTransport.h>
#import <CommonCrypto/CommonDigest.h>
#include <sys/socket.h>
#include <sys/select.h>
#include <netinet/in.h>
#include <netinet/tcp.h>
#include <arpa/inet.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdatomic.h>
#include <errno.h>
#include <mach/mach_time.h>
NSTimeInterval BTSessionClock(void){static mach_timebase_info_data_t info;static dispatch_once_t once;dispatch_once(&once,^{mach_timebase_info(&info);});return mach_continuous_time()*((double)info.numer/info.denom)/1000000000.0;}
static OSStatus BTSSLRead(SSLConnectionRef connection,void *data,size_t *length){size_t wanted=*length;ssize_t n=recv((int)(intptr_t)connection,data,wanted,0);*length=n>0?(size_t)n:0;if(n==0)return errSSLClosedGraceful;if(n<0)return errno==EAGAIN||errno==EWOULDBLOCK||errno==EINTR?errSSLWouldBlock:errSSLClosedAbort;return (size_t)n==wanted?noErr:errSSLWouldBlock;}
static OSStatus BTSSLWrite(SSLConnectionRef connection,const void *data,size_t *length){size_t wanted=*length;ssize_t n=send((int)(intptr_t)connection,data,wanted,0);*length=n>0?(size_t)n:0;if(n<0)return errno==EAGAIN||errno==EWOULDBLOCK||errno==EINTR?errSSLWouldBlock:errSSLClosedAbort;return (size_t)n==wanted?noErr:errSSLWouldBlock;}
static BOOL BTWaitSocket(int fd,BOOL writing,NSTimeInterval seconds){fd_set set;FD_ZERO(&set);FD_SET(fd,&set);struct timeval tv={(int)seconds,(int)((seconds-(int)seconds)*1000000)};return select(fd+1,writing?NULL:&set,writing?&set:NULL,NULL,&tv)>0;}
static BOOL BTPinMatches(SSLContextRef ssl,NSString *pin){SecTrustRef trust=NULL;if(SSLCopyPeerTrust(ssl,&trust)!=noErr||!trust)return NO;SecCertificateRef certificate=SecTrustGetCertificateAtIndex(trust,0);CFDataRef der=certificate?SecCertificateCopyData(certificate):NULL;uint8_t hash[CC_SHA256_DIGEST_LENGTH];BOOL match=NO;if(der&&pin.length==64){CC_SHA256(CFDataGetBytePtr(der),(CC_LONG)CFDataGetLength(der),hash);const char *expected=pin.lowercaseString.UTF8String;static const char hex[]="0123456789abcdef";uint8_t difference=0;for(unsigned i=0;i<32;i++){difference|=expected[2*i]^hex[hash[i]>>4];difference|=expected[2*i+1]^hex[hash[i]&15];}match=difference==0;}if(der)CFRelease(der);CFRelease(trust);return match;}
@implementation BTChannel {
    int _fd;BOOL _secure;atomic_bool _closed;SSLContextRef _ssl;
    NSCondition *_condition;NSMutableData *_received,*_sending;uint64_t _queued,_written;
    NSTimeInterval _deadline;
}
- (instancetype)initWithDescriptor:(int)descriptor{if((self=[super init])){_fd=descriptor;_readTimeout=12;atomic_init(&_closed,NO);_condition=[NSCondition new];}return self;}
- (int)descriptor{return _fd;}
- (BOOL)secure{return _secure;}
+ (instancetype)connectTLS:(NSString *)address port:(int)port pin:(NSString *)pin deadline:(NSTimeInterval)deadline error:(NSError **)error{
    int fd=-1;BTChannel *channel=nil;SSLContextRef ssl=NULL;BOOL pinned=NO;OSStatus status=errSSLInternal;
    struct sockaddr_in endpoint={0};endpoint.sin_len=sizeof(endpoint);endpoint.sin_family=AF_INET;endpoint.sin_port=htons((uint16_t)port);
    if(port<1024||port>65535||pin.length!=64||inet_pton(AF_INET,address.UTF8String,&endpoint.sin_addr)!=1)goto failure;
    fd=socket(AF_INET,SOCK_STREAM,0);if(fd<0)goto failure;if(fd>=FD_SETSIZE)goto failure;
    int yes=1;setsockopt(fd,SOL_SOCKET,SO_NOSIGPIPE,&yes,sizeof(yes));setsockopt(fd,IPPROTO_TCP,TCP_NODELAY,&yes,sizeof(yes));fcntl(fd,F_SETFL,fcntl(fd,F_GETFL,0)|O_NONBLOCK);
    if(connect(fd,(struct sockaddr *)&endpoint,sizeof(endpoint))<0){if(errno!=EINPROGRESS||!BTWaitSocket(fd,YES,3))goto failure;int socketError=0;socklen_t length=sizeof(socketError);if(getsockopt(fd,SOL_SOCKET,SO_ERROR,&socketError,&length)||socketError)goto failure;}
    ssl=SSLCreateContext(kCFAllocatorDefault,kSSLClientSide,kSSLStreamType);if(!ssl)goto failure;
    SSLCipherSuite ciphers[]={TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256,TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384};
    if(SSLSetIOFuncs(ssl,BTSSLRead,BTSSLWrite)!=noErr||SSLSetConnection(ssl,(SSLConnectionRef)(intptr_t)fd)!=noErr||SSLSetProtocolVersionMin(ssl,kTLSProtocol12)!=noErr||SSLSetProtocolVersionMax(ssl,kTLSProtocol12)!=noErr||SSLSetEnabledCiphers(ssl,ciphers,2)!=noErr||SSLSetSessionOption(ssl,kSSLSessionOptionBreakOnServerAuth,YES)!=noErr)goto failure;
    NSTimeInterval end=MIN(deadline,BTSessionClock()+5);
    do{status=SSLHandshake(ssl);if(status==errSSLServerAuthCompleted){pinned=BTPinMatches(ssl,pin);if(!pinned)goto failure;}else if(status==errSSLWouldBlock){BTWaitSocket(fd,NO,.03);}else if(status!=noErr)goto failure;}while(status!=noErr&&BTSessionClock()<end);
    if(status!=noErr||!pinned||BTSessionClock()>=deadline)goto failure;
    channel=[[BTChannel alloc] initWithDescriptor:fd];channel->_secure=YES;channel->_ssl=ssl;channel->_deadline=deadline;channel->_received=[NSMutableData new];channel->_sending=[NSMutableData new];
    [channel startPump];return channel;
failure:
    if(ssl)CFRelease(ssl);if(fd>=0)close(fd);if(error)*error=[NSError errorWithDomain:@"BackThread.TLS" code:status userInfo:@{NSLocalizedDescriptionKey:@"Peer unreachable, TLS negotiation failed or enrolled identity rejected"}];return nil;
}
- (void)startPump{dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED,0),^{@autoreleasepool{[self pump];}});}
- (void)pump{
    const NSUInteger capacity=256*1024;uint8_t buffer[8192];
    while(!atomic_load(&_closed)&&BTSessionClock()<_deadline){
        [_condition lock];NSUInteger available=capacity-_received.length;BOOL wantsWrite=_sending.length>0,canRead=available>0;[_condition unlock];
        fd_set reads,writes;FD_ZERO(&reads);FD_ZERO(&writes);if(canRead)FD_SET(_fd,&reads);if(wantsWrite)FD_SET(_fd,&writes);struct timeval timeout={0,30000};int ready=select(_fd+1,&reads,&writes,NULL,&timeout);if(ready<0&&errno!=EINTR)break;
        size_t buffered=0;SSLGetBufferedReadSize(_ssl,&buffered);
        if(canRead&&(FD_ISSET(_fd,&reads)||buffered)){
            size_t read=0;OSStatus result=SSLRead(_ssl,buffer,MIN(sizeof(buffer),available),&read);[_condition lock];if(read)[_received appendBytes:buffer length:read];[_condition broadcast];[_condition unlock];if(result!=noErr&&result!=errSSLWouldBlock)break;
        }
        if(wantsWrite&&FD_ISSET(_fd,&writes)){
            [_condition lock];size_t written=0;OSStatus result=SSLWrite(_ssl,_sending.bytes,MIN(_sending.length,(NSUInteger)16384),&written);if(written){[_sending replaceBytesInRange:NSMakeRange(0,written) withBytes:NULL length:0];_written+=written;}[_condition broadcast];[_condition unlock];if(result!=noErr&&result!=errSSLWouldBlock)break;
        }
    }
    [self close];if(_ssl){CFRelease(_ssl);_ssl=NULL;}
}
- (BOOL)plain:(void *)bytes count:(size_t)count write:(BOOL)write{uint8_t *cursor=bytes;while(count&&!atomic_load(&_closed)){ssize_t n=write?send(_fd,cursor,count,0):recv(_fd,cursor,count,0);if(n<0&&errno==EINTR)continue;if(n<=0)return NO;cursor+=n;count-=(size_t)n;}return count==0;}
- (BOOL)readExact:(void *)bytes count:(size_t)count{
    if(!_secure)return [self plain:bytes count:count write:NO];uint8_t *cursor=bytes;NSTimeInterval end=BTSessionClock()+self.readTimeout;
    [_condition lock];while(count&&!atomic_load(&_closed)&&BTSessionClock()<end){if(_received.length){NSUInteger n=MIN(count,_received.length);memcpy(cursor,_received.bytes,n);[_received replaceBytesInRange:NSMakeRange(0,n) withBytes:NULL length:0];cursor+=n;count-=n;}else [_condition waitUntilDate:[NSDate dateWithTimeIntervalSinceNow:.1]];}[_condition unlock];if(count)[self close];return count==0;
}
- (BOOL)writeExact:(const void *)bytes count:(size_t)count{
    if(!_secure)return [self plain:(void *)bytes count:count write:YES];if(count>256*1024)return NO;NSTimeInterval end=BTSessionClock()+3;
    [_condition lock];while(_sending.length+count>256*1024&&!atomic_load(&_closed)&&BTSessionClock()<end)[_condition waitUntilDate:[NSDate dateWithTimeIntervalSinceNow:.1]];
    if(atomic_load(&_closed)||_sending.length+count>256*1024){[_condition unlock];[self close];return NO;}[_sending appendBytes:bytes length:count];_queued+=count;uint64_t target=_queued;
    while(_written<target&&!atomic_load(&_closed)&&BTSessionClock()<end)[_condition waitUntilDate:[NSDate dateWithTimeIntervalSinceNow:.1]];BOOL success=_written>=target;[_condition unlock];if(!success)[self close];return success;
}
- (void)close{if(!atomic_exchange(&_closed,YES)&&_fd>=0)shutdown(_fd,SHUT_RDWR);[_condition lock];[_condition broadcast];[_condition unlock];}
- (void)dealloc{if(_fd>=0)close(_fd);}
@end
