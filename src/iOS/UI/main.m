#import <UIKit/UIKit.h>
#import "BTViewController.h"
#import "../Agent/BTAgent.h"
@interface BTAppDelegate : UIResponder <UIApplicationDelegate>
@property(nonatomic,strong) UIWindow *window;
@property(nonatomic,strong) BTAgent *agent;
@end
@implementation BTAppDelegate
- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)options {
    BTViewController *vc=[BTViewController new];self.window=[[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];self.window.rootViewController=vc;[self.window makeKeyAndVisible];
    NSString *keyString=[[NSString stringWithContentsOfFile:[NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread/session.key"] encoding:NSASCIIStringEncoding error:nil] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    NSMutableData *key=[NSMutableData new];if(keyString.length==64&&[keyString rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet]].location==NSNotFound)for(NSUInteger i=0;i<64;i+=2){unsigned int byte=0;[[NSScanner scannerWithString:[keyString substringWithRange:NSMakeRange(i,2)]] scanHexInt:&byte];uint8_t b=(uint8_t)byte;[key appendBytes:&b length:1];}
    if(key.length==32){self.agent=[[BTAgent alloc] initWithKey:key storage:[NSHomeDirectory() stringByAppendingPathComponent:@"Documents/BackThread"]];vc.agent=self.agent;__weak BTViewController *weak=vc;self.agent.stateChanged=^(NSString *type,NSDictionary *body){[weak receive:type body:body];};[self.agent start];}
    else [vc receive:@"connection" body:@{@"state":@"Tap Pair to provision the host key"}];return YES;
}
- (void)applicationWillTerminate:(UIApplication *)application { [self.agent stop]; }
- (void)applicationDidReceiveMemoryWarning:(UIApplication *)application { self.agent.memoryPressure=YES; }
@end
int main(int argc,char **argv) { @autoreleasepool { return UIApplicationMain(argc,argv,nil,NSStringFromClass(BTAppDelegate.class)); } }
