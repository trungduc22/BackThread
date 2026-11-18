#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTIdleHubView : UIView
@property(nonatomic, copy, nullable) void (^onNavigateMode)(NSString *modeKey);
@property(nonatomic, copy, nullable) void (^onActionInvoked)(NSString *actionId);

- (void)updateConnectionText:(NSString *)connectionText
                usbConnected:(BOOL)usbConnected
                    pcmStats:(NSDictionary *)pcmStats
                 mediaTitle:(NSString *)mediaTitle
                 mediaMode:(NSString *)mediaMode
                    actions:(NSArray<NSDictionary *> *)actions
                    lastJob:(NSDictionary * _Nullable)lastJob
               recentEvents:(NSArray<NSDictionary *> *)recentEvents;
@end

NS_ASSUME_NONNULL_END
