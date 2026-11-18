#import "BTMediaSource.h"
NSURL *BTDirectMediaURL(NSString *text) {
    if(![text isKindOfClass:NSString.class]||!text.length||text.length>12000)return nil;
    NSURL *url=[NSURL URLWithString:text];
    if(![url.scheme.lowercaseString isEqual:@"https"]||!url.host.length||url.user||url.password||(url.port&&url.port.integerValue!=443))return nil;
    NSSet *extensions=[NSSet setWithArray:@[@"mp4",@"m4v",@"mov",@"m3u8",@"mp3",@"aac",@"m4a",@"wav"]];
    if([extensions containsObject:url.pathExtension.lowercaseString])return url;
    // YouTube CDN signed media URLs have no file extension. Accept only its
    // precise host suffix/path; a watch page is still not a playable stream.
    NSString *host=url.host.lowercaseString;
    BOOL cdn=[host hasSuffix:@".googlevideo.com"]&&host.length>@".googlevideo.com".length;
    if(cdn&&[url.path isEqual:@"/videoplayback"]&&url.query.length)return url;
    return nil;
}
