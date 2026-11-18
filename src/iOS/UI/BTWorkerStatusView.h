#import <UIKit/UIKit.h>

NS_ASSUME_NONNULL_BEGIN

@interface BTWorkerStatusView : UIView
@property(nonatomic, copy, nullable) void (^onCancelJobTapped)(NSString *jobId);

- (void)updateWithJob:(NSDictionary * _Nullable)job
       memoryPressure:(BOOL)memoryPressure
       bufferedStatus:(NSDictionary * _Nullable)bufferedStatus;
@end

NS_ASSUME_NONNULL_END
