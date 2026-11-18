#import <UIKit/UIKit.h>
#import <mach/mach.h>
#import <objc/message.h>
#include <unistd.h>

static void mark(NSString *phase) {
    mach_task_basic_info_data_t info={0};mach_msg_type_number_t count=MACH_TASK_BASIC_INFO_COUNT;
    kern_return_t result=task_info(mach_task_self(),MACH_TASK_BASIC_INFO,(task_info_t)&info,&count);
    NSDictionary *record=@{@"component":@"BackThread.Probe",@"phase":phase,@"pid":@(getpid()),@"residentBytes":@(result==KERN_SUCCESS?info.resident_size:0),@"uptime":@([NSProcessInfo processInfo].systemUptime),@"brightness":@([phase isEqual:@"main-entered"]?-1:UIScreen.mainScreen.brightness)};
    NSString *directory=[NSHomeDirectory() stringByAppendingPathComponent:@"Documents/BackThreadProbe"];
    [[NSFileManager defaultManager] createDirectoryAtPath:directory withIntermediateDirectories:YES attributes:nil error:NULL];
    [[NSJSONSerialization dataWithJSONObject:record options:0 error:NULL] writeToFile:[directory stringByAppendingPathComponent:@"entry.json"] atomically:YES];
    NSLog(@"BackThread.Probe %@",phase);
}
@interface BTProbeDelegate : UIResponder <UIApplicationDelegate>
@property(nonatomic,strong) UIWindow *window;
@end
@implementation BTProbeDelegate
- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)options {
    (void)application;(void)options;
    self.window=[[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
    UIViewController *controller=[UIViewController new];controller.view.backgroundColor=UIColor.blackColor;
    UILabel *label=[[UILabel alloc] initWithFrame:controller.view.bounds];label.autoresizingMask=UIViewAutoresizingFlexibleWidth|UIViewAutoresizingFlexibleHeight;label.text=@"BackThread native entry verified";label.textColor=UIColor.whiteColor;label.textAlignment=NSTextAlignmentCenter;
    [controller.view addSubview:label];self.window.rootViewController=controller;[self.window makeKeyAndVisible];mark(@"UIKit-ready");
#ifdef BT_PROBE_LAUNCH_UI
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW,NSEC_PER_SEC),dispatch_get_main_queue(),^{
        Class workspaceClass=NSClassFromString(@"LSApplicationWorkspace");SEL workspaceSelector=NSSelectorFromString(@"defaultWorkspace"),openSelector=NSSelectorFromString(@"openApplicationWithBundleID:");
        if(!workspaceClass||![workspaceClass respondsToSelector:workspaceSelector]){mark(@"launcher-unavailable");return;}
        id workspace=((id(*)(id,SEL))objc_msgSend)(workspaceClass,workspaceSelector);
        if(![workspace respondsToSelector:openSelector]){mark(@"launcher-unavailable");return;}
        BOOL accepted=((BOOL(*)(id,SEL,id))objc_msgSend)(workspace,openSelector,@"app.backthread.companion");
        mark(accepted?@"launch-request-accepted":@"launch-request-rejected");
    });
#endif
    return YES;
}
- (void)applicationWillTerminate:(UIApplication *)application { (void)application;mark(@"terminating"); }
@end
int main(int argc,char **argv) { @autoreleasepool { mark(@"main-entered");return UIApplicationMain(argc,argv,nil,NSStringFromClass(BTProbeDelegate.class)); } }
