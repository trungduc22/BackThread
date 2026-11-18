#import <UIKit/UIKit.h>
#import <WebKit/WebKit.h>

NS_ASSUME_NONNULL_BEGIN

/// Opt-in experimental YouTube official youtube-nocookie.com iframe embed sheet.
/// Never performs HTML scraping, stream extraction, cipher decoding, or background playback bypasses.
/// Falls back cleanly to Windows Mode D + Live PCM on dismissal or embed restriction error.
@interface BTYouTubeEmbedController : UIViewController <WKNavigationDelegate>
@property(nonatomic, copy, readonly) NSURL *embedURL;
@property(nonatomic, copy, readonly) NSString *videoTitle;
@property(nonatomic, copy, readonly) NSString *sessionId;
@property(nonatomic, readonly) uint64_t generation;
@property(nonatomic, copy, nullable) void (^onConfirmed)(NSString *sessionId, uint64_t generation);
@property(nonatomic, copy, nullable) void (^onFallbackRequested)(NSString *sessionId, uint64_t generation, NSString *reason);

- (instancetype)initWithEmbedURL:(NSURL *)url
                           title:(NSString *)title
                       sessionId:(NSString *)sessionId
                      generation:(uint64_t)generation;
@end

NS_ASSUME_NONNULL_END
