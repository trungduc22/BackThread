#import <UIKit/UIKit.h>
#import "BTViewController.h"
#import "../Agent/BTAgent.h"
#include <unistd.h>
static void BTStartup(NSString *phase,NSException *exception) {
    @try {
        NSString *directory=[NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread"];
        [[NSFileManager defaultManager] createDirectoryAtPath:directory withIntermediateDirectories:YES attributes:@{NSFilePosixPermissions:@0700} error:NULL];
        NSMutableDictionary *record=[@{@"phase":phase,@"pid":@(getpid())} mutableCopy];
        if(![phase isEqual:@"main-entered"]){record[@"screenBrightness"]=@(UIScreen.mainScreen.brightness);record[@"applicationState"]=@(UIApplication.sharedApplication.applicationState);}
        if(exception){record[@"exception"]=exception.name?:@"unknown";record[@"reason"]=exception.reason?:@"";}
        NSString *path=[directory stringByAppendingPathComponent:@"startup-diagnostics.json"];
        [[NSJSONSerialization dataWithJSONObject:record options:0 error:NULL] writeToFile:path atomically:YES];
        [[NSFileManager defaultManager] setAttributes:@{NSFilePosixPermissions:@0600} ofItemAtPath:path error:NULL];
        NSLog(@"BackThread.UI startup %@",phase);
    } @catch(NSException *ignored){(void)ignored;}
}
static void BTStartupException(NSException *exception) { BTStartup(@"uncaught-exception",exception); }
@interface BTAppDelegate : UIResponder <UIApplicationDelegate>
@property(nonatomic,strong) UIWindow *window;
@property(nonatomic,strong) BTAgent *agent;
@end
@implementation BTAppDelegate
- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)options {
    BTStartup(@"application-delegate",nil);
    BTViewController *vc=[BTViewController new];self.window=[[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];self.window.rootViewController=vc;[self.window makeKeyAndVisible];
    BTStartup(@"window-visible",nil);
    NSString *keyString=[[NSString stringWithContentsOfFile:[NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread/session.key"] encoding:NSASCIIStringEncoding error:nil] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    NSMutableData *key=[NSMutableData new];if(keyString.length==64&&[keyString rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet]].location==NSNotFound)for(NSUInteger i=0;i<64;i+=2){unsigned int byte=0;[[NSScanner scannerWithString:[keyString substringWithRange:NSMakeRange(i,2)]] scanHexInt:&byte];uint8_t b=(uint8_t)byte;[key appendBytes:&b length:1];}
    if(key.length==32){BTStartup(@"key-valid",nil);self.agent=[[BTAgent alloc] initWithKey:key storage:[NSHomeDirectory() stringByAppendingPathComponent:@"Documents/BackThread"]];BTStartup(@"agent-created",nil);vc.agent=self.agent;__weak BTViewController *weak=vc;self.agent.stateChanged=^(NSString *type,NSDictionary *body){[weak receive:type body:body];};[self.agent start];BTStartup(@"listener-dispatched",nil);}
    else {[vc receive:@"connection" body:@{@"state":@"Tap Pair to provision the host key"}];BTStartup(@"key-required",nil);}return YES;
}
- (void)applicationWillTerminate:(UIApplication *)application { [self.agent stop]; }
- (void)applicationDidReceiveMemoryWarning:(UIApplication *)application { self.agent.memoryPressure=YES; }
@end
int main(int argc,char **argv) { @autoreleasepool { NSSetUncaughtExceptionHandler(BTStartupException);BTStartup(@"main-entered",nil);return UIApplicationMain(argc,argv,nil,NSStringFromClass(BTAppDelegate.class)); } }
