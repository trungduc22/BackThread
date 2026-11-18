#import "BTPhoneControl.h"
#import <UIKit/UIKit.h>
#include <fcntl.h>
#include <sys/stat.h>
#include <unistd.h>

@implementation BTPhoneControl {
    NSString *_directory;
    NSTimer *_timer;
}
- (void)start {
    if(_timer)return;
    _directory=[NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread/Control"];
    NSError *error=nil;
    if(![[NSFileManager defaultManager] createDirectoryAtPath:_directory withIntermediateDirectories:YES attributes:@{NSFilePosixPermissions:@0700,NSFileProtectionKey:NSFileProtectionCompleteUntilFirstUserAuthentication} error:&error])return;
    __weak typeof(self) weak=self;
    _timer=[NSTimer scheduledTimerWithTimeInterval:.5 repeats:YES block:^(NSTimer *timer){(void)timer;[weak poll];}];
}
- (void)poll {
    if(UIApplication.sharedApplication.applicationState!=UIApplicationStateActive||!self.perform)return;
    NSString *path=[_directory stringByAppendingPathComponent:@"request.json"];
    int fd=open(path.fileSystemRepresentation,O_RDONLY|O_NOFOLLOW|O_NONBLOCK);
    if(fd<0)return;
    struct stat st;
    BOOL safe=fstat(fd,&st)==0&&S_ISREG(st.st_mode)&&st.st_nlink==1&&st.st_size>0&&st.st_size<=16384&&(st.st_uid==getuid()||st.st_uid==0)&&!(st.st_mode&0022);
    NSMutableData *data=safe?[NSMutableData dataWithLength:(NSUInteger)st.st_size]:nil;
    ssize_t length=data?read(fd,data.mutableBytes,data.length):-1;
    close(fd);
    // Consume before invoking a command: app restarts cannot replay an old open.
    [[NSFileManager defaultManager] removeItemAtPath:path error:nil];
    if(!safe||length!=(ssize_t)data.length)return;
    id value=[NSJSONSerialization JSONObjectWithData:data options:0 error:nil];
    if(![value isKindOfClass:NSDictionary.class])return;
    NSDictionary *request=value;
    NSString *identifier=[request[@"id"] isKindOfClass:NSString.class]?request[@"id"]:@"";
    if(![[NSUUID alloc] initWithUUIDString:identifier])return;
    NSDictionary *result=nil;
    NSSet *fields=[NSSet setWithArray:@[@"id",@"version",@"command",@"url",@"seconds"]];
    BOOL valid=[request[@"version"] isKindOfClass:NSNumber.class]&&[request[@"version"] isEqual:@1];
    for(NSString *key in request)if(![fields containsObject:key])valid=NO;
    if(valid)result=self.perform(request);
    if(!result)result=@{@"success":@NO,@"error":@"Unsupported or invalid phone control request"};
    NSMutableDictionary *response=[result mutableCopy];response[@"id"]=identifier;response[@"version"]=@1;
    NSData *encoded=[NSJSONSerialization dataWithJSONObject:response options:0 error:nil];
    NSString *responsePath=[_directory stringByAppendingPathComponent:@"response.json"];
    if(encoded.length<=16384&&encoded)[encoded writeToFile:responsePath options:NSDataWritingAtomic|NSDataWritingFileProtectionCompleteUntilFirstUserAuthentication error:nil];
    [[NSFileManager defaultManager] setAttributes:@{NSFilePosixPermissions:@0600} ofItemAtPath:responsePath error:nil];
}
- (void)dealloc { [_timer invalidate]; }
@end
