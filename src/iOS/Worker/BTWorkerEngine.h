#import <Foundation/Foundation.h>

NS_ASSUME_NONNULL_BEGIN

typedef BOOL (^BTWorkerCancelCheck)(void);
typedef void (^BTWorkerProgressBlock)(double progress);

@interface BTWorkerEngine : NSObject

+ (NSArray<NSString *> *)supportedJobKinds;
+ (BOOL)supportsExtendedJobKind:(NSString *)kind;
+ (NSString *)thermalStateName;
+ (BOOL)isThermallyThrottled;

+ (nullable NSDictionary *)executeJobKind:(NSString *)kind
                                     name:(NSString *)name
                                inputPath:(nullable NSString *)inputPath
                               storageDir:(NSString *)storageDir
                               parameters:(nullable NSDictionary *)parameters
                                isCancelled:(BTWorkerCancelCheck)isCancelled
                               onProgress:(BTWorkerProgressBlock)onProgress
                                    error:(NSString * _Nullable * _Nullable)errorOut;

@end

NS_ASSUME_NONNULL_END
