#import "BTDisplayProfile.h"
#import <sys/utsname.h>

@implementation BTDisplayProfile

+ (NSString *)hardwareModelIdentifier {
    struct utsname sysinfo;
    if (uname(&sysinfo) != 0) return @"Unknown";
    return [NSString stringWithCString:sysinfo.machine encoding:NSASCIIStringEncoding] ?: @"Unknown";
}

+ (BTDisplayPanelKind)classifyModelIdentifier:(NSString *)model {
    if (!model.length) return BTDisplayPanelKindUnknown;
    static NSSet<NSString *> *lcdModels;
    static NSSet<NSString *> *oledModels;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        lcdModels = [NSSet setWithArray:@[
            @"iPhone7,1", @"iPhone7,2", @"iPhone8,1", @"iPhone8,2", @"iPhone8,4",
            @"iPhone9,1", @"iPhone9,2", @"iPhone9,3", @"iPhone9,4",
            @"iPhone10,1", @"iPhone10,2", @"iPhone10,4", @"iPhone10,5",
            @"iPhone11,8", @"iPhone12,1", @"iPhone12,8", @"iPhone14,6",
            @"iPad7,5", @"iPad11,3", @"iPad13,4"
        ]];
        oledModels = [NSSet setWithArray:@[
            @"iPhone10,3", @"iPhone10,6", @"iPhone11,2", @"iPhone11,4", @"iPhone11,6",
            @"iPhone12,3", @"iPhone12,5",
            @"iPhone13,1", @"iPhone13,2", @"iPhone13,3", @"iPhone13,4",
            @"iPhone14,2", @"iPhone14,3", @"iPhone14,4", @"iPhone14,5",
            @"iPhone14,7", @"iPhone14,8",
            @"iPhone15,2", @"iPhone15,3", @"iPhone15,4", @"iPhone15,5",
            @"iPhone16,1", @"iPhone16,2",
            @"iPad16,3", @"iPad16,4", @"iPad16,5", @"iPad16,6"
        ]];
    });
    if ([oledModels containsObject:model]) return BTDisplayPanelKindOLED;
    if ([lcdModels containsObject:model]) return BTDisplayPanelKindLCD;
    return BTDisplayPanelKindUnknown;
}

+ (CGPoint)pixelShiftOffsetForTick:(NSUInteger)tick {
    static const CGFloat offsets[][2] = {
        {0.0, 0.0}, {1.0, 0.0}, {2.0, 1.0}, {1.0, 2.0},
        {0.0, 1.0}, {-1.0, 0.0}, {-2.0, -1.0}, {-1.0, -2.0}
    };
    NSUInteger idx = tick % 8;
    return CGPointMake(offsets[idx][0], offsets[idx][1]);
}

+ (instancetype)currentProfile {
    BTDisplayProfile *profile = [BTDisplayProfile new];
    UIScreen *screen = [UIScreen mainScreen];
    profile->_modelIdentifier = [self hardwareModelIdentifier];
    profile->_nativeSize = screen.nativeBounds.size;
    profile->_logicalSize = screen.bounds.size;
    profile->_scale = screen.scale > 0 ? screen.scale : 2.0;
    CGFloat maxDim = MAX(profile->_nativeSize.width, profile->_nativeSize.height);
    CGFloat minDim = MAX(1.0, MIN(profile->_nativeSize.width, profile->_nativeSize.height));
    profile->_aspectRatio = maxDim / minDim;

    // Respect optional user override in NSUserDefaults (e.g. aftermarket LCD replacement on an OLED model).
    NSString *overrideVal = [[NSUserDefaults standardUserDefaults] stringForKey:@"bt.display.panelOverride"];
    if ([overrideVal isEqualToString:@"lcd"]) {
        profile->_panelKind = BTDisplayPanelKindLCD;
    } else if ([overrideVal isEqualToString:@"oled"]) {
        profile->_panelKind = BTDisplayPanelKindOLED;
    } else {
        profile->_panelKind = [self classifyModelIdentifier:profile->_modelIdentifier];
    }
    profile->_recommendOledGuard = (profile->_panelKind == BTDisplayPanelKindOLED);
    return profile;
}

- (NSDictionary *)toDictionary {
    NSString *panelStr = _panelKind == BTDisplayPanelKindOLED ? @"Oled"
                       : (_panelKind == BTDisplayPanelKindLCD ? @"Lcd" : @"Unknown");
    return @{
        @"modelIdentifier": _modelIdentifier ?: @"Unknown",
        @"nativeWidth": @((int)_nativeSize.width),
        @"nativeHeight": @((int)_nativeSize.height),
        @"logicalWidth": @((int)_logicalSize.width),
        @"logicalHeight": @((int)_logicalSize.height),
        @"scale": @(_scale),
        @"aspectRatio": @(_aspectRatio),
        @"panelKind": panelStr,
        @"recommendOledGuard": @(_recommendOledGuard)
    };
}

@end
