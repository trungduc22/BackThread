#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

typedef NS_ENUM(NSInteger, BTDisplayPanelKind) {
    BTDisplayPanelKindUnknown = 0,
    BTDisplayPanelKindLCD = 1,
    BTDisplayPanelKindOLED = 2
};

@interface BTDisplayProfile : NSObject
@property(nonatomic, copy, readonly) NSString *modelIdentifier;
@property(nonatomic, assign, readonly) CGSize nativeSize;
@property(nonatomic, assign, readonly) CGSize logicalSize;
@property(nonatomic, assign, readonly) CGFloat scale;
@property(nonatomic, assign, readonly) CGFloat aspectRatio;
@property(nonatomic, assign, readonly) BTDisplayPanelKind panelKind;
@property(nonatomic, assign, readonly) BOOL recommendOledGuard;

+ (instancetype)currentProfile;
+ (BTDisplayPanelKind)classifyModelIdentifier:(NSString *)modelIdentifier;
+ (CGPoint)pixelShiftOffsetForTick:(NSUInteger)tick;

- (NSDictionary *)toDictionary;
@end

NS_ASSUME_NONNULL_END
