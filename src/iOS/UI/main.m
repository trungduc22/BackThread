#import <UIKit/UIKit.h>
#import "BTViewController.h"
#import "../Agent/BTAgent.h"
#include <unistd.h>

static void BTStartup(NSString *phase, NSException *exception) {
    @try {
        NSString *directory = [NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread"];
        [[NSFileManager defaultManager] createDirectoryAtPath:directory withIntermediateDirectories:YES attributes:@{NSFilePosixPermissions: @0700} error:NULL];
        NSMutableDictionary *record = [@{@"phase": phase, @"pid": @(getpid())} mutableCopy];
        if (![phase isEqual:@"main-entered"]) {
            record[@"screenBrightness"] = @(UIScreen.mainScreen.brightness);
            record[@"applicationState"] = @(UIApplication.sharedApplication.applicationState);
        }
        if (exception) {
            record[@"exception"] = exception.name ?: @"unknown";
            record[@"reason"] = exception.reason ?: @"";
        }
        NSString *path = [directory stringByAppendingPathComponent:@"startup-diagnostics.json"];
        [[NSJSONSerialization dataWithJSONObject:record options:0 error:NULL] writeToFile:path atomically:YES];
        [[NSFileManager defaultManager] setAttributes:@{NSFilePosixPermissions: @0600} ofItemAtPath:path error:NULL];
        NSLog(@"BackThread.UI startup %@", phase);
    } @catch (NSException *ignored) {
        (void)ignored;
    }
}

static void BTStartupException(NSException *exception) { BTStartup(@"uncaught-exception", exception); }

@interface BTAppDelegate : UIResponder <UIApplicationDelegate>
@property(nonatomic, strong) UIWindow *window;
@property(nonatomic, strong) BTAgent *agent;
@property(nonatomic, strong) BTViewController *rootVC;
@end

@implementation BTAppDelegate

- (BOOL)application:(UIApplication *)application didFinishLaunchingWithOptions:(NSDictionary *)options {
    (void)application;
    (void)options;
    BTStartup(@"application-delegate", nil);
    BTViewController *vc = [BTViewController new];
    self.rootVC = vc;
    self.window = [[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
    self.window.rootViewController = vc;
    [self.window makeKeyAndVisible];
    BTStartup(@"window-visible", nil);

    NSString *keyString = [[NSString stringWithContentsOfFile:[NSHomeDirectory() stringByAppendingPathComponent:@"Library/BackThread/session.key"]
                                                     encoding:NSASCIIStringEncoding
                                                        error:nil] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    NSMutableData *key = [NSMutableData new];
    if (keyString.length == 64 && [keyString rangeOfCharacterFromSet:[[NSCharacterSet characterSetWithCharactersInString:@"0123456789abcdefABCDEF"] invertedSet]].location == NSNotFound) {
        for (NSUInteger i = 0; i < 64; i += 2) {
            unsigned int byte = 0;
            [[NSScanner scannerWithString:[keyString substringWithRange:NSMakeRange(i, 2)]] scanHexInt:&byte];
            uint8_t b = (uint8_t)byte;
            [key appendBytes:&b length:1];
        }
    }

    NSString *storage = [NSHomeDirectory() stringByAppendingPathComponent:@"Documents/BackThread"];
    self.agent = [[BTAgent alloc] initWithKey:(key.length == 32 ? key : nil) storage:storage];
    self.agent.deviceName = UIDevice.currentDevice.name ?: @"iPhone";
    vc.agent = self.agent;
    __weak BTViewController *weakVC = vc;
    self.agent.stateChanged = ^(NSString *type, NSDictionary *body) {
        [weakVC receive:type body:body];
    };
    __weak typeof(self) weakSelf = self;
    vc.onKeyUpdated = ^(NSData *newKey) {
        BTAppDelegate *strong = weakSelf;
        if (!strong || newKey.length != 32) return;
        [strong.agent updateKey:newKey];
        [strong.agent start];
    };

    if (key.length == 32) {
        BTStartup(@"key-valid", nil);
        [self.agent start];
        BTStartup(@"listener-dispatched", nil);
    } else {
        [vc receive:@"connection" body:@{@"state": @"Scan QR or tap Setup to pair with PC"}];
        BTStartup(@"key-required", nil);
    }
    return YES;
}

- (BOOL)application:(UIApplication *)app openURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey, id> *)options {
    (void)app;
    (void)options;
    if (url && [[url.scheme lowercaseString] isEqualToString:@"backthread"]) {
        [self.rootVC handlePairingURL:url];
        return YES;
    }
    return NO;
}

- (void)applicationWillTerminate:(UIApplication *)application {
    (void)application;
    [self.agent stop];
}

- (void)applicationDidReceiveMemoryWarning:(UIApplication *)application {
    (void)application;
    self.agent.memoryPressure = YES;
}

@end

int main(int argc, char **argv) {
    @autoreleasepool {
        NSSetUncaughtExceptionHandler(BTStartupException);
        BTStartup(@"main-entered", nil);
        return UIApplicationMain(argc, argv, nil, NSStringFromClass(BTAppDelegate.class));
    }
}
