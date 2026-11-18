#import <Foundation/Foundation.h>
#import "BTAgent.h"
#include <signal.h>
#include <unistd.h>
#include <pwd.h>
#include <grp.h>

static volatile sig_atomic_t stopping=0;
static void stopSignal(int signalNumber) { (void)signalNumber;stopping=1; }

int main(int argc,char **argv) {
    if(geteuid()==0){struct passwd *mobile=getpwnam("mobile");if(!mobile||setgroups(1,&mobile->pw_gid)||setgid(mobile->pw_gid)||setuid(mobile->pw_uid)){fprintf(stderr,"Cannot enter the mobile user context\n");return 1;}}
    @autoreleasepool {
        NSString *keyPath=nil,*storage=nil;
        for(int i=1;i<argc;i++) {
            NSString *argument=[NSString stringWithUTF8String:argv[i]];
            if([argument isEqual:@"--key-file"]&&i+1<argc)keyPath=[NSString stringWithUTF8String:argv[++i]];
            else if([argument isEqual:@"--state"]&&i+1<argc)storage=[NSString stringWithUTF8String:argv[++i]];
            else { fprintf(stderr,"Usage: BackThread.Agent --key-file <hex-key-file> --state <BackThread-owned directory>\n");return 2; }
        }
        if(!keyPath||!storage){fprintf(stderr,"A key file and state directory are required\n");return 2;}
        NSString *text=[[NSString stringWithContentsOfFile:keyPath encoding:NSASCIIStringEncoding error:NULL] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
        if(!storage||text.length!=64||[text rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet]].location!=NSNotFound){fprintf(stderr,"A valid private key file and state directory are required\n");return 2;}
        NSMutableData *key=[NSMutableData dataWithLength:32];
        for(NSUInteger i=0;i<32;i++){unsigned value=0;[[NSScanner scannerWithString:[text substringWithRange:NSMakeRange(i*2,2)]] scanHexInt:&value];((uint8_t *)key.mutableBytes)[i]=(uint8_t)value;}
        BTAgent *agent=[[BTAgent alloc] initWithKey:key storage:storage];
        agent.stateChanged=^(NSString *type,NSDictionary *body){if([type isEqual:@"connection"])NSLog(@"BackThread.Agent: %@",body[@"state"]);else if([type isEqual:@"error"])NSLog(@"BackThread.Agent: error %@",body[@"error"]);};
        signal(SIGTERM,stopSignal);signal(SIGINT,stopSignal);[agent start];
        while(!stopping) { @autoreleasepool { [[NSRunLoop currentRunLoop] runMode:NSDefaultRunLoopMode beforeDate:[NSDate dateWithTimeIntervalSinceNow:.2]]; } }
        [agent stop];return 0;
    }
}
