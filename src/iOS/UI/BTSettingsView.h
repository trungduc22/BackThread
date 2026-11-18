#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTSettingsView : UIView
@property(nonatomic, copy, nullable) void (^onPairTapped)(void);
@property(nonatomic, copy, nullable) void (^onEvictCacheTapped)(void);
@property(nonatomic, copy, nullable) void (^onClearHistoryTapped)(void);

- (void)updateDiagnosticsWithConnection:(NSString *)connection
                               pcmStats:(NSDictionary *)pcmStats
                          sessionStatus:(NSDictionary *)sessionStatus;
@end

NS_ASSUME_NONNULL_END
